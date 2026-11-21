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
        bool loading;
        // loopback / on-air state
        bool loopMuted;
        string loopEpName = "";
        volatile float loopLevelTarget = -1f; // -1 = untouched; worker applies endpoint-side
        float loopLevelApplied = -1f;
        long loopMs = -1;
        int ticks, outHot, loopDead;
        AirSpark airHistory;
        readonly List<AirRow> airRows = new List<AirRow>();
        AirRow[] airSnapshot = new AirRow[0];
        readonly Dictionary<string, double> airDb = new Dictionary<string, double>();
        // The capture worker owns every WASAPI client; the UI only reads this snapshot.
        readonly object snapLock = new object();
        double sIL = -100, sIR = -100, sOL = -100, sOR = -100, sLL = -100, sLR = -100;
        DateTime snapUtc = DateTime.MinValue;
        volatile bool needReopen = true;
        int capFails;

        class AirRow {
            public string Key; public Wasapi.AirSession S;
            public MiniBar Bar; public TextBlock Db, Name; public FontIcon Icon;
        }
        Preset[] presets;
        bool expOn;
        static readonly string PresetPath = Path.Combine(AppContext.BaseDirectory, "motu-ui-presets.json");
        static readonly string CfgPath = Path.Combine(AppContext.BaseDirectory, "motu-ui-cfg.txt");

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
            AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 640));
            var pres = AppWindow.Presenter as OverlappedPresenter;
            var root = (FrameworkElement)Content;
            root.Loaded += (s, e) => {
                var rs = root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(680 * rs), (int)(640 * rs)));
                if (pres != null) { pres.PreferredMinimumWidth = (int)(560 * rs); pres.PreferredMinimumHeight = (int)(440 * rs); }
                ApplyTitleBarTheme(root.ActualTheme);
            };
            root.ActualThemeChanged += (s, e) => ApplyTitleBarTheme(root.ActualTheme);
            // WinUI resets titlebar colors after minimize/restore — reapply on activation.
            Activated += (s, e) => ApplyTitleBarTheme(root.ActualTheme);

            RateBox.ItemsSource = new object[] { 44100, 48000, 88200, 96000, 176400, 192000 };
            BufBox.ItemsSource = new object[] { 16, 32, 64, 128, 256, 512, 1024 };

            loading = true; // cleared by the first Drv refresh below

            ToolTipService.SetToolTip(MeterInHost, "click to reset clip");
            ToolTipService.SetToolTip(MeterOutHost, "click to reset clip");
            Task.Run(CaptureLoop);

            RateBox.SelectionChanged += (s, e) => {
                if (loading || RateBox.SelectedItem == null) return;
                var r = (uint)(int)RateBox.SelectedItem;
                Drv(() => MOTU.MSeriesSetSampleRate(dev, r));
            };
            BufBox.SelectionChanged += (s, e) => {
                if (loading || BufBox.SelectedItem == null) return;
                var b = (uint)(int)BufBox.SelectedItem;
                Drv(() => MOTU.MSeriesSetHostBufferSize(dev, b));
            };
            SrcSwitch.Click += (s, e) => Drv(() => MOTU.MSeriesSetWaveSRCEnabled(dev, srcOn ? 0u : 1u));
            LlSwitch.Click += (s, e) => Drv(() => { MOTU.MSeriesEnableLowLatencyMode(dev, !llOn); llOn = !llOn; });
            BtnExp.Click += (s, e) => {
                expOn = !expOn;
                try { File.WriteAllText(CfgPath, expOn ? "1" : "0"); } catch {}
                PaintExp();
            };
            try { expOn = File.ReadAllText(CfgPath).Trim() == "1"; } catch {}
            PaintExp();
            LoadPresets();
            BtnRec.Click += (s, e) => ApplyPreset(0);
            BtnStream.Click += (s, e) => ApplyPreset(1);
            BtnEditRec.Click += (s, e) => EditPreset(0);
            BtnEditStream.Click += (s, e) => EditPreset(1);
            BtnRefresh.Click += (s, e) => Drv(() => {});
            BtnTest.Click += (s, e) => TestTone();
            BtnUpdate.Click += async (s, e) => await CheckDriverUpdate();
            MeterIn.Tapped += (s, e) => clipIn = false;
            MeterOut.Tapped += (s, e) => clipOut = false;
            MeterLoop.Tapped += (s, e) => clipLoop = false;
            BtnLoopMute.Click += (s, e) => {
                Task.Run(() => {
                    Wasapi.SetLoopMuted(!Wasapi.GetLoopMuted());
                    var m = Wasapi.GetLoopMuted();
                    RunOnUI(() => { loopMuted = m; PaintLoopState(); });
                });
            };
            LoopLevel.ValueChanged += (s, e) => loopLevelTarget = (float)e.NewValue / 100f;
            PaintLoopState();
            StartAirPoller();

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += Tick;
            timer.Start();
            if (dev != 0) Drv(() => {}); // initial state read, off the UI thread
            else { StatusText.Text = "—"; InfoText.Text = "waiting for device…"; }
        }

        // Called by App once the worker-side MSeriesInit probe resolves.
        internal void DeviceReady() { Drv(() => {}); }
        internal void NoDevice() { StatusText.Text = "—"; InfoText.Text = "no MOTU device found"; }

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

        void RunOnUI(Action a) {
            var q = DispatcherQueue;
            if (q != null && !q.HasThreadAccess) q.TryEnqueue(() => a()); else a();
        }

        // MSeriesLib calls can block for seconds inside the driver — read state off the
        // caller's thread and marshal UI updates back.
        void RefreshState() {
            var v = new byte[256]; var s = new byte[256]; var n = new byte[256];
            MOTU.MSeriesGetDeviceInfo(dev, v, s, n);
            var dv = new byte[256]; MOTU.MSeriesGetDriverVersion(dv);
            uint rate, buf, xso, ie, oe, src;
            MOTU.MSeriesGetCurrentState(dev, out rate, out buf, out xso, out ie, out oe);
            MOTU.MSeriesGetWaveSRCEnabled(dev, out src);
            string name = Str(n), ser = Str(s), fw = Str(v), dver = Str(dv);
            RunOnUI(() => {
                devName = name;
                Title = "MOTU " + name;
                StatusText.Text = (rate / 1000.0).ToString("0.#") + " kHz   buf " + buf + "   " +
                    (rate > 0 ? buf * 1000.0 / rate : 0).ToString("0.00") + " ms";
                InfoText.Text = "serial " + ser + "   fw " + fw + "   driver " + dver;
                bool was = loading; loading = true;
                RateBox.SelectedItem = (int)rate;
                BufBox.SelectedItem = (int)buf;
                srcOn = src != 0;
                loading = was;
                SyncLeds();
            });
        }

        // Driver writes run on the shared driver thread (MSeriesLib is
        // thread-affine) so a wedged IOCTL can't freeze the window. Extra clicks
        // while a write is in flight are dropped; the refresh at the end syncs
        // LEDs/boxes back to the real device state.
        bool drvBusy;
        void Drv(Action call) {
            if (drvBusy || dev == 0) return;
            drvBusy = true;
            MOTU.Post(() => {
                try { call(); RefreshState(); } catch {}
                RunOnUI(() => { drvBusy = false; loading = false; needReopen = true; Pulse(); });
            });
        }

        void SyncLeds() {
            Led(SrcDot, SrcState, srcOn);
            Led(LlDot, LlState, llOn);
        }

        void PaintExp() {
            ExpIcon.Foreground = expOn ? (Microsoft.UI.Xaml.Media.Brush)On
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            OnAirCard.Visibility = expOn ? Visibility.Visible : Visibility.Collapsed;
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
            Drv(() => {
                MOTU.MSeriesSetHostBufferSize(dev, (uint)p.Buffer);
                MOTU.MSeriesEnableLowLatencyMode(dev, p.LowLatency);
                llOn = p.LowLatency;
            });
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
                IAudioClient ac = null; IAudioRenderClient rc = null; IAudioCaptureClient lc = null;
                try {
                    int ch, rate, lch;
                    rc = Wasapi.OpenRender(out ac, out ch, out rate);
                    lc = Wasapi.OpenLoopback(out lch);
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
                    if (rc != null) Marshal.ReleaseComObject(rc);
                    if (ac != null) Marshal.ReleaseComObject(ac);
                    testing = false;
                }
            });
        }

        // One worker owns every capture client: a wedged WASAPI call freezes the
        // meters, never the window. UI reads only the snapshot under snapLock.
        void CaptureLoop() {
            while (true) {
                try {
                    if (needReopen) {
                        needReopen = false;
                        DoReopen();
                    }
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

        void DoReopen() {
            if (cin != null) Marshal.ReleaseComObject(cin);
            if (cout != null) Marshal.ReleaseComObject(cout);
            if (cloop != null) Marshal.ReleaseComObject(cloop);
            cin = null; cout = null; cloop = null;
            string name = "";
            bool m = false;
            float lv = 1f;
            try {
                cin = Wasapi.OpenCapture(EDataFlow.eCapture, out chIn);
                cout = Wasapi.OpenCapture(EDataFlow.eRender, out chOut);
                cloop = Wasapi.OpenLoopback(out chLoop, out _, out name);
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
        }

        // Session + mute enumeration runs on a worker — a wedged endpoint must not freeze the UI.
        bool sessBusy;
        void RefreshSessions() {
            if (sessBusy) return;
            sessBusy = true;
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
            ToolTipService.SetToolTip(btn, "mute this app on the stream only — you still hear it locally");
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
        }

        static double Peak(double v, ref double pk, ref int hold) {
            if (v >= pk) { hold = 16; return v; }
            if (hold > 0) { hold--; return pk; }
            return Math.Max(v, pk - 0.6);
        }
    }
}
