using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
namespace MotuUI {
    public class Preset {
        public string Name { get; set; }
        public int Buffer { get; set; }
        public bool LowLatency { get; set; }
    }

    public sealed partial class MainWindow : Window {
        static uint dev;
        internal static void SetDevice(uint d) { dev = d; }
        string devName = "";
        int chIn = 2, chOut = 2, chLoop = 2;
        LedMeter MeterIn, MeterOut, MeterLoop;
        IAudioCaptureClient cin, cout, cloop;
        DispatcherTimer timer;
        double pkIL = -100, pkIR = -100, pkOL = -100, pkOR = -100, pkLL = -100, pkLR = -100;
        double dIL = -100, dIR = -100, dOL = -100, dOR = -100, dLL = -100, dLR = -100;
        int holdIL, holdIR, holdOL, holdOR, holdLL, holdLR;
        bool clipIn, clipOut, clipLoop;
        bool srcOn, llOn;
        int reopens;
        bool loading;
        Preset[] presets;
        static readonly string PresetPath = Path.Combine(AppContext.BaseDirectory, "motu-ui-presets.json");

        static string Str(byte[] b) { var s = Encoding.ASCII.GetString(b); int z = s.IndexOf('\0'); return z < 0 ? s : s.Substring(0, z); }

        public MainWindow() {
            InitializeComponent();
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            MeterIn = new LedMeter(); MeterInHost.Children.Add(MeterIn);
            MeterOut = new LedMeter(); MeterOutHost.Children.Add(MeterOut);
            MeterLoop = new LedMeter(); MeterLoopHost.Children.Add(MeterLoop);
            ScaleHost.Children.Add(new DbScale());
            Title = "MOTU";
            try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "motu.ico")); } catch {}
            AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 540));
            var pres = AppWindow.Presenter as OverlappedPresenter;
            var root = (FrameworkElement)Content;
            root.Loaded += (s, e) => {
                var rs = root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(680 * rs), (int)(540 * rs)));
                if (pres != null) { pres.PreferredMinimumWidth = (int)(560 * rs); pres.PreferredMinimumHeight = (int)(440 * rs); }
                ApplyTitleBarTheme(root.ActualTheme);
            };
            root.ActualThemeChanged += (s, e) => ApplyTitleBarTheme(root.ActualTheme);
            // WinUI resets titlebar colors after minimize/restore — reapply on activation.
            Activated += (s, e) => ApplyTitleBarTheme(root.ActualTheme);

            RateBox.ItemsSource = new object[] { 44100, 48000, 88200, 96000, 176400, 192000 };
            BufBox.ItemsSource = new object[] { 16, 32, 64, 128, 256, 512, 1024 };

            loading = true;
            RefreshState();
            loading = false;

            cin = Wasapi.OpenCapture(EDataFlow.eCapture, out chIn);
            cout = Wasapi.OpenCapture(EDataFlow.eRender, out chOut);
            cloop = Wasapi.OpenLoopback(out chLoop);
            ToolTipService.SetToolTip(MeterInHost,
                chIn > 2 ? chIn + " channels — showing first stereo pair; click to reset clip" : "click to reset clip");
            ToolTipService.SetToolTip(MeterOutHost,
                chOut > 2 ? chOut + " channels — showing first stereo pair; click to reset clip" : "click to reset clip");

            RateBox.SelectionChanged += (s, e) => {
                if (loading || RateBox.SelectedItem == null) return;
                MOTU.MSeriesSetSampleRate(dev, (uint)(int)RateBox.SelectedItem);
                Applied();
            };
            BufBox.SelectionChanged += (s, e) => {
                if (loading || BufBox.SelectedItem == null) return;
                MOTU.MSeriesSetHostBufferSize(dev, (uint)(int)BufBox.SelectedItem);
                Applied();
            };
            SrcSwitch.Click += (s, e) => { srcOn = !srcOn; SyncLeds(); MOTU.MSeriesSetWaveSRCEnabled(dev, srcOn ? 1u : 0u); Pulse(); };
            LlSwitch.Click += (s, e) => { llOn = !llOn; SyncLeds(); MOTU.MSeriesEnableLowLatencyMode(dev, llOn); Pulse(); };
            LoadPresets();
            BtnRec.Click += (s, e) => ApplyPreset(0);
            BtnStream.Click += (s, e) => ApplyPreset(1);
            BtnEditRec.Click += (s, e) => EditPreset(0);
            BtnEditStream.Click += (s, e) => EditPreset(1);
            BtnRefresh.Click += (s, e) => RefreshState();
            BtnTest.Click += (s, e) => TestTone();
            BtnUpdate.Click += async (s, e) => await CheckDriverUpdate();
            MeterIn.Tapped += (s, e) => clipIn = false;
            MeterOut.Tapped += (s, e) => clipOut = false;
            MeterLoop.Tapped += (s, e) => clipLoop = false;

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += Tick;
            timer.Start();
        }

        void ApplyTitleBarTheme(ElementTheme theme) {
            bool dark = theme == ElementTheme.Dark;
            var tb = AppWindow.TitleBar;
            var bg = dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 240, 240, 240);
            tb.BackgroundColor = bg;
            tb.InactiveBackgroundColor = bg;
            tb.ButtonBackgroundColor = bg;
            tb.ButtonInactiveBackgroundColor = bg;
            var fg = dark ? Colors.White : Colors.Black;
            tb.ForegroundColor = fg;
            tb.ButtonForegroundColor = fg;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
            byte hov = 16, prs = 32;
            tb.ButtonHoverBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(hov, 255, 255, 255)
                : Windows.UI.Color.FromArgb(hov, 0, 0, 0);
            tb.ButtonPressedBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(prs, 255, 255, 255)
                : Windows.UI.Color.FromArgb(prs, 0, 0, 0);
        }

        void RefreshState() {
            var v = new byte[256]; var s = new byte[256]; var n = new byte[256];
            MOTU.MSeriesGetDeviceInfo(dev, v, s, n);
            devName = Str(n);
            var dv = new byte[256]; MOTU.MSeriesGetDriverVersion(dv);
            uint rate, buf, xso, ie, oe, src;
            MOTU.MSeriesGetCurrentState(dev, out rate, out buf, out xso, out ie, out oe);
            MOTU.MSeriesGetWaveSRCEnabled(dev, out src);
            Title = "MOTU " + Str(n);
            StatusText.Text = (rate / 1000.0).ToString("0.#") + " kHz   buf " + buf + "   " +
                (rate > 0 ? buf * 1000.0 / rate : 0).ToString("0.00") + " ms";
            InfoText.Text = "serial " + Str(s) + "   fw " + Str(v) + "   driver " + Str(dv);
            bool was = loading; loading = true;
            RateBox.SelectedItem = (int)rate;
            BufBox.SelectedItem = (int)buf;
            srcOn = src != 0;
            loading = was;
            SyncLeds();
        }

        void SyncLeds() {
            Led(SrcDot, SrcState, srcOn);
            Led(LlDot, LlState, llOn);
        }

        static readonly Microsoft.UI.Xaml.Media.SolidColorBrush On =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 205, 110));
        static void Led(Microsoft.UI.Xaml.Shapes.Ellipse dot, TextBlock st, bool on) {
            dot.Fill = on ? On : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            st.Text = on ? "On" : "Off";
        }

        // Status read-back + a short fade tells the user "applied" without a dialog.
        void Applied() { RefreshState(); ReopenCaptures(); Pulse(); }

        void Pulse() {
            var sb = new Storyboard();
            var a = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(260)) };
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            StatusText.Opacity = 0.3;
            Storyboard.SetTarget(a, StatusText);
            Storyboard.SetTargetProperty(a, "Opacity");
            sb.Children.Add(a);
            sb.Begin();
        }

        void LoadPresets() {
            try { presets = JsonSerializer.Deserialize<Preset[]>(File.ReadAllText(PresetPath)); } catch {}
            if (presets == null || presets.Length < 2)
                presets = new[] {
                    new Preset { Name = "Record", Buffer = 16, LowLatency = true },
                    new Preset { Name = "Stream", Buffer = 128, LowLatency = false },
                };
            PresetLabels();
        }

        void SavePresets() {
            try { File.WriteAllText(PresetPath, JsonSerializer.Serialize(presets)); } catch {}
        }

        void PresetLabels() {
            BtnRec.Content = presets[0].Name + "  ·  " + presets[0].Buffer + " smp" + (presets[0].LowLatency ? " + LL" : "");
            BtnStream.Content = presets[1].Name + "  ·  " + presets[1].Buffer + " smp" + (presets[1].LowLatency ? " + LL" : "");
        }

        void ApplyPreset(int i) {
            var p = presets[i];
            MOTU.MSeriesSetHostBufferSize(dev, (uint)p.Buffer);
            MOTU.MSeriesEnableLowLatencyMode(dev, p.LowLatency);
            llOn = p.LowLatency;
            Applied();
        }

        async void EditPreset(int i) {
            var p = presets[i];
            var name = new TextBox { Text = p.Name, Header = "Name", MaxLength = 16 };
            var buf = new ComboBox { ItemsSource = new object[] { 16, 32, 64, 128, 256, 512, 1024 }, SelectedItem = p.Buffer, Header = "Buffer (samples)", HorizontalAlignment = HorizontalAlignment.Stretch };
            var ll = new ToggleSwitch { IsOn = p.LowLatency, Header = "Low latency mode", OnContent = "On", OffContent = "Off" };
            var dlg = new ContentDialog {
                Title = "Edit preset",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot,
                Content = new StackPanel { Spacing = 10, Children = { name, buf, ll } },
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (!string.IsNullOrWhiteSpace(name.Text)) p.Name = name.Text.Trim();
            if (buf.SelectedItem is int b) p.Buffer = b;
            p.LowLatency = ll.IsOn;
            SavePresets();
            PresetLabels();
        }

        static bool Newer(string a, string b) {
            try { return new Version(a) > new Version(b); } catch { return a != b; }
        }

        Task Info(string title, string msg) =>
            new ContentDialog { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync().AsTask();

        // Product page per device family (universal M-Series installer lives on each model's page).
        static readonly System.Collections.Generic.Dictionary<string, int> ProductPage =
            new System.Collections.Generic.Dictionary<string, int> { ["M2"] = 408, ["M4"] = 410, ["M6"] = 507 };

        async Task CheckDriverUpdate() {
            try {
                var http = new HttpClient();
                int page;
                if (!ProductPage.TryGetValue(devName, out page)) page = 408;
                var html = await http.GetStringAsync("https://motu.com/en-us/download/product/" + page + "/");
                var m = Regex.Match(Regex.Replace(html, @"\s+", " "),
                    @"fa-windows.{0,400}?PC v([\d.]+).{0,1500}?download-center/download/(\d+)");
                if (!m.Success) { await Info("Driver update", "Couldn't find the Windows installer on motu.com — page layout may have changed."); return; }
                var dv = new byte[256]; MOTU.MSeriesGetDriverVersion(dv);
                var cur = Str(dv);
                var latest = m.Groups[1].Value;
                if (!Newer(latest, cur)) { await Info("Driver update", "Already up to date — v" + cur + "."); return; }
                var ask = new ContentDialog {
                    Title = "Driver update available",
                    Content = "Installed:  " + cur + "\nLatest:  " + latest +
                              "\n\nDownload and run the official MOTU installer?\nThe device may be unavailable while it installs.",
                    PrimaryButtonText = "Update", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot,
                };
                if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
                var prog = new ContentDialog { Title = "Downloading installer…", XamlRoot = Content.XamlRoot,
                    Content = new ProgressRing { IsActive = true, Width = 48, Height = 48, Margin = new Thickness(24) } };
                var showing = prog.ShowAsync();
                var exe = Path.Combine(Path.GetTempPath(), "MOTU-Setup-" + latest + ".exe");
                File.WriteAllBytes(exe, await http.GetByteArrayAsync("https://motu.com/en-us/download-center/download/" + m.Groups[2].Value));
                prog.Hide();
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            } catch (Exception ex) { await Info("Driver update", "Failed: " + ex.Message); }
        }

        bool testing;
        void TestTone() {
            if (testing) return;
            testing = true;
            Task.Run(() => {
                IAudioClient ac = null; IAudioRenderClient rc = null;
                try {
                    int ch, rate;
                    rc = Wasapi.OpenRender(out ac, out ch, out rate);
                    if (rc != null) Wasapi.Tone(ac, rc, ch, rate, 1.5);
                } catch {} finally {
                    if (rc != null) Marshal.ReleaseComObject(rc);
                    if (ac != null) Marshal.ReleaseComObject(ac);
                    testing = false;
                }
            });
        }

        void ReopenCaptures() {
            timer.Stop();
            if (cin != null) Marshal.ReleaseComObject(cin);
            if (cout != null) Marshal.ReleaseComObject(cout);
            if (cloop != null) Marshal.ReleaseComObject(cloop);
            cin = null; cout = null; cloop = null;
            try {
                cin = Wasapi.OpenCapture(EDataFlow.eCapture, out chIn);
                cout = Wasapi.OpenCapture(EDataFlow.eRender, out chOut);
                cloop = Wasapi.OpenLoopback(out chLoop);
            } catch {}
            timer.Start();
        }

        void Tick(object s, object e) {
            double il, ir, ol, orr, ll, lr;
            try {
                if (!Wasapi.Peak2(cin, chIn, out il, out ir)) { il = ir = -100; }
                if (!Wasapi.Peak2(cout, chOut, out ol, out orr)) { ol = orr = -100; }
                if (!Wasapi.Peak2(cloop, chLoop, out ll, out lr)) { ll = lr = -100; }
            } catch {
                if (reopens++ < 3) { ReopenCaptures(); return; }
                timer.Stop(); return;
            }
            reopens = 0;
            // Meter ballistics: instant attack, ~30 dB/s release; peaks hold ~0.8s then fall.
            dIL = Math.Max(il, dIL - 1.5);
            dIR = Math.Max(ir, dIR - 1.5);
            dOL = Math.Max(ol, dOL - 1.5);
            dOR = Math.Max(orr, dOR - 1.5);
            dLL = Math.Max(ll, dLL - 1.5);
            dLR = Math.Max(lr, dLR - 1.5);
            pkIL = Peak(il, ref pkIL, ref holdIL);
            pkIR = Peak(ir, ref pkIR, ref holdIR);
            pkOL = Peak(ol, ref pkOL, ref holdOL);
            pkOR = Peak(orr, ref pkOR, ref holdOR);
            pkLL = Peak(ll, ref pkLL, ref holdLL);
            pkLR = Peak(lr, ref pkLR, ref holdLR);
            if (il > -0.5 || ir > -0.5) clipIn = true;
            if (ol > -0.5 || orr > -0.5) clipOut = true;
            if (ll > -0.5 || lr > -0.5) clipLoop = true;
            MeterIn.SetLevels(dIL, dIR, pkIL, pkIR, clipIn);
            MeterOut.SetLevels(dOL, dOR, pkOL, pkOR, clipOut);
            MeterLoop.SetLevels(dLL, dLR, pkLL, pkLR, clipLoop);
            double mxI = Math.Max(il, ir), mxO = Math.Max(ol, orr), mxL = Math.Max(ll, lr);
            DbIn.Text = mxI <= -60 ? "-∞" : mxI.ToString(" 0.0;-0.0") + "dB";
            DbOut.Text = mxO <= -60 ? "-∞" : mxO.ToString(" 0.0;-0.0") + "dB";
            DbLoop.Text = cloop == null ? "—" : (mxL <= -60 ? "-∞" : mxL.ToString(" 0.0;-0.0") + "dB");
        }

        static double Peak(double v, ref double pk, ref int hold) {
            if (v >= pk) { hold = 16; return v; }
            if (hold > 0) { hold--; return pk; }
            return Math.Max(v, pk - 0.6);
        }
    }
}
