using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace MotuUI {
    public partial class App : Application {
        Window _window;
        public App() {
            InitializeComponent();
            // Silent "won't open" reports are undebuggable — always leave a
            // crash log next to the exe so a broken machine can be diagnosed.
            this.UnhandledException += (s, e) => {
                CrashLog(e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                CrashLog(e.ExceptionObject as Exception);
        }
        static void CrashLog(Exception ex) {
            try {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "motu-ui-crash.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\n\n");
            } catch {}
        }
        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            // Show the window immediately: MSeriesInit can wedge inside the driver, and
            // a process with no window looks like a failed launch (users click again).
            _window = new MainWindow();
            _window.Closed += (s, e) => {
                // A driver op frees its own session — give an in-flight one a
                // moment to finish: exiting inside a session poisons the driver
                // for the next client, like a hard kill does.
                for (int i = 0; MOTU.InFlight > 0 && i < 30; i++) Thread.Sleep(100);
            };
            _window.Activate();
            int retry = 0;
            var a = args.Arguments ?? "";
            int ri = a.IndexOf("--retry ");
            if (ri >= 0) int.TryParse(a.Substring(ri + 8).Split(' ')[0], out retry);
            // Probe on the shared driver thread — MSeriesLib binds to it.
            MOTU.Post(() => {
                var w = (MainWindow)_window;
                // MSeriesLib missing/broken = can't probe at all; say so instead of
                // sitting on "waiting for device…" forever (or dying on the throw).
                if (!MOTU.HasDriver) { q.TryEnqueue(() => w.NoDriver()); return; }
                int r;
                try {
                    // A recently-closed client leaves the driver refusing new init calls for
                    // a while; a failed init is sticky in-process, so recovery means a fresh
                    // process — relaunch ourselves a few times with a pause instead.
                    if (retry > 0) Thread.Sleep(15000);
                    r = MOTU.MSeriesInit();
                    uint d = 0;
                    // Same convention as the ops path: keep polling for a device
                    // slot — it can enumerate late after init.
                    for (int j = 0; r == 0 && d == 0 && j < 20; j++) {
                        MOTU.MSeriesGetDevice(0, out d);
                        if (d == 0) Thread.Sleep(300);
                    }
                    if (r != 0 || d == 0) {
                        try { MOTU.MSeriesFree(); } catch {}
                        // init failed → driver probably still settling → relaunch;
                        // init ok but no device → nothing to find, say so
                        if (r != 0 && retry < 4) {
                            try {
                                Process.Start(new ProcessStartInfo(
                                    Process.GetCurrentProcess().MainModule.FileName,
                                    "--retry " + (retry + 1)) { UseShellExecute = true });
                            } catch {}
                            q.TryEnqueue(() => Current.Exit());
                        } else {
                            q.TryEnqueue(() => w.NoDevice());
                        }
                        return;
                    }
                    // Read the initial state inside this session, then free it —
                    // holding the device would starve the stock control panel.
                    try { w.RefreshState(d); } catch {}
                    try { MOTU.MSeriesFree(); } catch {}
                    q.TryEnqueue(() => w.DeviceReady());
                } catch {
                    q.TryEnqueue(() => w.NoDriver());
                }
            });
        }
    }
}
