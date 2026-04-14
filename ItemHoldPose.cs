using UnityEngine;
using UnityEngine.Serialization;

namespace WalaPaNameHehe
{
    public class ItemHoldPose : MonoBehaviour
    {
        [Header("First-Person Hold Pose (relative to holder anchor)")]
        public Vector3 localPosition = Vector3.zero;
        public Vector3 localEulerAngles = Vector3.zero;

        [Header("Third-Person Hold Pose (optional override)")]
        [FormerlySerializedAs("overrideWorldViewPose")]
        public bool useThirdPersonPose = false;
        [FormerlySerializedAs("worldLocalPosition")]
        public Vector3 thirdPersonLocalPosition = Vector3.zero;
        [FormerlySerializedAs("worldLocalEulerAngles")]
        public Vector3 thirdPersonLocalEulerAngles = Vector3.zero;

        [Header("Custom Hold Scale (optional)")]
        [FormerlySerializedAs("overrideScale")]
        public bool useCustomHoldScale = false;
        [FormerlySerializedAs("localScale")]
        public Vector3 customHoldScale = Vector3.one;

        [Header("Per-View Scale Multipliers (optional)")]
        [FormerlySerializedAs("overrideViewScaleMultipliers")]
        public bool usePerViewScaleMultipliers = false;
        [FormerlySerializedAs("localViewScaleMultiplier")]
        public Vector3 firstPersonScaleMultiplier = Vector3.one;
        [FormerlySerializedAs("worldViewScaleMultiplier")]
        public Vector3 thirdPersonScaleMultiplier = Vector3.one;

        [ContextMenu("Capture Current -> First Person Pose")]
        private void CaptureCurrentToFirstPersonPose()
        {
            localPosition = transform.localPosition;
            localEulerAngles = transform.localEulerAngles;
        }

        [ContextMenu("Capture Current -> Third Person Pose")]
        private void CaptureCurrentToThirdPersonPose()
        {
            useThirdPersonPose = true;
            thirdPersonLocalPosition = transform.localPosition;
            thirdPersonLocalEulerAngles = transform.localEulerAngles;
        }

        [ContextMenu("Apply First Person Pose -> Transform")]
        private void ApplyFirstPersonPoseToTransform()
        {
            transform.localPosition = localPosition;
            transform.localEulerAngles = localEulerAngles;
        }

        [ContextMenu("Apply Third Person Pose -> Transform")]
        private void ApplyThirdPersonPoseToTransform()
        {
            if (!useThirdPersonPose)
            {
                return;
            }

            transform.localPosition = thirdPersonLocalPosition;
            transform.localEulerAngles = thirdPersonLocalEulerAngles;
        }
    }
}
