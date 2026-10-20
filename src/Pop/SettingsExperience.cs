using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pop.Core;
using Symbol = Wpf.Ui.Controls.SymbolRegular;
using static Pop.SettingsUi;

namespace Pop;

internal sealed partial class SettingsWindow
{
    private bool shortcutRecording;
    private string? ringApplication;
    private string shortcutQuery = "";

    public void EditApplicationRing(string application)
    {
        ringApplication = AppSettings.ApplicationName(application);
        Navigate("ring");
    }

    private IReadOnlyList<PopAction> CurrentRing(AppSettings settings) =>
        RingItems.Build(settings.SlotsFor(ringApplication ?? ""), settings.IsEnabled);

    private void SetCurrentRing(AppSettings settings, List<string> slots)
    {
        if (ringApplication is null) settings.RingSlots = slots;
        else settings.AppRings[ringApplication] = slots;
    }

    private void AddRingScope(StackPanel page)
    {
        if (ringApplication is not null && !Settings.AppRings.ContainsKey(ringApplication)) ringApplication = null;
        var names = new[] { "" }.Concat(Settings.AppRings.Keys.Order(StringComparer.OrdinalIgnoreCase)).ToArray();
        var scope = new ComboBox { MinWidth = 180 };
        foreach (var name in names) scope.Items.Add(name.Length == 0 ? "默认圆盘" : name + ".exe");
        scope.SelectedIndex = Array.IndexOf(names, ringApplication ?? "");
        scope.SelectionChanged += (_, _) =>
        {
            if (scope.SelectedIndex < 0) return;
            ringApplication = scope.SelectedIndex == 0 ? null : names[scope.SelectedIndex];
            ShowPage(false);
        };
        page.Children.Add(Card(Symbol.Apps24, "正在编辑", "唤起时自动使用当前应用的布局；其他应用继承默认圆盘", scope));
        var management = new StackPanel();
        var appName = new TextBox { Width = 140, ToolTip = "程序名，例如 Code.exe", VerticalContentAlignment = VerticalAlignment.Center };
        var message = Label("", Theme.Caption, "TextFillColorSecondaryBrush");
        void Add(string input)
        {
            var name = AppSettings.ApplicationName(input);
            if (name.Length is 0 or > 128 || name.Any(c => !char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.' or ' ')))
            { message.Text = "请输入程序名，例如 Code.exe"; return; }
            Update(s => { if (!s.AppRings.ContainsKey(name)) s.AppRings[name] = [.. s.RingSlots]; });
            ringApplication = name; ShowPage(false);
        }
        var add = ActionButton("添加", Symbol.Add24, () => Add(appName.Text));
        add.Margin = new Thickness(6, 0, 0, 0);
        var browse = ActionButton("选择程序…", null, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Windows 程序|*.exe", Title = "选择要单独配置圆盘的应用" };
            if (dialog.ShowDialog(this) == true) Add(dialog.FileName);
        });
        browse.Margin = new Thickness(6, 0, 0, 0);
        var addContent = new StackPanel { Margin = new Thickness(16) };
        addContent.Children.Add(Label("为应用添加圆盘", Theme.Body, "TextFillColorPrimaryBrush"));
        addContent.Children.Add(Label("程序名，例如 Code.exe", Theme.Caption, "TextFillColorSecondaryBrush"));
        var addRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        addRow.Children.Add(appName); addRow.Children.Add(add); addRow.Children.Add(browse);
        addContent.Children.Add(addRow);
        management.Children.Add(CardBorder(addContent));
        management.Children.Add(message);
        if (ringApplication is not null)
            management.Children.Add(Card(Symbol.ArrowReset24, "改用默认圆盘", "删除当前应用的独立布局，其他应用不受影响",
                ActionButton("使用默认", null, () =>
                {
                    var name = ringApplication; Update(s => s.AppRings.Remove(name)); ringApplication = null; ShowPage(false);
                })));
        page.Children.Add(new Expander { Header = "管理应用圆盘", Content = management, Margin = new Thickness(0, 4, 0, 12) });
    }

    private void GeneralPage(StackPanel page)
    {
        page.Children.Add(Note("选择内容后唤起 Pop：能直接处理的显示结果，其他内容打开圆盘。所有改动立即生效。"));
        page.Children.Add(Card(Symbol.CursorClick24, "鼠标唤起", "暂停鼠标唤起时，已经设置的全局快捷键仍可使用",
            Toggle(Settings.Enabled, v => Update(s => s.Enabled = v))));
        var autoStart = false;
        try { autoStart = AutoStart.IsEnabled; } catch (System.Security.SecurityException) { }
        page.Children.Add(Card(Symbol.Rocket24, "开机时启动", "登录 Windows 后在后台运行",
            Toggle(autoStart, enabled =>
            {
                try { AutoStart.Set(enabled); }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Log.Error("设置开机启动失败", e); }
            })));
        page.Children.Add(Section("操作方式"));
        page.Children.Add(Card(Symbol.CursorClick24, "唤起", "长按右键、修饰键右键或中键，选择适合自己的操作方式",
            ActionButton("设置唤起", null, () => Navigate("triggers"))));
        page.Children.Add(Card(Symbol.DataPie24, "圆盘", "4–12 格，可为不同应用设置不同布局",
            ActionButton("配置圆盘", null, () => Navigate("ring"))));
        page.Children.Add(Card(Symbol.Flash24, "直达规则", "按内容类型决定直接执行哪个功能，也可以始终显示圆盘",
            ActionButton("编辑规则", null, () => Navigate("rules"))));
        page.Children.Add(Card(Symbol.Keyboard24, "快捷键", "唤起圆盘或直接对当前选中内容执行指定功能",
            ActionButton("设置快捷键", null, () => Navigate("shortcuts"))));
    }

