using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

/// Renders a color selector, for a fixed hue value with
/// a saturation/brightness gradient.
public class GuiElementColorSatValSelector : GuiElement
{
    // color in selector, automatically set from hue
    public double r; // [0, 1]
    public double g; // [0, 1]
    public double b; // [0, 1]

    // current HSV saturation and value, [0, 1]
    public double currentSat = 0.0;
    public double currentVal = 0.0;

    // positions in color selector region, [0, 1]
    public double posX = 0; // = currentSat
    public double posY = 0; // = 1 - currentVal

    LoadedTexture colorTexture;
    LoadedTexture openCircleTexture;

    int unscaledSelectorSize;

    double selectorCircleSize = 8;
    double padding = 0.0; // TODO configurable
    public int borderDepth = 2; // for rectangle emboss edge effect

    bool enabled = true;

    bool mouseDownOnSlider = false;
    bool mouseOnSlider = false;

    bool triggerOnMouseUp = false;
    bool didChange = false;

    ActionConsumable<double, double> onNewSatVal;

    public GuiElementColorSatValSelector(
        ICoreClientAPI capi,
        ElementBounds bounds,
        double initialHue,
        double initialSat,
        double initialVal,
        ActionConsumable<double, double> onNewSatVal
    ) : base(capi, bounds)
    {
        colorTexture = new LoadedTexture(capi);
        openCircleTexture = new LoadedTexture(capi);

        currentSat = initialSat;
        currentVal = initialVal;

        posX = currentSat;
        posY = currentVal;
        
        // set background color from hue with max sat, val
        var rgb = KemonoColorUtil.HsvToRgb(initialHue, 1.0, 1.0);
        this.r = rgb.r / 255.0;
        this.g = rgb.g / 255.0;
        this.b = rgb.b / 255.0;

        this.onNewSatVal = onNewSatVal;

        unscaledSelectorSize = 10;
    }

    /// Creates open circle indicating current selected color.
    public void ComposeSelectorCircleTexture()
    {
        ImageSurface surface = new ImageSurface(Format.Argb32, (int)selectorCircleSize, (int)selectorCircleSize);
        Context ctx = genContext(surface);

        // make empty stroked circle
        RoundRectangle(ctx, 0, 0, selectorCircleSize, selectorCircleSize, selectorCircleSize/2.0);
        ctx.SetSourceRGBA(1, 1, 1, 1);
        ctx.LineWidth = 2;
        ctx.Stroke();

        generateTexture(surface, ref openCircleTexture);
        ctx.Dispose();
        surface.Dispose();
    }

    /// Create color rectangle texture with gradient for color
    /// and lightness. 
    public void ComposeColorTexture()
    {
        ImageSurface surface = new ImageSurface(Format.Argb32, (int)Bounds.InnerWidth, (int)Bounds.InnerHeight);
        Context ctx = genContext(surface);

        // hue gradient (color to white)
        LinearGradient gradColor = new LinearGradient(
            0, 0, // x0, y0
            Bounds.InnerWidth, 0 // x1, y1
        );
        gradColor.AddColorStop(0, new Color(1, 1, 1, 1)); // white at x = 0
        gradColor.AddColorStop(1, new Color(r, g, b, 1)); // color at x = 1

        // lightness gradient (black to transparent)
        LinearGradient gradLightness = new LinearGradient(
            0, 0, // x0, y0
            0, Bounds.InnerHeight // x1, y1
        );
        gradLightness.AddColorStop(0, new Color(0, 0, 0, 0)); // transparent at y = 0
        gradLightness.AddColorStop(1, new Color(0, 0, 0, 1)); // black at y = 1

        Rectangle(ctx, Bounds);
        ctx.SetSource(gradColor);
        ctx.Paint();
        ctx.SetSource(gradLightness);
        ctx.Paint();

        generateTexture(surface, ref colorTexture);

        gradLightness.Dispose();
        gradColor.Dispose();
        ctx.Dispose();
        surface.Dispose();
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        selectorCircleSize = scaled(unscaledSelectorSize) * Scale;
        Bounds.CalcWorldBounds();

        // surrounding inset rectangle
        EmbossRoundRectangleElement(ctx, Bounds, true, borderDepth);
        
        ComposeColorTexture();
        ComposeSelectorCircleTexture();
    }

