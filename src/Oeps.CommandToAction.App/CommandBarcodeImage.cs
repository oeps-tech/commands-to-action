using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.App;

internal static class CommandBarcodeImage
{
    internal static Bitmap Create(ActionCommand command)
    {
        var raster = CommandBarcode.Create(command);
        var bitmap = new Bitmap(raster.Width, raster.Height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(CommandBarcode.Dpi, CommandBarcode.Dpi);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[data.Stride];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                        row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = raster.Pixels[y * bitmap.Width + x];
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    internal static void CopyToClipboard(Bitmap image)
    {
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        png.Position = 0;
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, true, image);
        data.SetData("PNG", false, png);
        // Persist both formats after the image/stream are disposed and the application exits.
        Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 100);
    }
}
