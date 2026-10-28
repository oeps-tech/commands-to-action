using Oeps.CommandToAction.Core.Commands;
using Scanner.Contracts;
using ZXing;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;

namespace Oeps.CommandToAction.App;

internal sealed record CommandBarcodeRaster(string Payload, int Width, int Height, byte[] Pixels);

internal static class CommandBarcode
{
    internal const int ImageSize = 1024;
    internal const int Dpi = 300;

    internal static string PayloadFor(ActionCommand command)
    {
        var body = "C*" + CommandCatalog.Get(command).Name;
        return body + "*" + PayloadValidator.Crc32(body);
    }

    internal static CommandBarcodeRaster Create(ActionCommand command)
    {
        var payload = PayloadFor(command);
        var matrix = new DataMatrixWriter().encode(payload, BarcodeFormat.DATA_MATRIX, ImageSize, ImageSize,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE,
                [EncodeHintType.MARGIN] = 4
            });
        var pixels = new byte[matrix.Width * matrix.Height];
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
            pixels[y * matrix.Width + x] = matrix[x, y] ? (byte)0 : (byte)255;
        return new(payload, matrix.Width, matrix.Height, pixels);
    }
}
