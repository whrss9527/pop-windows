using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfUi = Wpf.Ui.Controls;
using Symbol = Wpf.Ui.Controls.SymbolRegular;

namespace Pop;

/// 设置窗口和插件编辑器共用的卡片和控件：颜色都引用 WPF-UI 的主题资源，跟着深浅色变
internal static class SettingsUi
{
    /// 屏幕矮（比如 1366×768 再放大 125%）的时候窗口不超出工作区，内容在窗口里滚动；打开后整个窗口都在工作区里
    public static void FitToWorkArea(Window window)
    {
        const double margin = 12;
        var work = SystemParameters.WorkArea;
        window.Width = Math.Min(window.Width, work.Width - 2 * margin);
        window.Height = Math.Min(window.Height, work.Height - 2 * margin);
        window.MinWidth = Math.Min(window.MinWidth, window.Width);
        window.MinHeight = Math.Min(window.MinHeight, window.Height);
        window.Loaded += (_, _) =>
        {
            window.Left = Math.Clamp(window.Left, work.Left, Math.Max(work.Left, work.Right - window.ActualWidth));
            window.Top = Math.Clamp(window.Top, work.Top, Math.Max(work.Top, work.Bottom - window.ActualHeight));
        };
    }

    public static TextBlock Label(string text, double size, string brush, FontWeight? weight = null, FontFamily? font = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (weight is { } w) label.FontWeight = w;
        if (font is not null) label.FontFamily = font;
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return label;
    }

    public static TextBlock Section(string text, bool first = false)
    {
        var label = Label(text, Theme.Body, "TextFillColorPrimaryBrush", FontWeights.SemiBold);
        label.Margin = new Thickness(2, first ? 0 : 28, 0, 8);
        return label;
    }

    public static TextBlock Note(string text)
    {
        var label = Label(text, Theme.Body, "TextFillColorSecondaryBrush");
        label.Margin = new Thickness(2, 0, 0, 16);
        label.LineHeight = 22;
        return label;
    }

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// 卡片底：圆角、浅浅的描边，颜色跟着深浅色走
    public static Border CardBorder(UIElement child)
    {
        var card = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(Theme.ControlRadius + 2),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    /// 一张设置卡片：图标、标题和说明在左边，控件在右边
    public static Border Card(Symbol? icon, string title, string? description, UIElement? control) =>
        CardBorder(Row(icon, title, description, control));

    /// 一组设置：上面是一行总开关（可以没有），下面几行用细线隔开
    public static Border Group(UIElement? header, IReadOnlyList<UIElement> rows)
    {
        var stack = new StackPanel();
        if (header is not null) stack.Children.Add(header);
        var items = new StackPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0 || header is not null) items.Children.Add(Divider());
            items.Children.Add(rows[i]);
        }
        if (header is not null)
        {
            var nested = new Border { Child = items, CornerRadius = new CornerRadius(0, 0, Theme.ControlRadius + 1, Theme.ControlRadius + 1) };
            nested.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
            stack.Children.Add(nested);
        }
        else
        {
            stack.Children.Add(items);
        }
        return CardBorder(stack);
    }

    public static Border Divider()
    {
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        return line;
    }

    public static Grid Row(Symbol? icon, string title, string? description, UIElement? control, double indent = 0)
    {
        var grid = new Grid { MinHeight = description is null ? 56 : 68, Margin = new Thickness(18 + indent, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon is { } symbol)
        {
            var glyph = new WpfUi.SymbolIcon { Symbol = symbol, FontSize = 20, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            grid.Children.Add(glyph);
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        texts.Children.Add(Label(title, Theme.Body, "TextFillColorPrimaryBrush"));
        if (description is not null)
        {
            var note = Label(description, Theme.Caption, "TextFillColorSecondaryBrush");
            note.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(note);
        }
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            element.Margin = new Thickness(24, element.Margin.Top, 0, element.Margin.Bottom);
            Grid.SetColumn(element, 2);
            grid.Children.Add(element);
        }
        return grid;
    }

    public static WpfUi.ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var toggle = new WpfUi.ToggleSwitch
        {
            IsChecked = value,
            OnContent = "开",
            OffContent = "关",
            LabelPosition = WpfUi.ElementPlacement.Left,
        };
        toggle.Click += (_, _) => set(toggle.IsChecked == true);
        return toggle;
    }

    public static WpfUi.Button ActionButton(string text, Symbol? icon, Action click, WpfUi.ControlAppearance appearance = WpfUi.ControlAppearance.Secondary)
    {
        var button = new WpfUi.Button
        {
            Content = text,
            Appearance = appearance,
            Padding = new Thickness(14, 6, 14, 7),
            MinWidth = 88,
        };
        if (icon is { } symbol) button.Icon = new WpfUi.SymbolIcon { Symbol = symbol, FontSize = 16 };
        button.Click += (_, _) => click();
        return button;
    }

    /// 快捷键的键帽
    public static UIElement Keys(string? combination)
    {
        if (combination is null) return Label("未注册", Theme.Body, "TextFillColorTertiaryBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var key in combination.Split('+'))
        {
            var text = new TextBlock { Text = key, FontSize = Theme.Caption, HorizontalAlignment = HorizontalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var cap = new Border
            {
                MinWidth = 30,
                Padding = new Thickness(9, 3, 9, 4),
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Child = text,
            };
            cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            cap.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            panel.Children.Add(cap);
        }
        return panel;
    }
}
