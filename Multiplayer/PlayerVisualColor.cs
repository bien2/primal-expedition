using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe
{
    public class PlayerVisualColor : NetworkBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Renderer[] targetRenderers;
        [SerializeField] private bool autoFindRenderers = true;

        [Header("Color Setup")]
        [SerializeField] private bool useOwnerPalette = true;
        [SerializeField] private Color defaultColor = new(0.75f, 0.75f, 0.75f, 1f);
        [SerializeField] private Color[] ownerPalette =
        {
            new(0.85f, 0.25f, 0.25f, 1f),
            new(0.25f, 0.55f, 0.95f, 1f),
            new(0.25f, 0.8f, 0.35f, 1f),
            new(0.95f, 0.75f, 0.2f, 1f)
        };

        [Header("Shader Property Names")]
        [SerializeField] private string urpColorProperty = "_BaseColor";
        [SerializeField] private string legacyColorProperty = "_Color";

        private readonly NetworkVariable<Color32> syncedColor = new(
            new Color32(191, 191, 191, 255),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock propertyBlock;

        private void Awake()
        {
            ResolveRenderers();
            propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            // Offline fallback.
            if (!IsNetworkActive())
            {
                ApplyColor(defaultColor);
            }
        }

        public override void OnNetworkSpawn()
        {
            syncedColor.OnValueChanged += OnColorChanged;

            if (IsServer)
            {
                syncedColor.Value = (Color32)PickSpawnColor();
            }

            ApplyColor(syncedColor.Value);
        }

        public override void OnNetworkDespawn()
        {
            syncedColor.OnValueChanged -= OnColorChanged;
        }

        private void OnColorChanged(Color32 _, Color32 current)
        {
            ApplyColor(current);
        }

        private Color PickSpawnColor()
        {
            if (!useOwnerPalette || ownerPalette == null || ownerPalette.Length == 0)
            {
                return defaultColor;
            }

            int index = (int)(OwnerClientId % (ulong)ownerPalette.Length);
            return ownerPalette[index];
        }

        private void ApplyColor(Color color)
        {
            ResolveRenderers();

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer r = targetRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(propertyBlock);

                bool hasUrp = r.sharedMaterial != null && r.sharedMaterial.HasProperty(urpColorProperty);
                bool hasLegacy = r.sharedMaterial != null && r.sharedMaterial.HasProperty(legacyColorProperty);

                if (hasUrp)
                {
                    propertyBlock.SetColor(urpColorProperty, color);
                }
                else if (hasLegacy)
                {
                    propertyBlock.SetColor(legacyColorProperty, color);
                }

                r.SetPropertyBlock(propertyBlock);
            }
        }

        private void ResolveRenderers()
        {
            if (!autoFindRenderers)
            {
                return;
            }

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                targetRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private bool IsNetworkActive()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }
    }
}
