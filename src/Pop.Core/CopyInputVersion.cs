namespace Pop.Core;

/// Pop 菜单内的选择不改变原选区，只有传给原应用的按键和外部指针操作使复制证据失效。
public sealed class CopyInputVersion
{
    private long version;
    public long Version => Interlocked.Read(ref version);
    public void Key(bool handledByPop) { if (!handledByPop) Interlocked.Increment(ref version); }
    public void Pointer(bool overPopInterface) { if (!overPopInterface) Interlocked.Increment(ref version); }
}
