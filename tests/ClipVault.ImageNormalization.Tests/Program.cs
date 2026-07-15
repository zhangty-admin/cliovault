using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var detectorType = typeof(ClipVault.Helpers.ClipboardFormatDetector);
var normalize = detectorType.GetMethod(
    "NormalizeClipboardImage",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("NormalizeClipboardImage not found.");

AssertNormalized(
    input: [10, 20, 30, 0, 40, 50, 60, 0],
    expectedAlpha: [255, 255],
    description: "zero alpha is repaired");

AssertNormalized(
    input: [10, 20, 30, 0, 40, 50, 60, 128],
    expectedAlpha: [0, 128],
    description: "meaningful alpha is preserved");

Console.WriteLine("Image normalization tests passed.");

void AssertNormalized(byte[] input, byte[] expectedAlpha, string description)
{
    var source = BitmapSource.Create(
        2, 1, 96, 96, PixelFormats.Bgra32, null, input, 8);
    var normalized = (BitmapSource?)normalize.Invoke(null, [source])
        ?? throw new InvalidOperationException("Normalization returned null.");
    var output = new byte[8];
    normalized.CopyPixels(output, 8, 0);
    var actualAlpha = new[] { output[3], output[7] };

    if (!actualAlpha.SequenceEqual(expectedAlpha))
        throw new InvalidOperationException(
            $"{description}: expected [{string.Join(',', expectedAlpha)}], " +
            $"actual [{string.Join(',', actualAlpha)}]");
    if (!normalized.IsFrozen)
        throw new InvalidOperationException($"{description}: bitmap must be frozen.");
}
