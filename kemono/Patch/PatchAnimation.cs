using HarmonyLib;
using Vintagestory.API.Common;

namespace kemono;

// make copy over anim states copy current easing factor
[HarmonyPatch(typeof(AnimationManager))]
[HarmonyPatch("CopyOverAnimStates")]
class PatchAnimationManagerCopyOverAnimStates
{
    static bool Prefix(AnimationManager __instance, RunningAnimation[] copyOverAnims, IAnimator animator)
    {
        if (copyOverAnims != null && animator != null)
        {
            for (int i = 0; i < copyOverAnims.Length; i++)
            {
                RunningAnimation sourceAnim = copyOverAnims[i];
                if (sourceAnim != null && sourceAnim.Active)
                {
                    AnimationMetaData meta;
                    __instance.ActiveAnimationsByAnimCode.TryGetValue(sourceAnim.Animation.Code, out meta);
                    if (meta != null)
                    {
                        meta.StartFrameOnce = sourceAnim.CurrentFrame;
                    }

                    // copy easing factor directly into animator animations if code matches
                    // easing factor needed so animation starts at same place
                    if (animator.Animations != null && i < animator.Animations.Length)
                    {
                        var newAnim = animator.Animations[i];
                        if (sourceAnim.Animation.Code == newAnim.Animation.Code)
                        {
                            newAnim.EasingFactor = sourceAnim.EasingFactor;
                        }
                    }
                    
                }
            }
        }

        return false;
    }
}
