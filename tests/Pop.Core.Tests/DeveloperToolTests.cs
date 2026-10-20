using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public class DeveloperToolTests
{
    [Fact]
    public void TextToolsUseExactSelectionBytesAndPreserveEmptyTableCells()
    {
        var original = " hello \n";
        var content = ContentClassifier.Classify(original);
        var codec = Actions.Codec.Run(content)!.Card!;
        Assert.Contains(codec.Lines, r => r.Label == "Base64 编码" && r.Value == "IGhlbGxvIAo=");
        var hash = Actions.Hash.Run(content)!.Card!;
        var md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(original))).ToLowerInvariant();
        Assert.Contains(hash.Lines, r => r.Label == "MD5" && r.Value == md5);
        var table = ContentClassifier.Classify("\tName\n1\tAlice");
        Assert.True(Actions.TableConvert.IsAvailable(table));
        Assert.Contains("|  | Name |", Actions.TableConvert.Run(table)!.Card!.Lines[0].Value);
        Assert.True(Actions.Cleanup.IsAvailable(ContentClassifier.Classify("  hello  ")));
    }
    [Fact]
    public void OptionalInputActionsWorkInRulesAndToolbarWithoutRequiringText()
    {
        var content = ContentClassifier.Classify("hello");
        Assert.Equal(Actions.Snippets, DirectRule.Resolve([new("foreign", "snippets", true)], content, _ => true));
        Assert.Equal([Actions.Snippets, Actions.Diff], SelectionToolbarLogic.Items([Actions.Snippets, Actions.Diff, Actions.Awake], content));
        var whitespace = ContentClassifier.Classify(" \t\n");
        Assert.True(whitespace.IsEmpty); Assert.Equal(" \t\n", whitespace.OriginalText);
        Assert.True(Actions.Snippets.IsAvailable(whitespace));
        Assert.True(Actions.IsBuiltIn("COPY")); Assert.Equal(Actions.Copy, Actions.Find("COPY"));
        Assert.False(PluginManifest.IsUsableId("COPY", Actions.IsBuiltIn));
    }
    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    [Fact]
    public void JwtDecodesPayloadAndClaimsLocallyWithoutClaimingValidation()
    {
        var jwt = Base64("""{"alg":"HS256","typ":"JWT"}""") + "." + Base64("""{"sub":"你好","exp":1735689600,"aud":["one","two"]}""") + ".signature";
        var decoded = JwtDecoder.Decode(jwt);
        Assert.NotNull(decoded); Assert.Contains("sub", decoded.Payload);
        Assert.Contains(decoded.Claims, r => r.Label == "过期时间" && r.Value == "2025-01-01 00:00:00 UTC");
        var card = JwtDecoder.Card(jwt)!; Assert.Equal(decoded.Payload, card.PrimaryText); Assert.Contains("未验证签名", card.Caption);
        Assert.True(Actions.Jwt.IsAvailable(ContentClassifier.Classify(jwt)));
    }
    [Theory]
    [InlineData("abc.def")]
    [InlineData("a.b.c")]
    [InlineData("e30.W10.signature")]
    [InlineData("e30.e30.!")]
    [InlineData("e30._w.signature")]
    public void MalformedJwtIsRejected(string token) => Assert.Null(JwtDecoder.Decode(token));
    [Fact]
    public void DiffReconstructsBothInputsIncludingRepeatedAndEmptyLines()
    {
        const string before = "first\nrepeat\nrepeat\n\nold\nlast\n";
        const string after = "first\nrepeat\nnew\n\nlast\n";
        var diff = TextDiff.Compare(before, after);
        Assert.Equal(before, string.Join('\n', diff.Lines.Where(l => l.Kind != DiffKind.Added).Select(l => l.Text)));
        Assert.Equal(after, string.Join('\n', diff.Lines.Where(l => l.Kind != DiffKind.Removed).Select(l => l.Text)));
        Assert.Equal(1, diff.Added); Assert.Equal(2, diff.Removed); Assert.Contains("- old", diff.Unified());
    }
    [Fact]
    public void DiffHandlesEmptyInputsLineEndingsWhitespaceAndFinalNewlines()
    {
        Assert.Empty(TextDiff.Compare("", "").Lines);
        Assert.Equal(1, TextDiff.Compare("", "x").Added); Assert.Equal(1, TextDiff.Compare("x", "").Removed);
        Assert.Equal(0, TextDiff.Compare("a\r\nb", "a\nb").Added);
        Assert.Equal(1, TextDiff.Compare("a", "a\n").Added);
        Assert.Equal(0, TextDiff.Compare("  a  ", "a", true).Added);
        Assert.Equal(1, TextDiff.Compare("  a  ", "a").Added);
    }
    [Fact]
    public void DiffSupportsSmallChangesInLongDocumentsAndBoundsUnrelatedInput()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => "line " + i).ToArray();
        var before = string.Join('\n', lines); lines[2500] = "changed";
        var diff = TextDiff.Compare(before, string.Join('\n', lines)); Assert.Equal(1, diff.Added); Assert.Equal(1, diff.Removed);
        Assert.Throws<ArgumentException>(() => TextDiff.Compare(string.Join('\n', Enumerable.Repeat("x", 3000)), string.Join('\n', Enumerable.Repeat("y", 3000))));
    }
}
