using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace WetheringWavesSteamHelper_WinUI;

public sealed partial class MainWindow
{
    private const double ReorderDragThreshold = 6;
    private NavigationViewItem? _reorderSource;
    private Pointer? _reorderPointer;
    private Point _reorderStart;
    private bool _isReordering;
    private List<string>? _reorderOriginalIds;

    private sealed record ReorderTarget(string? Id, bool After, Rect Bounds);

    private void InitializeGameReordering()
    {
        // NavigationViewItem handles pointer input internally. Listen to handled events as well
        // and capture at the pane, so its internal presenter cannot swallow the mouse gesture.
        NavView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(GameReorder_Pressed), true);
        NavView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(GameReorder_Moved), true);
        NavView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(GameReorder_Released), true);
        NavView.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(GameReorder_Canceled), true);
        NavView.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(GameReorder_CaptureLost), true);
        NavView.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GameReorder_KeyDown), true);
        NavView.Unloaded += (_, _) => CancelGameReordering();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                CancelGameReordering();
        };
    }

    private void GameReorder_Pressed(object sender, PointerRoutedEventArgs args)
    {
        if (!NavView.IsEnabled || args.Pointer.PointerDeviceType != PointerDeviceType.Mouse
            || !args.GetCurrentPoint(NavView).Properties.IsLeftButtonPressed) return;
        var element = args.OriginalSource as DependencyObject;
        while (element != null && element != NavView)
        {
            if (element is NavigationViewItem { Tag: CustomNavigationTag } item)
            {
                CancelGameReordering();
                // The internal presenter captures on press. CapturePointer on the pane fails
                // until that owner releases it, even when listening with handledEventsToo.
                var captureOwner = args.OriginalSource as DependencyObject;
                while (captureOwner != null && captureOwner != NavView)
                {
                    if (captureOwner is UIElement owner) owner.ReleasePointerCapture(args.Pointer);
                    captureOwner = VisualTreeHelper.GetParent(captureOwner);
                }
                _reorderSource = item;
                _reorderPointer = args.Pointer;
                _reorderStart = args.GetCurrentPoint(NavView).Position;
                _reorderOriginalIds = NavView.MenuItems.OfType<NavigationViewItem>()
                    .Select(i => i.Tag).OfType<CustomNavigationTag>().Select(t => t.Id).ToList();
                if (!NavView.CapturePointer(args.Pointer))
                {
                    CancelGameReordering();
                    return;
                }
                item.Focus(FocusState.Pointer);
                args.Handled = true;
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }
    }

    private void GameReorder_Moved(object sender, PointerRoutedEventArgs args)
    {
        if (_reorderSource == null || args.Pointer.PointerId != _reorderPointer?.PointerId) return;
        var point = args.GetCurrentPoint(NavView);
        if (!point.Properties.IsLeftButtonPressed)
        {
            CancelGameReordering();
            return;
        }
        if (!_isReordering && Math.Abs(point.Position.Y - _reorderStart.Y) < ReorderDragThreshold
            && Math.Abs(point.Position.X - _reorderStart.X) < ReorderDragThreshold) return;
        _isReordering = true;
        _reorderSource.Opacity = 0.55;
        UpdateReorderIndicator(FindReorderTarget(point.Position));
        args.Handled = true;
    }

    private async void GameReorder_Released(object sender, PointerRoutedEventArgs args)
    {
        if (_reorderSource == null || args.Pointer.PointerId != _reorderPointer?.PointerId) return;
        var source = _reorderSource;
        var sourceId = ((CustomNavigationTag)source.Tag).Id;
        var point = args.GetCurrentPoint(NavView).Position;
        var target = _isReordering ? FindReorderTarget(point) : null;
        var wasDragging = _isReordering;
        var originalIds = _reorderOriginalIds!;
        CancelGameReordering();
        args.Handled = true;

        if (!wasDragging)
        {
            // Capturing prevents the item's native click. Preserve ordinary click navigation.
            if (GetReorderBounds(source).Contains(point)) NavView.SelectedItem = source;
            return;
        }
        if (target == null || target.Id == sourceId) return;
        var order = originalIds.ToList();
        if (!order.Remove(sourceId)) return;
        var index = target.Id == null ? (target.After ? order.Count : 0) : order.IndexOf(target.Id);
        if (index < 0) return;
        if (target.Id != null && target.After) index++;
        order.Insert(index, sourceId);
        if (!_customManifestService.ReorderSidebarItems(order))
            await ShowInfoAsync("无法保存游戏顺序，列表可能已发生变化，请重试。");
    }

    private ReorderTarget? FindReorderTarget(Point point)
    {
        // Footer entries are never destinations. Bound checks also prevent drops in page content.
        foreach (var footer in NavView.FooterMenuItems.OfType<FrameworkElement>())
            if (GetReorderBounds(footer).Contains(point)) return null;
        foreach (var item in NavView.MenuItems.OfType<FrameworkElement>())
        {
            if (item.Visibility != Visibility.Visible || item.ActualWidth <= 0 || item.ActualHeight <= 0) continue;
            var bounds = GetReorderBounds(item);
            if (!bounds.Contains(point)) continue;
            if (item == CustomGamesHeader) return new(null, false, bounds);
            if (item == AddCustomNavItem) return new(null, true, bounds);
            if (item is NavigationViewItem { Tag: CustomNavigationTag tag })
                return new(tag.Id, point.Y >= bounds.Y + bounds.Height / 2, bounds);
        }
        return null;
    }

    private Rect GetReorderBounds(FrameworkElement element) => element.TransformToVisual(NavView)
        .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private void UpdateReorderIndicator(ReorderTarget? target)
    {
        if (target == null || target.Id == (_reorderSource?.Tag as CustomNavigationTag)?.Id)
        {
            GameReorderIndicator.Visibility = Visibility.Collapsed;
            return;
        }
        var y = target.Id == null
            ? (target.After ? target.Bounds.Top : target.Bounds.Bottom)
            : (target.After ? target.Bounds.Bottom : target.Bounds.Top);
        GameReorderIndicator.Width = Math.Max(0, target.Bounds.Width - 16);
        GameReorderIndicator.Margin = new Thickness(target.Bounds.Left + 8, Math.Max(0, y - 1.5), 0, 0);
        GameReorderIndicator.Visibility = Visibility.Visible;
    }

    private void GameReorder_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && _reorderSource != null)
        {
            CancelGameReordering();
            args.Handled = true;
        }
    }

    private void GameReorder_Canceled(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerId == _reorderPointer?.PointerId) CancelGameReordering();
    }

    private void GameReorder_CaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, NavView) && args.Pointer.PointerId == _reorderPointer?.PointerId)
            CancelGameReordering();
    }

    private void CancelGameReordering()
    {
        var pointer = _reorderPointer;
        if (_reorderSource != null) _reorderSource.Opacity = 1;
        _reorderSource = null;
        _reorderPointer = null;
        _reorderOriginalIds = null;
        _isReordering = false;
        GameReorderIndicator.Visibility = Visibility.Collapsed;
        if (pointer != null) NavView.ReleasePointerCapture(pointer);
    }
}
