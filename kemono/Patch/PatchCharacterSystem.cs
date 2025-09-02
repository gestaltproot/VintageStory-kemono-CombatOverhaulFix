using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace kemono;
// https://github.com/anegostudios/vssurvivalmod/blob/5a5528e1e95325741537ef152c813a8a69acd890/Systems/Character/Character.cs#L337
// need to override, we can no longer create a new chat command
// on top because it causes CharacterSystem to throw an exception.
[HarmonyPatch(typeof(Vintagestory.GameContent.CharacterSystem))]
[HarmonyPatch("onCharSelCmd")]
class PatchCharacterSystemOnCharSelCmd
{
    static bool Prefix(
        ICoreClientAPI ___capi,
        ref TextCommandResult __result,
        TextCommandCallingArgs textCommandCallingArgs
    )
    {
        var kemono = ___capi.ModLoader.GetModSystem<KemonoMod>();
        __result = kemono.OnCharSelCmd(textCommandCallingArgs);
        return false; // skip original
    }
}

// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L319
[HarmonyPatch(typeof(Vintagestory.GameContent.CharacterSystem))]
[HarmonyPatch("Event_PlayerJoin")]
class PatchCharacterSystemEventPlayerJoin
{
    static bool Prefix()
    //     Vintagestory.GameContent.CharacterSystem __instance,
    //     ICoreClientAPI ___capi,
    //     ref bool ___didSelect,
    //     ref Vintagestory.GameContent.GuiDialogCreateCharacter ___createCharDlg,
    //     IClientPlayer byPlayer
    // )
    {
        // System.Diagnostics.Debug.WriteLine("Event_PlayerJoin: HARMONY PATCHED!!!");

        // if ( !___didSelect && byPlayer.PlayerUID == ___capi.World.Player.PlayerUID )
        // {
        //     ___createCharDlg = new Vintagestory.GameContent.GuiDialogCreateCharacter(___capi, __instance);
        //     ___createCharDlg.PrepAndOpen();
        //     ___createCharDlg.OnClosed += () => ___capi.PauseGame(false);
        //     ___capi.Event.EnqueueMainThreadTask(() => ___capi.PauseGame(true), "pausegame");
        // }
        return false; // skip original
    }
}

// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L330
[HarmonyPatch(typeof(Vintagestory.GameContent.CharacterSystem))]
[HarmonyPatch("Event_IsPlayerReady")]
class PatchCharacterSystemEventIsPlayerReady
{
    static bool Prefix(
        ref bool __result,
        ref Vintagestory.API.Common.EnumHandling handling
    )
    {
        __result = true;
        return false; // skip original
    }
}

// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L387
// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L400
// need to skip, otherwise randomizeSkin will cause Exception 
// because no base game skinnable behavior. This stops
// downstream events, and corrupts game state.
[HarmonyPatch(typeof(Vintagestory.GameContent.CharacterSystem))]
[HarmonyPatch("randomizeSkin")]
class PatchCharacterSystemRandomizeSkin
{
    static bool Prefix(ref bool __result)
    {
        __result = true;
        return false; // skip original
    }
}
