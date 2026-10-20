using System.Windows;
using System.Windows.Controls;
using Pop.Core;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private void SnippetsPage(StackPanel page)
    {
        page.Children.Add(Note("在圆盘或全部功能里打开「常用短语」，选择后粘贴到原 App；也可在快捷键页为它录入组合键。短语会随设置一起备份。"));
        page.Children.Add(ActionButton("添加短语", Symbol.Add24, () => EditSnippet(null)));
        page.Children.Add(Section("已保存的短语"));
        if (Settings.Snippets.Count == 0) page.Children.Add(Note("还没有短语。可以保存常用回复、邮件结尾或带日期的模板。"));
        for (var i = 0; i < Settings.Snippets.Count; i++)
        {
            var index = i; var snippet = Settings.Snippets[i];
            var row = new WrapPanel();
            row.Children.Add(ActionButton("编辑", null, () => EditSnippet(snippet)));
            var up = ActionButton("↑", null, () => MoveSnippet(index, -1)); up.IsEnabled = i > 0; row.Children.Add(up);
            var down = ActionButton("↓", null, () => MoveSnippet(index, 1)); down.IsEnabled = i < Settings.Snippets.Count - 1; row.Children.Add(down);
            row.Children.Add(ActionButton("删除", null, async () =>
            {
                var confirm = new Wpf.Ui.Controls.MessageBox { Owner = this, Title = "删除短语？", Content = snippet.DisplayTitle,
                    PrimaryButtonText = "删除", CloseButtonText = "取消", FontFamily = Theme.TextFont };
                if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
                Update(s => s.Snippets.RemoveAll(v => v.Id == snippet.Id)); ShowPage(false);
            }));
            page.Children.Add(Card(Symbol.TextBulletList24, snippet.DisplayTitle, ShortPreview(snippet.Text), row));
        }
        page.Children.Add(Section("模板占位符"));
        page.Children.Add(Note("{date} 日期 · {time} 时间 · {datetime} 日期和时间 · {weekday} 星期\n{clipboard} 唤起时的剪贴板文字 · {selection} 唤起时的原始选区\n占位符只展开一次，剪贴板和选区中的花括号保持原样。"));
    }
    private static string ShortPreview(string text)
    {
        var line = text.Replace("\r", "").Replace("\n", " ↵ ");
        return line.Length > 70 ? line[..70] + "…" : line;
    }
    private void MoveSnippet(int index, int delta)
    {
        Update(s => (s.Snippets[index], s.Snippets[index + delta]) = (s.Snippets[index + delta], s.Snippets[index])); ShowPage(false);
    }
    private void EditSnippet(Snippet? original) => BuildSnippetEditor(original).ShowDialog();
    public Window PreviewSnippetEditor()
    {
        var editor = BuildSnippetEditor(Snippet.Examples[1]); editor.Show(); return editor;
    }
    private ToolWindow BuildSnippetEditor(Snippet? original)
    {
        var editor = new ToolWindow(original is null ? "添加常用短语" : "编辑常用短语", 720, 520, closeOnEscape: false) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var fields = new StackPanel();
        fields.Children.Add(Label("标题", Theme.Body, "TextFillColorPrimaryBrush"));
        var title = new TextBox { Text = original?.Title ?? "", MaxLength = 200, Padding = new Thickness(10), Margin = new Thickness(0, 6, 0, 12) }; fields.Children.Add(title);
        fields.Children.Add(Label("内容", Theme.Body, "TextFillColorPrimaryBrush"));
        var text = new TextBox { Text = original?.Text ?? "", MaxLength = 100_000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 180, Padding = new Thickness(10), Margin = new Thickness(0, 6, 0, 10) }; fields.Children.Add(text);
        fields.Children.Add(Note("支持 {date}、{time}、{datetime}、{weekday}、{clipboard}、{selection}。标题留空时使用内容的第一行。"));
        var problem = Label("", Theme.Caption, "TextFillColorSecondaryBrush"); fields.Children.Add(problem);
        var save = ActionButton("保存", null, () =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { problem.Text = "请填写短语内容"; return; }
            if (original is null && Settings.Snippets.Count >= 1000) { problem.Text = "最多保存 1000 条短语"; return; }
            if (Settings.Snippets.Where(s => s.Id != original?.Id).Sum(s => s.Text.Length) + text.Text.Length > 2_000_000)
            { problem.Text = "短语内容合计最多支持 200 万个字符"; return; }
            var snippet = new Snippet(original?.Id ?? Guid.NewGuid().ToString("N"), title.Text.Trim(), text.Text);
            Update(s => { var index = s.Snippets.FindIndex(v => v.Id == snippet.Id); if (index < 0) s.Snippets.Add(snippet); else s.Snippets[index] = snippet; });
            editor.Close(); ShowPage(false);
        });
        buttons.Children.Add(ActionButton("取消", null, editor.Close)); buttons.Children.Add(save);
        panel.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        editor.BodyContent(panel); return editor;
    }
}
