using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe
{
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayerOwnership : NetworkBehaviour
    {
        [Header("Local-Only Behaviours")]
        [SerializeField] private MonoBehaviour[] localOnlyBehaviours;

        [Header("Local-Only Objects")]
        [SerializeField] private GameObject[] localOnlyObjects;
        [SerializeField] private bool avoidDisablingCameraGameObjects = true;

        [Header("Hide For Owner")]
        [SerializeField] private Renderer[] hideForOwnerRenderers;

        [Header("Optional Auto-Detection")]
        [SerializeField] private bool autoDisableChildAudioListeners = true;

        private bool ownerHiddenRenderersOverrideActive;
        private bool ownerHiddenRenderersVisibleOverride;

        private void Start()
        {
            ApplyOwnershipState(IsOwner);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnershipState(IsOwner);
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            ApplyOwnershipState(true);
        }

        public override void OnLostOwnership()
        {
            base.OnLostOwnership();
            ApplyOwnershipState(false);
        }

        public void SetOwnerHiddenRenderersForcedVisible(bool forcedVisible)
        {
            ownerHiddenRenderersOverrideActive = true;
            ownerHiddenRenderersVisibleOverride = forcedVisible;
            ApplyOwnershipState(IsOwner);
        }

        public void ClearOwnerHiddenRenderersForcedVisible()
        {
            ownerHiddenRenderersOverrideActive = false;
            ApplyOwnershipState(IsOwner);
        }

        private void ApplyOwnershipState(bool isLocalOwner)
        {
            if (localOnlyBehaviours != null)
            {
                for (int i = 0; i < localOnlyBehaviours.Length; i++)
                {
                    MonoBehaviour behaviour = localOnlyBehaviours[i];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    // Keep NetworkBehaviours enabled so ServerRpc/ClientRpc continues to work.
                    // Owner checks should be handled inside those scripts (CoopGuard/CoopBehaviour).
                    if (behaviour is NetworkBehaviour)
                    {
                        behaviour.enabled = true;
                        continue;
                    }

                    behaviour.enabled = isLocalOwner;
                }
            }

            if (localOnlyObjects != null)
            {
                for (int i = 0; i < localOnlyObjects.Length; i++)
                {
                    if (localOnlyObjects[i] != null)
                    {
                        GameObject localObject = localOnlyObjects[i];

                        // Disabling the entire camera object can lead to "No cameras rendering"
                        // during ownership/spawn timing transitions. Keep object active and toggle
                        // camera/listener components instead.
                        if (avoidDisablingCameraGameObjects && ContainsCamera(localObject))
                        {
                            Camera[] cameras = localObject.GetComponentsInChildren<Camera>(true);
                            for (int c = 0; c < cameras.Length; c++)
                            {
                                if (cameras[c] != null)
                                {
                                    cameras[c].enabled = isLocalOwner;
                                }
                            }

                            AudioListener[] listeners = localObject.GetComponentsInChildren<AudioListener>(true);
                            for (int l = 0; l < listeners.Length; l++)
                            {
                                if (listeners[l] != null)
                                {
                                    listeners[l].enabled = isLocalOwner;
                                }
                            }

                            if (!localObject.activeSelf)
                            {
                                localObject.SetActive(true);
                            }
                            continue;
                        }

                        localObject.SetActive(isLocalOwner);
                    }
                }
            }

            if (autoDisableChildAudioListeners)
            {
                AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
                for (int i = 0; i < listeners.Length; i++)
                {
                    listeners[i].enabled = isLocalOwner;
                }
            }

            // Keep remote players visible by default, except explicit owner-hidden renderers.
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (ShouldHideForOwner(renderer))
                {
                    bool visibleForOwner = ownerHiddenRenderersOverrideActive
                        ? ownerHiddenRenderersVisibleOverride
                        : false;
                    renderer.enabled = isLocalOwner ? visibleForOwner : true;
                }
                else
                {
                    renderer.enabled = true;
                }
            }
        }

        private bool ShouldHideForOwner(Renderer renderer)
        {
            if (hideForOwnerRenderers == null || renderer == null)
            {
                return false;
            }

            for (int i = 0; i < hideForOwnerRenderers.Length; i++)
            {
                if (hideForOwnerRenderers[i] == renderer)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsCamera(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            return root.GetComponent<Camera>() != null || root.GetComponentInChildren<Camera>(true) != null;
        }
    }
}
