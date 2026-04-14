using UnityEngine;
using Unity.Netcode;

namespace WalaPaNameHehe
{
    public class ExtractableResource : NetworkBehaviour
    {
        [Header("Extraction")]
        [SerializeField] private bool disableWhenDepleted = true;
        [SerializeField] private bool keepObjectVisibleAfterExtraction = true;
        [SerializeField] private float stunDurationSeconds = 10f;

        [Header("Ready Indicator")]
        [SerializeField] private bool updateIndicatorColor = true;
        [SerializeField] private Renderer readyIndicatorRenderer;
        [SerializeField] private bool useReadyStateMaterials = true;
        [SerializeField] private bool enableOutlineWhenExtractable = true;
        [SerializeField] private Material notReadyMaterial;
        [SerializeField] private Material readyMaterial;
        [SerializeField] private Color notReadyColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color readyColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        [SerializeField] private bool useMaterialPropertyBlock = true;

        private readonly NetworkVariable<int> remainingExtractionCountNet = new(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> stunnedUntilServerTimeNet = new(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public int RemainingExtractionCount => IsNetworkRunning() ? remainingExtractionCountNet.Value : (wasExtractedOffline ? 0 : 1);
        public bool IsStunned => GetStunRemainingSeconds() > 0f;
        public float StunRemainingSeconds => GetStunRemainingSeconds();
        public bool CanExtract => RemainingExtractionCount > 0 && IsStunned;
        private bool wasExtractedOffline;
        private float stunnedUntilOfflineTime;
        private MaterialPropertyBlock indicatorMpb;
        private bool hasLastIndicatorState;
        private bool lastCanExtractState;
        private bool hasLastOutlineState;
        private bool lastOutlineState;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            remainingExtractionCountNet.OnValueChanged += OnRemainingExtractionChanged;
            stunnedUntilServerTimeNet.OnValueChanged += OnStunnedUntilChanged;

            if (IsServer)
            {
                remainingExtractionCountNet.Value = 1;
                stunnedUntilServerTimeNet.Value = 0d;
            }

            ApplyDepletedVisibilityState();
            ApplyReadyIndicatorColor(true);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            remainingExtractionCountNet.OnValueChanged -= OnRemainingExtractionChanged;
            stunnedUntilServerTimeNet.OnValueChanged -= OnStunnedUntilChanged;
        }

        private void Awake()
        {
            ExtractableResource[] all = GetComponents<ExtractableResource>();
            if (all != null && all.Length > 1)
            {
                Debug.LogWarning("ExtractableResource: multiple components found on same object. This can override indicator color updates.");
            }

            if (readyIndicatorRenderer == null)
            {
                readyIndicatorRenderer = GetComponent<Renderer>();
            }
        }

        private void Update()
        {
            ApplyReadyIndicatorColor();
            ApplyOutlineState();
        }

        public bool TryConsumeExtraction()
        {
            if (!CanExtract)
            {
                return false;
            }

            if (IsNetworkRunning())
            {
                if (!IsServer)
                {
                    return false;
                }

                remainingExtractionCountNet.Value = Mathf.Max(0, remainingExtractionCountNet.Value - 1);
            }
            else
            {
                wasExtractedOffline = true;
            }

            if (disableWhenDepleted && RemainingExtractionCount <= 0)
            {
                ApplyDepletedVisibilityState();
            }

            return true;
        }

        public bool TryApplyStun()
        {
            return TryApplyStun(stunDurationSeconds);
        }

        public bool TryApplyStun(float durationSeconds)
        {
            float duration = Mathf.Max(0.01f, durationSeconds);
            if (IsNetworkRunning())
            {
                if (!IsServer)
                {
                    return false;
                }

                double now = GetCurrentNetworkServerTime();
                double remaining = Mathf.Max(0f, (float)(stunnedUntilServerTimeNet.Value - now));
                stunnedUntilServerTimeNet.Value = now + System.Math.Max(remaining, (double)duration);
                ApplyDinoStun(duration);
                return true;
            }

            float nowOffline = Time.time;
            float remainingOffline = Mathf.Max(0f, stunnedUntilOfflineTime - nowOffline);
            stunnedUntilOfflineTime = nowOffline + Mathf.Max(remainingOffline, duration);
            ApplyDinoStun(duration);
            return true;
        }

        [ContextMenu("Reset Extractions")]
        public void ResetExtractions()
        {
            if (IsNetworkRunning())
            {
                if (!IsServer)
                {
                    return;
                }

                remainingExtractionCountNet.Value = 1;
                stunnedUntilServerTimeNet.Value = 0d;
            }
            else
            {
                wasExtractedOffline = false;
                stunnedUntilOfflineTime = 0f;
            }

            ApplyDepletedVisibilityState();
            ApplyReadyIndicatorColor(true);
        }

        private bool IsNetworkRunning()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        }

