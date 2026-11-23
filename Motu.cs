using System;
using System.Runtime.InteropServices;

namespace MotuUI {
    static class MOTU {
        static readonly string[] DIRS = {
            @"C:\Program Files (x86)\MOTU\CoreUAC",
            @"C:\Program Files\MOTU\CoreUAC",
        };
        [DllImport("kernel32", SetLastError = true)] static extern bool SetDllDirectory(string p);

        // False when MSeriesLib isn't on disk anywhere we know — the probe can
        // then say "driver not installed" instead of wedging on a missing dll.
        public static readonly bool HasDriver;
        static string dllDir;
        public static string DllDir { get { return dllDir; } }

        // MSeriesLib binds to the thread that first calls into it — calls from
        // random pool threads flake or wedge. Every library call goes through
        // this one dedicated thread, started with the class.
        static readonly System.Collections.Concurrent.BlockingCollection<Action> _q =
            new System.Collections.Concurrent.BlockingCollection<Action>();
        static MOTU() {
            foreach (var d in DIRS) {
                if (System.IO.File.Exists(System.IO.Path.Combine(d, "MSeriesLib.dll"))) {
                    dllDir = d;
                    break;
                }
            }
            if (dllDir != null) { SetDllDirectory(dllDir); HasDriver = true; }
            var t = new System.Threading.Thread(() => {
                foreach (var a in _q.GetConsumingEnumerable()) { try { a(); } catch {} }
            });
            t.IsBackground = true;
            t.Name = "mseries";
            t.Start();
        }
        static int inFlight;
        // >0 while a posted op is queued or running — Closed waits on this so an
        // in-flight session can free cleanly. (drvBusy can't be used for that:
        // it's cleared via the UI thread, which is the one sleeping.)
        public static int InFlight { get { return inFlight; } }
        public static void Post(Action a) {
            System.Threading.Interlocked.Increment(ref inFlight);
            _q.Add(() => { try { a(); } finally { System.Threading.Interlocked.Decrement(ref inFlight); } });
        }

        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesInit();
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesFree();
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesGetDevice(uint n, out uint devRef);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesGetDeviceInfo(uint d, byte[] ver, byte[] serial, byte[] name);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesGetDriverVersion(byte[] v);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesGetCurrentState(uint d, out uint rate, out uint buf, out uint xso, out uint inEn, out uint outEn);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesSetSampleRate(uint d, uint hz);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesSetHostBufferSize(uint d, uint n);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesEnableLowLatencyMode(uint d, bool en);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesGetWaveSRCEnabled(uint d, out uint en);
        [DllImport("MSeriesLib.dll", CallingConvention = CallingConvention.StdCall)] public static extern int MSeriesSetWaveSRCEnabled(uint d, uint en);
    }
}
