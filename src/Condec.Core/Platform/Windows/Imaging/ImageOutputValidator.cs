using Condec.Core.Conversion;
using Condec.Core.Formats;
using Windows.Graphics.Imaging;

namespace Condec.Core.Imaging;

/// <summary>Reopens a converted image with the Windows decoder for its format and decodes every pixel.</summary>
public sealed class ImageOutputValidator : IOutputValidator
{
    public bool CanValidate(string targetExtension) =>
        ImageFormats.FindTarget(FileExtension.Normalize(targetExtension)) is not null;

    public async Task ValidateAsync(string path, string targetExtension, CancellationToken ct)
    {
        var expected = ImageFormats.FindTarget(FileExtension.Normalize(targetExtension))
            ?? throw new NotSupportedException($"'{targetExtension}' is not an image target.");

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

        if (decoder.DecoderInformation.CodecId != expected.DecoderId)
        {
            throw new InvalidDataException(
                $"Expected a {targetExtension} image, but Windows reads it as {decoder.DecoderInformation.FriendlyName}.");
        }

        if (decoder.FrameCount == 0 || decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
        {
            throw new InvalidDataException("The image has no pixels.");
        }

        // Decoding the pixels proves the image data is readable, not only its header.
        _ = await decoder.GetPixelDataAsync().AsTask(ct).ConfigureAwait(false);
    }
}
