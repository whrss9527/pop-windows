using System.Windows;
using System.Windows.Controls;
using Pop.Core;

namespace Pop;

internal sealed class TextDiffWindow : ToolWindow
{
    private readonly TextBox before, after, output;
    private readonly TextBlock status;
    private readonly CheckBox ignore;
    private readonly Button compare, copy;
    private long version;
    private bool closed;

    public TextDiffWindow(string original, string comparison) : base("Pop 文字对比", 1000, 720)
    {
        MinWidth = 720; MinHeight = 500; SettingsUi.FitToWorkArea(this);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        compare = new Button { Content = "比较", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        copy = new Button { Content = "复制差异", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };
        ignore = new CheckBox { Content = "忽略行首尾空白", VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(compare); toolbar.Children.Add(copy); toolbar.Children.Add(ignore); grid.Children.Add(toolbar);
        before = Editor(original); after = Editor(comparison); output = Editor(""); output.IsReadOnly = true;
        System.Windows.Automation.AutomationProperties.SetAutomationId(before, "diff-before");
        System.Windows.Automation.AutomationProperties.SetAutomationId(after, "diff-after");
        System.Windows.Automation.AutomationProperties.SetAutomationId(output, "diff-result");
        System.Windows.Automation.AutomationProperties.SetAutomationId(compare, "diff-compare");
        System.Windows.Automation.AutomationProperties.SetAutomationId(copy, "diff-copy");
        var inputs = new Grid(); inputs.ColumnDefinitions.Add(new ColumnDefinition()); inputs.ColumnDefinitions.Add(new ColumnDefinition());
        var a = Section("原文（唤起时选区）", before); a.Margin = new Thickness(0, 0, 6, 0); inputs.Children.Add(a);
        var b = Section("对照（唤起时剪贴板）", after); b.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(b, 1); inputs.Children.Add(b);
        Grid.SetRow(inputs, 1); grid.Children.Add(inputs);
        status = SettingsUi.Label("", Theme.Caption, "TextFillColorSecondaryBrush"); status.Margin = new Thickness(0, 10, 0, 6);
        Grid.SetRow(status, 2); grid.Children.Add(status); Grid.SetRow(output, 3); grid.Children.Add(output);
        before.TextChanged += (_, _) => Changed(); after.TextChanged += (_, _) => Changed();
        ignore.Checked += (_, _) => Changed(); ignore.Unchecked += (_, _) => Changed();
        compare.Click += async (_, _) => await CompareAsync();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(output.Text); status.Text = "已复制差异"; }
            catch (System.Runtime.InteropServices.COMException) { status.Text = "剪贴板忙，请重试"; }
        };
        Closed += (_, _) => { closed = true; version++; }; Loaded += async (_, _) => await CompareAsync();
        Body.Children.Add(grid);
    }
    private static TextBox Editor(string text) => new()
    {
        Text = text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(10), FontFamily = Theme.MonoFont, FontSize = Theme.Body, MaxLength = TextDiff.MaximumCharacters,
    };
    private static DockPanel Section(string title, TextBox editor)
    {
        var panel = new DockPanel();
        var label = SettingsUi.Label(title, Theme.Caption, "TextFillColorSecondaryBrush"); label.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label); panel.Children.Add(editor); return panel;
    }
    private void Changed() { version++; copy.IsEnabled = false; status.Text = "输入已修改，请重新比较"; }
    private async Task CompareAsync()
    {
        var request = ++version; var a = before.Text; var b = after.Text; var trim = ignore.IsChecked == true;
        compare.IsEnabled = false; copy.IsEnabled = false; status.Text = "正在比较…";
        try
        {
            var result = await Task.Run(() => TextDiff.Compare(a, b, trim));
            if (closed || request != version) return;
            output.Text = result.Unified(); copy.IsEnabled = true;
            status.Text = result.Added == 0 && result.Removed == 0 ? "两段文字相同" : $"+ {result.Added} 行新增 · − {result.Removed} 行删除 · 两个空格表示保留";
        }
        catch (ArgumentException e) { if (!closed && request == version) { output.Clear(); status.Text = e.Message; } }
        finally { if (!closed) compare.IsEnabled = true; }
    }
}
