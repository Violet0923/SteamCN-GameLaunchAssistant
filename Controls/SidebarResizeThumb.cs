using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WetheringWavesSteamHelper_WinUI.Controls;

/// <summary>侧栏分隔条：捕获指针并报告水平位移，不参与导航项的拖动排序。</summary>
public sealed class SidebarResizeThumb : Control
{
    private Pointer? _pointer;
    private double _lastX;
    public event EventHandler<double>? DragDelta;
    public event EventHandler? DragCompleted;

    public SidebarResizeThumb()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        PointerCaptureLost += (_, _) => FinishDrag();
        PointerCanceled += (_, _) => FinishDrag();
        Unloaded += (_, _) => FinishDrag();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null || (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
        if (!CapturePointer(e.Pointer)) return;
        _pointer = e.Pointer;
        _lastX = e.GetCurrentPoint(XamlRoot.Content).Position.X;
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        // 分隔条本身会移动；使用窗口根坐标，避免位移反馈造成抖动。
        var point = e.GetCurrentPoint(XamlRoot.Content);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            FinishDrag();
            return;
        }
        var delta = point.Position.X - _lastX;
        _lastX = point.Position.X;
        DragDelta?.Invoke(this, delta);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        FinishDrag();
        e.Handled = true;
    }

    private void FinishDrag()
    {
        if (_pointer == null) return;
        var pointer = _pointer;
        // 先清除状态，再释放捕获，避免捕获丢失事件重入而重复保存。
        _pointer = null;
        ReleasePointerCapture(pointer);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        Opacity = 1;
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        if (_pointer == null) Opacity = 0.35;
    }
}
