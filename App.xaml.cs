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
                // MSeriesFree can wedge inside the driver — never block exit on it.
                Task.Run(() => { try { MOTU.MSeriesFree(); } catch {} });
            };
            _window.Activate();
            Task.Run(() => {
                int r = MOTU.MSeriesInit();
                uint d = 0;
                for (int i = 0; r == 0 && i < 20; i++) {
                    r = MOTU.MSeriesGetDevice(0, out d);
                    if (r == 0) break;
                    Thread.Sleep(300);
                }
                if (r != 0) {
                    Task.Run(() => { try { MOTU.MSeriesFree(); } catch {} });
                    q.TryEnqueue(() => ((MainWindow)_window).NoDevice());
                    return;
                }
                MainWindow.SetDevice(d);
                q.TryEnqueue(() => ((MainWindow)_window).DeviceReady());
            });
        }
    }
}
