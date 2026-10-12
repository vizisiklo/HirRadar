using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HirRadar;

/// <summary>
/// Apró vizuális, rugós követési effektus a főablak tartalmán a natív Windows ablakmozgatásakor.
/// A valódi ablak koordinátáit, méretét és a Windows snap/maximalizálás működését nem módosítja.
/// </summary>
internal sealed class ElasticWindowDrag : IDisposable
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmMoving = 0x0216;
    private const int WmExitSizeMove = 0x0232;
    private const double MaxOffset = 11.0;

    private readonly Window _window;
    private readonly TranslateTransform _offset = new();
    private HwndSource? _source;
    private bool _moveSizeActive;
    private bool _receivedMoving;
    private int _lastLeft;
    private int _lastTop;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public ElasticWindowDrag(Window window, UIElement content)
    {
        _window = window;
        content.RenderTransform = _offset;
        window.SourceInitialized += Window_SourceInitialized;
        window.Closed += Window_Closed;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _source = PresentationSource.FromVisual(_window) as HwndSource;
        _source?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Csak megfigyeljük a natív ablakmozgatást, egyetlen üzenetet sem nyelünk el.
        switch (msg)
        {
            case WmEnterSizeMove:
                _moveSizeActive = true;
                _receivedMoving = false;
                _offset.BeginAnimation(TranslateTransform.XProperty, null);
                _offset.BeginAnimation(TranslateTransform.YProperty, null);
                _offset.X = 0;
                _offset.Y = 0;
                break;

            case WmMoving when _moveSizeActive && lParam != IntPtr.Zero:
                var rect = Marshal.PtrToStructure<WindowRect>(lParam);
                if (_receivedMoving)
                {
                    UpdateOffset(rect.Left - _lastLeft, rect.Top - _lastTop);
                }
                _lastLeft = rect.Left;
                _lastTop = rect.Top;
                _receivedMoving = true;
                break;

            case WmExitSizeMove:
                if (_moveSizeActive && _receivedMoving)
                    SpringBack();
                _moveSizeActive = false;
                _receivedMoving = false;
                break;
        }
        return IntPtr.Zero;
    }

    private void UpdateOffset(int dx, int dy)
    {
        // A tartalom picit lemarad az ablak mozgatási irányától – ez csak képi effekt.
        if (dx != 0)
        {
            double targetX = -Math.Sign(dx) * Math.Min(MaxOffset, Math.Abs(dx) * 1.55 + 2.0);
            _offset.X = Math.Clamp(_offset.X * 0.38 + targetX * 0.62, -MaxOffset, MaxOffset);
        }
        if (dy != 0)
        {
            double targetY = -Math.Sign(dy) * Math.Min(MaxOffset, Math.Abs(dy) * 1.55 + 2.0);
            _offset.Y = Math.Clamp(_offset.Y * 0.38 + targetY * 0.62, -MaxOffset, MaxOffset);
        }
    }

    private void SpringBack()
    {
        var fromX = _offset.X;
        var fromY = _offset.Y;
        _offset.X = 0;
        _offset.Y = 0;

        // Finom visszarugózás az elengedés után, minimális túllendüléssel.
        var ease = new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut };
        _offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = fromX, To = 0, Duration = TimeSpan.FromMilliseconds(390),
            EasingFunction = ease, FillBehavior = FillBehavior.Stop
        });
        _offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = fromY, To = 0, Duration = TimeSpan.FromMilliseconds(390),
            EasingFunction = ease, FillBehavior = FillBehavior.Stop
        });
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source?.RemoveHook(WindowMessageHook);
        _window.SourceInitialized -= Window_SourceInitialized;
        _window.Closed -= Window_Closed;
    }
}
