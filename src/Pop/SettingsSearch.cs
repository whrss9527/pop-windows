using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private WpfUi.TextBox? settingsSearch;
    private bool clearingSearch;
    private double? beforeSearchOffset;
    private readonly List<WpfUi.Button> searchResults = [];

    private UIElement SearchInput()
    {
        settingsSearch = new WpfUi.TextBox { PlaceholderText = "搜索设置（Ctrl+F）", MaxLength = 200, FontSize = Theme.Caption, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(settingsSearch, "搜索设置");
        settingsSearch.TextChanged += (_, _) =>
        {
            if (clearingSearch) return;
            if (settingsSearch.Text.Trim().Length == 0)
            {
                var offset = beforeSearchOffset ?? 0;
                Navigate(current); scroller.ScrollToVerticalOffset(offset);
            }
            else ShowSearchResults(settingsSearch.Text);
        };
        return settingsSearch;
    }

    private bool SearchKey(KeyEventArgs e)
    {
        if (shortcutRecording || settingsSearch is null) return false;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            settingsSearch.Focus(); settingsSearch.SelectAll(); settingsSearch.BringIntoView(); return true;
        }
        if (e.Key == Key.Escape && settingsSearch.Text.Length > 0)
        {
            settingsSearch.Clear(); settingsSearch.Focus(); return true;
        }
        if (settingsSearch.IsKeyboardFocusWithin && searchResults.Count > 0)
        {
            if (e.Key == Key.Down) { searchResults[0].Focus(); return true; }
            if (e.Key == Key.Enter) return OpenFirstSearchResult();
        }
        return false;
    }

    private void ClearSearch()
    {
        clearingSearch = true;
        try { settingsSearch?.Clear(); }
        finally { clearingSearch = false; }
        searchResults.Clear();
        beforeSearchOffset = null;
    }

    private void ShowSearchResults(string query)
    {
        ClearSearchTarget();
        searchTargets.Clear();
        beforeSearchOffset ??= scroller.VerticalOffset;
        foreach (var (_, (button, pill, glyph)) in nav)
        {
            button.ClearValue(BackgroundProperty); pill.Visibility = Visibility.Hidden; glyph.Filled = false;
        }
        var matches = SettingsCatalog.Search(query);
        var page = new StackPanel { Margin = new Thickness(40, 12, 40, 40), MaxWidth = 1000 };
        page.Children.Add(Label("搜索设置", Theme.TitleSize, "TextFillColorPrimaryBrush", FontWeights.SemiBold, Theme.DisplayFont));
        page.Children.Add(Note(matches.Count == 0 ? "没有找到相关设置。试试「快捷键」「翻译」「剪贴板」或英文名称。" : $"找到 {matches.Count} 个相关入口 · Enter 打开首项，Esc 清空搜索"));
        searchResults.Clear();
        foreach (var entry in matches)
        {
            var target = Pages.First(p => p.Id == entry.Page);
            var button = ActionButton("定位", null, () => OpenSearchResult(entry));
            System.Windows.Automation.AutomationProperties.SetName(button, "定位到" + entry.Title);
            searchResults.Add(button);
            button.PreviewKeyDown += (_, e) =>
            {
                var index = searchResults.IndexOf(button);
                if (e.Key == Key.Down && index + 1 < searchResults.Count) { searchResults[index + 1].Focus(); e.Handled = true; }
                if (e.Key == Key.Up) { if (index > 0) searchResults[index - 1].Focus(); else settingsSearch?.Focus(); e.Handled = true; }
            };
            page.Children.Add(Card(target.Icon, entry.Title, $"{target.Title} · {entry.Description}", button));
        }
        scroller.Content = page; scroller.ScrollToTop();
    }

    internal void PreviewSettingsSearch(string query)
    {
        if (app.IsDemo && settingsSearch is not null) settingsSearch.Text = query;
    }

    internal string CurrentPage => current;
    internal bool SettingsSearchFocused => settingsSearch?.IsKeyboardFocusWithin == true;
    internal int FocusedSearchResult => searchResults.FindIndex(button => button.IsKeyboardFocused);

    internal bool OpenFirstSearchResult()
    {
        if (searchResults.Count == 0) return false;
        searchResults[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return true;
    }
}