    private void TriggersPage(StackPanel page)
    {
        var s = Settings;
        page.Children.Add(Note("长按模式保留短按右键菜单和右键拖动。修饰键右键、中键点一下唤起后，用鼠标点击或键盘选择功能。"));
        page.Children.Add(Card(Symbol.CursorClick24, "启用鼠标唤起", "关闭后仍可通过快捷键打开圆盘或运行功能",
            Toggle(s.Enabled, v => Update(x => x.Enabled = v))));
        var modes = new[] { (TriggerMode.LongPressRight, "长按右键"), (TriggerMode.ModifierRightClick, "修饰键 + 右键"), (TriggerMode.MiddleClick, "鼠标中键"), (TriggerMode.Disabled, "不用鼠标唤起") };
        var mode = new ComboBox { MinWidth = 180 };
        foreach (var (_, title) in modes) mode.Items.Add(title);
        mode.SelectedIndex = Array.FindIndex(modes, m => m.Item1 == s.TriggerMode);
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            Update(x => x.TriggerMode = modes[mode.SelectedIndex].Item1);
            Dispatcher.BeginInvoke(() => ShowPage(false));
        };
        page.Children.Add(Card(Symbol.CursorClick24, "唤起方式", null, mode));
        if (s.TriggerMode == TriggerMode.ModifierRightClick)
        {
            var modifiers = new[] { (TriggerModifier.Alt, "Alt"), (TriggerModifier.Control, "Ctrl"), (TriggerModifier.Shift, "Shift"), (TriggerModifier.Windows, "Win") };
            var modifier = new ComboBox { MinWidth = 160 };
            foreach (var (_, title) in modifiers) modifier.Items.Add(title);
            modifier.SelectedIndex = Array.FindIndex(modifiers, m => m.Item1 == s.TriggerModifier);
            modifier.SelectionChanged += (_, _) => { if (modifier.SelectedIndex >= 0) Update(x => x.TriggerModifier = modifiers[modifier.SelectedIndex].Item1); };
            page.Children.Add(Card(Symbol.Keyboard24, "按住哪个修饰键", "未按住时，右键照常交给原来的 App", modifier));
        }
        if (s.TriggerMode == TriggerMode.LongPressRight)
        {
            var holdValue = Label($"{s.HoldMilliseconds} 毫秒", Theme.Body, "TextFillColorSecondaryBrush");
            holdValue.Width = 90; holdValue.VerticalAlignment = VerticalAlignment.Center;
            var hold = new Slider { Minimum = AppSettings.MinHold, Maximum = AppSettings.MaxHold, TickFrequency = 50,
                IsSnapToTickEnabled = true, Value = s.HoldMilliseconds, Width = 180 };
            hold.ValueChanged += (_, e) => { holdValue.Text = $"{(int)e.NewValue} 毫秒"; Update(x => x.HoldMilliseconds = (int)e.NewValue); };
            page.Children.Add(Card(Symbol.Timer24, "长按时间", "比这个时间短的右键点击仍然弹出系统菜单", Horizontal(holdValue, hold)));
            page.Children.Add(Card(Symbol.Cursor24, "在圆心松开后保持圆盘", "划向功能再松开仍然执行；在圆心松开后，可以改用点击选择",
                Toggle(s.KeepRingOpen, v => Update(x => x.KeepRingOpen = v))));
        }
        page.Children.Add(Section("选择工具条"));
        page.Children.Add(Card(Symbol.Cursor24, "选中文字后显示工具条", "拖选、双击选词后显示常用功能；只读取辅助功能选区，不改动剪贴板。不支持的 App 仍可手动唤起",
            Toggle(s.SelectionToolbar, v => Update(x => x.SelectionToolbar = v))));
        page.Children.Add(Section("应用排除"));
        var excluded = new TextBox { Text = string.Join(", ", s.TriggerExcludedApps), MinHeight = 36,
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Padding = new Thickness(10), ToolTip = "程序名，以逗号或换行分隔，例如 Code.exe, notepad.exe" };
        System.Windows.Automation.AutomationProperties.SetName(excluded, "排除鼠标唤起的应用");
        excluded.LostKeyboardFocus += (_, _) => Update(x => x.TriggerExcludedApps = excluded.Text.Split([',', '，', ';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(AppSettings.ApplicationName).Where(n => n.Length > 0).Distinct().ToList());
        page.Children.Add(Note("这些程序不响应鼠标唤起，也不自动显示选择工具条；全局快捷键仍可使用。输入程序名，以逗号或换行分隔，移开焦点保存。"));
        page.Children.Add(excluded);
        page.Children.Add(Section("键盘唤起"));
        page.Children.Add(Card(Symbol.Keyboard24, "打开圆盘", "不会抢走原 App 的焦点，继续使用当前选中的内容",
            ShortcutEditor(KeyboardShortcut.RingTarget)));
    }

    private void RulesPage(StackPanel page)
    {
        page.Children.Add(Note("从上往下匹配，第一条已启用且能处理当前内容的规则直接运行。找不到适用规则时显示圆盘。关闭的功能和不适用的规则会跳过。"));
        page.Children.Add(Card(Symbol.Flash24, "直接出结果", "关闭后所有内容都先显示圆盘",
            Toggle(Settings.DirectResults, value => { Update(s => s.DirectResults = value); ShowPage(false); })));
        var rules = Settings.EffectiveRules.ToList();
        for (var i = 0; i < rules.Count; i++)
        {
            var index = i; var rule = rules[i];
            var kind = DirectResults.KindNamed(rule.Kind)!.Value;
            var title = DirectResults.Kinds.First(k => k.Name == rule.Kind).Title.Replace("（直接翻译）", "");
            var choices = Actions.List.Where(a => a.Category == Actions.PluginCategory || a.OptionalInput
                || a.Requires != ContentKind.None && (a.Requires & (kind | ContentKind.Text)) != 0).ToList();
            var box = new ComboBox { MinWidth = 150, MaxWidth = 190, MaxDropDownHeight = 320 };
            foreach (var action in choices) box.Items.Add(action.Title + (Settings.IsEnabled(action.Id) ? "" : "（已关闭）"));
            var selected = choices.FindIndex(a => a.Id == rule.ActionId);
            if (selected < 0) { box.Items.Add($"{rule.ActionId}（未安装）"); selected = box.Items.Count - 1; }
            box.SelectedIndex = selected;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0 || box.SelectedIndex >= choices.Count) return;
                EditRule(index, r => r with { ActionId = choices[box.SelectedIndex].Id });
            };
            var toggle = Toggle(rule.Enabled, enabled => EditRule(index, r => r with { Enabled = enabled }));
            toggle.Margin = new Thickness(8, 0, 0, 0);
            var moveUp = ActionButton("↑", null, () => MoveRule(index, -1)); moveUp.IsEnabled = i > 0; moveUp.ToolTip = "提高优先级";
            var moveDown = ActionButton("↓", null, () => MoveRule(index, 1)); moveDown.IsEnabled = i < rules.Count - 1; moveDown.ToolTip = "降低优先级";
            var header = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right); header.Children.Add(toggle);
            header.Children.Add(Label(title, Theme.Body, "TextFillColorPrimaryBrush"));
            var content = new StackPanel { Margin = new Thickness(16), IsEnabled = Settings.DirectResults };
            content.Children.Add(header);
            if (KindExample(rule.Kind) is { Length: > 0 } example)
                content.Children.Add(Label(example, Theme.Caption, "TextFillColorSecondaryBrush"));
            var selection = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var moves = Horizontal(moveUp, moveDown); DockPanel.SetDock(moves, Dock.Right); selection.Children.Add(moves);
            box.MaxWidth = double.PositiveInfinity; selection.Children.Add(box);
            content.Children.Add(selection);
            page.Children.Add(CardBorder(content));
        }
        page.Children.Add(ActionButton("恢复默认规则", Symbol.ArrowReset24, () =>
        {
            Update(s => { s.Rules = null; s.DirectKinds = [.. DirectResults.DefaultKindNames]; }); ShowPage(false);
        }));
    }

    private void EditRule(int index, Func<DirectRule, DirectRule> edit) => Update(s =>
    {
        var rules = s.EffectiveRules.ToList(); rules[index] = edit(rules[index]); s.Rules = rules;
    });

    private void MoveRule(int index, int offset)
    {
        Update(s => { var rules = s.EffectiveRules.ToList(); (rules[index], rules[index + offset]) = (rules[index + offset], rules[index]); s.Rules = rules; });
        ShowPage(false);
    }

    private void ShortcutsPage(StackPanel page)
    {
        page.Children.Add(Note("点击组合键后直接按下新组合；Backspace 清除，Esc 取消。功能快捷键直接处理当前选中内容，读取前会等修饰键松开，避免影响原 App。"));
        page.Children.Add(Card(Symbol.Apps24, "打开圆盘", app.ShortcutProblem(KeyboardShortcut.RingTarget), ShortcutEditor(KeyboardShortcut.RingTarget)));
        var search = new TextBox { Text = shortcutQuery, ToolTip = "按功能名称、拼音或英文搜索", Margin = new Thickness(0, 14, 0, 8), Padding = new Thickness(10, 7, 10, 7) };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索功能快捷键");
        page.Children.Add(Label("搜索功能", Theme.Caption, "TextFillColorSecondaryBrush"));
        page.Children.Add(search);
        var list = new StackPanel(); page.Children.Add(list);
        void Fill()
        {
            list.Children.Clear();
            foreach (var category in Actions.Categories)
            {
                var actions = Actions.List.Append(Actions.All).Where(a => a.Category == category && a.Matches(shortcutQuery)).ToList();
                if (actions.Count == 0) continue;
                list.Children.Add(Section(category));
                foreach (var action in actions)
                    list.Children.Add(Card(Icons.Parse(action.Glyph), action.Title, app.ShortcutProblem(action.Id), ShortcutEditor(action.Id)));
            }
            if (list.Children.Count == 0) list.Children.Add(Note("没有找到匹配的功能"));
        }
        search.TextChanged += (_, _) => { shortcutQuery = search.Text; Fill(); };
        Fill();
        page.Children.Add(ActionButton("恢复默认快捷键", Symbol.ArrowReset24, () => { Update(s => s.ActionShortcuts.Clear()); ShowPage(false); }));
    }

    private UIElement ShortcutEditor(string id)
    {
        var effective = KeyboardShortcut.Effective(Settings.ActionShortcuts).GetValueOrDefault(id);
        var button = new Button { Content = app.ShortcutText(id) ?? effective?.Display ?? "未设置", MinWidth = 160, Padding = new Thickness(10, 5, 10, 5) };
        var note = new TextBlock { MaxWidth = 210, TextWrapping = TextWrapping.Wrap, FontSize = Theme.Caption };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var recording = false;
        void Finish()
        {
            recording = false; shortcutRecording = false; app.SuspendShortcuts(false);
            button.Content = app.ShortcutText(id) ?? KeyboardShortcut.Effective(Settings.ActionShortcuts).GetValueOrDefault(id)?.Display ?? "未设置";
        }
        button.Click += (_, _) =>
        {
            if (recording) return;
            recording = true; shortcutRecording = true; app.SuspendShortcuts(true);
            button.Content = "请按组合键…"; note.Text = "Backspace 清除 · Esc 取消"; button.Focus();
        };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = true;
            if (key == Key.Escape) { note.Text = ""; Finish(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            KeyboardShortcut? shortcut = key is Key.Back or Key.Delete ? null
                : new((uint)Keyboard.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!app.SetShortcut(id, shortcut, out var error)) { note.Text = error; return; }
            Finish(); note.Text = app.ShortcutProblem(id) ?? "已保存";
        };
        button.LostKeyboardFocus += (_, _) => { if (recording) { note.Text = ""; Finish(); } };
        var panel = new StackPanel(); panel.Children.Add(button); panel.Children.Add(note);
        return panel;
    }
}
