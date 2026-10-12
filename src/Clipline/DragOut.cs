using DrawingFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using Forms = System.Windows.Forms;

namespace Clipline;

/// <summary>
/// Drags a photo out as a real file, with the photo itself under the pointer
/// the way Explorer shows it. Each destination decides what that means:
/// an app takes a copy and the photo stays on the line; a folder or the
/// Desktop moves the file there, and the photo leaves the line.
/// </summary>
internal static class DragOut
{
    static Forms.Control? source;

    public static Forms.DragDropEffects Run(string path, BitmapSource thumb, Size photoSize, double scale)
    {
        source ??= new Forms.Control();
        var data = new Forms.DataObject();
        data.SetData(Forms.DataFormats.FileDrop, new[] { path });
        data.SetData(ClipboardPolicy.SelfFormat, "1");
        using var image = DragImage(thumb, photoSize, scale);
        var offset = new System.Drawing.Point(image.Width / 2, image.Height / 2);
        try
        {
            return source.DoDragDrop(data, Forms.DragDropEffects.Copy | Forms.DragDropEffects.Move, image, offset, false);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            return Forms.DragDropEffects.None;
        }
    }

    /// <summary>The photo with rounded corners, a little smaller than on the line, as a premultiplied bitmap.</summary>
    static System.Drawing.Bitmap DragImage(BitmapSource thumb, Size photoSize, double scale)
    {
        var w = Math.Max(1, (int)Math.Round(photoSize.Width * scale * 0.9));
        var h = Math.Max(1, (int)Math.Round(photoSize.Height * scale * 0.9));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var r = 12 * scale * 0.9;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            dc.DrawImage(thumb, new Rect(0, 0, w, h));
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1), new Rect(0.5, 0.5, w - 1, h - 1), r, r);
        }
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var bitmap = new System.Drawing.Bitmap(w, h, DrawingFormat.Format32bppArgb);
        var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, DrawingFormat.Format32bppArgb);
        try
        {
            straight.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * h, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
