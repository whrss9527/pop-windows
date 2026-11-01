using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pop.Core;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private string toolsQuery = "";
    private ToolStateFilter toolsFilter;
    private WpfUi.TextBox? toolsQueryBox;
    private Action? toolsStateChanged;
    private object? toolViewOwner;

    private void ClearToolView()
    {
        toolsQueryBox = null; toolsStateChanged = null; toolViewOwner = null;
    }

    private void PrepareToolSearch(string? query)
    {
        toolsQuery = !string.IsNullOrWhiteSpace(query) && ToolSettingsCatalog.Select(Settings, query).MatchingTotal > 0 ? query : "";
        toolsFilter = ToolStateFilter.All;
    }

    private void ActionsPage(StackPanel page)
    {
        page.Children.Add(Note("用不上的功能可以关掉：关掉的功能不出现在圆盘和「全部功能」列表里，圆盘上空出来的格子自动换成别的功能，选中内容时也不再直接出它的结果。"));
        PluginsSection(page);
        page.Children.Add(SearchTarget("tools", Section("内置功能")));

        var owner = new object(); toolViewOwner = owner;
        var query = new WpfUi.TextBox { Text = toolsQuery, PlaceholderText = "查找内置功能（名称、拼音或英文）", MaxLength = 200, MinWidth = 180 };
        toolsQueryBox = query;
        System.Windows.Automation.AutomationProperties.SetName(query, "筛选内置功能");
        System.Windows.Automation.AutomationProperties.SetAutomationId(query, "settings-tools-query");
        var filter = new ComboBox { MinWidth = 128, Margin = new Thickness(10, 0, 0, 0) };
        foreach (var name in new[] { "全部功能", "已打开", "已关闭" }) filter.Items.Add(name);
        filter.SelectedIndex = (int)toolsFilter;
        System.Windows.Automation.AutomationProperties.SetName(filter, "按功能状态筛选");
        System.Windows.Automation.AutomationProperties.SetAutomationId(filter, "settings-tools-filter");
        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(query); Grid.SetColumn(filter, 1); searchRow.Children.Add(filter); page.Children.Add(searchRow);

        var countText = Label("", Theme.Body, "TextFillColorPrimaryBrush");
        var visibleText = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        visibleText.Margin = new Thickness(0, 3, 0, 0);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; names.Children.Add(countText); names.Children.Add(visibleText);
        var enable = ActionButton("全部打开", Symbol.CheckmarkCircle24, () =>
        {
            if (ToolSettingsCatalog.Select(Settings, toolsQuery, toolsFilter).EnableIds.Count == 0) return;
            Update(s => ToolSettingsCatalog.EnableVisible(s, toolsQuery, toolsFilter));
            toolsStateChanged?.Invoke();
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(enable, "settings-tools-enable-all");
        var summaryRow = new Grid { Margin = new Thickness(16) };
        summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summaryRow.ColumnDefinitions.Add(new ColumnDefinition());
        summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new WpfUi.SymbolIcon { Symbol = Symbol.Apps24, FontSize = 20, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush"); summaryRow.Children.Add(icon);
        Grid.SetColumn(names, 1); summaryRow.Children.Add(names);
        enable.Margin = new Thickness(18, 0, 0, 0); enable.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(enable, 2); summaryRow.Children.Add(enable);
        var summary = CardBorder(summaryRow);
        System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "settings-tools-summary"); page.Children.Add(summary);

        var list = new StackPanel(); page.Children.Add(list);
        string[]? shownIds = null;
        var toggles = new List<WpfUi.ToggleSwitch>();
        void Refresh()
        {
            if (!ReferenceEquals(toolViewOwner, owner)) return;
            var view = ToolSettingsCatalog.Select(Settings, toolsQuery, toolsFilter);
            countText.Text = $"已打开 {view.Enabled} 个内置功能，共 {view.Total} 个";
            visibleText.Text = $"当前显示 {view.Items.Count} 项";
            enable.Content = toolsQuery.Trim().Length == 0 && toolsFilter == ToolStateFilter.All ? "全部打开" : "打开匹配项";
            enable.IsEnabled = view.EnableIds.Count > 0;
            enable.ToolTip = view.EnableIds.Count == 0 ? "当前匹配项均已打开" : $"打开当前筛选中的 {view.EnableIds.Count} 个内置功能，其他功能和插件保持原设置";
            var ids = view.Items.Select(a => a.Id).ToArray();
            if (shownIds is not null && shownIds.SequenceEqual(ids)) return;
            var focusWasInList = toggles.Any(t => t.IsKeyboardFocusWithin);
            var offset = scroller.VerticalOffset;
            shownIds = ids; list.Children.Clear(); toggles.Clear();
            foreach (var category in Actions.Categories.Where(c => c != Actions.PluginCategory))
            {
                var actions = view.Items.Where(a => a.Category == category).ToList();
                if (actions.Count == 0) continue;
                list.Children.Add(Section(category));
                var rows = new List<UIElement>();
                foreach (var action in actions)
                {
                    var toggle = EnableToggle(action.Id); toggle.IsEnabled = Actions.CanDisable(action); toggles.Add(toggle);
                    var description = toggle.IsEnabled ? action.Summary : $"{action.Summary}。一直打开，其他功能都能从这里找到";
                    rows.Add(Row(Icons.Parse(action.Glyph), action.Title, description, toggle));
                }
                list.Children.Add(Group(null, rows));
            }
            if (view.Items.Count == 0)
                list.Children.Add(Note(view.MatchingTotal == 0 ? "没有匹配的内置功能。试试名称、拼音或英文，或清空搜索。"
                    : toolsFilter == ToolStateFilter.Disabled ? "匹配的功能已全部打开。" : "匹配的功能尚未打开，可切换到「已关闭」查看。"));
            scroller.UpdateLayout(); scroller.ScrollToVerticalOffset(offset);
            if (focusWasInList) Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(toolViewOwner, owner) && IsVisible) query.Focus();
            });
        }
        toolsStateChanged = Refresh;
        query.TextChanged += (_, _) => { toolsQuery = query.Text; Refresh(); };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedIndex < 0) return;
            toolsFilter = (ToolStateFilter)filter.SelectedIndex; Refresh();
        };
        Refresh();
    }

    private WpfUi.ToggleSwitch EnableToggle(string id)
    {
        var toggle = Toggle(id == Actions.All.Id || Settings.IsEnabled(id), value =>
        {
            if (id == Actions.All.Id) return;
            Update(s =>
            {
                s.DisabledActions.RemoveAll(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
                if (!value) s.DisabledActions.Add(id);
            });
            toolsStateChanged?.Invoke();
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "settings-action-toggle-" + id);
        return toggle;
    }
}
