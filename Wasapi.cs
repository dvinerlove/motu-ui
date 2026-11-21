using System;
using System.Runtime.InteropServices;

namespace MotuUI {
    enum EDataFlow { eRender = 0, eCapture = 1 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(EDataFlow flow, int stateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection coll);
        int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice dev);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { int GetCount(out uint n); int Item(uint i, [MarshalAs(UnmanagedType.Interface)] out IMMDevice dev); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        int OpenPropertyStore(int access, out IPropertyStore ps);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore { int GetCount(out uint n); int GetAt(uint i, out PROPERTYKEY k); int GetValue(ref PROPERTYKEY k, out PROPVARIANT v); }
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public int dummy; }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        int Initialize(int share, int flags, long buf, long per, IntPtr fmt, IntPtr sess);
        int GetBufferSize(out uint n);
        int GetStreamLatency(out long l);
        int GetCurrentPadding(out uint n);
        int IsFormatSupported(int share, IntPtr fmt, IntPtr closest);
        int GetMixFormat(out IntPtr fmt);
        int GetDevicePeriod(out long d1, out long d2);
        int Start(); int Stop(); int Reset(); int SetEventHandle(IntPtr h);
        int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient {
        int GetBuffer(out IntPtr data, out uint frames, out int flags, out ulong pos, out ulong qpc);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    static class Wasapi {
        static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        static readonly PROPERTYKEY PKEY_Name = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        static string FriendlyName(IMMDevice d) {
            IPropertyStore ps; if (d.OpenPropertyStore(0, out ps) != 0) return "";
            var k = PKEY_Name; PROPVARIANT v;
            if (ps.GetValue(ref k, out v) != 0) return "";
            return Marshal.PtrToStringUni(v.p) ?? "";
        }

        public static IAudioCaptureClient OpenCapture(EDataFlow flow) { return OpenCapture(flow, out _); }

        public static IAudioCaptureClient OpenCapture(EDataFlow flow, out int channels) {
            // Capture endpoints carry channel-pair names ("In 1-2", "Loopback",
            // "Loopback Mix"); prefer the physical inputs, avoid virtual ones.
            return OpenCapture(flow, out channels,
                flow == EDataFlow.eCapture ? "In " : null,
                flow == EDataFlow.eCapture ? "Loopback" : null);
        }

        // The Loopback capture endpoint = playback audio as recorders hear it.
        public static IAudioCaptureClient OpenLoopback(out int channels) {
            var cc = OpenCapture(EDataFlow.eCapture, out channels, "Loopback", "Mix");
            if (cc == null) cc = OpenCapture(EDataFlow.eCapture, out channels, "Loopback", null);
            return cc;
        }

        static IAudioCaptureClient OpenCapture(EDataFlow flow, out int channels, string prefer, string avoid) {
            channels = 2;
            var e = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            IMMDeviceCollection coll;
            if (e.EnumAudioEndpoints(flow, 1, out coll) != 0) return null;
            uint n; coll.GetCount(out n);
            IMMDevice first = null;
            for (uint i = 0; i < n; i++) {
                IMMDevice d; coll.Item(i, out d);
                var nm = FriendlyName(d);
                if (!nm.Contains("MOTU")) continue;
                if (avoid != null && nm.Contains(avoid)) continue;
                if (prefer != null && nm.StartsWith(prefer)) { first = d; break; }
                if (first == null) first = d;
            }
            if (first == null) return null;
            object o;
            if (first.Activate(IID_IAudioClient, 0x17, IntPtr.Zero, out o) != 0) return null;
            var ac = (IAudioClient)o;
            IntPtr pf; if (ac.GetMixFormat(out pf) != 0) return null;
            int ch = Marshal.ReadInt16(pf, 2); // WAVEFORMATEX.nChannels
            if (ch >= 1) channels = ch;
            int flags = (flow == EDataFlow.eRender) ? 0x00020000 : 0;
            if (ac.Initialize(0, flags, 0, 0, pf, IntPtr.Zero) != 0) return null;
            object cc; if (ac.GetService(IID_IAudioCaptureClient, out cc) != 0) return null;
            ac.Start();
            return (IAudioCaptureClient)cc;
        }

        public static unsafe bool Peak2(IAudioCaptureClient cc, out double pl, out double pr) {
            return Peak2(cc, 2, out pl, out pr);
        }

        // ch = channel count of the stream's mix format (2 for M2, 4+ for M4/M6).
        // L/R meters show channels 0 and 1, the first pair on every model.
        public static unsafe bool Peak2(IAudioCaptureClient cc, int ch, out double pl, out double pr) {
            pl = pr = -100;
            if (cc == null) return false;
            if (ch < 1) ch = 2;
            uint fr; if (cc.GetNextPacketSize(out fr) != 0 || fr == 0) return true;
            IntPtr p; int fl; ulong pos, qpc; double l = 0, r = 0;
            while (true) {
                if (cc.GetBuffer(out p, out fr, out fl, out pos, out qpc) != 0) break;
                if (fr > 0 && (fl & 1) == 0) {
                    float* f = (float*)p;
                    for (uint i = 0; i + 1 < fr * ch; i += (uint)ch) {
                        float a = f[i] < 0 ? -f[i] : f[i]; if (a > l) l = a;
                        if (ch > 1) { float b = f[i + 1] < 0 ? -f[i + 1] : f[i + 1]; if (b > r) r = b; }
                    }
                }
                cc.ReleaseBuffer(fr);
                if (cc.GetNextPacketSize(out fr) != 0 || fr == 0) break;
            }
            pl = l <= 0 ? -100 : 20 * Math.Log10(l);
            pr = r <= 0 ? -100 : 20 * Math.Log10(r);
            return true;
        }
    }
}
