using System.IO.Compression;
using System.Net;
using Pop.Core;

namespace Pop.Core.Tests;

public sealed class UpdateProgressTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pop-update-progress-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpDownloadReportsKnownSizeOnlyAndPreparesVerifiedPackage(bool known)
    {
        var (release, bytes, sums) = Package();
        using var http = new HttpClient(new PackageHandler(bytes, sums, known));
        var reports = new List<UpdatePreparationProgress>();
        var client = new UpdateClient(http);
        var exe = await client.DownloadAndPrepareAsync(release, Path.Combine(directory, "work"), null, CancellationToken.None,
            new CaptureProgress<UpdatePreparationProgress>(reports.Add));
        Assert.Equal("verified sample", File.ReadAllText(exe));
        Assert.Equal(new UpdatePreparationProgress(UpdatePreparationStage.Downloading), reports[0]);
        Assert.Equal(UpdatePreparationStage.Preparing, reports[^1].Stage);
        var fractions = reports.Where(p => p.Stage == UpdatePreparationStage.Downloading && p.Fraction is not null).Select(p => p.Fraction!.Value).ToArray();
        if (known)
        {
            Assert.Contains(fractions, value => value > 0 && value < 1);
            Assert.Equal(1, fractions[^1]);
            Assert.Equal(fractions.Order().ToArray(), fractions);
        }
        else Assert.Empty(fractions);
    }

    [Fact]
    public async Task BadChecksumStillRejectsPackageDuringBackgroundPreparation()
    {
        var (release, bytes, _) = Package();
        using var http = new HttpClient(new PackageHandler(bytes, new string('0', 64) + "  " + release.PackageName, true));
        var client = new UpdateClient(http);
        var error = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAndPrepareAsync(release,
            Path.Combine(directory, "work"), null, CancellationToken.None));
        Assert.Contains("校验和不对", error.Message);
        Assert.False(Directory.Exists(Path.Combine(directory, "work", "staging")));
    }

    [Fact]
    public async Task AlreadyCancelledLocalCopyLeavesSourceAndDestinationUntouched()
    {
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.zip");
        var destination = Path.Combine(directory, "destination.zip");
        File.WriteAllText(source, "source"); File.WriteAllText(destination, "keep destination");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var http = new HttpClient();
        var client = new UpdateClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(source, destination, null, cancellation.Token));
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("keep destination", File.ReadAllText(destination));
    }

    [Fact]
    public async Task CancellationDuringHttpReadDoesNotStartPreparation()
    {
        var (release, _, sums) = Package();
        var stream = new WaitingStream();
        using var http = new HttpClient(new WaitingHandler(sums, stream));
        using var cancellation = new CancellationTokenSource();
        var reports = new List<UpdatePreparationProgress>();
        var task = new UpdateClient(http).DownloadAndPrepareAsync(release, Path.Combine(directory, "work"), null,
            cancellation.Token, new CaptureProgress<UpdatePreparationProgress>(reports.Add));
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.DoesNotContain(reports, p => p.Stage == UpdatePreparationStage.Preparing);
        Assert.False(Directory.Exists(Path.Combine(directory, "work", "staging")));
    }

    private (ReleaseInfo Release, byte[] Bytes, string Sums) Package()
    {
        Directory.CreateDirectory(directory);
        var name = "Pop-0.2.0-win-x64.zip";
        var path = Path.Combine(directory, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Pop.exe").Open())) writer.Write("verified sample");
            using var padding = zip.CreateEntry("sample.bin", CompressionLevel.NoCompression).Open();
            padding.Write(new byte[200_000]);
        }
        return (new ReleaseInfo(new SemVersion(0, 2, 0), "v0.2.0", "", false, name,
            "https://updates.invalid/package.zip", "https://updates.invalid/SHA256SUMS.txt"),
            File.ReadAllBytes(path), Checksums.Sha256File(path) + "  " + name);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private sealed class CaptureProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class PackageHandler(byte[] bytes, string sums, bool known) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? new StringContent(sums) : new UnknownLengthContent(bytes);
            if (known && content is UnknownLengthContent) content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class WaitingHandler(string sums, Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? new StringContent(sums) : new StreamContent(stream) });
    }

    private sealed class WaitingStream : Stream
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true); await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
