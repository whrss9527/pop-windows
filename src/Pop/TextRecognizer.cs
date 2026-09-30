using Pop.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Pop;

/// 系统自带的离线文字识别（Windows.Media.Ocr）。识别的语言跟随系统的语言设置，需要装了对应的语言包
internal static class TextRecognizer
{
    public sealed class UnavailableException(string message) : Exception(message);

    private static OcrEngine? engine;

    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    /// 识别 PNG 图片里的文字；没有文字时返回空字符串
    public static async Task<string> RecognizeAsync(byte[] png)
    {
        var ocr = Engine();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        // 太大的图系统识别不了，按比例缩小
        using var scaled = bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension
            ? await Scale(decoder, bitmap)
            : null;
        var result = await ocr.RecognizeAsync(scaled ?? bitmap);
        return OcrText.Join(result.Lines.Select(l => l.Words.Select(w => w.Text)));
    }

    private static OcrEngine Engine()
    {
        if (engine is not null) return engine;
        engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? (OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } language ? OcrEngine.TryCreateFromLanguage(language) : null);
        if (engine is null)
            throw new UnavailableException("这台电脑没有可用的文字识别语言包：在「设置 → 时间和语言 → 语言和区域」里给中文或英文装上「光学字符识别」");
        Log.Info($"文字识别语言：{engine.RecognizerLanguage.LanguageTag}");
        return engine;
    }

    private static async Task<SoftwareBitmap> Scale(BitmapDecoder decoder, SoftwareBitmap bitmap)
    {
        var ratio = Math.Min((double)OcrEngine.MaxImageDimension / bitmap.PixelWidth, (double)OcrEngine.MaxImageDimension / bitmap.PixelHeight);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)(bitmap.PixelWidth * ratio),
            ScaledHeight = (uint)(bitmap.PixelHeight * ratio),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    }
}
