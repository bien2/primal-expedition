using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;

namespace WalaPaNameHehe
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class CameraEffect : MonoBehaviour
    {
        [Header("Low-Res Look")]
        [SerializeField] [Range(0.2f, 1f)] private float renderScale = 0.78f;
        [SerializeField] private bool disableCameraAntialiasing = true;
        [SerializeField] private bool applyInEditMode = true;

        private UniversalRenderPipelineAsset urpAsset;
        private float originalRenderScale = 1f;
        private bool hasOriginalRenderScale;
        private AntialiasingMode originalCameraAA;
        private bool hasOriginalCameraAA;
        private bool warnedNoUrp;
        private bool hasApplied;

        private void OnEnable()
        {
            RefreshEffectState();
        }

        private void OnDisable()
        {
            if (hasApplied)
            {
                RestoreDefaults();
                hasApplied = false;
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            RefreshEffectState();
        }

        private void Update()
        {
            // Keep visual state stable if ownership/camera context changes at runtime.
            RefreshEffectState();
        }

        [ContextMenu("Apply Low-Res Look")]
        public void ApplyLowResLook()
        {
            if (!CanRunNow() || !ShouldApplyForThisCamera())
            {
                return;
            }

            renderScale = Mathf.Clamp(renderScale, 0.2f, 1f);
            urpAsset = GetActiveUrpAsset();
            if (urpAsset != null)
            {
                if (!hasOriginalRenderScale)
                {
                    originalRenderScale = urpAsset.renderScale;
                    hasOriginalRenderScale = true;
                }

                urpAsset.renderScale = renderScale;
            }
            else if (!warnedNoUrp)
            {
                Debug.LogWarning("CameraEffect: No active URP asset found. Render scale was not applied.");
                warnedNoUrp = true;
            }

            if (!disableCameraAntialiasing)
            {
                return;
            }

            UniversalAdditionalCameraData camData = GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                if (!hasOriginalCameraAA)
                {
                    originalCameraAA = camData.antialiasing;
                    hasOriginalCameraAA = true;
                }

                camData.antialiasing = AntialiasingMode.None;
            }
        }

        [ContextMenu("Restore Default Render Quality")]
        public void RestoreDefaults()
        {
            if (!CanRunNow())
            {
                return;
            }

            if (urpAsset == null)
            {
                urpAsset = GetActiveUrpAsset();
            }

            if (urpAsset != null && hasOriginalRenderScale)
            {
                urpAsset.renderScale = originalRenderScale;
            }

            UniversalAdditionalCameraData camData = GetComponent<UniversalAdditionalCameraData>();
            if (camData != null && hasOriginalCameraAA)
            {
                camData.antialiasing = originalCameraAA;
            }
        }

        private void RefreshEffectState()
        {
            if (!CanRunNow())
            {
                if (hasApplied)
                {
                    RestoreDefaults();
                    hasApplied = false;
                }
                return;
            }

            bool shouldApply = ShouldApplyForThisCamera();
            if (shouldApply)
            {
                ApplyLowResLook();
                hasApplied = true;
            }
            else if (hasApplied)
            {
                RestoreDefaults();
                hasApplied = false;
            }
        }

        private bool CanRunNow()
        {
            return Application.isPlaying || applyInEditMode;
        }

        private UniversalRenderPipelineAsset GetActiveUrpAsset()
        {
            UniversalRenderPipelineAsset qualityAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (qualityAsset != null)
            {
                return qualityAsset;
            }

            UniversalRenderPipelineAsset currentAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (currentAsset != null)
            {
                return currentAsset;
            }

            return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        }

        private bool ShouldApplyForThisCamera()
        {
            if (!Application.isPlaying)
            {
                return true;
            }

            NetworkObject networkObject = GetComponentInParent<NetworkObject>();
            if (networkObject == null || NetworkManager.Singleton == null)
            {
                return true;
            }

            return networkObject.IsOwner;
        }
    }
}
