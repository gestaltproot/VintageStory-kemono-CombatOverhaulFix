using System;
using SkiaSharp;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

/// Based on BakedBitmap from vintage story api:
/// https://github.com/anegostudios/vsapi/blob/master/Common/Texture/BitmapRef.cs/
/// 
/// NOTE THAT VINTAGE SHIT USES (B,G,R,A) FORMAT
public class BitmapSimple: IBitmap
{
    public int[] TexturePixels;
    public int Width;
    public int Height;

    /// Creates a new bitmap from the given bitmap.
    public BitmapSimple(IBitmap bmp)
    {
        Width = bmp.Width;
        Height = bmp.Height;
        TexturePixels = (int[])bmp.Pixels.Clone();
    }

    /// Creates a new bitmap from width and height.
    public BitmapSimple(int width, int height)
    {
        Width = width;
        Height = height;
        TexturePixels = new int[width * height];
    }

    /// Creates a new bitmap from width and height and pixels.
    /// Note: creates new pixels and copies in the input pixels.
    public BitmapSimple(int width, int height, int[] pixels)
    {
        Width = width;
        Height = height;
        TexturePixels = new int[width * height];
        Array.Copy(pixels, TexturePixels, pixels.Length);
    }

    public int[] Pixels
    {
        get
        {
            return TexturePixels;
        }
    }

    int IBitmap.Width => Width;

    int IBitmap.Height => Height;

    public SKColor GetPixel(int x, int y)
    {
        var rgb = KemonoColorUtil.ToRgb(TexturePixels[Width * y + x]);
        return new SKColor((byte)rgb.r, (byte)rgb.g, (byte)rgb.b);
    }

    public int GetPixelArgb(int x, int y)
    {
        return TexturePixels[Width * y + x];
    }

    public SKColor GetPixelRel(float x, float y)
    {
        var rgb = KemonoColorUtil.ToRgb(TexturePixels[Width * (int)(y * Height) + (int)(x * Width)]);
        return new SKColor((byte)rgb.r, (byte)rgb.g, (byte)rgb.b);
    }

    /// Safely gets pixel from (x, y) in normalized coordinates from [0,1],
    /// where (0,0) is the top left and (1,1) is the bottom right.
    /// Clamps to the bounds of the bitmap if out of bounds.
    public int GetPixelClamped(double x, double y)
    {
        // convert x, y to int coords, clamp in width/height
        int ix = (int) Math.Floor(x * Width);
        int iy = (int) Math.Floor(y * Height);
        ix = GameMath.Clamp(ix, 0, Width - 1);
        iy = GameMath.Clamp(iy, 0, Height - 1);

        return TexturePixels[Width * iy + ix];
    }

    public void SetPixel(int x, int y, int col)
    {
        TexturePixels[Width * y + x] = col;

    }

    /// Set pixel with (x, y) in normalized coordinates from [0,1],
    /// where (0,0) is the top left and (1,1) is the bottom right.
    public void SetPixel(double x, double y, int col)
    {
        // convert x, y to int coords, clamp in width/height
        int ix = (int) Math.Floor(x * Width);
        int iy = (int) Math.Floor(y * Height);
        ix = GameMath.Clamp(ix, 0, Width - 1);
        iy = GameMath.Clamp(iy, 0, Height - 1);

        TexturePixels[Width * iy + ix] = col;
    }
    
    /// Set pixels within radius r from (x, y) in normalized coordinates
    /// from [0,1], where (0, 0) is the top left and (1,1) is the
    /// bottom right.
    public void SetPixelsInRadius(double x, double y, double r, int col)
    {
        // get center point ixCenter and iyCenter
        int ixCenter = (int) Math.Floor(x * Width);
        int iyCenter = (int) Math.Floor(y * Height);
        ixCenter = GameMath.Clamp(ixCenter, 0, Width - 1);
        iyCenter = GameMath.Clamp(iyCenter, 0, Height - 1);

        // determine integer ix, iy range square around (x, y)
        int ixMin = (int) Math.Floor((x - r) * Width);
        int ixMax = (int) Math.Ceiling((x + r) * Width);
        int iyMin = (int) Math.Floor((y - r) * Height);
        int iyMax = (int) Math.Ceiling((y + r) * Height);

        // clamp to width/height
        ixMin = GameMath.Clamp(ixMin, 0, Width - 1);
        ixMax = GameMath.Clamp(ixMax, 0, Width - 1);
        iyMin = GameMath.Clamp(iyMin, 0, Height - 1);
        iyMax = GameMath.Clamp(iyMax, 0, Height - 1);

        // iterate from [ixMin, ixMax] and [iyMin, iyMax]
        // set pixels if within radius r
        double r2 = r * r;
        for (int ix = ixMin; ix <= ixMax; ix++)
        {
            for (int iy = iyMin; iy <= iyMax; iy++)
            {
                double dx = (ix - ixCenter) / (double) Width;
                double dy = (iy - iyCenter) / (double) Height;
                double d2 = dx * dx + dy * dy;
                if (d2 <= r2)
                {
                    TexturePixels[Width * iy + ix] = col;
                }
            }
        }
    }

    public int[] GetPixelsTransformed(int rot = 0, int alpha = 100)
    {
        int[] bmpPixels = new int[Width * Height];

        // Could be more compact, but this is therefore more efficient
        switch (rot)
        {
            case 0:
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        bmpPixels[x + y * Width] = GetPixelArgb(x, y);
                    }
                }
                break;
            case 90:
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        bmpPixels[y + x * Width] = GetPixelArgb(Width - x - 1, y);
                    }
                }
                break;
            case 180:
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        bmpPixels[x + y * Width] = GetPixelArgb(Width - x - 1, Height - y - 1);
                    }
                }
                break;

            case 270:
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        bmpPixels[y + x * Width] = GetPixelArgb(x, Height - y - 1);
                    }
                }
                break;
        }

        if (alpha != 100)
        {
            float af = alpha / 100f;
            int clearAlpha = ~(0xff << 24);
            for (int i = 0; i < bmpPixels.Length; i++)
            {
                int col = bmpPixels[i];
                int a = (col >> 24) & 0xff;
                col &= clearAlpha;

                bmpPixels[i] = col | ((int)(a * af) << 24);
            }
        }

        return bmpPixels;
    }

    /// Set each pixel in this bitmap to the given color.
    public void Fill(int col)
    {
        for (int i = 0; i < TexturePixels.Length; i++)
        {
            TexturePixels[i] = col;
        }
    }

    /// Multiply each pixel in this bitmap by the given color.
    public void MultiplyRgb(int col)
    {
        var (r, g, b, a) = KemonoColorUtil.ToRgba(col);
        
        for (int i = 0; i < TexturePixels.Length; i++)
        {
            var (pb, pg, pr, pa) = KemonoColorUtil.ToRgba(TexturePixels[i]);

            int newPixel = ColorUtil.ColorFromRgba(
                pb * b / 255,
                pg * g / 255,
                pr * r / 255,
                pa * a / 255
            );

            TexturePixels[i] = newPixel;
        }
    }
}
