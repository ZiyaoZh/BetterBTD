using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace BetterBTD.Helpers.Extensions;

public static class MatExtension
{
    public static WriteableBitmap ToWriteableBitmap(this Mat mat)
    {
        var pixelFormat = mat.Type() switch
        {
            var type when type == MatType.CV_8UC3 => PixelFormats.Bgr24,
            var type when type == MatType.CV_8UC4 => PixelFormats.Bgra32,
            _ => throw new NotSupportedException($"Unsupported pixel format {mat.Type()}")
        };

        var bitmap = new WriteableBitmap(mat.Width, mat.Height, 96, 96, pixelFormat, null);
        mat.UpdateWriteableBitmap(bitmap);
        return bitmap;
    }

    public static unsafe void UpdateWriteableBitmap(this Mat mat, WriteableBitmap bitmap)
    {
        bitmap.Lock();
        try
        {
            var stride = bitmap.BackBufferStride;
            var step = checked((int)mat.Step());
            if (stride == step)
            {
                var length = stride * bitmap.PixelHeight;
                Buffer.MemoryCopy(mat.Data.ToPointer(), bitmap.BackBuffer.ToPointer(), length, length);
            }
            else
            {
                var rowLength = Math.Min(stride, step);
                for (var rowIndex = 0; rowIndex < bitmap.PixelHeight; rowIndex++)
                {
                    Buffer.MemoryCopy(
                        (void*)(mat.Data + rowIndex * step),
                        (void*)(bitmap.BackBuffer + rowIndex * stride),
                        rowLength,
                        rowLength);
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        }
        finally
        {
            bitmap.Unlock();
        }
    }
}
