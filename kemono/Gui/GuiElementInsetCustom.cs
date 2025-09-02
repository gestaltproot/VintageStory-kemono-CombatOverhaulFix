using Cairo;
using Vintagestory.API.Client;

namespace kemono;

/// https://raw.githubusercontent.com/anegostudios/vsapi/master/Client/UI/Elements/Impl/Static/GuiElementInset.cs
/// Because this is private for whatever reason wtf?
public class GuiElementInsetCustom : GuiElement
{
    int depth;
    float brightness;

    /// <summary>
    /// Creates a new inset for the GUI.
    /// </summary>
    /// <param name="capi">The Client API</param>
    /// <param name="bounds">The bounds of the Element.</param>
    /// <param name="depth">The depth of the element.</param>
    /// <param name="brightness">The brightness of the inset.</param>
    public GuiElementInsetCustom(ICoreClientAPI capi, ElementBounds bounds, int depth, float brightness) : base(capi, bounds)
    {
        this.depth = depth;
        this.brightness = brightness;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();

        if (brightness < 1)
        {
            ctx.SetSourceRGBA(0, 0, 0, 1 - brightness);
            Rectangle(ctx, Bounds);
            ctx.Fill();
        }

        EmbossRoundRectangleElement(ctx, Bounds, true, depth);
    }
}
