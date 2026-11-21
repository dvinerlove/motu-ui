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
                if (MOTU.MSeriesInit() != 0) return;
                uint d = 0; int r = 1;
                for (int i = 0; i < 20; i++) {
                    r = MOTU.MSeriesGetDevice(0, out d);
                    if (r == 0) break;
                    Thread.Sleep(300);
                }
                if (r != 0) { MOTU.MSeriesFree(); return; }
                MainWindow.SetDevice(d);
                q.TryEnqueue(() => {
                    _window = new MainWindow();
                    _window.Activate();
                    _window.Closed += (s, e) => MOTU.MSeriesFree();
                });
            });
        }
    }
}
