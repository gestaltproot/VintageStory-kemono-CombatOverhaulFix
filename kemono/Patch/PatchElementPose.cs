using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

// makes element pose Add() include weight in rotate shortest distance mode
[HarmonyPatch(typeof(ElementPose))]
[HarmonyPatch("Add")]
class PatchElementPose
{
    static bool Prefix(
        ElementPose __instance,
        ElementPose tf,
        ElementPose tfNext,
        float l,
        float weight
    ) {
        if (tf.RotShortestDistanceX)
        {
            float distX = GameMath.AngleDegDistance(tf.degX, tfNext.degX);
            __instance.degX += (tf.degX + distX * l) * weight;
        }
        else
        {
            __instance.degX += (tf.degX * (1 - l) + tfNext.degX * l) * weight;
        }

        if (tf.RotShortestDistanceY)
        {
            float distY = GameMath.AngleDegDistance(tf.degY, tfNext.degY);
            __instance.degY += (tf.degY + distY * l) * weight;
        }
        else
        {
            __instance.degY += (tf.degY * (1 - l) + tfNext.degY * l) * weight;
        }

        if (tf.RotShortestDistanceZ)
        {
            float distZ = GameMath.AngleDegDistance(tf.degZ, tfNext.degZ);
            __instance.degZ += (tf.degZ + distZ * l) * weight;
        }
        else
        {
            __instance.degZ += (tf.degZ * (1 - l) + tfNext.degZ * l) * weight;
        }


        __instance.scaleX += ((tf.scaleX - 1) * (1-l) + (tfNext.scaleX - 1) * l) * weight;
        __instance.scaleY += ((tf.scaleY - 1) * (1 - l) + (tfNext.scaleY - 1) * l) * weight;
        __instance.scaleZ += ((tf.scaleZ - 1) * (1 - l) + (tfNext.scaleZ - 1) * l) * weight;
        __instance.translateX += (tf.translateX * (1 - l) + tfNext.translateX * l) * weight;
        __instance.translateY += (tf.translateY * (1 - l) + tfNext.translateY * l) * weight;
        __instance.translateZ += (tf.translateZ * (1 - l) + tfNext.translateZ * l) * weight;

        return false;
    }
}
