using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private sealed record SearchDestination(FrameworkElement Element, Action? Prepare);
    private readonly Dictionary<string, SearchDestination> searchTargets = [];
    private int searchNavigationVersion;
    private FrameworkElement? searchFocus;
    private bool searchFocusWasFocusable;
    private SearchTargetOutline? searchOutline;
    private AdornerLayer? searchOutlineLayer;
    private DispatcherTimer? searchOutlineTimer;
    internal string? CurrentSearchTarget { get; private set; }
    internal Task<bool> PendingSearchNavigation { get; private set; } = Task.FromResult(false);

    private T SearchTarget<T>(string id, T element, Action? prepare = null) where T : FrameworkElement
    {
        searchTargets.Add(id, new(element, prepare));
        return element;
    }

    private void OpenSearchResult(SettingsEntry entry)
    {
        if (entry.Id == "tools") PrepareToolSearch(settingsSearch?.Text);
        PendingSearchNavigation = OpenSearchTargetAsync(entry);
    }

    private async Task<bool> OpenSearchTargetAsync(SettingsEntry entry)
    {
        // 搜索跳转不做页面位移动画，防止布局尚未稳定时滚动到错误的位置。
        Navigate(entry.Page, animate: false);
        var version = searchNavigationVersion;
        if (!searchTargets.TryGetValue(entry.Id, out var destination)) return false;
        destination.Prepare?.Invoke();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        if (version != searchNavigationVersion || !IsVisible) return false;

        var target = destination.Element;
        searchFocus = target;
        searchFocusWasFocusable = target.Focusable;
        target.Focusable = true;
        target.LostKeyboardFocus += OnSearchTargetLostFocus;
        target.Focus();
        scroller.UpdateLayout();
        // 高于视口的折叠区也从标题开始显示；不调用默认的整块 BringIntoView。
        var top = target.TranslatePoint(new Point(), (UIElement)scroller.Content).Y;
        scroller.ScrollToVerticalOffset(Math.Max(0, top - 12));
        CurrentSearchTarget = entry.Id;
        searchOutlineLayer = AdornerLayer.GetAdornerLayer(target);
        if (searchOutlineLayer is not null)
        {
            searchOutline = new SearchTargetOutline(target);
            searchOutlineLayer.Add(searchOutline);
            searchOutline.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, Motion.Ms(600))
            { BeginTime = TimeSpan.FromMilliseconds(1600) });
            searchOutlineTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(2200) };
            searchOutlineTimer.Tick += OnSearchOutlineExpired;
            searchOutlineTimer.Start();
        }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        return version == searchNavigationVersion;
    }

    private void OnSearchTargetLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (searchFocus is not null && !searchFocus.IsKeyboardFocused) ReleaseSearchFocus();
    }

    private void ReleaseSearchFocus()
    {
        if (searchFocus is null) return;
        searchFocus.LostKeyboardFocus -= OnSearchTargetLostFocus;
        searchFocus.Focusable = searchFocusWasFocusable;
        searchFocus = null;
    }

    private void OnSearchOutlineExpired(object? sender, EventArgs e) => RemoveSearchOutline();

    private void RemoveSearchOutline()
    {
        if (searchOutlineTimer is not null)
        {
            searchOutlineTimer.Stop();
            searchOutlineTimer.Tick -= OnSearchOutlineExpired;
            searchOutlineTimer = null;
        }
        if (searchOutline is not null) searchOutlineLayer?.Remove(searchOutline);
        searchOutline = null;
        searchOutlineLayer = null;
    }

    private void ClearSearchTarget()
    {
        searchNavigationVersion++;
        CurrentSearchTarget = null;
        RemoveSearchOutline();
        ReleaseSearchFocus();
    }

    internal (double Top, double Height, double Viewport, bool Focused, bool Highlighted) SearchTargetPosition()
    {
        if (!app.IsDemo || CurrentSearchTarget is null || !searchTargets.TryGetValue(CurrentSearchTarget, out var target))
            return (double.NaN, 0, 0, false, false);
        return (target.Element.TranslatePoint(new Point(), scroller).Y, target.Element.ActualHeight, scroller.ViewportHeight,
            target.Element.IsKeyboardFocused, searchOutline is not null);
    }

    internal double SettingsScrollOffset => scroller.VerticalOffset;
    internal bool SelectedNavigationIsVisible
    {
        get
        {
            if (!app.IsDemo || navigationScroll is null) return false;
            var button = nav[current].Button;
            var top = button.TranslatePoint(new Point(), navigationScroll).Y;
            return top >= -1 && top + button.ActualHeight <= navigationScroll.ViewportHeight + 1;
        }
    }
    internal bool ApplicationRingEditorIsAvailable
    {
        get
        {
            if (!app.IsDemo || current != "ring" || applicationRingInput is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } input) return false;
            var center = input.TranslatePoint(new Point(input.ActualWidth / 2, input.ActualHeight / 2), this);
            var hit = InputHitTest(center) as DependencyObject;
            while (hit is not null)
            {
                if (ReferenceEquals(hit, input)) return true;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return false;
        }
    }
    internal void PreviewSettingsScroll(double offset)
    {
        if (app.IsDemo) scroller.ScrollToVerticalOffset(offset);
    }

    private sealed class SearchTargetOutline : Adorner
    {
        public SearchTargetOutline(FrameworkElement target) : base(target) => IsHitTestVisible = false;
        protected override void OnRender(DrawingContext context)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width <= 4 || size.Height <= 4) return;
            var brush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? SystemColors.HighlightBrush;
            context.DrawRoundedRectangle(null, new Pen(brush, 2), new System.Windows.Rect(1, 1, size.Width - 2, size.Height - 2), 7, 7);
        }
    }
}
