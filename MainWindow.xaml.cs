using System;
using System.Collections.Generic;
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
    public class UiSettings {
        public bool InOut { get; set; } = true;
        public bool Loop { get; set; } = true;
        public bool OnAir { get; set; }
    }

    public class Preset {
        public string Name { get; set; }
        public int Buffer { get; set; }
        public bool LowLatency { get; set; }
    }

    public sealed partial class MainWindow : Window {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr ins, int x, int y, int cx, int cy, uint f);

        // The probe only proves the device exists; every driver call then opens
        // a short init/free session — a session held for the app's lifetime
        // starves the stock control panel (it can't reach the device while a
        // client holds it).
        bool haveDev;
        string devName = "";
        int chIn = 2, chOut = 2, chLoop = 2;
        LedMeter MeterIn, MeterOut, MeterLoop;
        IAudioCaptureClient cin, cout, cloop;
        IAudioClient acIn, acOut, acLoop; // stream owners — a finalized client kills its stream
        DispatcherTimer timer;
        double pkIL = -100, pkIR = -100, pkOL = -100, pkOR = -100, pkLL = -100, pkLR = -100;
        double dIL = -100, dIR = -100, dOL = -100, dOR = -100, dLL = -100, dLR = -100;
        int holdIL, holdIR, holdOL, holdOR, holdLL, holdLR;
        bool clipIn, clipOut, clipLoop;
        bool srcOn, llOn;
        bool shownOnce;
        bool loading;
        // loopback / on-air state
        bool loopMuted;
        string loopEpName = "";
        volatile float loopLevelTarget = -1f; // -1 = untouched; worker applies endpoint-side
        volatile float loopLevelApplied = -1f;
        long loopMs = -1;
        int ticks, outHot, loopDead;
        AirSpark airHistory;
        readonly List<AirRow> airRows = new List<AirRow>();
        AirRow[] airSnapshot = new AirRow[0];
        string drvVer = "";
        // The capture worker owns every WASAPI client; the UI only reads this snapshot.
        readonly object snapLock = new object();
        double sIL = -100, sIR = -100, sOL = -100, sOR = -100, sLL = -100, sLR = -100;
        DateTime snapUtc = DateTime.MinValue;
        volatile bool needReopen = true;
        int loopRetry;
        int capFails;
        // hotplug: the capture worker polls endpoints (cheap, no driver session);
        // the UI probes the driver only on transitions — a present endpoint with a
        // blind driver means the driver is still re-enumerating after a settings
        // change, while absent endpoints + blind driver twice in a row = unplugged.
        volatile bool epPresent = true;
        bool epUi = true, probeBusy, devUi = true;
        long epSince, lastProbe, blindSince;

        class AirRow {
            public string Key; public Wasapi.AirSession S;
            public MiniBar Bar; public TextBlock Db, Name; public FontIcon Icon;
        }
        Preset[] presets;
        bool expOn;
        int expH; // DIP the ON AIR card adds to the window height
        UiSettings ui = new UiSettings();
        int rowH;       // measured height of one meter row incl. spacing — 0 until layout
        int metersCardH; // measured whole meters card incl. its row spacing
        DispatcherTimer fitTimer; // debounce: collapse rapid toggles into one resize request
        int fitRetries; // re-asserts while the real size hasn't converged yet
        OverlappedPresenter pres;
        // writable state lives in %LOCALAPPDATA% — next to the exe is read-only
        // under Program Files and volatile in single-file extraction dirs
        static readonly string DataDir = InitDataDir();
        static readonly string PresetPath = Path.Combine(DataDir, "motu-ui-presets.json");
        static readonly string CfgPath = Path.Combine(DataDir, "motu-ui-cfg.txt");
        static readonly string SettingsPath = Path.Combine(DataDir, "motu-ui-settings.json");
        internal static string CrashLogPath = Path.Combine(DataDir, "motu-ui-crash.txt");

        static string InitDataDir() {
            try {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "motu-ui");
                Directory.CreateDirectory(dir);
                // one-time migration for installs that kept state next to the exe
                foreach (var f in new[] { "motu-ui-presets.json", "motu-ui-cfg.txt" }) {
                    var oldp = Path.Combine(AppContext.BaseDirectory, f);
                    var newp = Path.Combine(dir, f);
                    if (!File.Exists(newp) && File.Exists(oldp))
                        try { File.Copy(oldp, newp); } catch {}
                }
                return dir;
            } catch { return AppContext.BaseDirectory; }
        }

        static string Str(byte[] b) { var s = Encoding.ASCII.GetString(b); int z = s.IndexOf('\0'); return z < 0 ? s : s.Substring(0, z); }

        public MainWindow() {
            InitializeComponent();
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            MeterIn = new LedMeter(); MeterInHost.Children.Add(MeterIn);
            MeterOut = new LedMeter(); MeterOutHost.Children.Add(MeterOut);
            MeterLoop = new LedMeter(); MeterLoopHost.Children.Add(MeterLoop);
            ScaleHost.Children.Add(new DbScale());
            airHistory = new AirSpark(); AirHistoryHost.Children.Add(airHistory);
            ToolTipService.SetToolTip(LoopNote, "playback→loopback delay measured by the last test tone");
            Title = "MOTU";
            try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "motu.ico")); } catch {}
            pres = AppWindow.Presenter as OverlappedPresenter;
            var root = (FrameworkElement)Content;
            fitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            fitTimer.Tick += (s, e) => { fitTimer.Stop(); ResizeToFit(); };
            root.Loaded += (s, e) => {
                if (rowH == 0 && MeterInHost.ActualHeight > 0) {
                    rowH = (int)Math.Ceiling(MeterInHost.ActualHeight + 8);  // + RowSpacing
                    metersCardH = (int)Math.Ceiling(MetersCard.ActualHeight + 12); // + grid RowSpacing
                }
                ApplyMinSize(root.XamlRoot.RasterizationScale);
                ApplyTitleBarTheme(root.ActualTheme);
                ResizeToFit();
                ScheduleFit(); // re-assert once in-flight resizes have settled
                // Show only once the first frame is composed — an immediately
                // activated WinUI window flashes black at its default size.
                if (!shownOnce) {
                    shownOnce = true;
                    var show = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                    show.Tick += (s2, e2) => { show.Stop(); Activate(); };
                    show.Start();
                }
            };
            root.ActualThemeChanged += (s, e) => ApplyTitleBarTheme(root.ActualTheme);
            // WinUI resets titlebar colors and misplaces the caption controls
            // after minimize/restore — reapply colors and force a frame recalc.
            Activated += (s, e) => {
                ApplyTitleBarTheme(root.ActualTheme);
                SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), IntPtr.Zero,
                    0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); // NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED
            };

            RateBox.ItemsSource = new object[] { 44100, 48000, 88200, 96000, 176400, 192000 };
            BufBox.ItemsSource = new object[] { 16, 32, 64, 128, 256, 512, 1024 };

            loading = true; // cleared by the first Drv refresh below

            ToolTipService.SetToolTip(MeterInHost, "click to reset clip");
            ToolTipService.SetToolTip(MeterOutHost, "click to reset clip");
            Task.Run(CaptureLoop);

            RateBox.SelectionChanged += (s, e) => {
                if (loading || RateBox.SelectedItem == null) return;
                var r = (uint)(int)RateBox.SelectedItem;
                Drv(d => MOTU.MSeriesSetSampleRate(d, r));
            };
            BufBox.SelectionChanged += (s, e) => {
                if (loading || BufBox.SelectedItem == null) return;
                var b = (uint)(int)BufBox.SelectedItem;
                Drv(d => MOTU.MSeriesSetHostBufferSize(d, b));
            };
            SrcSwitch.Click += (s, e) => Drv(d => MOTU.MSeriesSetWaveSRCEnabled(d, srcOn ? 0u : 1u));
            LlSwitch.Click += (s, e) => Drv(d => { MOTU.MSeriesEnableLowLatencyMode(d, !llOn); llOn = !llOn; });
            BtnExp.Click += (s, e) => OpenSettings();
            LoadUiSettings();
            expOn = ui.OnAir;
            PaintExp();
            ApplyMetersLayout();
            OnAirCard.SizeChanged += (s, e) => {
                if (!expOn || OnAirCard.Visibility != Visibility.Visible) return;
                int h = (int)Math.Ceiling(e.NewSize.Height + 12); // + row spacing
                if (h == expH) return; // fires per animation frame too — ignore noise
                expH = h;
                ScheduleFit();
            };
            LoadPresets();
            BtnRec.Click += (s, e) => ApplyPreset(0);
            BtnStream.Click += (s, e) => ApplyPreset(1);
            BtnEditRec.Click += (s, e) => EditPreset(0);
            BtnEditStream.Click += (s, e) => EditPreset(1);
            BtnRefresh.Click += (s, e) => Drv(d => {});
            BtnTest.Click += (s, e) => TestTone();
            BtnUpdate.Click += async (s, e) => {
                if (dlgOpen) return;
                dlgOpen = true;
                try { await CheckDriverUpdate(); } finally { dlgOpen = false; }
            };
            MeterIn.Tapped += (s, e) => clipIn = false;
            MeterOut.Tapped += (s, e) => clipOut = false;
            MeterLoop.Tapped += (s, e) => clipLoop = false;
            NoDevDriverBtn.Click += (s, e) => {
                try {
                    Process.Start(new ProcessStartInfo("https://motu.com/en-us/download/product/408/") { UseShellExecute = true });
                } catch {}
            };
            NoDevCredit.Click += (s, e) => {
                try {
                    Process.Start(new ProcessStartInfo("https://github.com/dvinerlove/motu-ui") { UseShellExecute = true });
                } catch {}
            };
            BtnLoopMute.Click += (s, e) => {
                Task.Run(() => {
                    Wasapi.SetLoopMuted(!Wasapi.GetLoopMuted());
                    var m = Wasapi.GetLoopMuted();
                    RunOnUI(() => { loopMuted = m; PaintLoopState(); });
                });
            };
            LoopLevel.ValueChanged += (s, e) => {
                loopLevelTarget = (float)e.NewValue / 100f;
                LoopLevelText.Text = ((int)e.NewValue) + "%";
            };
            PaintLoopState();
            StartAirPoller();

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += Tick;
            timer.Start();
            StatusText.Text = "—";
            InfoText.Text = "waiting for device…";
        }

        // Called by App once the probe has found and released the device.
        internal void DeviceReady() {
            haveDev = true; loading = false; needReopen = true;
            blindSince = 0;
            ShowDeviceUi();
            FlashTaskbar();
            Pulse();
        }
        internal void NoDevice() { loading = false; ShowNoDevice(false); }
        internal void NoDriver() { loading = false; ShowNoDevice(true); }

        void DeviceGone() {
            haveDev = false;
            blindSince = 0;
            ShowNoDevice(false);
            FlashTaskbar();
        }

        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO f);
        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        // Taskbar attention flash, same as a notification — no-op while the
        // window already has focus, which is exactly how it should be.
        void FlashTaskbar() {
            try {
                var f = new FLASHWINFO {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this),
                    dwFlags = 3 | 12, // FLASHW_ALL | FLASHW_TIMERNOFG
                    uCount = 6, dwTimeout = 0,
                };
                FlashWindowEx(ref f);
            } catch {}
        }

        void ShowDeviceUi() {
            devUi = true;
            NoDeviceOverlay.Visibility = Visibility.Collapsed;

            ScheduleFit();
        }

        void ShowNoDevice(bool driverMissing) {
            devUi = false;
            NoDevTitle.Text = driverMissing ? "driver not installed" : "no device connected";
            NoDevHint.Text = driverMissing
                ? "motu-ui drives the box through the official MOTU M Series driver. Install it, then plug your interface in"
                : "plug in your M2, M4 or M6. It connects by itself, no restart needed";
            WatchRow.Visibility = driverMissing ? Visibility.Collapsed : Visibility.Visible;
            NoDevDriverBtn.Visibility = driverMissing ? Visibility.Visible : Visibility.Collapsed;
            Title = "MOTU";
            if (NoDeviceOverlay.Visibility != Visibility.Visible) {
                NoDeviceOverlay.Opacity = 0;
                NoDeviceOverlay.Visibility = Visibility.Visible;
                var sb = new Storyboard();
                var a = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(280)) };
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                Storyboard.SetTarget(a, NoDeviceOverlay);
                Storyboard.SetTargetProperty(a, "Opacity");
                sb.Children.Add(a);
                sb.Begin();
            }

            ScheduleFit();
        }

        // Probe the driver once on the shared driver thread. Connect: endpoints
        // appeared but haveDev is false. Unplug: endpoints gone while haveDev —
        // the flip happens only if endpoints stay gone through a confirm window,
        // so a settings-change re-enumeration (they return within seconds)
        // never flashes the no-device screen.
        void ProbeDevice() {
            probeBusy = true;
            lastProbe = Environment.TickCount64;
            MOTU.Post(() => {
                bool found = false;
                try {
                    if (MOTU.MSeriesInit() == 0) {
                        uint d = 0;
                        for (int j = 0; d == 0 && j < 6; j++) {
                            MOTU.MSeriesGetDevice(0, out d);
                            if (d == 0) Thread.Sleep(250);
                        }
                        if (d != 0) {
                            found = true;
                            if (!haveDev) try { RefreshState(d); } catch {}
                        }
                    }
                } catch {} finally { try { MOTU.MSeriesFree(); } catch {} }
                RunOnUI(() => {
                    probeBusy = false;
                    if (found) {
                        blindSince = 0;
                        if (!haveDev) DeviceReady();
                    } else {
                        blindSince = Environment.TickCount64;
                    }
                });
            });
        }

        void ApplyTitleBarTheme(ElementTheme theme) {
            bool dark = theme == ElementTheme.Dark;
            var tb = AppWindow.TitleBar;
            var bg = dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 240, 240, 240);
            // The empty-state overlay must hide the main UI completely — an
            // opaque solid in the window's own color, not a translucent card brush.
            NoDeviceOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(bg);
            tb.BackgroundColor = bg;
            tb.InactiveBackgroundColor = bg;
            tb.ButtonBackgroundColor = bg;
            tb.ButtonInactiveBackgroundColor = bg;
            var fg = dark ? Colors.White : Colors.Black;
            tb.ForegroundColor = fg;
            tb.ButtonForegroundColor = fg;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
            // WinUI ignores the alpha channel of caption-button colors — a
            // 6%-alpha white hover paints as opaque white. Pre-blend against
            // the titlebar background and pass opaque colors instead.
            // dark bg 32: hover 32+223*16/255≈46, pressed 32+223*32/255≈60
            // light bg 240: hover 240*(1-16/255)≈225, pressed 240*(1-32/255)≈210
            tb.ButtonHoverBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(255, 46, 46, 46)
                : Windows.UI.Color.FromArgb(255, 225, 225, 225);
            tb.ButtonPressedBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(255, 60, 60, 60)
                : Windows.UI.Color.FromArgb(255, 210, 210, 210);
        }

        void RunOnUI(Action a) {
            var q = DispatcherQueue;
            if (q == null) return; // shutting down — dropping a paint is safe, a cross-thread touch is not
            if (!q.HasThreadAccess) q.TryEnqueue(() => a()); else a();
        }

        // MSeriesLib calls can block for seconds inside the driver — read state off the
        // caller's thread and marshal UI updates back. Must run inside a session (Drv or
        // the App probe).
        internal void RefreshState(uint d) {
            var v = new byte[256]; var s = new byte[256]; var n = new byte[256];
            MOTU.MSeriesGetDeviceInfo(d, v, s, n);
            var dv = new byte[256]; MOTU.MSeriesGetDriverVersion(dv);
            uint rate, buf, xso, ie, oe, src;
            MOTU.MSeriesGetCurrentState(d, out rate, out buf, out xso, out ie, out oe);
            MOTU.MSeriesGetWaveSRCEnabled(d, out src);
            string name = Str(n), ser = Str(s), fw = Str(v), dver = Str(dv);
            RunOnUI(() => {
                devName = name;
                drvVer = dver;
                Title = "MOTU " + name;
                StatusText.Text = (rate / 1000.0).ToString("0.#") + " kHz   buf " + buf + "   " +
                    (rate > 0 ? buf * 1000.0 / rate : 0).ToString("0.00") + " ms";
                InfoText.Text = "serial " + ser + "   fw " + fw + "   driver " + dver;
                bool was = loading; loading = true;
                RateBox.SelectedItem = (int)rate;
                BufBox.SelectedItem = (int)buf;
                srcOn = src != 0;
                // Low latency = zero output safety offset, same read-back the stock
                // panel uses; uint.MaxValue means the driver didn't report one.
                if (xso != uint.MaxValue) llOn = xso == 0;
                loading = was;
                SyncLeds();
            });
        }

        // Driver ops run on the shared driver thread (MSeriesLib is
        // thread-affine) inside a short init/free session, so a wedged IOCTL
        // can't freeze the window and an idle app never holds the device.
        // Extra clicks while a write is in flight are dropped; the refresh at
        // the end syncs LEDs/boxes back to the real device state.
        internal bool drvBusy;
        void Drv(Action<uint> call) {
            if (drvBusy || !haveDev) return;
            drvBusy = true;
            MOTU.Post(() => {
                try {
                    if (MOTU.MSeriesInit() == 0) {
                        uint d = 0;
                        for (int j = 0; d == 0 && j < 20; j++) {
                            MOTU.MSeriesGetDevice(0, out d);
                            if (d == 0) Thread.Sleep(300);
                        }
                        if (d != 0) try { call(d); RefreshState(d); } catch {}
                    }
                } catch {} finally { try { MOTU.MSeriesFree(); } catch {} }
                RunOnUI(() => { drvBusy = false; loading = false; needReopen = true; Pulse(); });
            });
        }

        void SyncLeds() {
            // Shown inverted on purpose: the stock panel and our label describe
            // "sync Windows rate to device" (= WaveSRC off), not the raw flag.
            Led(SrcDot, SrcState, !srcOn);
            Led(LlDot, LlState, llOn);
        }

        void LoadUiSettings() {
            try { ui = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath)) ?? new UiSettings(); }
            catch {
                ui = new UiSettings();
                // one-time migration: the old cfg only knew the ON AIR toggle
                try { ui.OnAir = File.ReadAllText(CfgPath).Trim() == "1"; } catch {}
            }
        }

        void SaveUiSettings() {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(ui)); } catch {}
        }

        // Window height contribution of hidden meter rows: each hidden row frees
        // its measured height; with all three gone the whole card (padding,
        // scale, border) collapses too.
        int MetersDelta() {
            if (rowH == 0) return 0;
            int hidden = (ui.InOut ? 0 : 2) + (ui.Loop ? 0 : 1);
            if (hidden == 3) return metersCardH;
            return hidden * rowH;
        }

        static readonly GridLength StarRow = new GridLength(1, GridUnitType.Star);
        static readonly GridLength ZeroRow = new GridLength(0);

        void SetRow(RowDefinition row, TextBlock lbl, FrameworkElement host, TextBlock db, bool vis) {
            row.Height = vis ? StarRow : ZeroRow;
            lbl.Visibility = host.Visibility = db.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }

        void ApplyMetersLayout() {
            SetRow(RowIn, LblIn, MeterInHost, DbIn, ui.InOut);
            SetRow(RowOut, LblOut, MeterOutHost, DbOut, ui.InOut);
            SetRow(RowLoop, LblLoop, MeterLoopHost, DbLoop, ui.Loop);
            MetersCard.Visibility = !ui.InOut && !ui.Loop ? Visibility.Collapsed : Visibility.Visible;
            ScheduleFit();
        }

        async void OpenSettings() {
            if (dlgOpen) return;
            dlgOpen = true;
            try {
                var io = new ToggleSwitch { Header = "IN / OUT meters", IsOn = ui.InOut, OnContent = "Shown", OffContent = "Hidden" };
                var lp = new ToggleSwitch { Header = "Loopback meter", IsOn = ui.Loop, OnContent = "Shown", OffContent = "Hidden" };
                var oa = new ToggleSwitch { Header = "ON AIR card", IsOn = ui.OnAir, IsEnabled = ui.Loop, OnContent = "Shown", OffContent = "Hidden", Margin = new Thickness(16, 0, 0, 0) };
                var exp = new TextBlock {
                    Text = "experimental", FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    Margin = new Thickness(0, 6, 0, 0),
                };
                var credit = new HyperlinkButton {
                    Content = "developed by dvinerlove  ·  source on GitHub",
                    FontSize = 11, Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    Margin = new Thickness(0, 14, 0, 0),
                    NavigateUri = new Uri("https://github.com/dvinerlove/motu-ui"),
                };
                var panel = new StackPanel { Spacing = 10, MinWidth = 300 };
                panel.Children.Add(io);
                panel.Children.Add(exp);
                panel.Children.Add(lp);
                panel.Children.Add(oa);
                panel.Children.Add(credit);
                var dlg = new ContentDialog {
                    Title = "Settings", CloseButtonText = "Done",
                    XamlRoot = Content.XamlRoot, Content = panel,
                };
                io.Toggled += (s2, e2) => { ui.InOut = io.IsOn; ApplyMetersLayout(); SaveUiSettings(); };
                lp.Toggled += (s2, e2) => { ui.Loop = lp.IsOn; oa.IsEnabled = lp.IsOn; ApplyMetersLayout(); PaintExp(); SaveUiSettings(); };
                oa.Toggled += (s2, e2) => { ui.OnAir = oa.IsOn; expOn = oa.IsOn; PaintExp(); SaveUiSettings(); };
                await dlg.ShowAsync();
            } finally { dlgOpen = false; }
        }

        void PaintExp() {
            if (expOn && ui.Loop) {
                OnAirCard.Visibility = Visibility.Visible;
                // SizeChanged doesn't fire when re-shown at the same size, so
                // read the real height once the layout pass has completed
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
                    if (!expOn) return;
                    expH = (int)Math.Ceiling(OnAirCard.ActualHeight + 12); // + row spacing
                    ScheduleFit();
                });
            } else {
                expH = 0;
                OnAirCard.Visibility = Visibility.Collapsed;
            }
            ScheduleFit();
        }

        void ScheduleFit() { fitRetries = 0; fitTimer.Stop(); fitTimer.Start(); }

        void ApplyMinSize(double rs) {
            if (pres == null) return;
            pres.PreferredMinimumWidth = (int)(560 * rs);
            // device UI: base 440 + ON AIR card, else the *-sized meters squash to
            // slivers; no-device overlay is a compact card, not a squeezed main UI
            pres.PreferredMinimumHeight = (int)((devUi ? 440 + expH - MetersDelta() : 300) * rs);
        }

        // Absolute target — not a delta. AppWindow.Resize lands asynchronously
        // and out of order, so an earlier in-flight request can undo ours:
        // re-check the real size until it converges (cap for screens that
        // physically can't reach the target height).
        void ResizeToFit() {
            var aw = AppWindow;
            if (aw == null) return;
            double rs = Content.XamlRoot?.RasterizationScale ?? 1;
            ApplyMinSize(rs);
            var s = aw.Size;
            int w = (int)(680 * rs);
            int h = (int)Math.Round((devUi ? 640 + expH - MetersDelta() : 330) * rs);
            if (w == s.Width && h == s.Height) return;
            aw.Resize(new Windows.Graphics.SizeInt32(w, h));
            if (++fitRetries < 30) { fitTimer.Stop(); fitTimer.Start(); }
        }

        static readonly Microsoft.UI.Xaml.Media.SolidColorBrush On =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 205, 110));
        static void Led(Microsoft.UI.Xaml.Shapes.Ellipse dot, TextBlock st, bool on) {
            dot.Fill = on ? On : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            st.Text = on ? "On" : "Off";
        }

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
            Drv(d => {
                MOTU.MSeriesSetHostBufferSize(d, (uint)p.Buffer);
                MOTU.MSeriesEnableLowLatencyMode(d, p.LowLatency);
                llOn = p.LowLatency;
            });
        }

        bool dlgOpen; // WinUI allows one ContentDialog at a time — swallow re-entrants
        async void EditPreset(int i) {
            if (dlgOpen) return;
            dlgOpen = true;
            try { await EditPresetCore(i); } finally { dlgOpen = false; }
        }

        async Task EditPresetCore(int i) {
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
                if (!m.Success) { await Info("Driver update", "Couldn't find the Windows installer on motu.com. The page layout may have changed."); return; }
                var cur = drvVer;
                if (string.IsNullOrEmpty(cur)) { await Info("Driver update", "No device connected, so the installed driver version is unknown."); return; }
                var latest = m.Groups[1].Value;
                if (!Newer(latest, cur)) { await Info("Driver update", "Already up to date: v" + cur + "."); return; }
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
                try {
                    var exe = Path.Combine(Path.GetTempPath(), "MOTU-Setup-" + latest + ".exe");
                    File.WriteAllBytes(exe, await http.GetByteArrayAsync("https://motu.com/en-us/download-center/download/" + m.Groups[2].Value));
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                } finally { prog.Hide(); } // a failed download must not leave the dialog up
            } catch (Exception ex) { await Info("Driver update", "Failed: " + ex.Message); }
        }

        bool testing;
        void TestTone() {
            if (testing) return;
            testing = true;
            Task.Run(() => {
                IAudioClient ac = null, lac = null; IAudioRenderClient rc = null; IAudioCaptureClient lc = null;
                try {
                    int ch, rate, lch;
                    rc = Wasapi.OpenRender(out ac, out ch, out rate);
                    lc = Wasapi.OpenLoopback(out lch, out _, out _, out lac);
                    if (lc == null) RunOnUI(() => loopMs = -1);
                    // drain stale packets so the first signal we see is this tone's
                    if (lc != null) {
                        uint fr; IntPtr p; int fl; ulong pos, qpc;
                        while (lc.GetNextPacketSize(out fr) == 0 && fr > 0) {
                            if (lc.GetBuffer(out p, out fr, out fl, out pos, out qpc) != 0) break;
                            lc.ReleaseBuffer(fr);
                        }
                    }
                    var tone = Task.Run(() => {
                        if (rc != null) Wasapi.Tone(ac, rc, ch, rate, 1.5);
                    });
                    if (lc != null) {
                        var sw = Stopwatch.StartNew();
                        while (!tone.IsCompleted && sw.ElapsedMilliseconds < 4000) {
                            double l, r;
                            if (Wasapi.Peak2(lc, lch, out l, out r) && Math.Max(l, r) > -30) {
                                RunOnUI(() => loopMs = sw.ElapsedMilliseconds);
                                break;
                            }
                            Thread.Sleep(5);
                        }
                    }
                    tone.Wait();
                } catch {} finally {
                    if (lc != null) Marshal.ReleaseComObject(lc);
                    if (lac != null) Marshal.ReleaseComObject(lac);
                    if (rc != null) Marshal.ReleaseComObject(rc);
                    if (ac != null) Marshal.ReleaseComObject(ac);
                    testing = false;
                }
            });
        }

        // One worker owns every capture client: a wedged WASAPI call freezes the
        // meters, never the window. UI reads only the snapshot under snapLock.
        // References must live forever — a collected callback or enumerator
        // silently ends the OS notifications.
        static object epEnumKeepalive;
        static object epNotifyKeepalive;

        void CaptureLoop() {
            int epTick = 250; // evaluate endpoints on the first iteration
            try {
                var en = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
                epNotifyKeepalive = new EpNotify();
                en.RegisterEndpointNotificationCallback((IMMNotificationClient)epNotifyKeepalive);
                epEnumKeepalive = en;
            } catch {}
            while (true) {
                try {
                    // OS endpoint events are the real signal; the timer is only a
                    // fallback in case a notification is ever missed.
                    if (EpNotify.Dirty || ++epTick >= 250) {
                        epTick = 0;
                        EpNotify.Dirty = false;
                        try { epPresent = Wasapi.MotuEndpointPresent(); } catch {}
                    }
                    if (!epPresent) {
                        // box unplugged (or the driver is re-enumerating): the old
                        // streams are dead either way — drop them, don't reopen
                        // against nothing. needReopen rearms for when it returns.
                        if (cin != null || cout != null || cloop != null) {
                            ReleaseCap(ref cin, ref acIn);
                            ReleaseCap(ref cout, ref acOut);
                            ReleaseCap(ref cloop, ref acLoop);
                            needReopen = true;
                            RunOnUI(() => PaintLoopState());
                        }
                        Thread.Sleep(150);
                        continue;
                    }
                    if (needReopen) {
                        needReopen = false;
                        DoReopen();
                    }
                    // loopback endpoint may appear late (driver still re-creating
                    // endpoints after a rate/buffer change) — keep retrying
                    if (cloop != null) loopRetry = 0;
                    else if (++loopRetry > 150) { needReopen = true; loopRetry = 0; }
                    if (loopLevelTarget >= 0f && loopLevelTarget != loopLevelApplied) {
                        Wasapi.SetLoopLevel(loopLevelTarget);
                        loopLevelApplied = loopLevelTarget;
                    }
                    double il, ir, ol, orr, ll, lr;
                    if (!Wasapi.Peak2(cin, chIn, out il, out ir)) { il = ir = -100; }
                    if (!Wasapi.Peak2(cout, chOut, out ol, out orr)) { ol = orr = -100; }
                    if (!Wasapi.Peak2(cloop, chLoop, out ll, out lr)) { ll = lr = -100; }
                    lock (snapLock) {
                        sIL = il; sIR = ir; sOL = ol; sOR = orr; sLL = ll; sLR = lr;
                        snapUtc = DateTime.UtcNow;
                    }
                    capFails = 0;
                    Thread.Sleep(20);
                } catch {
                    if (++capFails > 5) { needReopen = true; capFails = 0; }
                    Thread.Sleep(200);
                }
            }
        }

        static void ReleaseCap(ref IAudioCaptureClient cc, ref IAudioClient ac) {
            if (ac != null) { try { ac.Stop(); } catch {} try { Marshal.ReleaseComObject(ac); } catch {} ac = null; }
            if (cc != null) { try { Marshal.ReleaseComObject(cc); } catch {} cc = null; }
        }

        void DoReopen() {
            ReleaseCap(ref cin, ref acIn);
            ReleaseCap(ref cout, ref acOut);
            ReleaseCap(ref cloop, ref acLoop);
            string name = "";
            bool m = false;
            float lv = 1f;
            try {
                cin = Wasapi.OpenCapture(EDataFlow.eCapture, out chIn, out acIn);
                cout = Wasapi.OpenCapture(EDataFlow.eRender, out chOut, out acOut);
                cloop = Wasapi.OpenLoopback(out chLoop, out _, out name, out acLoop);
                m = Wasapi.GetLoopMuted();
                lv = Wasapi.GetLoopLevel();
            } catch {}
            RunOnUI(() => {
                loopEpName = name;
                loopMuted = m;
                loopLevelApplied = loopLevelTarget = lv;
                LoopLevel.Value = lv * 100;
                PaintLoopState();
                ToolTipService.SetToolTip(MeterInHost,
                    chIn > 2 ? chIn + " channels — showing first stereo pair; click to reset clip" : "click to reset clip");
                ToolTipService.SetToolTip(MeterOutHost,
                    chOut > 2 ? chOut + " channels — showing first stereo pair; click to reset clip" : "click to reset clip");
            });
        }

        static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RedBrush =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 70, 58));

        void PaintLoopState() {
            MixBadge.Visibility = loopEpName != null && loopEpName.Contains("Mix")
                ? Visibility.Visible : Visibility.Collapsed;
            LoopMuteIcon.Glyph = loopMuted ? "\uE74F" : "\uE767";
            LoopMuteIcon.Foreground = loopMuted
                ? (Microsoft.UI.Xaml.Media.Brush)RedBrush
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            BtnLoopMute.IsEnabled = LoopLevel.IsEnabled = cloop != null;
            LoopLevelText.Foreground = loopMuted
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        // Session + mute enumeration runs on a worker — a wedged endpoint must not freeze the UI.
        bool sessBusy; long sessSince;
        void RefreshSessions() {
            // a wedged enumeration must not freeze the list forever — retry after 10 s
            if (sessBusy && Environment.TickCount64 - sessSince < 10000) return;
            sessBusy = true; sessSince = Environment.TickCount64;
            Task.Run(() => {
                List<Wasapi.AirSession> list = null; bool m = false; bool mok = false;
                try { list = Wasapi.AirSessions(); m = Wasapi.GetLoopMuted(); mok = true; } catch {}
                RunOnUI(() => {
                    sessBusy = false;
                    if (mok) {
                        bool wasMuted = loopMuted;
                        loopMuted = m;
                        if (loopMuted != wasMuted) PaintLoopState();
                    }
                    if (list != null) ApplySessions(list);
                });
            });
        }

        void ApplySessions(List<Wasapi.AirSession> list) {
            bool same = list.Count == airRows.Count;
            for (int i = 0; same && i < list.Count; i++)
                if (list[i].Id != airRows[i].Key) same = false;
            if (!same) {
                airRows.Clear();
                AirSessions.Children.Clear();
                if (list.Count == 0)
                    AirSessions.Children.Add(new TextBlock {
                        Text = "nothing is playing into the bus", FontSize = 11,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    });
                foreach (var s in list) airRows.Add(MakeRow(s));
            }
            foreach (var r in airRows) {
                r.S = list.Find(x => x.Id == r.Key) ?? r.S;
                PaintRow(r);
            }
            airSnapshot = airRows.ToArray();
        }

        // Peak polling off the UI thread — a stuck session can't freeze the window.
        void StartAirPoller() {
            Task.Run(() => {
                var local = new Dictionary<string, double>();
                while (true) {
                    var rows = airSnapshot;
                    local.Clear();
                    foreach (var r in rows) {
                        double db = -100;
                        try {
                            var s = r.S;
                            if (s != null) {
                                float p = Wasapi.SessionPeak(s);
                                db = p <= 0 ? -100 : 20 * Math.Log10(p);
                            }
                        } catch {}
                        local[r.Key] = db;
                    }
                    RunOnUI(() => {
                        foreach (var r in airSnapshot) {
                            double db;
                            if (!local.TryGetValue(r.Key, out db)) continue;
                            r.Bar.SetDb(db);
                            r.Db.Text = db <= -60 ? "-∞" : db.ToString(" 0.0;-0.0");
                        }
                    });
                    Thread.Sleep(120);
                }
            });
        }

        AirRow MakeRow(Wasapi.AirSession s) {
            var r = new AirRow { Key = s.Id, S = s };
            r.Name = new TextBlock {
                FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            r.Bar = new MiniBar { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            r.Db = new TextBlock {
                FontSize = 10, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                TextAlignment = TextAlignment.Right, Width = 56, VerticalAlignment = VerticalAlignment.Center,
            };
            r.Icon = new FontIcon { Glyph = "\uE767", FontSize = 11 };
            var btn = new Button {
                Width = 26, Height = 22, Padding = new Thickness(0), Content = r.Icon,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(btn, "mute this app: silences it for you and the stream (Windows session volume, not bus-only)");
            var row = r;
            btn.Click += (bs, be) => {
                var s = row.S;
                if (s == null) return;
                var m = !s.Muted;
                Task.Run(() => Wasapi.SessionSetMute(s, m));
                s.Muted = m;
                PaintRow(row);
            };
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(r.Name);
            Grid.SetColumn(r.Bar, 1); g.Children.Add(r.Bar);
            Grid.SetColumn(r.Db, 2); g.Children.Add(r.Db);
            Grid.SetColumn(btn, 3); g.Children.Add(btn);
            AirSessions.Children.Add(g);
            PaintRow(r);
            return r;
        }

        void PaintRow(AirRow r) {
            var res = Application.Current.Resources;
            r.Name.Text = r.S?.Name ?? "";
            bool act = r.S?.Active ?? false, mut = r.S?.Muted ?? false;
            r.Name.Foreground = (Microsoft.UI.Xaml.Media.Brush)res[act ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"];
            r.Db.Foreground = (Microsoft.UI.Xaml.Media.Brush)res["TextFillColorTertiaryBrush"];
            r.Icon.Glyph = mut ? "\uE74F" : "\uE767";
            r.Icon.Foreground = mut ? (Microsoft.UI.Xaml.Media.Brush)RedBrush
                : (Microsoft.UI.Xaml.Media.Brush)res["TextFillColorTertiaryBrush"];
        }

        void Tick(object s, object e) {
            double il, ir, ol, orr, ll, lr; DateTime snapAt;
            lock (snapLock) { il = sIL; ir = sIR; ol = sOL; orr = sOR; ll = sLL; lr = sLR; snapAt = snapUtc; }
            // dead feed: decay everything and show "—" instead of frozen numbers
            bool stale = (DateTime.UtcNow - snapAt).TotalSeconds > 1.5;
            if (stale) { il = ir = ol = orr = ll = lr = -100; }
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
            DbIn.Text = stale ? "—" : (mxI <= -60 ? "-∞" : mxI.ToString(" 0.0;-0.0") + "dB");
            DbOut.Text = stale ? "—" : (mxO <= -60 ? "-∞" : mxO.ToString(" 0.0;-0.0") + "dB");
            DbLoop.Text = (stale || cloop == null) ? "—" : (mxL <= -60 ? "-∞" : mxL.ToString(" 0.0;-0.0") + "dB");

            // ON AIR card: history strip, per-app meters, silence watchdog, status line.
            ticks++;
            if (ticks % 5 == 0) airHistory.Push(mxL);
            if (ticks % 20 == 0) RefreshSessions();
            if (mxO > -30) outHot++; else outHot = 0;
            if (mxL <= -55) loopDead++; else loopDead = 0;
            LoopWarn.Visibility = !loopMuted && !stale && cloop != null && outHot > 60 && loopDead > 60
                ? Visibility.Visible : Visibility.Collapsed;
            string note;
            if (cloop == null) note = stale ? "" : "no loopback endpoint";
            else if (loopMuted) note = "muted";
            else if (loopMs >= 0) note = "≈ " + loopMs + " ms";
            else note = "";
            if (LoopNote.Text != note) LoopNote.Text = note;
            LoopNote.Foreground = loopMuted ? (Microsoft.UI.Xaml.Media.Brush)RedBrush
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            // breathing dot on the no-device screen — sine pulse, no storyboard
            if (!devUi) WatchDot.Opacity = 0.55 + 0.45 * Math.Sin(Environment.TickCount64 / 420.0);

            // Hotplug watchdog: endpoint transitions arrive via OS events through
            // the capture worker. Probe the driver once they've settled (0.8 s),
            // not more often than every 2.5 s. Unplug is confirmed when the probe
            // is blind AND endpoints haven't returned within 3.5 s — a settings
            // re-enumeration brings them back sooner and cancels the flip.
            long nowMs = Environment.TickCount64;
            bool ep = epPresent;
            if (ep != epUi) { epUi = ep; epSince = nowMs; }
            if (ep) blindSince = 0;
            bool need = (ep && !haveDev) || (!ep && haveDev);
            if (need && !drvBusy && !probeBusy && nowMs - epSince > 800 && nowMs - lastProbe > 2500)
                ProbeDevice();
            if (!ep && haveDev && blindSince != 0 && nowMs - epSince > 3500)
                DeviceGone();
        }

        static double Peak(double v, ref double pk, ref int hold) {
            if (v >= pk) { hold = 16; return v; }
            if (hold > 0) { hold--; return pk; }
            return Math.Max(v, pk - 0.6);
        }
    }
}
