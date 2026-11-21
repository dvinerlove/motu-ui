using System;
using System.Runtime.InteropServices;

namespace MotuUI {
    static class MOTU {
        const string DIR = @"C:\Program Files (x86)\MOTU\CoreUAC";
        [DllImport("kernel32", SetLastError = true)] static extern bool SetDllDirectory(string p);

        // MSeriesLib binds to the thread that first calls into it — calls from
        // random pool threads flake or wedge. Every library call goes through
        // this one dedicated thread, started with the class.
        static readonly System.Collections.Concurrent.BlockingCollection<Action> _q =
            new System.Collections.Concurrent.BlockingCollection<Action>();
        static MOTU() {
            SetDllDirectory(DIR);
            var t = new System.Threading.Thread(() => {
                foreach (var a in _q.GetConsumingEnumerable()) { try { a(); } catch {} }
            });
            t.IsBackground = true;
            t.Name = "mseries";
            t.Start();
        }
        public static void Post(Action a) { _q.Add(a); }

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
