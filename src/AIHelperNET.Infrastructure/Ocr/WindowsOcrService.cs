using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace AIHelperNET.Infrastructure.Ocr;

public sealed class WindowsOcrService : IScreenOcrService
{
    public async Task<Result<string>> CaptureAndReadAsync(CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            return Result.Fail("No OCR engine available for the system's user profile languages.");

        try
        {
            using var bmp = ScreenGrabber.CaptureForeground();
            using var processed = ImagePreprocessor.Enhance(bmp);

            var softwareBitmap = await BitmapToSoftwareBitmapAsync(processed, ct);
            var result = await engine.RecognizeAsync(softwareBitmap);
            return Result.Ok(result.Text);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Screen capture failed: {ex.Message}");
        }
    }

    private static async Task<SoftwareBitmap> BitmapToSoftwareBitmapAsync(Bitmap bmp, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Seek(0, SeekOrigin.Begin);

        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync()
            .AsTask(ct);
    }
}
