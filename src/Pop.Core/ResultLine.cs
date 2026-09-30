namespace Pop.Core;

/// 结果卡片上的一行：左边是说明（「十六进制」「英里」），右边是可以复制、替换原文的值
public sealed record ResultLine(string Label, string Value);
