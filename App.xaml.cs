using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace MotuUI {
    public partial class App : Application {
        Window _window;
        public App() { InitializeComponent(); }
        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            // Show the window immediately: MSeriesInit can wedge inside the driver, and
            // a process with no window looks like a failed launch (users click again).
            _window = new MainWindow();
            _window.Closed += (s, e) => {
                // A driver op frees its own session — give an in-flight one a
                // moment to finish: exiting inside a session poisons the driver
                // for the next client, like a hard kill does.
                for (int i = 0; ((MainWindow)_window).drvBusy && i < 30; i++) Thread.Sleep(100);
            };
            _window.Activate();
            int retry = 0;
            var a = args.Arguments ?? "";
            int ri = a.IndexOf("--retry ");
            if (ri >= 0) int.TryParse(a.Substring(ri + 8).Split(' ')[0], out retry);
            // Probe on the shared driver thread — MSeriesLib binds to it.
            MOTU.Post(() => {
                // A recently-closed client leaves the driver refusing new init calls for
                // a while; a failed init is sticky in-process, so recovery means a fresh
                // process — relaunch ourselves a few times with a pause instead.
                if (retry > 0) Thread.Sleep(15000);
                int r = MOTU.MSeriesInit();
                uint d = 0;
                for (int j = 0; r == 0 && j < 20; j++) {
                    r = MOTU.MSeriesGetDevice(0, out d);
                    if (r == 0) break;
                    Thread.Sleep(300);
                }
                var w = (MainWindow)_window;
                if (r != 0) {
                    try { MOTU.MSeriesFree(); } catch {}
                    if (retry < 4) {
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
            });
        }
    }
}
