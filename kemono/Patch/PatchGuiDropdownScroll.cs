using HarmonyLib;
using Vintagestory.API.Client;

namespace kemono;

// https://github.com/anegostudios/vsapi/blob/f7c6a7e56cfaf870a942f7377e2edf3f521f15db/Client/UI/Elements/Impl/Interactive/Controls/GuiElementDropDown.cs#L366
// https://github.com/anegostudios/vsapi/blob/f7c6a7e56cfaf870a942f7377e2edf3f521f15db/Client/UI/Elements/Impl/Interactive/Controls/GuiElementListMenu.cs#L545
// https://github.com/anegostudios/vsapi/blob/f7c6a7e56cfaf870a942f7377e2edf3f521f15db/Client/UI/Elements/Impl/Interactive/Controls/GuiElementScrollbar.cs#L159
// stop this from scrolling background container:
// - vanilla behavior: if list is not scrollable (long enough),
//   scrolls background container instead of just not scrolling list
//   (= retarded, shouldnt change scroll behavior based on list item count).
// - patched: dont do this
[HarmonyPatch(typeof(GuiElementScrollbar))]
[HarmonyPatch("OnMouseWheel")]
class PatchGuiDropdownScrollBeingRetarded
{
    static void Postfix(MouseWheelEventArgs args)
    {
        args.SetHandled(true);
    }
}
