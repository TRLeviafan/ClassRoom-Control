using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace ClassRoom_Control.Services.Student;

public static class ScreenShotService
{
    public static string CaptureThumbnailBase64(int targetWidth = 384, int targetHeight = 216, long quality = 70L)
    {
        try
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                screenWidth = 1920;
                screenHeight = 1080;
            }

            using var fullBitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(fullBitmap))
            {
                g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight), CopyPixelOperation.SourceCopy);
            }

            // Downscale to thumbnail
            using var thumbBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(thumbBitmap))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.DrawImage(fullBitmap, 0, 0, targetWidth, targetHeight);
            }

            // Encode to JPEG
            using var ms = new MemoryStream();
            var jpegCodec = GetEncoder(ImageFormat.Jpeg);
            if (jpegCodec != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                thumbBitmap.Save(ms, jpegCodec, encoderParams);
            }
            else
            {
                thumbBitmap.Save(ms, ImageFormat.Jpeg);
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Screenshot capture failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return null;
    }
}
