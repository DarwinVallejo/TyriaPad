# Generates src/TyriaPad.App/Assets/TyriaPad.ico from TyriaPad.png (in the same folder).
#   powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
# Crops the extra black background (the drawing ends up bigger in the tray) and saves the sizes
# Windows uses: 16-64 px as bitmaps (any API can read them, NotifyIcon too) and 256 px
# as PNG (Explorer and the taskbar at high scaling). It only needs to be rerun if
# the image changes: the .ico is in the repo.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\TyriaPad.App\Assets'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IconMaker
{
    public static void Make(string source, string target, int[] sizes)
    {
        using (var image = new Bitmap(source))
        {
            Rectangle crop = ContentSquare(image);
            var frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (var frame = Resize(image, crop, size))
                {
                    frames.Add(size >= 256 ? Png(frame) : Dib(frame));
                }
            }

            using (var file = new BinaryWriter(File.Create(target)))
            {
                file.Write((short)0);
                file.Write((short)1);
                file.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    file.Write((byte)0);
                    file.Write((byte)0);
                    file.Write((short)1);
                    file.Write((short)32);
                    file.Write(frames[i].Length);
                    file.Write(offset);
                    offset += frames[i].Length;
                }

                foreach (byte[] frame in frames)
                {
                    file.Write(frame);
                }
            }
        }
    }

    // Square centered on what isn't black background, with a 3% margin.
    private static Rectangle ContentSquare(Bitmap image)
    {
        byte[] pixels = Bgra(image);
        int w = image.Width, h = image.Height;
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 120)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (right < 0)
        {
            return new Rectangle(0, 0, w, h);
        }

        int side = (int)(Math.Max(right - left + 1, bottom - top + 1) * 1.06);
        side = Math.Min(side, Math.Min(w, h));
        int cx = (left + right) / 2, cy = (top + bottom) / 2;
        int sx = Math.Max(0, Math.Min(w - side, cx - side / 2));
        int sy = Math.Max(0, Math.Min(h - side, cy - side / 2));
        return new Rectangle(sx, sy, side, side);
    }

    private static Bitmap Resize(Bitmap image, Rectangle crop, int size)
    {
        var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(image, new Rectangle(0, 0, size, size), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, attributes);
        }

        return frame;
    }

    private static byte[] Png(Bitmap frame)
    {
        using (var stream = new MemoryStream())
        {
            frame.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    // BITMAPINFOHEADER + bottom-up BGRA + empty AND mask (transparency goes in the alpha).
    private static byte[] Dib(Bitmap frame)
    {
        int size = frame.Width;
        byte[] pixels = Bgra(frame);
        int maskStride = ((size + 31) / 32) * 4;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            for (int y = size - 1; y >= 0; y--)
            {
                writer.Write(pixels, y * size * 4, size * 4);
            }

            writer.Write(new byte[maskStride * size]);
            return stream.ToArray();
        }
    }

    private static byte[] Bgra(Bitmap image)
    {
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        BitmapData data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * image.Width * 4, image.Width * 4);
            }

            return pixels;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }
}
'@

$target = Join-Path $assets 'TyriaPad.ico'
[IconMaker]::Make((Join-Path $assets 'TyriaPad.png'), $target, @(16, 20, 24, 32, 40, 48, 64, 256))
Write-Host "Done: $target ($([math]::Round((Get-Item $target).Length / 1KB)) KB)"
