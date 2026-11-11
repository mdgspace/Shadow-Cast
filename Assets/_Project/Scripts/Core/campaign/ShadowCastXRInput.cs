using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Handedness = Oculus.Interaction.Input.Handedness;

namespace ShadowCast.Core.Campaign
{
    /// <summary>One input source for bare OpenXR scenes and existing Meta rigs.</summary>
    public static class ShadowCastXRInput
    {
        private static InputActionAsset actions;
        public static Transform TrackingSpace { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            TrackingSpace = null;
            actions = Resources.Load<InputActionAsset>("ShadowCastXR");
            actions?.Enable();
        }

        public static void EnsureCameraTracking(Camera camera)
        {
            if (camera == null) return;
            var modernDriver = camera.GetComponent<TrackedPoseDriver>();
            var legacyDriver = camera.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
            if (modernDriver != null && legacyDriver != null) legacyDriver.enabled = false;
            // OVRCameraRig and authored pose drivers already own their eye transforms.
            if (camera.GetComponentInParent<OVRCameraRig>() != null ||
                camera.GetComponent<TrackedPoseDriver>() != null ||
                camera.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>() != null)
            {
                TrackingSpace = camera.GetComponentInParent<OVRCameraRig>()?.trackingSpace ?? camera.transform.parent;
                return;
            }
            GameObject origin = new GameObject("Shadow Cast XR Origin");
            origin.transform.SetParent(camera.transform.parent, false);
            origin.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            camera.transform.SetParent(origin.transform, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            TrackingSpace = origin.transform;
            var driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(actions.FindAction("XR/HeadPosition", true));
            driver.rotationInput = new InputActionProperty(actions.FindAction("XR/HeadRotation", true));
            driver.trackingStateInput = new InputActionProperty(actions.FindAction("XR/HeadTrackingState", true));
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            // Device tracking is relative to the authored camera position; request floor
            // tracking only when the scene has explicitly provided its own rig.
            var input = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<UnityEngine.XR.XRInputSubsystem>();
            input?.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Device);
        }

        public static bool RawButton(string name) => actions != null && actions.FindAction("XR/" + name)?.IsPressed() == true;
        public static float Move => actions != null ? actions.FindAction("XR/Move").ReadValue<Vector2>().x : 0f;

        public static bool TryControllerPose(bool left, out Pose pose)
        {
            string side = left ? "Left" : "Right";
            pose = default;
            if (actions == null || !RawButton(side + "Tracked")) return false;
            pose = ToWorld(new Pose(actions.FindAction("XR/" + side + "Position").ReadValue<Vector3>(),
                actions.FindAction("XR/" + side + "Rotation").ReadValue<Quaternion>()));
            return true;
        }

        public static Pose ToWorld(Pose pose) => TrackingSpace == null ? pose :
            new Pose(TrackingSpace.TransformPoint(pose.position), TrackingSpace.rotation * pose.rotation);

        public static bool TryJoint(Handedness side, XRHandJointID joint, out Pose pose)
        {
            pose = default;
            XRHandSubsystem subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null || !subsystem.running) return false;
            XRHand hand = side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(joint).TryGetPose(out pose)) return false;
            pose = ToWorld(pose);
            return true;
        }

        public static bool Pinch(Handedness side, HandFinger finger)
        {
            MetaAimHand aim = side == Handedness.Left ? MetaAimHand.left : MetaAimHand.right;
            if (aim != null && aim.added)
            {
                MetaAimFlags flags = (MetaAimFlags)aim.aimFlags.ReadValue();
                if ((flags & MetaAimFlags.Valid) != 0 && (flags & MetaAimFlags.SystemGesture) == 0)
                {
                    MetaAimFlags pinch = finger == HandFinger.Index ? MetaAimFlags.IndexPinching :
                        finger == HandFinger.Middle ? MetaAimFlags.MiddlePinching :
                        finger == HandFinger.Ring ? MetaAimFlags.RingPinching : MetaAimFlags.LittlePinching;
                    return (flags & pinch) != 0;
                }
            }
            XRHandJointID tip = finger == HandFinger.Index ? XRHandJointID.IndexTip :
                finger == HandFinger.Middle ? XRHandJointID.MiddleTip :
                finger == HandFinger.Ring ? XRHandJointID.RingTip : XRHandJointID.LittleTip;
            return TryJoint(side, tip, out Pose a) && TryJoint(side, XRHandJointID.ThumbTip, out Pose b) &&
                Vector3.Distance(a.position, b.position) < 0.022f;
        }

        public static bool TryPointer(Handedness side, out Pose pose)
        {
            IHand hand = TrackedHands.Find(side);
            if (hand != null && hand.GetPointerPose(out pose)) return true;
            MetaAimHand aim = side == Handedness.Left ? MetaAimHand.left : MetaAimHand.right;
            if (aim != null && aim.added && ((MetaAimFlags)aim.aimFlags.ReadValue() & MetaAimFlags.Valid) != 0)
            {
                pose = ToWorld(new Pose(aim.devicePosition.ReadValue(), aim.deviceRotation.ReadValue()));
                return true;
            }
            if (!TryControllerPose(side == Handedness.Left, out pose)) return false;
            string prefix = side == Handedness.Left ? "Left" : "Right";
            InputAction rotation = actions.FindAction("XR/" + prefix + "PointerRotation");
            if (rotation.controls.Count > 0)
            {
                Quaternion controllerAim = rotation.ReadValue<Quaternion>();
                if (Quaternion.Dot(controllerAim, controllerAim) > .5f)
                    pose = ToWorld(new Pose(actions.FindAction("XR/" + prefix + "PointerPosition").ReadValue<Vector3>(), controllerAim));
            }
            return true;
        }
    }
}
