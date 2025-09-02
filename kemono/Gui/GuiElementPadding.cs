using Cairo;
using Vintagestory.API.Client;

namespace kemono;

/// Dummy gui element to just force padding into a container.
public class GuiElementPadding : GuiElement
{
    public GuiElementPadding(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
    {

    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
    }
}
