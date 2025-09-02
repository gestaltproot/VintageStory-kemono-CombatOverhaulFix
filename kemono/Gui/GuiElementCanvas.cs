using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

/// Canvas gui paint modes.
/// Currently unused, hard-coded ctrl for eraser.
// public enum CanvasPaintMode
// {
//     Paint,
//     Erase,
// }

/// Renders a canvas with paintable bitmap.
public class GuiElementCanvas : GuiElement
{
    private LoadedTexture canvasTexture; // bitmap texture being painted
    private LoadedTexture backgroundTexture; // canvas background texture
    private LoadedTexture openCircleTexture; // open circle paint brush indicator
    private LoadedTexture openSquareTexture; // open square paint brush indicator for color selector

    // bitmap for canvas texture
    public int DEFAULT_CANVAS_SIZE = 32; // hard-coded 32x32 canvas size, if no initial bitmap provided
    public BitmapSimple Canvas;

    // callback when painting
    public ActionConsumable OnPaint;
    // callback when selecting color from pixel on canvas
    public ActionConsumable<int> OnSelectColor;

    // canvas background options
    public bool UseBackground = false;
    public int BackgroundColor = ColorUtil.ColorFromRgba(255, 255, 255, 255);

    // paint brush cursor screen color
    public int CursorColor = ColorUtil.ColorFromRgba(255, 255, 255, 255);

    // paint color
    public int EraseColor = ColorUtil.ColorFromRgba(0, 0, 0, 0);
    public int BrushColor = ColorUtil.ColorFromRgba(255, 0, 0, 255);
    public int BrushColorBgr = ColorUtil.ColorFromRgba(0, 0, 255, 255);

    // paint mode
    // currently unused, hard-coded ctrl for eraser
    // public CanvasPaintMode PaintMode = CanvasPaintMode.Paint;

    // positions of brush in canvas region, [0, 1]
    public double posX = 0;
    public double posY = 0;
    
    // brush radius, in scale of canvas [0, 1]
    public double BrushRadius = 0.05;
    // store original brush radius before shift key adjusting brush size
    public double BrushRadiusBeforeShift = 0.05;
    // threshold when radius <1 pixel, TODO find correct value
    public double BrushRadiusMin = 0.015;
    public double BrushRadiusMax = 0.25;
    public double BrushRadiusDelta = 0.4; // delta scale when changing brush with mouse movement
    public double BrushRadiusSinglePixelThreshold = 0.025;
    // main paint brush circle size, set internally from bounds and gui scale
    double brushCircleSize = 8;

    // open square brush for selecting color off canvas
    public double BrushSquareRadius = 0.02;
    // color selector brush size, set internally from bounds and gui scale
    double brushSquareSize = 8;

    double padding = 0.0; // TODO configurable
    public int borderDepth = 2; // for rectangle emboss edge effect

    public bool Enabled = true;

    bool triggerOnMouseUp = false;
    bool didChange = false;
    
    // flag that pixels have been painted and need to be re-rendered
    bool pixelsDirty = true;

    bool isMouseDown = false;
    bool isMouseOnCanvas = false;
    bool isCtrlDown = false;
    bool isAltDown = false;
    bool isShiftDown = false;

    public GuiElementCanvas(
        ICoreClientAPI capi,
        ElementBounds bounds,
        bool enabled,
        int initialBrushColor,
        IBitmap initialBmp,
        ActionConsumable onPaint,
        ActionConsumable<int> onSelectColor
    ) : base(capi, bounds)
    {
        Enabled = enabled;

        // callbacks
        OnPaint = onPaint;
        OnSelectColor = onSelectColor;

        // create textures
        canvasTexture = new LoadedTexture(capi);
        backgroundTexture = new LoadedTexture(capi);
        openCircleTexture = new LoadedTexture(capi);
        openSquareTexture = new LoadedTexture(capi);

        posX = 0.0;
        posY = 0.0;

        SetBrushColor(initialBrushColor);

        // canvas editable bitmap pixels
        if (initialBmp != null)
        {
            Canvas = new BitmapSimple(initialBmp);
        }
        else {
            Canvas = new BitmapSimple(DEFAULT_CANVAS_SIZE, DEFAULT_CANVAS_SIZE);
        }
        
        // generate canvas texture
        // https://apidocs.vintagestory.at/api/Vintagestory.API.Client.IRenderAPI.html#Vintagestory_API_Client_IRenderAPI_LoadTexture_Vintagestory_API_Common_IBitmap_Vintagestory_API_Client_LoadedTexture__System_Boolean_System_Int32_System_Boolean_
        api.Render.LoadTexture(
            Canvas,
            ref canvasTexture,
            false, // linearMag
            0,     // clampMode
            false  // generateMipMaps
        );
    }

