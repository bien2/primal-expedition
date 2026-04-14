using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public class PlayerExtractor : NetworkBehaviour
    {
        [Header("Interaction")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float interactDistance = 3f;
        [SerializeField] private LayerMask boxLayers = ~0;
        [SerializeField] private bool requireExtractableResource = true;
        [SerializeField] private bool requireExtractorEquipped = true;
        [SerializeField] private GameObject requiredExtractorPrefab;

        [Header("Extraction")]
        [SerializeField] private float extractDuration = 5f;
        [SerializeField] private int itemReward = 1;
        [SerializeField] private GameObject rewardItemPrefab;
        [SerializeField] private InventorySystem inventorySystem;

        [Header("UI")]
        [SerializeField] private bool showPromptUi = true;

        [Header("Audio")]
        [SerializeField] private AudioSource extractionAudioSource;
        [SerializeField] private AudioClip extractionLoopClip;
        [SerializeField] [Range(0f, 1f)] private float extractionLoopVolume = 1f;
        [SerializeField] private float extractionSoundEmitInterval = 0.3f;

        private bool isExtracting;
        private float extractTimer;
        private int itemCount;
        private Collider currentBox;
        private Transform currentTargetRoot;
        private Collider detectedBox;
        private ExtractableResource detectedResource;
        private ExtractableResource currentResource;
        private ulong currentResourceNetworkId;
        private bool hasCurrentResourceNetworkId;
        private Texture2D uiPixel;
        private WeaponDrone weaponDrone;
        private string transientPromptMessage;
        private float transientPromptUntil;
        private float nextExtractionSoundTime;

        private const int ExtractionFailNotFound = 1;
        private const int ExtractionFailNotSedated = 2;
        private const int ExtractionFailAlreadyDepleted = 3;

        public bool IsExtractingInProgress => isExtracting;

        private void Start()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }

            if (inventorySystem == null)
            {
                inventorySystem = GetComponent<InventorySystem>();
            }

            if (weaponDrone == null)
            {
                weaponDrone = GetComponent<WeaponDrone>();
            }

            if (extractionAudioSource == null)
            {
                extractionAudioSource = GetComponent<AudioSource>();
            }

            if (uiPixel == null)
            {
                uiPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                uiPixel.SetPixel(0, 0, Color.white);
                uiPixel.Apply();
            }
        }

        private void Update()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.gKey.wasPressedThisFrame)
            {
                DropInventoryItem();
            }

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }

            if (playerCamera == null)
            {
                return;
            }

            if (!isExtracting)
            {
                bool hasBoxInFront = TryGetValidTargetInFront(out detectedBox, out detectedResource);
                if (Keyboard.current.eKey.wasPressedThisFrame && hasBoxInFront)
                {
                    if (!CanExtractWithCurrentItem())
                    {
                        return;
                    }

                    if (requireExtractableResource && (detectedResource == null || !detectedResource.CanExtract))
                    {
                        return;
                    }

                    StartExtraction(detectedBox, detectedResource);
                }

                return;
            }

            if (!Keyboard.current.eKey.isPressed)
            {
                CancelExtraction();
                return;
            }

            if (IsMovementInputPressed())
            {
                CancelExtraction();
                return;
            }

            if (!IsStillValidExtractionTarget())
            {
                CancelExtraction();
                return;
            }

            if (!CanExtractWithCurrentItem())
            {
                CancelExtraction();
                return;
            }

            extractTimer += Time.deltaTime;
            TryEmitExtractionSound();
            if (extractTimer >= extractDuration)
            {
                CompleteExtraction();
            }
        }

        private void OnDisable()
        {
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            StopExtractionLoopSound();
            if (uiPixel != null)
            {
                Destroy(uiPixel);
                uiPixel = null;
            }
        }

        private bool TryGetValidTargetInFront(out Collider box, out ExtractableResource resource)
        {
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, interactDistance, boxLayers, QueryTriggerInteraction.Collide);
            box = null;
            resource = null;

            if (!hit)
            {
                return false;
            }

            box = hitInfo.collider;
            resource = box.GetComponentInParent<ExtractableResource>();

            if (!requireExtractableResource)
            {
                return true;
            }

            return resource != null;
        }

        private void StartExtraction(Collider box, ExtractableResource resource)
        {
            currentBox = box;
            currentTargetRoot = GetTargetRoot(box);
            currentResource = resource;
            if (currentResource != null)
            {
                NetworkObject networkObject = currentResource.GetComponentInParent<NetworkObject>();
                hasCurrentResourceNetworkId = networkObject != null && networkObject.IsSpawned;
                currentResourceNetworkId = hasCurrentResourceNetworkId ? networkObject.NetworkObjectId : 0;
            }
            else
            {
                hasCurrentResourceNetworkId = false;
                currentResourceNetworkId = 0;
            }
            detectedBox = null;
            detectedResource = null;
            extractTimer = 0f;
            isExtracting = true;
            StartExtractionLoopSound();
        }

        private bool IsStillValidExtractionTarget()
        {
            if (currentBox == null || currentTargetRoot == null)
            {
                return false;
            }

            if (requireExtractableResource && (currentResource == null || !currentResource.CanExtract))
            {
                return false;
            }

            Vector3 cameraPos = playerCamera.transform.position;
            Vector3 nearestPoint = currentBox.ClosestPoint(cameraPos);
            Vector3 toNearestPoint = nearestPoint - cameraPos;
            if (toNearestPoint.sqrMagnitude > interactDistance * interactDistance)
            {
                return false;
            }

            return true;
        }

        private void CompleteExtraction()
        {
            StopExtractionLoopSound();
            if (NetworkManager != null && NetworkManager.IsListening)
            {
                Vector3 resourcePosition = currentResource != null ? currentResource.transform.position : Vector3.zero;
                string resourceName = currentResource != null ? CleanName(currentResource.name) : string.Empty;
                RequestCompleteExtractionServerRpc(hasCurrentResourceNetworkId, currentResourceNetworkId, resourcePosition, resourceName);
                isExtracting = false;
                extractTimer = 0f;
                currentBox = null;
                currentTargetRoot = null;
                currentResource = null;
                hasCurrentResourceNetworkId = false;
                currentResourceNetworkId = 0;
                return;
            }

            if (requireExtractableResource && currentResource != null && !currentResource.TryConsumeExtraction())
            {
                CancelExtraction();
                return;
            }

            ApplyExtractionResultLocal(
                itemReward,
                hasCurrentResourceNetworkId,
                currentResourceNetworkId,
                currentResource != null ? currentResource.transform.position : Vector3.zero,
                currentResource != null ? CleanName(currentResource.name) : string.Empty,
                false,
                0,
                HasEquippedLuckyCharm());
            isExtracting = false;
            extractTimer = 0f;
            currentBox = null;
            currentTargetRoot = null;
            currentResource = null;
            hasCurrentResourceNetworkId = false;
            currentResourceNetworkId = 0;
        }

        private void CancelExtraction()
        {
            StopExtractionLoopSound();
            isExtracting = false;
            extractTimer = 0f;
            currentBox = null;
            currentTargetRoot = null;
            currentResource = null;
            hasCurrentResourceNetworkId = false;
            currentResourceNetworkId = 0;
        }

        private Transform GetTargetRoot(Collider target)
        {
            if (target == null)
            {
                return null;
            }

            if (target.attachedRigidbody != null)
            {
                return target.attachedRigidbody.transform;
            }

            return target.transform.root;
        }

        private bool IsMovementInputPressed()
        {
            if (Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current.wKey.isPressed
                || Keyboard.current.aKey.isPressed
                || Keyboard.current.sKey.isPressed
                || Keyboard.current.dKey.isPressed
                || Keyboard.current.upArrowKey.isPressed
                || Keyboard.current.leftArrowKey.isPressed
                || Keyboard.current.downArrowKey.isPressed
                || Keyboard.current.rightArrowKey.isPressed;
        }

        private void GiveRewardItem()
        {
            if (rewardItemPrefab == null || inventorySystem == null)
            {
                return;
            }

            for (int i = 0; i < itemReward; i++)
            {
                bool stored = inventorySystem.TryStoreItem(rewardItemPrefab, out _);
                if (!stored)
                {
                    Debug.Log("Inventory is full. Could not store more extracted items.");
                    break;
                }
            }
        }

        private void GiveRewardItemCount(int rewardCount)
        {
            if (rewardCount <= 0 || rewardItemPrefab == null || inventorySystem == null)
            {
                return;
            }

            for (int i = 0; i < rewardCount; i++)
            {
                bool stored = inventorySystem.TryStoreItem(rewardItemPrefab, out _);
                if (!stored)
                {
                    Debug.Log("Inventory is full. Could not store more extracted items.");
                    break;
                }
            }
        }

        [ServerRpc]
        private void RequestCompleteExtractionServerRpc(bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName, ServerRpcParams rpcParams = default)
        {
            ulong requesterClientId = rpcParams.Receive.SenderClientId;
            ExtractableResource resource = ResolveResourceReference(hasResourceNetworkId, resourceNetworkId, resourcePosition, resourceName);
            if (requireExtractableResource)
            {
                if (resource == null)
                {
                    NotifyExtractionFailedClientRpc(ExtractionFailNotFound, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new[] { requesterClientId }
                        }
                    });
                    return;
                }

                if (!resource.IsStunned)
                {
                    NotifyExtractionFailedClientRpc(ExtractionFailNotSedated, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new[] { requesterClientId }
                        }
                    });
                    return;
                }

                if (resource.RemainingExtractionCount <= 0)
                {
                    NotifyExtractionFailedClientRpc(ExtractionFailAlreadyDepleted, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new[] { requesterClientId }
                        }
                    });
                    return;
                }

                if (!resource.TryConsumeExtraction())
                {
                    NotifyExtractionFailedClientRpc(ExtractionFailAlreadyDepleted, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new[] { requesterClientId }
                        }
                    });
                    return;
                }
            }

            int luckySlotIndex = -1;
            GameObject bonusPrefab = null;
            bool hasLuckyCharmEquipped = inventorySystem != null && inventorySystem.TryFindLuckyCharmSlot(out luckySlotIndex, out bonusPrefab);

            if (inventorySystem != null && rewardItemPrefab != null)
            {
                if (hasLuckyCharmEquipped && bonusPrefab != null)
                {
                    inventorySystem.ServerReplaceSlotItemWithPrefab(luckySlotIndex, bonusPrefab);
                }

                inventorySystem.ServerGrantNetworkedItem(rewardItemPrefab, itemReward);
            }

            bool grantLuckyBonus = hasLuckyCharmEquipped;

            bool includeNetworkId = false;
            ulong resolvedNetworkId = 0;
            Vector3 resolvedPosition = resourcePosition;
            string resolvedName = resourceName;

            if (resource != null)
            {
                resolvedPosition = resource.transform.position;
                resolvedName = CleanName(resource.name);
                NetworkObject no = resource.GetComponentInParent<NetworkObject>();
                if (no != null && no.IsSpawned)
                {
                    includeNetworkId = true;
                    resolvedNetworkId = no.NetworkObjectId;
                }
            }

            HunterMeterManager.Instance?.ReportBloodSampleExtracted(requesterClientId);

            ApplyExtractionResultClientRpc(itemReward, includeNetworkId, resolvedNetworkId, resolvedPosition, resolvedName, requesterClientId, grantLuckyBonus);
        }

        [ClientRpc]
        private void ApplyExtractionResultClientRpc(int rewardCount, bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName, ulong rewardReceiverClientId, bool grantLuckyBonus)
        {
            bool resourceAlreadyConsumedOnServer = IsServer;
            ApplyExtractionResultLocal(rewardCount, hasResourceNetworkId, resourceNetworkId, resourcePosition, resourceName, resourceAlreadyConsumedOnServer, rewardReceiverClientId, grantLuckyBonus);
        }

        [ClientRpc]
        private void NotifyExtractionFailedClientRpc(int reasonCode, ClientRpcParams clientRpcParams = default)
        {
            StopExtractionLoopSound();
            isExtracting = false;
            extractTimer = 0f;
            currentBox = null;
            currentTargetRoot = null;
            currentResource = null;
            hasCurrentResourceNetworkId = false;
            currentResourceNetworkId = 0;

            string message = reasonCode switch
            {
                ExtractionFailNotSedated => "Target is no longer sedated",
                ExtractionFailAlreadyDepleted => "Target already extracted",
                _ => "Extraction failed"
            };
            SetTransientPrompt(message, 1.5f);
        }

        private void ApplyExtractionResultLocal(int rewardCount, bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName, bool skipResourceConsume, ulong rewardReceiverClientId, bool grantLuckyBonus)
        {
            if (requireExtractableResource && !skipResourceConsume)
            {
                ExtractableResource resource = ResolveResourceReference(hasResourceNetworkId, resourceNetworkId, resourcePosition, resourceName);
                if (resource != null)
                {
                    resource.TryConsumeExtraction();
                }
            }

            bool shouldReceiveReward =
                NetworkManager == null ||
                !NetworkManager.IsListening ||
                NetworkManager.LocalClientId == rewardReceiverClientId;

            if (shouldReceiveReward)
            {
                itemCount += Mathf.Max(0, rewardCount);

                bool isNetworkSession = NetworkManager != null && NetworkManager.IsListening;
                if (!isNetworkSession)
                {
                    if (grantLuckyBonus
                        && inventorySystem != null
                        && inventorySystem.TryFindLuckyCharmSlot(out int luckySlotIndex, out GameObject bonusPrefab)
                        && bonusPrefab != null)
                    {
                        inventorySystem.ReplaceSlotItemWithPrefabLocal(luckySlotIndex, bonusPrefab);
                    }

                    GiveRewardItemCount(rewardCount);
                }
            }
        }

        private void StartExtractionLoopSound()
        {
            if (extractionAudioSource == null || extractionLoopClip == null)
            {
                return;
            }

            extractionAudioSource.clip = extractionLoopClip;
            extractionAudioSource.loop = true;
            extractionAudioSource.volume = Mathf.Clamp01(extractionLoopVolume);
            if (!extractionAudioSource.isPlaying)
            {
                extractionAudioSource.Play();
            }
        }

        private bool HasEquippedLuckyCharm()
        {
            if (inventorySystem == null)
            {
                return false;
            }

            return IsLuckyCharmSelected(out LuckyCharmItem charm) && charm != null && charm.bonusPrefab != null;
        }

        private bool IsLuckyCharmSelected(out LuckyCharmItem charm)
        {
            charm = null;
            GameObject selected = inventorySystem != null ? inventorySystem.GetSelectedItem() : null;
            if (selected == null)
            {
                return false;
            }

            charm = selected.GetComponentInChildren<LuckyCharmItem>(true);
            return charm != null;
        }

        private void StopExtractionLoopSound()
        {
            if (extractionAudioSource == null)
            {
                return;
            }

            if (extractionAudioSource.loop)
            {
                extractionAudioSource.Stop();
            }
            extractionAudioSource.loop = false;
            if (extractionAudioSource.clip == extractionLoopClip)
            {
                extractionAudioSource.clip = null;
            }
        }

        private void SetTransientPrompt(string message, float durationSeconds)
        {
            transientPromptMessage = message;
            transientPromptUntil = Time.time + Mathf.Max(0.1f, durationSeconds);
        }

        private void TryEmitExtractionSound()
        {
            if (!isExtracting)
            {
                return;
            }

            if (Time.time < nextExtractionSoundTime)
            {
                return;
            }

            nextExtractionSoundTime = Time.time + Mathf.Max(0.05f, extractionSoundEmitInterval);
            Vector3 sourcePosition = currentTargetRoot != null ? currentTargetRoot.position : transform.position;
            EmitWorldSound(sourcePosition, 1f);
        }

        private void EmitWorldSound(Vector3 position, float radius)
        {
            float clampedRadius = Mathf.Max(0f, radius);
            if (clampedRadius <= 0f)
            {
                return;
            }

            if (NetworkManager != null && NetworkManager.IsListening)
            {
                if (IsServer)
                {
                    WorldSoundStimulus.Emit(position, clampedRadius);
                }
                else
                {
                    RelayWorldSoundServerRpc(position, clampedRadius);
                }

                return;
            }

            WorldSoundStimulus.Emit(position, clampedRadius);
        }

        [ServerRpc]
        private void RelayWorldSoundServerRpc(Vector3 position, float radius)
        {
            float clampedRadius = Mathf.Max(0f, radius);
            if (clampedRadius <= 0f)
            {
                return;
            }

            WorldSoundStimulus.Emit(position, clampedRadius);
        }

        private ExtractableResource ResolveResourceReference(bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName)
        {
            if (hasResourceNetworkId && NetworkManager != null && NetworkManager.SpawnManager != null)
            {
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(resourceNetworkId, out NetworkObject no) && no != null)
                {
                    ExtractableResource byNetworkId = no.GetComponentInParent<ExtractableResource>();
                    if (byNetworkId == null)
                    {
                        byNetworkId = no.GetComponentInChildren<ExtractableResource>(true);
                    }

                    if (byNetworkId != null)
                    {
                        return byNetworkId;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                return null;
            }

            ExtractableResource[] all = FindObjectsByType<ExtractableResource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ExtractableResource best = null;
            float bestSqrDist = float.MaxValue;
            string wantedName = CleanName(resourceName);
            for (int i = 0; i < all.Length; i++)
            {
                ExtractableResource candidate = all[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.Equals(CleanName(candidate.name), wantedName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float sqr = (candidate.transform.position - resourcePosition).sqrMagnitude;
                if (sqr < bestSqrDist)
                {
                    bestSqrDist = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        private static string CleanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            string name = rawName;
            int cloneSuffixIndex = name.IndexOf("(Clone)");
            if (cloneSuffixIndex >= 0)
            {
                name = name.Substring(0, cloneSuffixIndex).Trim();
            }

            return name.Trim();
        }

        private void DropInventoryItem()
        {
            if (inventorySystem == null)
            {
                return;
            }

            inventorySystem.TryDropSelectedItemNetworked(transform);
        }

        private bool CanExtractWithCurrentItem()
        {
            if (!requireExtractorEquipped)
            {
                return true;
            }

            if (inventorySystem == null)
            {
                return false;
            }

            return inventorySystem.IsSelectedItemMatchingPrefab(requiredExtractorPrefab);
        }

        private void OnGUI()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (!showPromptUi)
            {
                return;
            }

            GUIStyle centeredLabel = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold
            };

            if (isExtracting)
            {
                float progress = Mathf.Clamp01(extractTimer / Mathf.Max(0.01f, extractDuration));
                float cx = Screen.width * 0.5f;
                float cy = Screen.height * 0.5f;
                DrawCircularProgressRing(cx, cy, 34f, 8f, progress, 96, new Color(1f, 1f, 1f, 0.25f), new Color(0.208f, 0.816f, 0.886f, 1f));
                GUI.Label(new Rect(cx - 180f, cy + 44f, 360f, 32f), "Extracting...", centeredLabel);
                return;
            }

            if (Time.time < transientPromptUntil && !string.IsNullOrWhiteSpace(transientPromptMessage))
            {
                float x = (Screen.width - 460f) * 0.5f;
                float y = (Screen.height * 0.5f) + 36f;
                GUI.Label(new Rect(x, y, 460f, 40f), transientPromptMessage, centeredLabel);
                return;
            }

            if (detectedBox != null)
            {
                if (weaponDrone != null && weaponDrone.IsSummoningDrone)
                {
                    return;
                }

                string prompt;
                if (!CanExtractWithCurrentItem())
                {
                    string requiredName = requiredExtractorPrefab != null ? requiredExtractorPrefab.name : "extractor";
                    prompt = $"Equip '{requiredName}' to extract";
                }
                else
                {
                    if (requireExtractableResource && detectedResource != null && !detectedResource.CanExtract)
                    {
                        prompt = "Sedate target first";
                    }
                    else
                    {
                        prompt = $"Hold E to extract ({extractDuration:0.#}s)";
                    }
                }
                float x = (Screen.width - 460f) * 0.5f;
                float y = (Screen.height * 0.5f) + 36f;
                GUI.Label(new Rect(x, y, 460f, 40f), prompt, centeredLabel);
            }
        }

        private bool IsLocallyControlled()
        {
            return CoopGuard.IsLocalOwnerOrOffline(this);
        }

        private void DrawCircularProgressRing(
            float centerX,
            float centerY,
            float radius,
            float thickness,
            float progress,
            int segments,
            Color backgroundColor,
            Color fillColor)
        {
            if (uiPixel == null)
            {
                return;
            }

            int segCount = Mathf.Max(16, segments);
            float clamped = Mathf.Clamp01(progress);
            int fillSegments = Mathf.RoundToInt(segCount * clamped);

            for (int i = 0; i < segCount; i++)
            {
                float t = (float)i / segCount;
                float angle = t * Mathf.PI * 2f - (Mathf.PI * 0.5f);
                float x = centerX + Mathf.Cos(angle) * radius;
                float y = centerY + Mathf.Sin(angle) * radius;

                GUI.color = i < fillSegments ? fillColor : backgroundColor;
                GUI.DrawTexture(new Rect(x - (thickness * 0.5f), y - (thickness * 0.5f), thickness, thickness), uiPixel);
            }

            GUI.color = Color.white;
        }

    }
}
