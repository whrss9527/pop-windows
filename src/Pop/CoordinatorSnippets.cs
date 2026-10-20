using Pop.Core;

namespace Pop;

internal sealed partial class Coordinator
{
    private readonly SnippetWindow snippetWindow = new();
    private volatile bool snippetsOpen;
    private (IntPtr Target, string Selection, string Clipboard)? snippetContext;

    private void SetUpSnippets()
    {
        hook.PointerActivity += (x, y, scroll) =>
        {
            if (snippetsOpen && !scroll) dispatcher.BeginInvoke(() => { if (!snippetWindow.ContainsPhysical(x, y)) CloseSnippets(); });
        };
        snippetWindow.Chosen += async snippet =>
        {
            snippetsOpen = false;
            if (snippetContext is not { } context || !await WaitForModifiersAsync() || Native.GetForegroundWindow() != context.Target) return;
            if (!settings().Snippets.Any(s => s.Id == snippet.Id && s.Text == snippet.Text)) return;
            var expanded = SnippetExpander.Expand(snippet.Text, DateTimeOffset.Now, clipboard: context.Clipboard, selection: context.Selection);
            await Replace(expanded);
        };
    }

    private void ShowSnippets(ClassifiedContent content, int x, int y)
    {
        toolbar.Dismiss(); CloseList(); CloseHistory(); CloseCard();
        snippetContext = (Native.GetForegroundWindow(), content.OriginalText, clipboard.GetText() ?? "");
        snippetWindow.ShowAt(settings().Snippets, x, y); snippetsOpen = true;
    }

    private void CloseSnippets() { snippetsOpen = false; snippetWindow.Dismiss(); }

    private bool FilterSnippetKey(int vk)
    {
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        if (vk is 0x11 or 0xA2 or 0xA3) return false;
        if (Down(0x11) && !Down(0x12) && !Down(0x5B) && !Down(0x5C))
        {
            if (vk is >= 0x31 and <= 0x39) { dispatcher.BeginInvoke(() => snippetWindow.ChooseNth(vk - 0x31)); return true; }
            if (vk == 0x56) { dispatcher.BeginInvoke(() => snippetWindow.Paste(clipboard.GetText() ?? "")); return true; }
        }
        if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C))
        { dispatcher.BeginInvoke(CloseSnippets); return false; }
        switch (vk)
        {
            case 0x1B: dispatcher.BeginInvoke(CloseSnippets); return true;
            case 0x0D: dispatcher.BeginInvoke(snippetWindow.Choose); return true;
            case 0x26: dispatcher.BeginInvoke(() => snippetWindow.Move(-1)); return true;
            case 0x28: dispatcher.BeginInvoke(() => snippetWindow.Move(1)); return true;
            case 0x08: dispatcher.BeginInvoke(snippetWindow.Backspace); return true;
        }
        if (TypedChar(vk) is { } c) { dispatcher.BeginInvoke(() => snippetWindow.Type(c)); return true; }
        return vk is not (0x10 or 0xA0 or 0xA1 or 0x14);
    }
}
