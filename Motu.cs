using System;
using System.Runtime.InteropServices;

namespace MotuUI {
    static class MOTU {
        const string DIR = @"C:\Program Files (x86)\MOTU\CoreUAC";
        [DllImport("kernel32", SetLastError = true)] static extern bool SetDllDirectory(string p);
        static MOTU() { SetDllDirectory(DIR); }

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
