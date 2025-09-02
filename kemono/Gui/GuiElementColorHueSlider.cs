using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

/// Renders a hue selector, e.g. a bar with gradient of
/// [RED ... GREEN ... BLUE ... RED]
public class GuiElementColorHueSlider : GuiElement
{
    // current hue value, [0, 1]
    public double currentHue = 0.0;

    LoadedTexture handleTexture;

    int unscaledHandleWidth;
    int unscaledHandleHeight;
    int unscaledHandleRadius;

    double handleWidth = 8;
    double handleHeight = 24;
    double handleRadius = 2;
    double padding = 0.0; // TODO configurable
    public int borderDepth = 2; // for rectangle emboss edge effect

    bool enabled = true;

    bool mouseDownOnSlider = false;
    bool mouseOnSlider = false;

    bool triggerOnMouseUp = false;
    bool didChangeValue = false;

    ActionConsumable<double> onNewSliderValue;

    public GuiElementColorHueSlider(ICoreClientAPI capi, ElementBounds bounds, double initialHue, ActionConsumable<double> onNewSliderValue) : base(capi, bounds)
    {
        handleTexture = new LoadedTexture(capi);

        this.currentHue = GameMath.Clamp(initialHue, 0.0, 1.0);
        this.onNewSliderValue = onNewSliderValue;

        unscaledHandleWidth = 8;
        unscaledHandleHeight = (int) bounds.fixedHeight - 4;
        unscaledHandleRadius = 2;
    }

    public void ComposeHandleTexture()
    {
        ImageSurface surfaceColor = new ImageSurface(Format.Argb32, (int)handleWidth, (int)handleHeight);
        Context ctx = genContext(surfaceColor);

        // fill rectangle with color
        ctx.SetSourceRGBA(1.0, 1.0, 1.0, 1.0);
        RoundRectangle(ctx, 0, 0, handleWidth, handleHeight, 4.0);
        ctx.Paint();

        ctx.SetSourceRGBA(0, 0, 0, 0.6);
        ctx.LineWidth = 4;
        ctx.Stroke();

        generateTexture(surfaceColor, ref handleTexture);
        ctx.Dispose();
        surfaceColor.Dispose();
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        handleWidth = scaled(unscaledHandleWidth) * Scale;
        handleHeight = scaled(unscaledHandleHeight) * Scale;
        handleRadius = scaled(unscaledHandleRadius) * Scale;
        Bounds.CalcWorldBounds();

        // surrounding inset rectangle
        EmbossRoundRectangleElement(ctx, Bounds, true, borderDepth);

        // draw hue gradient rectangle in background
        LinearGradient grad = new LinearGradient(
            Bounds.drawX, 0, // x0, y0
            Bounds.drawX + Bounds.OuterWidth, 0 // x1, y1
        );
        grad.AddColorStop(0,       new Color(1, 0, 0, 1)); // red    at x = 0/6
        grad.AddColorStop(1.0/6.0, new Color(1, 1, 0, 1)); // yellow at x = 1/6
        grad.AddColorStop(2.0/6.0, new Color(0, 1, 0, 1)); // green  at x = 2/6
        grad.AddColorStop(0.5,     new Color(0, 1, 1, 1)); // cyan   at x = 3/6
        grad.AddColorStop(4.0/6.0, new Color(0, 0, 1, 1)); // blue   at x = 4/6
        grad.AddColorStop(5.0/6.0, new Color(1, 0, 1, 1)); // purple at x = 5/6
        grad.AddColorStop(1,       new Color(1, 0, 0, 1)); // red    at x = 6/6

        ctx.SetSource(grad);
        Rectangle(ctx, Bounds);
        ctx.Fill();
        
        ComposeHandleTexture();

        grad.Dispose();
    }

    public override void Dispose()
    {
        base.Dispose();
        handleTexture.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        // draw handle white rectangle
        // note slider width must account for making sure handle appears
        // entirely within bounds
        // IDK WHY BUT NEED 2 PADDING OFFSET
        double sliderWidth = Bounds.InnerWidth - 2 * padding - handleWidth;

        // Translate current value into position
        // IDK WHY BUT NEED 2 PADDING OFFSET
        double handlePosition = sliderWidth * currentHue;
        double dy = (Bounds.InnerHeight - handleHeight + padding) / 2;

        api.Render.Render2DTexturePremultipliedAlpha(
            handleTexture.TextureId,
            Bounds.renderX + handlePosition,
            Bounds.renderY + dy,
            (int) handleWidth,
            (int) handleHeight
        );
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!enabled) return;

        if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;

        args.Handled = updateValue(api.Input.MouseX);

        mouseDownOnSlider = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        mouseDownOnSlider = false;

        if (!enabled) return;

        if (onNewSliderValue != null && didChangeValue && triggerOnMouseUp)
        {
            onNewSliderValue(currentHue);
        }

        didChangeValue = false;
    }


    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        mouseOnSlider = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);

        if (!enabled) return;

        if (mouseDownOnSlider)
        {
            args.Handled = updateValue(api.Input.MouseX);
        }
    }

    bool updateValue(int mouseX)
    {
        double sliderWidth = Bounds.InnerWidth - 2 * padding - handleWidth / 2;
        // Translate mouse position into current value
        double mouseDeltaX = GameMath.Clamp(mouseX - Bounds.renderX - padding, 0, sliderWidth);

        double newValue = mouseDeltaX / sliderWidth;

        bool didChangeNow = newValue != currentHue;

        if (didChangeNow) didChangeValue = true;
        currentHue = newValue;

        // Console.WriteLine($"[HueSlider] New hue: {currentHue}");

        if (onNewSliderValue != null)
        {
            if (!triggerOnMouseUp && didChangeNow) return onNewSliderValue(currentHue);
        }

        return true;
    }

    public void SetColor(int r, int g, int b)
    {
        var hsv = KemonoColorUtil.RgbToHsv(r, g, b);
        this.currentHue = hsv.h;
    }

    public void SetHue(double hue)
    {
        this.currentHue = GameMath.Clamp(hue, 0, 1);
    }
}


public static partial class GuiComposerHelpers
{
    public static GuiComposer AddColorHueSlider(
        this GuiComposer composer,
        ElementBounds bounds,
        double initialHue,
        ActionConsumable<double> onNewSliderValue,
        string key = null
    )
    {
        var elem = new GuiElementColorHueSlider(
            composer.Api,
            bounds,
            initialHue,
            onNewSliderValue
        );

        if (!composer.Composed)
        {
            composer.AddInteractiveElement(elem, key);
        }
        return composer;
    }

    public static GuiElementColorHueSlider GetColorHueSlider(this GuiComposer composer, string key)
    {
        return (GuiElementColorHueSlider) composer.GetElement(key);
    }
}
