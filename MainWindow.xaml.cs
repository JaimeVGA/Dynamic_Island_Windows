using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double CollapsedW = 130, CollapsedH = 34, CollapsedR = 17;
    private const double ExpandedW = 424, ExpandedH = 160, ExpandedR = 30;

    private readonly SpringAnimator _spring = new(0) { Stiffness = 380, Damping = 30, Mass = 1 };
    private readonly MediaService _media = new();
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private IntPtr _hwnd;
    private bool _expanded;
    private bool _animating;
    private TimeSpan _lastTick;

    public MainWindow()
    {
        InitializeComponent();
        Width = 460;
        Height = 200;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;

        Pill.MouseEnter += (_, _) => { _collapseDelay.Stop(); SetExpanded(true); };
        Pill.MouseLeave += (_, _) => { _collapseDelay.Stop(); _collapseDelay.Start(); };
        _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); if (!Pill.IsMouseOver) SetExpanded(false); };

        PrevBtn.Click += async (_, _) => await _media.PreviousAsync();
        PlayBtn.Click += async (_, _) => await _media.TogglePlayPauseAsync();
        NextBtn.Click += async (_, _) => await _media.NextAsync();

        _clockTimer.Tick += (_, _) => UpdateClock();
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _topmostTimer.Tick += (_, _) => Win32.ReassertTopmost(_hwnd);
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
        ApplyPill(0);
        UpdateClock();
        _topmostTimer.Start();
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
                var local = Pill.PointFromScreen(new Point(x, y));
                bool inside = local.X >= 0 && local.Y >= 0 &&
                              local.X <= Pill.ActualWidth && local.Y <= Pill.ActualHeight;
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
        bool moving = _spring.Update(dt);
        ApplyPill(_spring.Value);
        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
    }

    private void ApplyPill(double p)
    {
        Pill.Width = Math.Max(CollapsedW, CollapsedW + (ExpandedW - CollapsedW) * p);
        Pill.Height = Math.Max(CollapsedH, CollapsedH + (ExpandedH - CollapsedH) * p);
        double r = Math.Clamp(CollapsedR + (ExpandedR - CollapsedR) * p, CollapsedR, ExpandedR + 6);
        Pill.CornerRadius = new CornerRadius(r);

        double clamped = Math.Clamp(p, 0, 1);
        ExpandedView.Opacity = Math.Clamp((clamped - 0.45) / 0.55, 0, 1);
        CollapsedView.Opacity = Math.Clamp(1 - clamped * 2.2, 0, 1);
    }

    // ---------- Reloj sincronizado al minuto ----------
    private void UpdateClock()
    {
        var now = DateTime.Now;
        string text = now.ToString("h:mm tt", CultureInfo.InvariantCulture);
        CollapsedTime.Text = text;
        ExpandedTime.Text = text;
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
        PlayBtn.Content = _media.IsPlaying ? "\uE769" : "\uE768";
        PlayingDot.Visibility = _media.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        PrevBtn.IsEnabled = PlayBtn.IsEnabled = NextBtn.IsEnabled = has;
        UpdateProgress();
        UpdateProgressTimer();
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