    /// Creates open circle indicating current selected color.
    public void ComposeBrushCircleTexture()
    {
        brushCircleSize = scaled(2.0 * BrushRadius * Bounds.InnerWidth);

        ImageSurface surface = new ImageSurface(Format.Argb32, (int) brushCircleSize, (int) brushCircleSize);
        Context ctx = genContext(surface);

        // make empty stroked circle
        RoundRectangle(ctx, 0, 0, brushCircleSize, brushCircleSize, brushCircleSize/2.0);
        if (isCtrlDown)
        {
            ctx.SetSourceRGBA(1, 0, 0, 1); // make eraser color red
        }
        else
        {
            var rgb = KemonoColorUtil.ToRgb(CursorColor);
            ctx.SetSourceRGBA(rgb.r / 255.0, rgb.g / 255.0, rgb.b / 255.0, 1);
        }
        ctx.LineWidth = 2;
        ctx.Stroke();

        generateTexture(surface, ref openCircleTexture);
        ctx.Dispose();
        surface.Dispose();
    }

    /// Creates open circle indicating current selected color.
    public void ComposeBrushSquareTexture()
    {
        brushSquareSize = scaled(2.0 * BrushSquareRadius * Bounds.InnerWidth);

        ImageSurface surface = new ImageSurface(Format.Argb32, (int) brushSquareSize, (int) brushSquareSize);
        Context ctx = genContext(surface);

        // make empty stroked square
        Rectangle(ctx, 0, 0, brushSquareSize, brushSquareSize);
        var rgb = KemonoColorUtil.ToRgb(CursorColor);
        ctx.SetSourceRGBA(rgb.r / 255.0, rgb.g / 255.0, rgb.b / 255.0, 1);
        ctx.LineWidth = 2;
        ctx.Stroke();

        generateTexture(surface, ref openSquareTexture);
        ctx.Dispose();
        surface.Dispose();
    }

    /// Create static color rectangle background for canvas.
    public void ComposeBackgroundTexture()
    {
        ImageSurface surface = new ImageSurface(Format.Argb32, (int)Bounds.InnerWidth, (int)Bounds.InnerHeight);
        Context ctx = genContext(surface);

        if (UseBackground)
        {
            var rgb = KemonoColorUtil.ToRgb(BackgroundColor);
            ctx.SetSourceRGBA(rgb.r / 255.0, rgb.g / 255.0, rgb.b / 255.0, 1.0);
            ctx.Paint();
        }

        generateTexture(surface, ref backgroundTexture);

        ctx.Dispose();
        surface.Dispose();
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();

        // surrounding inset rectangle
        EmbossRoundRectangleElement(ctx, Bounds, true, borderDepth);
        
        ComposeBackgroundTexture();
        ComposeBrushCircleTexture();
        ComposeBrushSquareTexture();
    }

    public override void Dispose()
    {
        base.Dispose();
        canvasTexture.Dispose();
        backgroundTexture.Dispose();
        openCircleTexture.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        bool linearMag = false; // use nearest rendering
        int clampMode = 0;      // clamp mode

        // render canvas background color
        api.Render.Render2DTexturePremultipliedAlpha(
            backgroundTexture.TextureId,
            Bounds.renderX + padding,
            Bounds.renderY + padding,
            (int) Bounds.InnerWidth,
            (int) Bounds.InnerHeight
        );
        
        if (pixelsDirty)
        {
            api.Render.LoadOrUpdateTextureFromBgra(
                Canvas.Pixels,
                linearMag,
                clampMode,
                ref canvasTexture
            );
            pixelsDirty = false;
        }

        // render canvas internal bitmap pixels
        api.Render.Render2DTexturePremultipliedAlpha(
            canvasTexture.TextureId,
            Bounds.renderX + padding,
            Bounds.renderY + padding,
            (int) Bounds.InnerWidth,
            (int) Bounds.InnerHeight
        );

        // brush selector rendering
        if (!Enabled) return;

        // brush selector circle (only if Enabled and
        // mouse on element and not shift changing brush size)
        if (isMouseOnCanvas && isAltDown)
        {
            double sqPositionX = (posX * Bounds.InnerWidth) + padding - brushSquareSize / 2.0;
            double sqPositionY = (posY * Bounds.InnerHeight) + padding - brushSquareSize / 2.0;

            api.Render.Render2DTexturePremultipliedAlpha(
                openSquareTexture.TextureId,
                Bounds.renderX + sqPositionX,
                Bounds.renderY + sqPositionY,
                (int) brushSquareSize,
                (int) brushSquareSize
            );
        }
        else if (isMouseOnCanvas || isShiftDown)
        {
            double circlePositionX = (posX * Bounds.InnerWidth) + padding - brushCircleSize / 2.0;
            double circlePositionY = (posY * Bounds.InnerHeight) + padding - brushCircleSize / 2.0;

            api.Render.Render2DTexturePremultipliedAlpha(
                openCircleTexture.TextureId,
                Bounds.renderX + circlePositionX,
                Bounds.renderY + circlePositionY,
                (int) brushCircleSize,
                (int) brushCircleSize
            );
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!Enabled) return;

        if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;

        args.Handled = updateValue(api.Input.MouseX, api.Input.MouseY);

        isMouseDown = true;
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!Enabled) return;
        
