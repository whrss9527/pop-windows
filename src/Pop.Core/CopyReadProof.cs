namespace Pop.Core;

/// 本次模拟复制的交互证据；未知来源或用户已有新操作时，保留新的剪贴板。
public readonly record struct CopyReadProof(long Operation, long Activity, uint TargetProcess)
{
    public bool IsCurrent(long operation, long activity, bool targetIsForeground) =>
        TargetProcess != 0 && Operation == operation && Activity == activity && targetIsForeground;

    public bool AcceptsOwner(uint ownerProcess, long operation, long activity, bool targetIsForeground) =>
        ownerProcess != 0 && ownerProcess == TargetProcess && IsCurrent(operation, activity, targetIsForeground);
}
