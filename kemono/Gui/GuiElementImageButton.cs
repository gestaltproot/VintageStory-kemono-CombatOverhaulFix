using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

/// https://raw.githubusercontent.com/anegostudios/vsapi/master/Client/UI/Elements/Impl/Static/GuiElementInset.cs
/// Because this is private for whatever reason wtf?
public class GuiElementImageButton : GuiElement
{
    public AssetLocation ImagePath;

    // Callback when button is clicked. Calls with current selection state,
    // returns new bool for new selected state.
    public System.Func<bool, bool> OnClick;

    // color tint R, G, B, A array
    public double[] Tint;

    // highlight tint R, G, B, A array
    public Vec4f HighlightTint = null;

    // white tint when button is selected
    public Vec4f SelectedTint = new Vec4f(1, 1, 1, 0.5f);

    // optional highlight texture
    LoadedTexture highlightTexture;

    // if mouse is hovering over this element
    bool isMouseOver = false;

    // if currently selected
    public bool Selected { get; set; } = false;


    /// Create an clickable image button.
    public GuiElementImageButton(
        ICoreClientAPI capi,
        ElementBounds bounds,
        AssetLocation imagePath,
        System.Func<bool, bool> onClick,
        double[] tint,
        float[] highlightTint = null,
        bool selected = false
    ) : base(capi, bounds)
    {
        ImagePath = imagePath?.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png");
        OnClick = onClick;
        Tint = tint;
        Selected = selected;

        if (highlightTint != null)
        {
            HighlightTint = new Vec4f(highlightTint[0], highlightTint[1], highlightTint[2], highlightTint[3]);
            highlightTexture = new LoadedTexture(capi);
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);

        if (OnClick != null)
        {
            bool newSelected = OnClick(Selected);
            Selected = newSelected;
        }
    }

    // reference:
    // https://github.com/anegostudios/vsapi/blob/master/Client/UI/Elements/Impl/Static/GuiElementImage.cs
    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();

        ImageSurface imageSurface = null;

        if (ImagePath != null)
        {
            imageSurface = TryGetImageSurfaceFromAsset(api, ImagePath);
        }

        if (imageSurface != null)
        {
            float scale = (float)Math.Min(Bounds.InnerWidth / imageSurface.Width, Bounds.InnerHeight / imageSurface.Height);
            // ctx.Rectangle(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight);
            ctx.PushGroup();
            // ctx.SetSource(pattern);
            // ctx.Rectangle(0, 0, Bounds.OuterWidth, Bounds.OuterHeight);
            ctx.Scale(scale, scale);
            ctx.SetSourceSurface(imageSurface, (int)(Bounds.drawX / scale), (int)(Bounds.drawY / scale));
            // ctx.SetSourceSurface(imageSurface, (int)Bounds.OuterWidth, (int)Bounds.OuterHeight);
            ctx.Paint();
            ctx.PopGroupToSource();
            ctx.Paint();

            imageSurface.Dispose();
        }
        else
        {
            if (ImagePath != null)
            {
                // only print when not null as error, assume if null its intentional
                api.Logger.Error($"[GuiElementImageButton] Image not found: {ImagePath}, writing solid color");
            }
            // paint solid texture if image is not found
            Rectangle(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight);
            ctx.SetSourceRGBA(0, 0, 0, 1);
            ctx.Fill();
        }

        // color tint
        if (Tint != null)
        {
            Rectangle(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight);
            ctx.SetSourceRGBA(Tint[0], Tint[1], Tint[2], Tint[3]);
            ctx.Fill();
        }

        // highlight tint
        ComposeHighlightTexture();
    }

    public void ComposeHighlightTexture()
    {
        if (HighlightTint == null || highlightTexture == null) return;

        ImageSurface surface = new ImageSurface(Format.Argb32, (int)Bounds.OuterWidth, (int)Bounds.OuterHeight);
        Context ctx = genContext(surface);

        Rectangle(ctx, 0, 0, surface.Width, surface.Height);
        ctx.SetSourceRGBA(HighlightTint[0], HighlightTint[1], HighlightTint[2], HighlightTint[3]);
        ctx.Fill();

        generateTexture(surface, ref highlightTexture);
        ctx.Dispose();
        surface.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        // hover button highlight shading
        if (highlightTexture != null)
        {
            Vec4f tint = null;
            if (Selected) tint = SelectedTint;
            else if (isMouseOver) tint = HighlightTint;

            if (tint != null)
            {
                api.Render.Render2DTexturePremultipliedAlpha(
                    highlightTexture.TextureId,
                    Bounds.renderX,
                    Bounds.renderY,
                    (int)Bounds.OuterWidth,
                    (int)Bounds.OuterHeight,
                    color: tint
                );
            }
        }
    }

    /// <summary>
    /// If mouse over, set isMouseOver to render highlight texture.
    /// </summary>
    /// <param name="api"></param>
    /// <param name="args"></param>
    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseMove(api, args);
        if (highlightTexture == null) return;
        bool newIsMouseOver = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        isMouseOver = newIsMouseOver;
    }

    public override void Dispose()
    {
        base.Dispose();

        highlightTexture?.Dispose();
    }

    /// <summary>
    /// Tries to fetch an image surface from a named file.
    /// Return null if the file does not exist.
    /// </summary>
    /// <param name="capi">The Client API</param>
    /// <param name="textureLoc">The name of the text file.</param>
    /// <param name="mulAlpha"></param>
    /// <returns></returns>
    public static ImageSurface TryGetImageSurfaceFromAsset(ICoreClientAPI capi, AssetLocation textureLoc, int mulAlpha = 255)
    {
        if (capi.Assets.Exists(textureLoc))
        {
            return getImageSurfaceFromAsset(capi, textureLoc, mulAlpha);
        }

        return null;
    }
    
}


public static partial class GuiComposerHelpers
{
    public static GuiComposer AddImageButton(
        this GuiComposer composer,
        ElementBounds bounds,
        AssetLocation image,
        System.Func<bool, bool> onClick,
        double[] tint = null,
        string key = null
    ) {
        var elem = new GuiElementImageButton(
            composer.Api,
            bounds,
            image,
            onClick,
            tint
        );

        if (!composer.Composed)
        {
            composer.AddInteractiveElement(elem, key);
        }
        return composer;
    }

    public static GuiElementImageButton GetImageButton(this GuiComposer composer, string key)
    {
        return (GuiElementImageButton) composer.GetElement(key);
    }
}