        isAltDown = args.AltPressed;
        if (!isCtrlDown)
        {
            isCtrlDown = args.CtrlPressed;
            ComposeBrushCircleTexture();
        }

        if (!isShiftDown)
        {
            BrushRadiusBeforeShift = BrushRadius;
            isShiftDown = args.ShiftPressed;
        }
    }

    public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
    {
        if (!Enabled) return;
        BrushRadiusBeforeShift = BrushRadius;
        isShiftDown = args.ShiftPressed;
        isAltDown = args.AltPressed;

        if (isCtrlDown)
        {
            isCtrlDown = args.CtrlPressed;
            ComposeBrushCircleTexture();
        }
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        isMouseDown = false;

        if (!Enabled) return;

        if (OnPaint != null && didChange && triggerOnMouseUp)
        {
            OnPaint();
        }

        didChange = false;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        isMouseOnCanvas = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);

        if (!Enabled) return;

        if (isMouseOnCanvas)
        {
            args.Handled = updateValue(api.Input.MouseX, api.Input.MouseY);
        }
    }

    bool updateValue(int mouseX, int mouseY)
    {
        double width = Bounds.InnerWidth - 2 * padding;
        double height = Bounds.InnerHeight - 2 * padding;

        // Translate mouse position into current value
        double mousePosX = GameMath.Clamp(mouseX - Bounds.renderX - padding, 0, width);
        double mousePosY = GameMath.Clamp(mouseY - Bounds.renderY - padding, 0, height);
        
        if (isAltDown) // select color from canvas mouse location
        {
            posX = mousePosX / width;
            posY = mousePosY / height;

            if (isMouseDown)
            {
                // get color from canvas
                var colAtPos = Canvas.GetPixelClamped(posX, posY);
                SetBrushColor(colAtPos);
                OnSelectColor(colAtPos);
            }
        }
        else if (isShiftDown) // change brush size
        {
            double newPosX = mousePosX / width;
            double newPosY = mousePosY / height;
            double dx = newPosX - posX;
            double dy = -(newPosY - posY); // invert because I want move up as positive

            // for increase vs. decrease, choose absolute value max
            double direction = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
            
            // Console.WriteLine($"onShiftPaint {BrushRadiusBeforeShift} ({newPosX}, {newPosY})  |  {dx}, {dy} => {direction}");

            double newBrushRadius = BrushRadiusBeforeShift + direction * BrushRadiusDelta;
            newBrushRadius = GameMath.Clamp(newBrushRadius, BrushRadiusMin, BrushRadiusMax);

            // do comparison so that at min/max edges, avoids re-triggering
            // and re-composing texture
            if (newBrushRadius != BrushRadius)
            {
                BrushRadius = newBrushRadius;
                ComposeBrushCircleTexture();
            }
        }
        else // regular paint
        {
            posX = mousePosX / width;
            posY = mousePosY / height;

            if (isMouseDown)
            {
                int paintColor;
                if (isCtrlDown) // eraser
                {
                    paintColor = EraseColor;
                }
                else
                {
                    paintColor = BrushColorBgr; // pixels stored as bgr
                }

                if (BrushRadius > BrushRadiusSinglePixelThreshold)
                {
                    Canvas.SetPixelsInRadius(posX, posY, BrushRadius, paintColor);
                }
                else
                {
                    Canvas.SetPixel(posX, posY, paintColor);
                }

                pixelsDirty = true;
                didChange = true;
                
                if (OnPaint != null)
                {
                    if (!triggerOnMouseUp) return OnPaint();
                }
            }

        }

        return true;
    }

    public void SetBrushColor(int color)
    {
        BrushColor = color;
        BrushColorBgr = KemonoColorUtil.FlipRgb(color);
    }

    public int[] GetPixels()
    {
        return Canvas.Pixels;
    }

    public void SetBackground(bool useBackground, int backgroundColor)
    {
        UseBackground = useBackground;
        BackgroundColor = backgroundColor;
        ComposeBackgroundTexture();
    }

    public void SetCursorColor(int color)
    {
        CursorColor = color;
        ComposeBrushCircleTexture();
        ComposeBrushSquareTexture();
    }
}


public static partial class GuiComposerHelpers
{
    public static GuiComposer AddCanvas(
        this GuiComposer composer,
        ElementBounds bounds,
        bool enabled,
        int brushColor,
        IBitmap initialBmp,
        ActionConsumable onPaint,
        ActionConsumable<int> onSelectColor,
        string key = null
    )
    {
        var elem = new GuiElementCanvas(
            composer.Api,
            bounds,
            enabled,
            brushColor,
            initialBmp,
            onPaint,
            onSelectColor
        );

        if (!composer.Composed)
        {
            composer.AddInteractiveElement(elem, key);
        }

        return composer;
    }

    public static GuiElementCanvas GetCanvas(this GuiComposer composer, string key)
    {
        return (GuiElementCanvas) composer.GetElement(key);
    }
}
