using Pop.Core;

namespace Pop.Core.Tests;

public class ClickTriggerTests
{
    [Theory]
    [InlineData(TriggerMode.ModifierRightClick, TriggerButton.Right, true)]
    [InlineData(TriggerMode.MiddleClick, TriggerButton.Middle, false)]
    public void ConfiguredClicksConsumeOnlyTheirOwnPair(TriggerMode mode, TriggerButton button, bool modifier)
    {
        var tracker = new ClickTriggerTracker();
        Assert.True(tracker.Down(button, mode, modifier, true).Swallow);
        var other = button == TriggerButton.Middle ? TriggerButton.Right : TriggerButton.Middle;
        Assert.False(tracker.Up(other, true).Swallow);
        Assert.True(tracker.Down(button, mode, modifier, true).Swallow);
        Assert.Equal(new ClickTriggerDecision(true, true), tracker.Up(button, true));
        Assert.False(tracker.Up(button, true).Swallow);
    }

    [Theory]
    [InlineData(TriggerMode.LongPressRight, TriggerButton.Right, true)]
    [InlineData(TriggerMode.Disabled, TriggerButton.Middle, true)]
    [InlineData(TriggerMode.MiddleClick, TriggerButton.Right, true)]
    [InlineData(TriggerMode.ModifierRightClick, TriggerButton.Right, false)]
    [InlineData(TriggerMode.ModifierRightClick, TriggerButton.Middle, true)]
    public void UnconfiguredClicksPassThrough(TriggerMode mode, TriggerButton button, bool modifier)
    {
        var tracker = new ClickTriggerTracker();
        Assert.False(tracker.Down(button, mode, modifier, true).Swallow);
        Assert.False(tracker.Up(button, true).Swallow);
    }

    [Fact]
    public void DisablingAfterDownStillConsumesTheMatchingUpWithoutActivating()
    {
        var tracker = new ClickTriggerTracker();
        Assert.False(tracker.Down(TriggerButton.Middle, TriggerMode.MiddleClick, false, false).Swallow);
        Assert.True(tracker.Down(TriggerButton.Middle, TriggerMode.MiddleClick, false, true).Swallow);
        Assert.Equal(new ClickTriggerDecision(true, false), tracker.Up(TriggerButton.Middle, false));
        Assert.False(tracker.Up(TriggerButton.Middle, true).Swallow);
    }
}

public class InteractionSettingsTests
{
    [Theory]
    [InlineData(false, false, null, false)]
    [InlineData(false, true, null, true)]
    [InlineData(false, true, 1, false)]
    [InlineData(false, false, 0, false)]
    [InlineData(true, false, null, true)]
    [InlineData(true, false, 3, true)]
    public void ReleaseOnlyKeepsTheRingInTheConfiguredCases(bool clickMode, bool keepOnCenter, int? selected, bool keep) =>
        Assert.Equal(keep, RingNavigation.KeepAfterRelease(clickMode, keepOnCenter, selected));

    [Fact]
    public void OldSettingsKeepTheirTriggerAndDirectBehavior()
    {
        var settings = AppSettings.FromJson("""{"directKinds":["number"],"ringSlots":["calc","copy","hash","all"]}""");
        Assert.Equal(TriggerMode.LongPressRight, settings.TriggerMode);
        Assert.Equal(TriggerModifier.Alt, settings.TriggerModifier);
        Assert.Null(settings.Rules);
        Assert.Equal(Actions.Number, settings.DirectActionFor(ContentClassifier.Classify("42")));
        Assert.Null(settings.DirectActionFor(ContentClassifier.Classify("hello world")));
        Assert.Equal(settings.RingSlots, settings.SlotsFor("unknown.exe"));
        Assert.Equal(settings.DirectKinds, AppSettings.FromJson(settings.ToJson()).DirectKinds);
    }

    [Fact]
    public void PerAppRingsNormalizeNamesAndStayIndependentFromDefault()
    {
        var settings = AppSettings.FromJson("""
            {"appRings":{"C:\\Program Files\\Code.exe":["json","codec","hash","all"],
             "notepad.EXE":["copy","search","translate","all"], "broken":["copy"], "bad":42}}
            """);
        Assert.Equal(["json", "codec", "hash", "all"], settings.SlotsFor("CODE"));
        Assert.Equal(settings.SlotsFor("code"), settings.SlotsFor("/apps/Code.exe"));
        Assert.Equal(["copy", "search", "translate", "all"], settings.SlotsFor("Notepad.exe"));
        Assert.Equal(settings.RingSlots, settings.SlotsFor("broken"));
        Assert.Equal(2, settings.AppRings.Count);
        settings.AppRings["code"][0] = "number";
        Assert.Equal(RingItems.DefaultIds, settings.RingSlots);
        var restored = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(settings.SlotsFor("CODE"), restored.SlotsFor("code.exe"));
    }

    [Fact]
    public void TriggerAndShortcutSettingsRoundTripAndRejectMalformedValues()
    {
        var settings = AppSettings.FromJson("""
            {"triggerMode":"ModifierRightClick","triggerModifier":"Control",
             "actionShortcuts":{"upper":{"modifiers":6,"key":85},"ocr":null,
             "bad":{"modifiers":4,"key":65},"malformed":12}}
            """);
        Assert.Equal(TriggerMode.ModifierRightClick, settings.TriggerMode);
        Assert.Equal(TriggerModifier.Control, settings.TriggerModifier);
        Assert.Equal(new KeyboardShortcut(6, 85), settings.ActionShortcuts["upper"]);
        Assert.Null(settings.ActionShortcuts["ocr"]);
        Assert.False(settings.ActionShortcuts.ContainsKey("bad"));
        Assert.False(settings.ActionShortcuts.ContainsKey("malformed"));
        var restored = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(settings.ActionShortcuts, restored.ActionShortcuts);
        Assert.Equal(settings.TriggerMode, restored.TriggerMode);
        Assert.Equal(settings.TriggerModifier, restored.TriggerModifier);
        var invalid = AppSettings.FromJson("""{"triggerMode":"999","triggerModifier":123}""");
        Assert.Equal(TriggerMode.LongPressRight, invalid.TriggerMode);
        Assert.Equal(TriggerModifier.Alt, invalid.TriggerModifier);
    }

