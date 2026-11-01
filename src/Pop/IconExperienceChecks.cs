using Wpf.Ui.Controls;

namespace Pop;

/// 用随包的真实图标枚举检查兼容性，防止选中态或旧插件图标变成异常字符。
internal static class IconExperienceChecks
{
    public static void Verify()
    {
        foreach (var page in SettingsWindow.Pages)
            if (Icons.Parse(page.Icon.ToString()) != page.Icon)
                throw new InvalidOperationException($"设置页图标不能正确显示：{page.Id}");
        var brush = Theme.Current().Brush(Theme.Current().Text);
        if (Icons.Parse("TextBulletList24") != SymbolRegular.TextBulletListLtr24
            || Icons.Parse("ArrowExport24") != SymbolRegular.ArrowExportLtr24
            || Icons.Make(SymbolRegular.TextBulletList24, 16, brush, filled: true).Symbol != SymbolRegular.TextBulletListLtr24)
            throw new InvalidOperationException("旧列表或导出图标没有映射到兼容的样式");
        if (Icons.Make(SymbolRegular.Calendar24, 16, brush).Symbol != SymbolRegular.Apps24
            || Icons.Parse("missing-icon", SymbolRegular.Calendar24) != SymbolRegular.Apps24)
            throw new InvalidOperationException("不支持的图标没有安全回退");
    }
}
