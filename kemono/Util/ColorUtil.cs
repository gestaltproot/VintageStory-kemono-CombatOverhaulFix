using Newtonsoft.Json;
using System;
using Vintagestory.API.MathTools;

namespace kemono;

/// Packed RGB color in range [0, 255].
/// For JSON serialization.
public struct KemonoColorRGB
{
    public int r { get; init; }
    public int g { get; init; }
    public int b { get; init; }

    public KemonoColorRGB() {}

    public KemonoColorRGB(int r, int g, int b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public KemonoColorRGB(int rgba)
    {
        (r, g, b) = KemonoColorUtil.ToRgb(rgba);
    }
}

/// Additional color utility functions. For color packing
/// conventions, see:
/// https://github.com/anegostudios/vsapi/blob/master/Math/ColorUtil.cs
public static class KemonoColorUtil
{
    /// Convert packed RGBA integer to RGB integers in range [0, 255].
    public static (int r, int g, int b) ToRgb(int rgba)
    {
        return (
            ((rgba >>  0) & 0xff), // r
            ((rgba >>  8) & 0xff), // g
            ((rgba >> 16) & 0xff)  // b
        );
    }

    /// Convert packed RGBA integer to RGB integers in range [0, 255].
    public static (int r, int g, int b, int a) ToRgba(int rgba)
    {
        return (
            ((rgba >>  0) & 0xff), // r
            ((rgba >>  8) & 0xff), // g
            ((rgba >> 16) & 0xff), // b
            ((rgba >> 24) & 0xff)  // a
        );
    }

    /// <summary>
    /// Flip packed RGBA integer from RGBA to BGRA or vice versa.
    /// </summary>
    /// <param name="rgba"></param>
    /// <returns></returns>
    public static int FlipRgb(int rgba)
    {
        int r = (rgba >>  0) & 0xff;
        int g = (rgba >>  8) & 0xff;
        int b = (rgba >> 16) & 0xff;
        int a = (rgba >> 24) & 0xff;
        return (a << 24) | (r << 16) | (g << 8) | (b << 0);

    }

    /// Returns (h, s, v) calculated from RGB values in range [0, 1].
    public static (double h, double s, double v) RgbToHsv(double r, double g, double b)
    {
        double h, s, v;

        double cmin = Math.Min(Math.Min(r, g), b);
        double cmax = Math.Max(Math.Max(r, g), b);
        double c = cmax - cmin; // chroma

        v = cmax;

        // avoid divide by c = 0 below
        if (c < 1e-9)
        {
            h = 0;
            s = 0;
            return (h, s, v);
        }

        if (cmax > 0)
        {
            s = c / cmax;
        }
        else
        {
            // r = g = b = 0
            // s = 0, v is undefined
            h = 0;
            s = 0;
            v = 0;
            return (h, s, v);
        }

        if (r == cmax)
        {
            h = (g < b ? 6.0 : 0.0) + (g - b) / c; // between yellow - magenta
        }
        else if (g == cmax)
        {
            h = 2.0 + (b - r) / c; // between cyan - yellow
        }
        else
        {
            h = 4.0 + (r - g) / c; // between magenta - cyan
        }

        if (h < 0.0)
        {
            h += 6.0;
        }

        // normalize h to [0, 1]
        h /= 6.0;

        // IN DEGREES, may have better precision but more conversions needed
        // h *= 60.0; // degrees
        // if (h < 0.0)
        // {
        //     h += 360.0;
        // }

        return (h, s, v);
    }

    /// Returns (h, s, v) calculated from RGB values in range [0, 255].
    public static (double h, double s , double v) RgbToHsv(int red, int green, int blue)
    {
        return RgbToHsv(red / 255.0, green / 255.0, blue / 255.0);
    }

    /// Returns (r, g, b) in [0, 255] calculated from HSV values in range [0, 1].
    public static (int r, int g, int b) HsvToRgb(double hue, double sat, double val)
    {
        double hueScaled = hue * 6.0;
        double c = sat * val;
        double x = c * (1 - Math.Abs((hueScaled % 2) - 1));
        double m = val - c;

        double r, g, b;
        switch (Math.Floor(hueScaled % 6)) {
            case 0: r = c + m; g = x + m; b = m    ; break;
            case 1: r = x + m; g = c + m; b = m    ; break;
            case 2: r = m    ; g = c + m; b = x + m; break;
            case 3: r = m    ; g = x + m; b = c + m; break;
            case 4: r = x + m; g = m    ; b = c + m; break;
            case 5: r = c + m; g = m    ; b = x + m; break;
            default: r = m; g = m; b = m; break;
        }

        // convert to integer [0, 255]
        int red = GameMath.Clamp((int) Math.Round(r * 255.0), 0, 255);
        int green = GameMath.Clamp((int) Math.Round(g * 255.0), 0, 255);
        int blue = GameMath.Clamp((int) Math.Round(b * 255.0), 0, 255);

        return (red, green, blue);
    }
}
