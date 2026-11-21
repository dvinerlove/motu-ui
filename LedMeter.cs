using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MotuUI {
    // Stereo LED ladder: two rows of segments, green->amber->red zones,
    // dim unlit segments, white peak-hold, latched red clip tab.
    public class LedMeter : Grid {
        const int NSeg = 44;
        const double MinDb = -60;

        Rectangle[] segs = new Rectangle[NSeg * 2];
        Rectangle clipTab;
        double gap = 2;

        static readonly Color Green = Color.FromArgb(255, 70, 205, 110);
        static readonly Color Amber = Color.FromArgb(255, 235, 175, 55);
        static readonly Color Red = Color.FromArgb(255, 230, 70, 58);
        static readonly Color Well = Color.FromArgb(255, 12, 13, 15);
        static readonly Color PeakWhite = Color.FromArgb(255, 235, 245, 250);

        static Color Zone(int i) {
            double f = (double)i / NSeg;
            return f > 0.9 ? Red : f > 0.7 ? Amber : Green;
        }
        static double SegDb(int i) { return MinDb + (-MinDb) * i / (NSeg - 1); }

        public LedMeter() {
            var canvas = new Canvas { Background = new SolidColorBrush(Well) };
            for (int i = 0; i < NSeg * 2; i++) {
                segs[i] = new Rectangle { RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Color.FromArgb(30, Green.R, Green.G, Green.B)) };
                canvas.Children.Add(segs[i]);
            }
            clipTab = new Rectangle { Fill = new SolidColorBrush(Red), Visibility = Visibility.Collapsed };
            canvas.Children.Add(clipTab);
            Children.Add(canvas);
            SizeChanged += (s, e) => Layout();
        }

        void Layout() {
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 10) return;
            int rowH = (int)((h - gap) / 2);
            double segW = (w - gap * (NSeg - 1)) / NSeg;
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < NSeg; i++) {
                    var rc = segs[r * NSeg + i];
                    rc.Width = segW; rc.Height = rowH;
                    Canvas.SetLeft(rc, i * (segW + gap));
                    Canvas.SetTop(rc, r * (rowH + gap));
                }
            clipTab.Width = 4; clipTab.Height = h;
            Canvas.SetLeft(clipTab, w - 4);
        }

        public void SetLevels(double l, double r, double pkL, double pkR, bool clip) {
            SetRow(0, l, pkL);
            SetRow(1, r, pkR);
            clipTab.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetRow(int row, double db, double peak) {
            for (int i = 0; i < NSeg; i++) {
                double sd = SegDb(i);
                Color c;
                if (peak > -59.5 && sd <= peak && sd > peak - 60.0 / NSeg)
                    c = PeakWhite;
                else if (sd <= db)
                    c = Zone(i);
                else {
                    var z = Zone(i);
                    c = Color.FromArgb(28, z.R, z.G, z.B);
                }
                var b = (SolidColorBrush)segs[row * NSeg + i].Fill;
                if (b.Color != c) b.Color = c;
            }
        }
    }

    // dB ruler using the same proportional mapping as LedMeter's segments.
    public class DbScale : Canvas {
        static readonly int[] Ticks = { -60, -40, -24, -12, -6, 0 };
        TextBlock[] labels;
        public DbScale() {
            var fg = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            labels = new TextBlock[Ticks.Length];
            for (int i = 0; i < Ticks.Length; i++) {
                labels[i] = new TextBlock { Text = Ticks[i].ToString(), FontSize = 10, Foreground = fg };
                Children.Add(labels[i]);
            }
            SizeChanged += (s, e) => Layout();
        }
        void Layout() {
            double w = ActualWidth;
            if (w < 40) return;
            for (int i = 0; i < labels.Length; i++) {
                var tb = labels[i];
                tb.Measure(new Windows.Foundation.Size(1000, 100));
                double tw = tb.DesiredSize.Width;
                double x = (Ticks[i] + 60) / 60.0 * w - tw / 2;
                if (i == 0) x = 0;
                if (i == labels.Length - 1) x = w - tw;
                SetLeft(tb, x);
            }
        }
    }
}