    public override void Dispose()
    {
        base.Dispose();
        colorTexture.Dispose();
        openCircleTexture.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        // color rectangle
        api.Render.Render2DTexturePremultipliedAlpha(
            colorTexture.TextureId,
            Bounds.renderX + padding,
            Bounds.renderY + padding,
            (int) Bounds.InnerWidth,
            (int) Bounds.InnerHeight
        );

        // selector circle
        double circlePositionX = (posX * Bounds.InnerWidth) + padding - selectorCircleSize / 2.0;
        double circlePositionY = (posY * Bounds.InnerHeight) + padding - selectorCircleSize / 2.0;

        api.Render.Render2DTexturePremultipliedAlpha(
            openCircleTexture.TextureId,
            Bounds.renderX + circlePositionX,
            Bounds.renderY + circlePositionY,
            (int) selectorCircleSize,
            (int) selectorCircleSize
        );
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!enabled) return;

        if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;

        args.Handled = updateValue(api.Input.MouseX, api.Input.MouseY);

        mouseDownOnSlider = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        mouseDownOnSlider = false;

        if (!enabled) return;

        if (onNewSatVal != null && didChange && triggerOnMouseUp)
        {
            onNewSatVal(currentSat, currentVal);
        }

        didChange = false;
    }


    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        mouseOnSlider = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);

        if (!enabled) return;

        if (mouseDownOnSlider)
        {
            args.Handled = updateValue(api.Input.MouseX, api.Input.MouseY);
        }
    }

    bool updateValue(int mouseX, int mouseY)
    {
        double width = Bounds.InnerWidth - 2 * padding;
        double height = Bounds.InnerHeight - 2 * padding;

        // Translate mouse position into current value
        double mouseDeltaX = GameMath.Clamp(mouseX - Bounds.renderX - padding, 0, width);
        double mouseDeltaY = GameMath.Clamp(mouseY - Bounds.renderY - padding, 0, height);
        
        posX = mouseDeltaX / width;
        posY = mouseDeltaY / height;

        double newSat = posX;
        double newVal = 1.0 - posY;

        bool didChangeNow = (newSat != currentSat) || (newVal != currentVal);

        if (didChangeNow) didChange = true;
        
        currentSat = newSat;
        currentVal = newVal;

        // Console.WriteLine($"[SatValSelector] New sat = {currentSat} New val = {currentVal}");

        if (onNewSatVal != null)
        {
            if (!triggerOnMouseUp && didChangeNow) return onNewSatVal(currentSat, currentVal);
        }

        return true;
    }

    public void UpdateHsv()
    {
        var hsv = KemonoColorUtil.RgbToHsv(this.r, this.g, this.b);
        currentSat = hsv.s;
        currentVal = hsv.v;

        posX = currentSat;
        posY = currentVal;
    }

    public void SetHue(double newHue)
    {
        var rgb = KemonoColorUtil.HsvToRgb(newHue, 1.0, 1.0);
        this.r = rgb.r / 255.0;
        this.g = rgb.g / 255.0;
        this.b = rgb.b / 255.0;
        ComposeColorTexture();
    }

    public void SetHueSatVal(double newHue, double newSat, double newVal)
    {
        currentSat = newSat;
        currentVal = newVal;

        posX = currentSat;
        posY = 1.0 - currentVal;

        var rgb = KemonoColorUtil.HsvToRgb(newHue, 1.0, 1.0);
        this.r = rgb.r / 255.0;
        this.g = rgb.g / 255.0;
        this.b = rgb.b / 255.0;
        ComposeColorTexture();
    }
}


public static partial class GuiComposerHelpers
{
    public static GuiComposer AddColorSatValSelector(
        this GuiComposer composer,
        ElementBounds bounds,
        double initialHue,
        double initialSat,
        double initialVal,
        ActionConsumable<double, double> onNewSatVal,
        string key = null
    )
    {
        var elem = new GuiElementColorSatValSelector(
            composer.Api,
            bounds,
            initialHue,
            initialSat,
            initialVal,
            onNewSatVal
        );

        if (!composer.Composed)
        {
            composer.AddInteractiveElement(elem, key);
        }
        return composer;
    }

    public static GuiElementColorSatValSelector GetColorSatValSelector(this GuiComposer composer, string key)
    {
        return (GuiElementColorSatValSelector) composer.GetElement(key);
    }
}
