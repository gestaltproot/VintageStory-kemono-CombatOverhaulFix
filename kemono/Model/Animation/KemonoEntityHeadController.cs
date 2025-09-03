using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

// based on:
// https://github.com/anegostudios/vsapi/blob/master/Common/Model/Animation/EntityHeadController.cs


public class KemonoPlayerHeadController : EntityHeadController
{
    protected IPlayer player = null;
    protected EntityPlayer entityPlayer;
    protected bool turnOpposite;
    protected bool rotateTpYawNow;

    protected bool AllJointsFound;

    public float CameraYawInterpolateThreshold = 1.2f;
    public float CameraYawInterpolateThresholdMoving = 0.01f;
    public float CameraYawInterpolateSpeedBase = 0.05f;
    public float CameraYawInterpolateSpeedDistMultiplier = 3.5f;

    public KemonoPlayerHeadController(
        IAnimationManager animator,
        EntityPlayer entity,
        Shape entityShape,
        string head = "b_Head",
        string neck = "b_Neck",
        string torsoUpper = "UpperTorso",
        string torsoLower = "LowerTorso",
        string footUpperL = "b_FootUpperL",
        string footUpperR = "b_FootUpperR",
        float cameraYawInterpolateThreshold = 0.5f,
        float cameraYawInterpolateSpeedBase = 0.05f,
        float cameraYawInterpolateSpeedDistMultiplier = 3.5f
    ) : base(animator, entity, entityShape)
    {
        entityPlayer = entity;

        // camera movement controls settings
        CameraYawInterpolateThreshold = cameraYawInterpolateThreshold;
        CameraYawInterpolateThresholdMoving = cameraYawInterpolateThreshold > 0 ? 0.01f : 0;
        CameraYawInterpolateSpeedBase = cameraYawInterpolateSpeedBase;
        CameraYawInterpolateSpeedDistMultiplier = cameraYawInterpolateSpeedDistMultiplier;

        // re-write base joints
        HeadPose = animator.Animator.GetPosebyName(head);
        NeckPose = animator.Animator.GetPosebyName(neck);
        UpperTorsoPose = animator.Animator.GetPosebyName(torsoUpper);
        LowerTorsoPose = animator.Animator.GetPosebyName(torsoLower);
        UpperFootRPose = animator.Animator.GetPosebyName(footUpperR);
        UpperFootLPose = animator.Animator.GetPosebyName(footUpperL);

        // if any are null, disable base head controller
        AllJointsFound = 
            HeadPose != null &&
            NeckPose != null &&
            UpperTorsoPose != null &&
            LowerTorsoPose != null &&
            UpperFootRPose != null &&
            UpperFootLPose != null
        ;
    }

    public override void OnFrame(float dt)
    {
        if (player == null) player = entityPlayer.Player;
        
        var capi = entity.Api as ICoreClientAPI;
        bool isSelf = capi.World.Player.Entity.EntityId == entity.EntityId;

        if (!isSelf)
        {
            if (AllJointsFound)
            {
                base.OnFrame(dt);
            }
            if (entity.BodyYawServer == 0)
            {
                entity.BodyYaw = entity.Pos.Yaw;
            }
            return;
        }

        float diff = GameMath.AngleRadDistance(entity.BodyYaw, entity.Pos.Yaw);

        if (Math.Abs(diff) > GameMath.PIHALF * 1.2f) turnOpposite = true;
        if (turnOpposite)
        {
            if (Math.Abs(diff) < GameMath.PIHALF * 0.9f) turnOpposite = false;
            else diff = 0;
        }

        var cameraMode = (player as IClientPlayer).CameraMode;

        bool overheadLookAtMode = capi.Settings.Bool["overheadLookAt"] && cameraMode == EnumCameraMode.Overhead;

        if (!overheadLookAtMode && capi.Input.MouseGrabbed)
        {
            entity.Pos.HeadYaw += (diff - entity.Pos.HeadYaw) * dt * 6;
            entity.Pos.HeadYaw = GameMath.Clamp(entity.Pos.HeadYaw, -0.75f, 0.75f);

            entity.Pos.HeadPitch = GameMath.Clamp((entity.Pos.Pitch - GameMath.PI) * 0.75f, -1.2f, 1.2f);
        }

        EnumMountAngleMode angleMode = EnumMountAngleMode.Unaffected;
        var mount = player.Entity.MountedOn;
        if (player.Entity.MountedOn != null)
        {
            angleMode = mount.AngleMode;
        }

        if (player?.Entity == null || angleMode == EnumMountAngleMode.Fixate || angleMode == EnumMountAngleMode.FixateYaw || cameraMode == EnumCameraMode.Overhead)
        {
            if (capi.Input.MouseGrabbed)
            {
                entity.BodyYaw = entity.Pos.Yaw;

                if (overheadLookAtMode)
                {
                    float dist = -GameMath.AngleRadDistance((entity.Api as ICoreClientAPI).Input.MouseYaw, entity.Pos.Yaw);
                    float targetHeadYaw = GameMath.PI + dist;
                    var targetpitch = GameMath.Clamp(-entity.Pos.Pitch - GameMath.PI + GameMath.TWOPI, -1, +0.8f);

                    if (targetHeadYaw > GameMath.PI) targetHeadYaw -= GameMath.TWOPI;

                    if (targetHeadYaw < -1f || targetHeadYaw > 1f)
                    {
                        targetHeadYaw = 0;

                        entity.Pos.HeadPitch += (GameMath.Clamp((entity.Pos.Pitch - GameMath.PI) * 0.75f, -1.2f, 1.2f) - entity.Pos.HeadPitch) * dt * 6;
                    }
                    else
                    {
                        entity.Pos.HeadPitch += (targetpitch - entity.Pos.HeadPitch) * dt * 6;
                    }

                    entity.Pos.HeadYaw += (targetHeadYaw - entity.Pos.HeadYaw) * dt * 6;
                }
            }
        }
        else
        {
            if (player?.Entity.Alive == true)
            {
                float yawDist = GameMath.AngleRadDistance(entity.BodyYaw, entity.Pos.Yaw);
                bool ismoving = player.Entity.Controls.TriesToMove || player.Entity.ServerControls.TriesToMove;

                // float threshold = 1.2f - (ismoving ? 1.19f : 0); // VANILLA
                float threshold = CameraYawInterpolateThreshold;
                if (ismoving)
                {
                    threshold = CameraYawInterpolateThresholdMoving; // 0.01f,
                }
                if (entity.Controls.Gliding)
                {
                    threshold = 0;
                }
                
                if (player.PlayerUID == capi.World.Player.PlayerUID && !capi.Settings.Bool["immersiveFpMode"] && cameraMode != EnumCameraMode.FirstPerson)
                {
                    if (Math.Abs(yawDist) > threshold || rotateTpYawNow)
                    {
                        // float speed = 0.05f + Math.Abs(yawDist) * 3.5f; // VANILLA
                        float speed = CameraYawInterpolateSpeedBase
                            + Math.Abs(yawDist) * CameraYawInterpolateSpeedDistMultiplier;

                        entity.BodyYaw += GameMath.Clamp(yawDist, -dt * speed, dt * speed);
                        rotateTpYawNow = Math.Abs(yawDist) > 0.01f;
                    }
                }
                else
                {
                    entity.BodyYaw = entity.Pos.Yaw;
                }
            }
        }

        if (AllJointsFound)
        {
            base.OnFrame(dt);
        }
    }
}
