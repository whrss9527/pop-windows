using System.Text;

namespace Pop.Core;

public sealed record SettingsEntry(string Id, string Page, string Title, string Description, string Keywords);

/// 设置入口的用途索引；不读取设置值、凭据或剪贴板内容。
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingsEntry> Entries { get; } =
    [
        new("enabled", "general", "暂停或恢复鼠标唤起", "保留全局快捷键，临时关闭鼠标触发", "通用 开关 启用 暂停 enabled pause mouse"),
        new("startup", "general", "开机启动", "登录 Windows 后自动运行 Pop", "通用 自动启动 自启 startup autostart"),
        new("trigger", "triggers", "唤起方式与长按时间", "设置右键长按、修饰键右键、中键或仅使用快捷键", "唤起 按住 延迟 时间 灵敏度 鼠标 trigger hold delay hk"),
        new("excluded", "triggers", "排除指定应用", "保留这些程序原有的鼠标操作", "唤起 游戏 远程 桌面 排除 黑名单 excluded ignore apps"),
        new("toolbar", "triggers", "选中文字工具条", "选中文字后显示常用工具，不自动复制", "唤起 拖选 选择 文字 selection toolbar"),
        new("rules", "rules", "直达规则与优先级", "按内容类型选择直接运行的功能及匹配顺序", "规则 自动 结果 算式 颜色 外文 direct rules priority"),
        new("shortcuts", "shortcuts", "功能快捷键", "录入组合键、查看冲突或恢复默认", "快捷键 热键 键盘 kjj kuaijiejian hotkey shortcut keyboard ctrl alt win"),
        new("ring", "ring", "圆盘布局", "设置格数、各方向的功能和松开后的行为", "圆盘 菜单 格子 方向 布局 ring menu slots yp"),
        new("app-ring", "ring", "每个应用的独立圆盘", "为指定程序设置布局，其他应用继承默认", "圆盘 独立 应用 程序 布局 app ring layout"),
        new("tools", "actions", "内置工具开关", "启用或停用文字、编码、截图、取色等功能", "功能 大写 小写 JSON 编码 截图 OCR 取色 标尺 哈希 base64 hash tools actions"),
        new("plugins", "actions", "我的插件", "新建、导入、编辑和试运行网址或脚本插件", "功能 插件 脚本 网址 扩展 plugin javascript shell powershell cj"),
        new("translation", "translate", "翻译服务", "选择必应或 Microsoft Translator，设置目标语言并测试", "翻译 密钥 语言 中文 英语 译文 fy fanyi translation bing azure key"),
        new("history", "clipboard", "剪贴板历史", "调整记录开关、保存天数、条数及清理方式", "剪贴板 历史 清空 保存 天数 数量 保留 clipboard history jtb"),
        new("privacy", "clipboard", "不记录哪些应用", "为密码管理器等应用设置剪贴板记录排除项", "剪贴板 密码 隐私 排除 安全 clipboard privacy password exclude"),
        new("snippets", "snippets", "常用短语", "保存常用回复、邮件签名和带日期的模板", "短语 模板 常用 回复 邮件 日期 时间 snippet template phrase dy"),
        new("backup", "backup", "备份与迁移", "导出、预览导入和恢复设置及自定义插件", "备份 迁移 导入 导出 恢复 backup import export restore bf"),
        new("update", "about", "版本与更新", "检查新版本、设置测试版及自动更新", "关于 版本 更新 轻量 便携 beta version update release"),
        new("logs", "about", "日志与开源许可", "排查运行问题、打开日志和查看开源许可", "关于 日志 问题 帮助 许可 license log debug support"),
    ];

    public static IReadOnlyList<SettingsEntry> Search(string? query)
    {
        var normalized = SearchQuery.Append("", query ?? "").Normalize(NormalizationForm.FormKC).Trim();
        var words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        return Entries.Where(entry => words.All(word => Contains(entry.Title, word) || Contains(entry.Description, word) || Contains(entry.Keywords, word)))
            .OrderBy(entry => string.Equals(entry.Title, normalized, StringComparison.OrdinalIgnoreCase) ? 0 : Contains(entry.Title, normalized) ? 1 : 2)
            .ToArray();
    }

    private static bool Contains(string value, string word) => value.Contains(word, StringComparison.OrdinalIgnoreCase);
}