    [Fact]
    public void OrderedRulesCanRunAnyApplicableTextToolAndFallBackToRing()
    {
        var settings = new AppSettings { Rules = [new("foreign", "upper", true), new("text", "hash", true)] };
        Assert.Equal(Actions.Upper, settings.DirectActionFor(ContentClassifier.Classify("hello world")));
        settings.DisabledActions.Add("upper");
        Assert.Equal(Actions.Hash, settings.DirectActionFor(ContentClassifier.Classify("hello world")));
        settings.DisabledActions.Add("hash");
        Assert.Null(settings.DirectActionFor(ContentClassifier.Classify("hello world")));
        settings.DisabledActions.Clear(); settings.DirectResults = false;
        Assert.Null(settings.DirectActionFor(ContentClassifier.Classify("hello world")));
    }

    [Fact]
    public void MissingAndInapplicableRuleTargetsDoNotIntercept()
    {
        var settings = new AppSettings { Rules = [new("foreign", "uninstalled", true), new("word", "numberStats", true), new("text", "copy", true)] };
        Assert.Equal(Actions.Copy, settings.DirectActionFor(ContentClassifier.Classify("hello")));
        settings.Rules = [new("text", "annotate", true)];
        Assert.Null(settings.DirectActionFor(ContentClassifier.Classify("hello world")));
    }

    [Fact]
    public void RulesPreserveOrderNormalizeDuplicatesAndLoadGoodSiblings()
    {
        var settings = AppSettings.FromJson("""
            {"rules":[42,null,{"kind":"not-real","actionId":"copy","enabled":true},
             {"kind":"TEXT","actionId":"hash","enabled":true},
             {"kind":"text","actionId":"copy","enabled":true},
             {"kind":"math","actionId":"copy","enabled":false}]}
            """);
        Assert.NotNull(settings.Rules);
        Assert.Equal(new DirectRule("text", "hash", true), settings.EffectiveRules[0]);
        Assert.Single(settings.EffectiveRules, r => r.Kind == "text");
        Assert.Equal(DirectResults.Kinds.Count, settings.EffectiveRules.Count);
        Assert.All(settings.EffectiveRules.Skip(2), r => Assert.False(r.Enabled));
        var restored = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(settings.EffectiveRules, restored.EffectiveRules);
        Assert.Equal(Actions.Hash, settings.DirectActionFor(ContentClassifier.Classify("1 + 2")));
    }
}

public class KeyboardShortcutTests
{
    [Fact]
    public void OverridesReplaceOrDisableDefaultsAndRetainPluginBindings()
    {
        var overrides = new Dictionary<string, KeyboardShortcut?>
        {
            ["ocr"] = null, [KeyboardShortcut.RingTarget] = new(6, 0x20), ["user-test"] = new(3, 0x75),
        };
        var bindings = KeyboardShortcut.Effective(overrides);
        Assert.False(bindings.ContainsKey("ocr"));
        Assert.Equal("Ctrl+Shift+Space", bindings[KeyboardShortcut.RingTarget].Display);
        Assert.Equal("Ctrl+Alt+F6", bindings["user-test"].Display);
        Assert.Equal("Win+Alt+V", bindings["clipboard"].Display);
        Assert.True(KeyboardShortcut.Defaults.ContainsKey("ocr"));
    }

    [Theory]
    [InlineData(4u, 65u, false)]
    [InlineData(0u, 65u, false)]
    [InlineData(16u, 65u, false)]
    [InlineData(2u, 0x10u, false)]
    [InlineData(2u, 65u, true)]
    [InlineData(2u, 0x43u, false)]
    [InlineData(2u, 0x56u, false)]
    [InlineData(9u, 0x20u, true)]
    [InlineData(3u, 0x87u, true)]
    public void OnlyValidCombinationsCanBeRegistered(uint modifiers, uint key, bool valid) =>
        Assert.Equal(valid, new KeyboardShortcut(modifiers, key).IsValid);
}

[Collection("Actions registry")]
public class DirectPluginRuleTests
{
    [Fact]
    public void UnconstrainedPluginsCanRunDirectlyAndUnloadedPluginsFallBack()
    {
        var manifest = new PluginManifest
        {
            Id = "user-direct", Name = "自定义搜索",
            Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" },
        };
        var settings = new AppSettings { Rules = [new("foreign", manifest.Id, true), new("text", "copy", true)] };
        var content = ContentClassifier.Classify("hello world");
        try
        {
            Actions.SetPlugins(PluginRunner.ToActions([manifest]));
            var action = Assert.IsType<PopAction>(settings.DirectActionFor(content));
            Assert.Equal(manifest.Id, action.Id);
            Assert.Equal(ActionEffect.RunPlugin, action.Run(content)!.Effect);
            settings.DisabledActions.Add(manifest.Id);
            Assert.Equal(Actions.Copy, settings.DirectActionFor(content));
            settings.DisabledActions.Clear(); Actions.SetPlugins([]);
            Assert.Equal(Actions.Copy, settings.DirectActionFor(content));
        }
        finally { Actions.SetPlugins([]); }
    }
}
