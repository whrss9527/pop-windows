using System.Security.Cryptography;
using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public class ClipboardHashPerformanceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("中文与 emoji 👩‍💻🇨🇳\r\n")]
    public void HashMatchesExistingUtf8FormatAcrossBlockBoundaries(string sample)
        => Verify(sample);

    [Fact]
    public void InvalidUtf16UsesTheSameReplacementFallback()
        => Verify("a\uD800b\uDC00\0tail");

    private static void Verify(string sample)
    {
        foreach (var prefix in new[] { 0, 4093, 4095, 4096, 8191 })
        {
            var text = new string('a', prefix) + sample + string.Concat(Enumerable.Repeat(sample, 3000));
            var digest = Digests.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            Assert.Equal("text:" + digest, new ClipboardCapture(ClipboardKind.Text, text).Hash);
            Assert.Equal("files:" + digest, new ClipboardCapture(ClipboardKind.Files, text).Hash);
        }
    }

    [Fact]
    public void LargeCaptureDoesNotAllocateTheEntireUtf8Payload()
    {
        var capture = new ClipboardCapture(ClipboardKind.Text, new string('中', 500_000));
        _ = capture.Hash;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var digest = capture.Hash;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(69, digest.Length);
        Assert.InRange(allocated, 0, 16_384);
    }
}
