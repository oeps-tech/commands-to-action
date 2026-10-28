using System.Text.Json;
using Oeps.CommandToAction.App;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Scanning;
using Scanner.Contracts;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace Oeps.CommandToAction.Tests;

public static class CommandBarcodeTests
{
    [Test]
    public static void CommandPayloadMatchesScannerChecksumVector()
    {
        Assert.Equal("C*Disable*7434FE41", CommandBarcode.PayloadFor(ActionCommand.Disable));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandBarcode.Create((ActionCommand)999));
    }

    [Test]
    public static async Task EveryGeneratedDataMatrixDecodesToAValidatedSelectableCommand()
    {
        await using var queue = new ActionQueue(new NoKeyboard());
        var dispatcher = new LabelDispatcher(queue);
        var payloads = new HashSet<string>();
        foreach (var descriptor in CommandCatalog.All)
        {
            var image = CommandBarcode.Create(descriptor.Command);
            var result = new DataMatrixReader().decode(new BinaryBitmap(new HybridBinarizer(
                new PlanarYUVLuminanceSource(image.Pixels, image.Width, image.Height, 0, 0, image.Width, image.Height, false))));
            Assert.True(result is not null, "Image did not decode: " + descriptor.Name);
            Assert.Equal(BarcodeFormat.DATA_MATRIX, result!.BarcodeFormat);
            Assert.Equal(image.Payload, result.Text);
            Assert.True(payloads.Add(result.Text), "Commands must have different codes.");
            var validated = PayloadValidator.Validate(result.Text);
            Assert.True(validated.Valid && validated.Data!.IsCommand);
            Assert.Equal(descriptor.Name, validated.Data!.Command);
            Assert.Equal(DispatchResult.CommandSelected, dispatcher.HandleJson(JsonSerializer.Serialize(validated.Data)));
            Assert.Equal(descriptor.Command, dispatcher.SelectedCommand);
            Assert.Equal(0, queue.PendingCount);
        }
        Assert.Equal(7, payloads.Count);
    }

    [Test]
    public static void ImagesAreHighResolutionWithSolidWhiteBordersAndNoAntialiasing()
    {
        foreach (var descriptor in CommandCatalog.All)
        {
            var image = CommandBarcode.Create(descriptor.Command);
            Assert.Equal(1024, image.Width);
            Assert.Equal(1024, image.Height);
            Assert.True(image.Pixels.All(value => value is 0 or 255));
            Assert.True(image.Pixels.Any(value => value == 0));
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (x < 32 || y < 32 || x >= image.Width - 32 || y >= image.Height - 32)
                    Assert.Equal((byte)255, image.Pixels[y * image.Width + x]);
        }
    }

    private sealed class NoKeyboard : IActionKeyboard
    {
        public Task TypeTextAsync(string text, CancellationToken cancellationToken) => throw new Exception("A command code must not type text.");
        public Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken) => throw new Exception("A command code must not send keys.");
    }
}
