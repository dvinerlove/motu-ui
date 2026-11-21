using System.Threading;
using Microsoft.UI.Xaml;

namespace MotuUI {
    public partial class App : Application {
        Window _window;
        public App() { InitializeComponent(); }
        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            if (MOTU.MSeriesInit() != 0) return;
            uint d = 0; int r = 1;
            for (int i = 0; i < 20; i++) {
                r = MOTU.MSeriesGetDevice(0, out d);
                if (r == 0) break;
                Thread.Sleep(300);
            }
            if (r != 0) { MOTU.MSeriesFree(); return; }
            MainWindow.SetDevice(d);
            _window = new MainWindow();
            _window.Activate();
            _window.Closed += (s, e) => MOTU.MSeriesFree();
        }
    }
}
