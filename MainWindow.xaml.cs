using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double ExpandedW = 424, ExpandedH = 160, ExpandedR = 30, ExpandedTop = 6;
    private (double w, double h, double r, double top) _fromDims = (130, 34, 17, 6), _toDims = (130, 34, 17, 6);
    private bool _modeInitialized;
    private readonly SpringAnimator _modeSpring = new(1) { Stiffness = 260, Damping = 30, Mass = 1 };
    private string _shownTrack = "";
    private readonly Settings _settings;

    private readonly SpringAnimator _spring = new(0) { Stiffness = 380, Damping = 30, Mass = 1 };
    private readonly MediaService _media = new();
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private IntPtr _hwnd;
    private IslandMode _mode = IslandMode.Classic;
    private bool _hasMaximized;
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _expanded;
    private bool _animating;
    private TimeSpan _lastTick;

    public MainWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();
        Width = 460;
        Height = 200;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;

        Hover.MouseEnter += (_, _) => { _collapseDelay.Stop(); SetExpanded(true); };
        Hover.MouseLeave += (_, _) => { _collapseDelay.Stop(); _collapseDelay.Start(); };
        _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); if (!Hover.IsMouseOver) SetExpanded(false); };
        _settings.Changed += () => { ApplyMode(); UpdateClock(); };

        PrevBtn.Click += async (_, _) => await _media.PreviousAsync();
        PlayBtn.Click += async (_, _) => await _media.TogglePlayPauseAsync();
        NextBtn.Click += async (_, _) => await _media.NextAsync();
        SettingsBtn.Click += (_, _) => SettingsWindow.ShowSingle(_settings);

        _clockTimer.Tick += (_, _) => UpdateClock();
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _topmostTimer.Tick += (_, _) => Win32.ReassertTopmost(_hwnd);
        _foregroundTimer.Tick += (_, _) => PollForeground();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        PositionWindow();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyMode();
        UpdateClock();
        _topmostTimer.Start();
        _foregroundTimer.Start();
        _media.Changed += OnMediaChanged;
        try { await _media.InitializeAsync(); } catch { /* GSMTC no disponible */ }
        OnMediaChanged();
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea; // monitor principal
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        _ = area;
    }

    // ---------- Win32: sin foco + click-through fuera de la píldora ----------
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                handled = true;
                return (IntPtr)Win32.MA_NOACTIVATE;

            case Win32.WM_NCHITTEST:
                long l = lParam.ToInt64();
                int x = (short)(l & 0xFFFF);
                int y = (short)((l >> 16) & 0xFFFF);
                var local = Hover.PointFromScreen(new Point(x, y));
                bool inside = local.X >= 0 && local.Y >= 0 &&
                              local.X <= Hover.ActualWidth && local.Y <= Hover.ActualHeight;
                handled = true;
                return (IntPtr)(inside ? Win32.HTCLIENT : Win32.HTTRANSPARENT);
        }
        return IntPtr.Zero;
    }

    // ---------- Animación spring ----------
    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        _spring.SetTarget(expanded ? 1 : 0);
        ExpandedView.IsHitTestVisible = expanded;
        if (expanded) { UpdateProgress(); }
        UpdateProgressTimer();
        StartAnimating();
    }

    private void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastTick == TimeSpan.Zero ? 1.0 / 60 : (now - _lastTick).TotalSeconds;
        _lastTick = now;
        bool movingExpand = _spring.Update(dt);
        bool movingMode = _modeSpring.Update(dt);
        ApplyPill(_spring.Value);
        if (!movingExpand && !movingMode)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
    }

    private void PollForeground()
    {
        if (!_settings.AutoSwitch) { if (_hasMaximized) { _hasMaximized = false; ApplyMode(); } return; }
        bool? result = Win32.ForegroundIsMaximizedOnPrimary((uint)Environment.ProcessId);
        if (result is bool maximized && maximized != _hasMaximized)
        {
            _hasMaximized = maximized;
            ApplyMode();
        }
    }

    private static (double w, double h, double r, double top) DimsFor(IslandMode mode) => mode switch
    {
        IslandMode.Bar => (110d, 5d, 2.5d, 2d),
        IslandMode.Wide => (300d, 38d, 19d, 6d),
        _ => (130d, 34d, 17d, 6d)
    };

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Dimensiones colapsadas actuales, a mitad de camino durante una transición de modo.</summary>
    private (double w, double h, double r, double top) CurrentCollapsed()
    {
        double t = _modeSpring.Value;
        return (Lerp(_fromDims.w, _toDims.w, t), Lerp(_fromDims.h, _toDims.h, t),
                Lerp(_fromDims.r, _toDims.r, t), Lerp(_fromDims.top, _toDims.top, t));
    }

    private void ApplyMode()
    {
        var newMode = _settings.EffectiveMode(_media.IsPlaying, _hasMaximized);
        bool first = !_modeInitialized;
        if (!first && newMode == _mode) return;

        var from = first ? DimsFor(newMode) : CurrentCollapsed();
        _mode = newMode;
        _fromDims = from;
        _toDims = DimsFor(newMode);
        _modeInitialized = true;

        CollapsedView.Visibility = _mode == IslandMode.Classic ? Visibility.Visible : Visibility.Collapsed;
        WideView.Visibility = _mode == IslandMode.Wide ? Visibility.Visible : Visibility.Collapsed;

        if (first)
        {
            _modeSpring.Snap(1);
            ApplyPill(_spring.Value);
        }
        else
        {
            _modeSpring.Snap(0);
            _modeSpring.SetTarget(1);
            StartAnimating();
        }
    }

    private void ApplyPill(double p)
    {
        var (collW, collH, collR, collTop) = CurrentCollapsed();
        double c = Math.Clamp(p, 0, 1);
        double top = Lerp(collTop, ExpandedTop, c);
        double w = Math.Max(Math.Max(collW, 2), Lerp(collW, ExpandedW, p));
        double h = Math.Max(Math.Max(collH, 2), Lerp(collH, ExpandedH, p));

        Pill.Width = w;
        Pill.Height = h;
        Pill.Margin = new Thickness(0, top, 0, 0);
        Pill.CornerRadius = new CornerRadius(Math.Clamp(Lerp(collR, ExpandedR, p), Math.Min(collR, ExpandedR), ExpandedR + 6));

        // Zona de hover algo más grande que la píldora (cómoda en modo barra).
        Hover.Width = Math.Max(w + 16, _mode == IslandMode.Bar ? 150 : 0);
        Hover.Height = Math.Max(top + h + 8, _mode == IslandMode.Bar ? 20 : 0);

        double modeFade = Math.Clamp((_modeSpring.Value - 0.25) / 0.75, 0, 1);
        ExpandedView.Opacity = Math.Clamp((c - 0.45) / 0.55, 0, 1);
        CollapsedHost.Opacity = _mode == IslandMode.Bar ? 0 : Math.Clamp(1 - c * 2.2, 0, 1) * modeFade;
    }

    // ---------- Reloj sincronizado al minuto ----------
    private void UpdateClock()
    {
        var now = DateTime.Now;
        string text = now.ToString(_settings.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
        CollapsedTime.Text = text;
        ExpandedTime.Text = text;
        WideTime.Text = text;
        var next = now.AddMinutes(1);
        next = new DateTime(next.Year, next.Month, next.Day, next.Hour, next.Minute, 0);
        _clockTimer.Stop();
        _clockTimer.Interval = next - now + TimeSpan.FromMilliseconds(50);
        _clockTimer.Start();
    }

    // ---------- Datos de medios ----------
    private void OnMediaChanged()
    {
        bool has = _media.HasSession && !string.IsNullOrEmpty(_media.Title);
        TitleText.Text = has ? _media.Title : "Nada en reproducción";
        ArtistText.Text = has ? _media.Artist : "";
        Cover.Source = _media.Thumbnail;
        WideCover.Source = _media.Thumbnail;
        WideTitle.Text = has ? (string.IsNullOrEmpty(_media.Artist) ? _media.Title : _media.Title + " · " + _media.Artist) : "";
        PlayBtn.Content = _media.IsPlaying ? "\uE769" : "\uE768";
        PlayingDot.Visibility = _media.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        PrevBtn.IsEnabled = PlayBtn.IsEnabled = NextBtn.IsEnabled = has;
        string trackKey = _media.Title + "|" + _media.Artist;
        if (trackKey != _shownTrack)
        {
            bool hadTrack = _shownTrack.Length > 1;
            _shownTrack = trackKey;
            if (hadTrack) FadeInTrackInfo();
        }
        if (_settings.EffectiveMode(_media.IsPlaying, _hasMaximized) != _mode) ApplyMode();
        UpdateProgress();
        UpdateProgressTimer();
    }

    private void FadeInTrackInfo()
    {
        foreach (UIElement el in new UIElement[] { Cover, TitleText, ArtistText, WideCover, WideTitle })
        {
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void UpdateProgressTimer()
    {
        if (_expanded && _media.IsPlaying) _progressTimer.Start();
        else _progressTimer.Stop();
    }

    private void UpdateProgress()
    {
        var pos = _media.CurrentPosition();
        var dur = _media.Duration;
        PosText.Text = Format(pos);
        DurText.Text = Format(dur);
        double trackWidth = Math.Max(0, ExpandedView.ActualWidth - 32 - 68 - 8);
        double ratio = dur.TotalSeconds > 0 ? Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1) : 0;
        ProgressFill.Width = trackWidth * ratio;
    }

    private static string Format(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