        private float GetStunRemainingSeconds()
        {
            if (IsNetworkRunning())
            {
                double now = GetCurrentNetworkServerTime();
                return Mathf.Max(0f, (float)(stunnedUntilServerTimeNet.Value - now));
            }

            return Mathf.Max(0f, stunnedUntilOfflineTime - Time.time);
        }

        private double GetCurrentNetworkServerTime()
        {
            if (NetworkManager.Singleton == null)
            {
                return 0d;
            }

            return NetworkManager.Singleton.ServerTime.Time;
        }

        private void OnRemainingExtractionChanged(int _, int __)
        {
            ApplyDepletedVisibilityState();
            ApplyReadyIndicatorColor(true);
        }

        private void OnStunnedUntilChanged(double _, double __)
        {
            ApplyReadyIndicatorColor(true);
        }

        private void ApplyDepletedVisibilityState()
        {
            bool depleted = RemainingExtractionCount <= 0;
            bool hide = disableWhenDepleted && depleted && !keepObjectVisibleAfterExtraction;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = !hide;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = !hide;
            }

            Outline[] outlines = GetComponentsInChildren<Outline>(true);
            for (int i = 0; i < outlines.Length; i++)
            {
                outlines[i].enabled = CanExtract;
            }
        }

        private void ApplyReadyIndicatorColor(bool force = false)
        {
            if (!updateIndicatorColor || readyIndicatorRenderer == null)
            {
                return;
            }

            bool canExtractNow = CanExtract;
            if (!force && hasLastIndicatorState && canExtractNow == lastCanExtractState)
            {
                return;
            }

            if (useReadyStateMaterials && TryApplyReadyStateMaterial(canExtractNow))
            {
                hasLastIndicatorState = true;
                lastCanExtractState = canExtractNow;
                return;
            }

            Color target = canExtractNow ? readyColor : notReadyColor;
            if (useMaterialPropertyBlock)
            {
                if (indicatorMpb == null)
                {
                    indicatorMpb = new MaterialPropertyBlock();
                }

                readyIndicatorRenderer.GetPropertyBlock(indicatorMpb);
                indicatorMpb.SetColor("_BaseColor", target);
                indicatorMpb.SetColor("_Color", target);
                indicatorMpb.SetColor("BaseColor", target);
                indicatorMpb.SetColor("_GridColor", target);
                indicatorMpb.SetColor("GridColor", target);
                readyIndicatorRenderer.SetPropertyBlock(indicatorMpb);
            }
            else
            {
                Material material = readyIndicatorRenderer.material;
                if (material != null)
                {
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", target);
                    }
                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", target);
                    }
                    if (material.HasProperty("BaseColor"))
                    {
                        material.SetColor("BaseColor", target);
                    }
                    if (material.HasProperty("_GridColor"))
                    {
                        material.SetColor("_GridColor", target);
                    }
                    if (material.HasProperty("GridColor"))
                    {
                        material.SetColor("GridColor", target);
                    }

                    if (!material.HasProperty("_BaseColor")
                        && !material.HasProperty("_Color")
                        && !material.HasProperty("BaseColor")
                        && !material.HasProperty("_GridColor")
                        && !material.HasProperty("GridColor"))
                    {
                        material.color = target;
                    }
                }
            }

            hasLastIndicatorState = true;
            lastCanExtractState = canExtractNow;
        }

        private void ApplyOutlineState(bool force = false)
        {
            if (!enableOutlineWhenExtractable)
            {
                return;
            }

            bool shouldEnable = CanExtract;
            if (!force && hasLastOutlineState && shouldEnable == lastOutlineState)
            {
                return;
            }

            Outline[] outlines = GetComponentsInChildren<Outline>(true);
            for (int i = 0; i < outlines.Length; i++)
            {
                Outline outline = outlines[i];
                if (outline != null)
                {
                    outline.enabled = shouldEnable;
                }
            }

            hasLastOutlineState = true;
            lastOutlineState = shouldEnable;
        }

        private bool TryApplyReadyStateMaterial(bool canExtractNow)
        {
            if (readyIndicatorRenderer == null)
            {
                return false;
            }

            Material targetMaterial = canExtractNow ? readyMaterial : notReadyMaterial;
            if (targetMaterial == null)
            {
                return false;
            }

            Material[] mats = readyIndicatorRenderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                readyIndicatorRenderer.sharedMaterial = targetMaterial;
                return true;
            }

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != targetMaterial)
                {
                    mats[i] = targetMaterial;
                    changed = true;
                }
            }

            if (changed)
            {
                readyIndicatorRenderer.sharedMaterials = mats;
            }

            return true;
        }

        private void ApplyDinoStun(float durationSeconds)
        {
            DinoAI dino = GetComponentInParent<DinoAI>();
            if (dino == null)
            {
                dino = GetComponentInChildren<DinoAI>(true);
            }

            if (dino == null)
            {
                return;
            }

            dino.Stun(durationSeconds, true);
        }
    }
}
