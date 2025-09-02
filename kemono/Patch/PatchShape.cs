using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace kemono;

// https://github.com/anegostudios/vsapi/blob/23af8d44f58835f7e7e7d957f354ae4c27b15f1e/Common/Model/Shape/Shape.cs#L274C55-L274C86
// causes crash when player adds shape cuz of code typo and incorrectness lmao
[HarmonyPatch(typeof(Shape))]
[HarmonyPatch("getOrCreateKeyFrame")]
class PatchShapeGetOrCreateKeyFrame
{
    static bool Prefix(Animation entityAnim, int frame, ref AnimationKeyFrame __result) {
        for (int ei = 0; ei < entityAnim.KeyFrames.Length; ei++)
        {
            var entityKeyFrame = entityAnim.KeyFrames[ei];
            if (entityKeyFrame.Frame == frame)
            {
                    __result = entityKeyFrame;
                return false;
            }
        }

        for (int ei = 0; ei < entityAnim.KeyFrames.Length; ei++)
        {
            var entityKeyFrame = entityAnim.KeyFrames[ei];
            if (entityKeyFrame.Frame > frame)
            {
                var kfm = new AnimationKeyFrame() {
                    Frame = frame,
                    Elements = new Dictionary<string, AnimationKeyFrameElement>(),
                };
                entityAnim.KeyFrames = entityAnim.KeyFrames.InsertAt(kfm, ei);
                __result = kfm;
                return false;
            }
        }

        // append to end
        var kf = new AnimationKeyFrame() {
            Frame = frame,
            Elements = new Dictionary<string, AnimationKeyFrameElement>(),
        };
        entityAnim.KeyFrames = entityAnim.KeyFrames.Append(kf);
        __result = kf;

        return false;
    }
}
