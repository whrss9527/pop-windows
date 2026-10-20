namespace Pop;

internal sealed partial class Coordinator
{
    private readonly KeepAwakeController awake = new();
    private void ShowKeepAwake(int x, int y)
    {
        toolbar.Dismiss(); CloseSnippets(); CloseCard(); CloseList(); CloseHistory();
        awake.ShowWindow();
    }
}
