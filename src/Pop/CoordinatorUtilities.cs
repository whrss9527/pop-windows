namespace Pop;

internal sealed partial class Coordinator
{
    private readonly KeepAwakeController awake = new();
    public bool IsKeepingAwake => awake.IsActive;
    public string AwakeStatus => awake.Status;
    private void ShowKeepAwake(int x, int y)
    {
        toolbar.Dismiss(); CloseSnippets(); CloseCard(); CloseList(); CloseHistory();
        awake.ShowWindow();
    }
}
