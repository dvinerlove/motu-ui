using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace MotuUI {
    public partial class App : Application {
        Window _window;
        public App() { InitializeComponent(); }
        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            // MSeriesInit can block inside the driver — probe on a worker so a wedged
            // driver never produces a dead window.
            Task.Run(() => {
                int r = MOTU.MSeriesInit();
                uint d = 0;
                for (int i = 0; r == 0 && i < 20; i++) {
                    r = MOTU.MSeriesGetDevice(0, out d);
                    if (r == 0) break;
                    Thread.Sleep(300);
                }
                if (r != 0) {
                    // No device: free off-thread, then quit — a headless app has no UI to close it with.
                    Task.Run(() => { try { MOTU.MSeriesFree(); } catch {} });
                    q.TryEnqueue(() => Current.Exit());
                    return;
                }
                MainWindow.SetDevice(d);
                q.TryEnqueue(() => {
                    _window = new MainWindow();
                    _window.Activate();
                    _window.Closed += (s, e) => {
                        // MSeriesFree can wedge inside the driver — never block exit on it.
                        Task.Run(() => { try { MOTU.MSeriesFree(); } catch {} });
                    };
                });
            });
        }
    }
}
