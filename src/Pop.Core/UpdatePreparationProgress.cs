namespace Pop.Core;

public enum UpdatePreparationStage { Downloading, Preparing }

/// 下载大小未知时不显示百分比；校验和解压是独立的准备阶段。
public sealed record UpdatePreparationProgress(UpdatePreparationStage Stage, double? Fraction = null);
