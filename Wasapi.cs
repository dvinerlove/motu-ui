using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient {
        int GetBuffer(uint frames, out IntPtr data);
        int ReleaseBuffer(uint frames, int flags);
    }
    // IAudioEndpointVolume — only the head of the vtable we need (through GetMute).
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume {
        int RegisterControlChangeNotify(IntPtr c);
        int UnregisterControlChangeNotify(IntPtr c);
        int GetChannelCount(out uint n);
        int SetMasterVolumeLevel(float db, IntPtr g);
        int SetMasterVolumeLevelScalar(float v, IntPtr g);
        int GetMasterVolumeLevel(out float db);
        int GetMasterVolumeLevelScalar(out float v);
        int SetChannelVolumeLevel(uint ch, float db, IntPtr g);
        int SetChannelVolumeLevelScalar(uint ch, float v, IntPtr g);
        int GetChannelVolumeLevel(uint ch, out float db);
        int GetChannelVolumeLevelScalar(uint ch, out float v);
        int SetMute(int mute, IntPtr g);
        int GetMute(out int mute);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B34"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 {
        int GetAudioSessionControl(IntPtr g, uint flags, [MarshalAs(UnmanagedType.Interface)] out object s);
        int GetSimpleAudioVolume(IntPtr g, uint flags, [MarshalAs(UnmanagedType.Interface)] out object s);
        int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }
    [ComImport, Guid("E2F5DC11-5719-4B24-9CD4-7563A35F6568"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator {
        int GetCount(out int n);
        int GetSession(int i, [MarshalAs(UnmanagedType.Interface)] out object s);
    }
    // vtable = IAudioSessionControl (9 methods) + IAudioSessionControl2 (5 methods).
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2 {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr g);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr g);
        int GetGroupingParam(out Guid g);
        int SetGroupingParam(IntPtr g, IntPtr o);
        int RegisterAudioSessionNotification(IntPtr n);
        int UnregisterAudioSessionNotification(IntPtr n);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint pid);
        int IsSystemSoundsSession();
        int SetDuckingPreference(int opt);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume {
        int SetMasterVolume(float v, IntPtr g);
        int GetMasterVolume(out float v);
        int SetMute(int mute, IntPtr g);
        int GetMute(out int mute);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation {
        int GetPeakValue(out float peak);
        int GetMeteringChannelCount(out uint n);
        int GetChannelsPeakValues(uint n, float[] vals);
        int QueryHardwareSupport(out uint mask);
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
            return OpenCapture(flow, out channels, out _, out _,
                flow == EDataFlow.eCapture ? "In " : null,
                flow == EDataFlow.eCapture ? "Loopback" : null);
        }

        // First MOTU render endpoint, opened for actual playback (shared mode).
        public static IAudioRenderClient OpenRender(out IAudioClient ac, out int channels, out int rate) {
            ac = null; channels = 2; rate = 48000;
            var e = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            IMMDeviceCollection coll;
            if (e.EnumAudioEndpoints(EDataFlow.eRender, 1, out coll) != 0) return null;
            uint n; coll.GetCount(out n);
            for (uint i = 0; i < n; i++) {
                IMMDevice d; coll.Item(i, out d);
                if (!FriendlyName(d).Contains("MOTU")) continue;
                object o;
                if (d.Activate(IID_IAudioClient, 0x17, IntPtr.Zero, out o) != 0) continue;
                var a = (IAudioClient)o;
                IntPtr pf; if (a.GetMixFormat(out pf) != 0) continue;
                channels = Marshal.ReadInt16(pf, 2);
                rate = Marshal.ReadInt32(pf, 4);
                if (a.Initialize(0, 0, 0, 0, pf, IntPtr.Zero) != 0) continue;
                object rc; if (a.GetService(IID_IAudioRenderClient, out rc) != 0) continue;
                ac = a;
                return (IAudioRenderClient)rc;
            }
            return null;
        }

        static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

        // Blocking sine at mix-format rate — call from a worker thread.
        public static unsafe void Tone(IAudioClient ac, IAudioRenderClient rc, int ch, int rate, double sec) {
            ac.Start();
            uint bufN; ac.GetBufferSize(out bufN);
            int total = (int)(rate * sec), wrote = 0;
            while (wrote < total) {
                uint pad; ac.GetCurrentPadding(out pad);
                uint avail = bufN > pad ? bufN - pad : 0;
                if (avail == 0) { Thread.Sleep(5); continue; }
                uint chunk = Math.Min(avail, (uint)(total - wrote));
                IntPtr p;
                if (rc.GetBuffer(chunk, out p) != 0) break;
                float* f = (float*)p;
                for (uint i = 0; i < chunk; i++) {
                    float s = (float)Math.Sin(2 * Math.PI * 440.0 * (wrote + i) / rate) * 0.3f;
                    for (int c = 0; c < ch; c++) f[i * ch + c] = s;
                }
                rc.ReleaseBuffer(chunk, 0);
                wrote += (int)chunk;
            }
            Thread.Sleep(200); // drain
            ac.Stop();
        }

        // The Loopback capture endpoint = playback audio as recorders hear it.
        // "Loopback Mix" adds the live inputs — flagged via epName so the UI can warn.
        public static IAudioCaptureClient OpenLoopback(out int channels) {
            return OpenLoopback(out channels, out _, out _);
        }

        public static IAudioCaptureClient OpenLoopback(out int channels, out int rate, out string epName) {
            var cc = OpenCapture(EDataFlow.eCapture, out channels, out rate, out epName, "Loopback", "Mix");
            if (cc == null) cc = OpenCapture(EDataFlow.eCapture, out channels, out rate, out epName, "Loopback", null);
            return cc;
        }

        static IAudioCaptureClient OpenCapture(EDataFlow flow, out int channels, out int rate, out string epName, string prefer, string avoid) {
            channels = 2; rate = 48000; epName = "";
            var d = FindEndpoint(flow, out epName, prefer, avoid);
            if (d == null) return null;
            object o;
            if (d.Activate(IID_IAudioClient, 0x17, IntPtr.Zero, out o) != 0) return null;
            var ac = (IAudioClient)o;
            IntPtr pf; if (ac.GetMixFormat(out pf) != 0) return null;
            int ch = Marshal.ReadInt16(pf, 2); // WAVEFORMATEX.nChannels
            if (ch >= 1) channels = ch;
            int r = Marshal.ReadInt32(pf, 4);  // nSamplesPerSec
            if (r > 0) rate = r;
            int flags = (flow == EDataFlow.eRender) ? 0x00020000 : 0;
            if (ac.Initialize(0, flags, 0, 0, pf, IntPtr.Zero) != 0) return null;
            object cc; if (ac.GetService(IID_IAudioCaptureClient, out cc) != 0) return null;
            ac.Start();
            return (IAudioCaptureClient)cc;
        }

        static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B34");

        // First MOTU endpoint whose name passes prefer/avoid, or null.
        static IMMDevice FindEndpoint(EDataFlow flow, out string epName, string prefer, string avoid) {
            epName = "";
            var e = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            IMMDeviceCollection coll;
            if (e.EnumAudioEndpoints(flow, 1, out coll) != 0) return null;
            uint n; coll.GetCount(out n);
            IMMDevice first = null; string firstName = "";
            for (uint i = 0; i < n; i++) {
                IMMDevice d; coll.Item(i, out d);
                var nm = FriendlyName(d);
                if (!nm.Contains("MOTU")) continue;
                if (avoid != null && nm.Contains(avoid)) continue;
                if (prefer != null && nm.StartsWith(prefer)) { first = d; firstName = nm; break; }
                if (first == null) { first = d; firstName = nm; }
            }
            epName = firstName;
            return first;
        }

        static IMMDevice FindLoopbackDevice() {
            var d = FindEndpoint(EDataFlow.eCapture, out _, "Loopback", "Mix");
            return d ?? FindEndpoint(EDataFlow.eCapture, out _, "Loopback", null);
        }

        // Master mute on the Loopback capture endpoint — silences what recorders hear.
        public static bool GetLoopMuted() {
            var d = FindLoopbackDevice();
            if (d == null) return false;
            object o;
            if (d.Activate(IID_IAudioEndpointVolume, 0x17, IntPtr.Zero, out o) != 0) return false;
            int m; return ((IAudioEndpointVolume)o).GetMute(out m) == 0 && m != 0;
        }

        public static bool SetLoopMuted(bool mute) {
            var d = FindLoopbackDevice();
            if (d == null) return false;
            object o;
            if (d.Activate(IID_IAudioEndpointVolume, 0x17, IntPtr.Zero, out o) != 0) return false;
            return ((IAudioEndpointVolume)o).SetMute(mute ? 1 : 0, IntPtr.Zero) == 0;
        }

        // Apps with a session on a MOTU render endpoint = what's feeding the loopback bus.
        public sealed class AirSession {
            public string Id;       // session instance id — stable key across re-enumerations
            public string Name;
            public bool Muted;
            public bool Active;
            internal object Ctl;    // IAudioSessionControl — QI'd for meter/volume
        }

        public static System.Collections.Generic.List<AirSession> AirSessions() {
            var list = new System.Collections.Generic.List<AirSession>();
            var e = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            IMMDeviceCollection coll;
            if (e.EnumAudioEndpoints(EDataFlow.eRender, 1, out coll) != 0) return list;
            uint n; coll.GetCount(out n);
            for (uint i = 0; i < n; i++) {
                IMMDevice d; coll.Item(i, out d);
                if (!FriendlyName(d).Contains("MOTU")) continue;
                object o;
                if (d.Activate(IID_IAudioSessionManager2, 0x17, IntPtr.Zero, out o) != 0) continue;
                IAudioSessionEnumerator en;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out en) != 0) continue;
                int cnt; if (en.GetCount(out cnt) != 0) continue;
                for (int j = 0; j < cnt; j++) {
                    object s;
                    if (en.GetSession(j, out s) != 0 || s == null) continue;
                    var c2 = s as IAudioSessionControl2;
                    if (c2 == null) continue;
                    var sess = new AirSession { Ctl = s, Name = "app" };
                    c2.GetSessionInstanceIdentifier(out sess.Id);
                    int st; c2.GetState(out st);
                    if (st == 2) continue; // expired
                    sess.Active = st == 1;
                    uint pid; string dn;
                    c2.GetDisplayName(out dn);
                    if (c2.IsSystemSoundsSession() == 0) sess.Name = "system sounds";
                    else if (!string.IsNullOrEmpty(dn)) sess.Name = dn;
                    else if (c2.GetProcessId(out pid) == 0 && pid != 0) sess.Name = ProcName(pid);
                    var vol = s as ISimpleAudioVolume;
                    if (vol != null) { int m; sess.Muted = vol.GetMute(out m) == 0 && m != 0; }
                    if (sess.Id == null) sess.Id = sess.Name + j;
                    list.Add(sess);
                }
            }
            return list;
        }

        static string ProcName(uint pid) {
            try {
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                string d = null;
                try { d = p.MainModule?.FileVersionInfo?.FileDescription; } catch {}
                return string.IsNullOrEmpty(d) ? p.ProcessName : d;
            } catch { return "pid " + pid; }
        }

        public static float SessionPeak(AirSession s) {
            var m = s?.Ctl as IAudioMeterInformation;
            if (m == null) return 0;
            float p; return m.GetPeakValue(out p) == 0 ? p : 0;
        }

        public static void SessionSetMute(AirSession s, bool mute) {
            (s?.Ctl as ISimpleAudioVolume)?.SetMute(mute ? 1 : 0, IntPtr.Zero);
        }

        // Blocking WAV recorder on the Loopback endpoint — call from a worker thread.
        // Writes 16-bit PCM (float mix format converted); patches the header on stop.
        public static void RecordLoop(string path, ManualResetEventSlim stop) {
            int ch, rate;
            var cc = OpenLoopback(out ch, out rate, out _);
            if (cc == null) throw new InvalidOperationException("no loopback endpoint");
            using (var w = new WavWriter(path, ch, rate)) {
                while (!stop.IsSet) {
                    uint fr; IntPtr p; int fl; ulong pos, qpc;
                    if (cc.GetNextPacketSize(out fr) != 0 || fr == 0) { Thread.Sleep(5); continue; }
                    int guard = 0;
                    while (guard++ < 64 && cc.GetBuffer(out p, out fr, out fl, out pos, out qpc) == 0) {
                        if (fr > 0 && (fl & 1) == 0) w.Write(p, fr);
                        cc.ReleaseBuffer(fr);
                    }
                    Thread.Sleep(2); // a wedged GetBuffer must not spin at 100% CPU
                }
            }
            Marshal.ReleaseComObject(cc);
        }

        sealed unsafe class WavWriter : IDisposable {
            readonly FileStream fs; readonly int ch;
            uint dataBytes;
            public WavWriter(string path, int channels, int rate) {
                ch = channels;
                fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                var bw = new BinaryWriter(fs, Encoding.ASCII, true);
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(0u);                       // patched on close
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16u);
                bw.Write((ushort)1);                // PCM
                bw.Write((ushort)ch);
                bw.Write((uint)rate);
                bw.Write((uint)(rate * ch * 2));
                bw.Write((ushort)(ch * 2));
                bw.Write((ushort)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(0u);                       // patched on close
                bw.Flush();
            }
            public void Write(IntPtr data, uint frames) {
                float* f = (float*)data;
                uint n = frames * (uint)ch;
                var buf = new byte[n * 2];
                fixed (byte* bp = buf) {
                    short* s = (short*)bp;
                    for (uint i = 0; i < n; i++) {
                        float v = f[i] * 32767f;
                        s[i] = (short)Math.Max(-32768, Math.Min(32767, (int)v));
                    }
                }
                fs.Write(buf, 0, buf.Length);
                dataBytes += (uint)buf.Length;
            }
            public void Dispose() {
                fs.Seek(4, SeekOrigin.Begin);
                fs.Write(BitConverter.GetBytes(36u + dataBytes), 0, 4);
                fs.Seek(40, SeekOrigin.Begin);
                fs.Write(BitConverter.GetBytes(dataBytes), 0, 4);
                fs.Dispose();
            }
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
