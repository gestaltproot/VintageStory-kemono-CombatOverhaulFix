using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace kemono;

/// Renders a colored rectangle.
public class GuiElementColorRectangle : GuiElement
{
    public int r;     // [0, 255]
    public int g;     // [0, 255]
    public int b;     // [0, 255]
    public int alpha; // [0, 255]
    public int rgba;  // packed RGBA in integer format
    public bool useHsv; // if true, also store and calculate hsv
    public double hue = 0.0; // [0, 1]
    public double sat = 0.0; // [0, 1]
    public double val = 0.0; // [0, 1]

    // gui emboss outline depth
    public int depth = 2;

    LoadedTexture colorTexture;
    ElementBounds colorBounds;

    public GuiElementColorRectangle(ICoreClientAPI capi, ElementBounds bounds, int r, int g, int b, int alpha, bool useHsv) : base(capi, bounds)
    {
        colorTexture = new LoadedTexture(capi);
        this.r = r;
        this.g = g;
        this.b = b;
        this.alpha = alpha;
        this.rgba = ColorUtil.ColorFromRgba(r, g, b, alpha);
        this.useHsv = useHsv;

        if (useHsv)
        {
            (hue, sat, val) = KemonoColorUtil.RgbToHsv(r, g, b);
        }
    }

    public void ComposeColor()
    {
        ImageSurface surfaceColor = new ImageSurface(Format.Argb32, (int)Bounds.OuterWidth, (int)Bounds.OuterHeight);
        Context ctx = genContext(surfaceColor);

        ctx.SetSourceRGBA(r / 255.0f, g / 255.0f, b / 255.0f, alpha / 255.0f);
        ctx.Paint();

        generateTexture(surfaceColor, ref colorTexture);

        ctx.Dispose();
        surfaceColor.Dispose();
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();

        EmbossRoundRectangleElement(ctx, Bounds, true, depth);

        ComposeColor();

        colorBounds = Bounds.FlatCopy();
        colorBounds.CalcWorldBounds();
    }

    public override void Dispose()
    {
        base.Dispose();
        colorTexture.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        api.Render.Render2DTexturePremultipliedAlpha(
            colorTexture.TextureId,
            colorBounds.renderX + colorBounds.absPaddingX,
            colorBounds.renderY + colorBounds.absPaddingY,
            colorBounds.InnerWidth,
            colorBounds.InnerHeight
        );
    }

    public void SetColor(int r, int g, int b, int alpha = 255)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.alpha = alpha;
        this.rgba = ColorUtil.ColorFromRgba(r, g, b, alpha);

        if (colorTexture != null && colorBounds != null)
        {
            ComposeColor();
        }
    }

    public int GetRGBIndex(int index)
    {
        switch (index)
        {
            case 0:
                return this.r;
            case 1:
                return this.g;
            case 2:
                return this.b;
            case 3:
                return this.alpha;
            default:
                return -1;
        }
    }

    public void SetRGBIndex(int index, int val)
    {
        int valClamped = GameMath.Clamp(val, 0, 255);
        
        switch (index)
        {
            case 0:
                this.r = valClamped;
                break;
            case 1:
                this.g = valClamped;
                break;
            case 2:
                this.b = valClamped;
                break;
            case 3:
                this.alpha = valClamped;
                break;
        }

        this.rgba = ColorUtil.ColorFromRgba(r, g, b, alpha);

        if (colorTexture != null && colorBounds != null)
        {
            ComposeColor();
        }
    }

    /// Call this after setting hue, sat, or val to recalculate rgb
    /// and re-render RGB color rectangle texture.
    public void RecalculateRgbFromHsv()
    {
        (r, g, b) = KemonoColorUtil.HsvToRgb(hue, sat, val);
        rgba = ColorUtil.ColorFromRgba(r, g, b, alpha);

        if (colorTexture != null && colorBounds != null)
        {
            ComposeColor();
        }
    }

    /// Set HSV hue component in range [0, 1]
    public void SetHue(double newHue)
    {
        hue = newHue;
        RecalculateRgbFromHsv();
    }

    /// Set HSV sat component in range [0, 1]
    public void SetSat(double newSat)
    {
        sat = newSat;
        RecalculateRgbFromHsv();
    }

    /// Set HSV value component in range [0, 1]
    public void SetVal(double newVal)
    {
        val = newVal;
        RecalculateRgbFromHsv();
    }
}


public static partial class GuiComposerHelpers
{
    public static GuiComposer AddColorRectangle(
        this GuiComposer composer,
        ElementBounds bounds,
        int r,
        int g,
        int b,
        int alpha = 255,
        bool useHsv = false,
        string key = null
    )
    {
        var elem = new GuiElementColorRectangle(composer.Api, bounds, r, g, b, alpha, useHsv);

        if (!composer.Composed)
        {
            composer.AddInteractiveElement(elem, key);
        }

        return composer;
    }

    public static GuiElementColorRectangle GetColorRectangle(this GuiComposer composer, string key)
    {
        return (GuiElementColorRectangle) composer.GetElement(key);
    }
}
