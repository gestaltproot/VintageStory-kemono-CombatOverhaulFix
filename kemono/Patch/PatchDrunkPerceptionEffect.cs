using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;


namespace kemono;

// https://github.com/anegostudios/vsapi/blob/ba72ea5762b2a865584306c3db1f3c2c618b04c1/Client/Render/PerceptionEffects/DrunkPerceptionEffect.cs#L75
// .GetPosebyName("root"); and subsequent operations will null ref crash client on 1.20.0
[HarmonyPatch(typeof(DrunkPerceptionEffect))]
[HarmonyPatch("ApplyToTpPlayer")]
class PatchDrunkPerceptionEffectApplyToTpPlayer
{
    static FieldInfo accumProp = typeof(DrunkPerceptionEffect).GetField("accum", BindingFlags.Instance | BindingFlags.NonPublic);

    static bool Prefix(
        DrunkPerceptionEffect __instance,
        EntityPlayer entityPlr,
        float[] modelMatrix,
        float? playerIntensity = null
    ) {
        var rplr = entityPlr?.Player as IClientPlayer;
        if (rplr == null || entityPlr.AnimManager.Animator == null || (rplr.CameraMode == EnumCameraMode.FirstPerson && !rplr.ImmersiveFpMode)) return false;

        float inten = playerIntensity == null ? __instance.Intensity : (float)playerIntensity;

        var pos = entityPlr.AnimManager.Animator.GetPosebyName("root"); // change to bone name
        var accum = (float) accumProp.GetValue(__instance);
        pos.degOffX = GameMath.Sin(accum) / 5f * inten * GameMath.RAD2DEG;
        pos.degOffZ = GameMath.Sin(accum * 1.2f) / 5f * inten * GameMath.RAD2DEG;
        return false;
    }
}
