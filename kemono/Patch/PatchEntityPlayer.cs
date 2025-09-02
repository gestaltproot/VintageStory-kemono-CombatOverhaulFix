using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using System;

namespace kemono;

// https://github.com/anegostudios/vsapi/blob/e78624b6eee6920e45edcd25ba94e8199b2193af/Common/Entity/EntityPlayer.cs#L392
// need to overwrite the AnimManager.HeadController created after
// base.OnTesselation call
[HarmonyPatch(typeof(EntityPlayer))]
[HarmonyPatch("OnTesselation")]
class PatchEntityPlayerOnTesselation
{
    static FieldInfo animManagerProp = typeof(EntityPlayer).GetField("animManager", BindingFlags.Instance | BindingFlags.NonPublic);

    static void Postfix(EntityPlayer __instance, ref Shape entityShape)
    {
        var skin = __instance.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin != null && skin.Model != null)
        {
            var animManager = (IAnimationManager)animManagerProp.GetValue(__instance);
            animManager.HeadController = new KemonoPlayerHeadController(
                animManager,
                __instance,
                entityShape,
                skin.Model.JointHead,
                skin.Model.JointNeck,
                skin.Model.JointTorsoUpper,
                skin.Model.JointTorsoLower,
                skin.Model.JointLegUpperL,
                skin.Model.JointLegUpperR
            );

            // https://github.com/anegostudios/vsapi/blob/cad83424ee89915ef206d0b23845af0a4ef72348/Common/Entity/EntityPlayer.cs#L273
            bool isSelf = __instance.PlayerUID == (__instance.Api as ICoreClientAPI)?.Settings.String["playeruid"];
            if (isSelf)
            {
                __instance.OtherAnimManager.HeadController = new KemonoPlayerHeadController(
                    __instance.OtherAnimManager,
                    __instance,
                    entityShape,
                    skin.Model.JointHead,
                    skin.Model.JointNeck,
                    skin.Model.JointTorsoUpper,
                    skin.Model.JointTorsoLower,
                    skin.Model.JointLegUpperL,
                    skin.Model.JointLegUpperR
                );
            }
        }
    }
}
