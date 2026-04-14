using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections;
using System.Collections.Generic;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public partial class InventorySystem : NetworkBehaviour
    {
        [Header("Slots")]
        [SerializeField] [Min(2)] private int slotCount = 5;
        [SerializeField] private Transform[] slotAnchors;
        [SerializeField] private bool showInventoryUi = true;
        [SerializeField] private Transform defaultItemAnchor;
        [SerializeField] private bool useSeparateViewAnchors = true;
        [SerializeField] private Transform localViewAnchor;
        [SerializeField] private Transform worldViewAnchor;
        [Header("World Held Anchor")]
        [SerializeField] private bool autoResolveWorldAnchorFromHandBone = true;
        [SerializeField] private Transform worldHandTransform;
        [SerializeField] private HumanBodyBones worldHandBone = HumanBodyBones.RightHand;
        [SerializeField] private string worldHandBoneNameFallback = "mixamorig:RightHand";
        [SerializeField] private bool createWorldHandHolderIfMissing = true;
        [SerializeField] private string worldHandHolderName = "WorldItemHolder";
        [Header("Pickup Animation")]
        [SerializeField] private bool normalizeHeldItemScale = true;
        [SerializeField] private bool restoreOriginalScaleOnThrow = true;
        [SerializeField] private float throwCooldownSeconds = 0.12f;
        [SerializeField] private bool debugThrowFlow = false;
        [SerializeField] private float throwSpawnForward = 0.7f;
        [SerializeField] private float throwSpawnUp = 0.2f;
        [Header("Drop")]
        [SerializeField] private float dropRayDistance = 3f;
        [SerializeField] private float dropRayStartHeight = 0.25f;
        [SerializeField] private float dropGroundOffset = 0.05f;
        [SerializeField] private float dropFallbackDown = 0.4f;

        [Header("Local First-Person View")]
        [SerializeField] private bool applyLocalViewOffset = true;
        [SerializeField] private Vector3 localViewPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 localViewEulerOffset = Vector3.zero;
        [SerializeField] private bool useDifferentHeldScalePerView = true;
        [SerializeField] private Vector3 localHeldScaleMultiplier = Vector3.one;
        [SerializeField] private Vector3 worldHeldScaleMultiplier = Vector3.one;

        // --- PlayerItemPickup (merged) ---
        [Header("Pickup")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float interactDistance = 3f;
        [SerializeField] private float interactRadius = 0.22f;
        [SerializeField] private LayerMask pickupLayers = ~0;
        [SerializeField] private bool showPickupPrompt = true;
        [SerializeField] private AudioSource pickupAudioSource;
        [SerializeField] private AudioClip[] pickupClips;
        [SerializeField] [Range(0f, 1f)] private float pickupVolume = 0.8f;

        // --- PlayerCrosshair (merged) ---
        [Header("Crosshair")]
        [SerializeField] private bool showCrosshair = true;
        [SerializeField] private float crosshairSize = 16f;
        [SerializeField] private float crosshairThickness = 2f;
        [SerializeField] private Color crosshairColor = Color.white;

        [Header("Inventory UI Style")]
        [SerializeField] [Range(0.8f, 1.5f)] private float inventoryUiScale = 1f;
        [SerializeField] private Color inventoryPanelColor = new Color(0.05f, 0.08f, 0.12f, 0.68f);
        [SerializeField] private Color slotColor = new Color(0.08f, 0.13f, 0.2f, 0.82f);
        [SerializeField] private Color selectedSlotColor = new Color(0.12f, 0.4f, 0.58f, 0.95f);
        [SerializeField] private Color slotBorderColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color selectedSlotBorderColor = new Color(0.5f, 0.93f, 1f, 1f);

        [Header("Inventory Input")]
        [SerializeField] private bool enableMouseWheelSelection = true;
        [SerializeField] private bool invertMouseWheel = false;
        [SerializeField] private float mouseWheelSwitchCooldown = 0.08f;

        [Header("Loadout")]
        [SerializeField] private bool enableLoadoutUi = true;
        [SerializeField] private bool showLoadoutPrompt = true;
        [SerializeField] private LayerMask loadoutLayers = ~0;
        [SerializeField] private AudioSource loadoutAudioSource;
        [SerializeField] private AudioClip loadoutSelectClip;
        [SerializeField] [Range(0f, 1f)] private float loadoutSelectVolume = 0.9f;

        private GameObject[] slotItems;
        private ulong[] slotItemIds;
        private int selectedSlot;
        private readonly Dictionary<GameObject, Vector3> originalScales = new();
        private GameObject detectedPickupObject;
        private Outline detectedOutline;
        private Outline lastHighlightedOutline;
        private Texture2D crosshairPixel;
        private float nextMouseWheelSwitchTime;
        private float nextThrowTime;
        private GameObject localViewProxy;
        private GameObject localViewSource;
        private LoadoutStation detectedLoadoutStation;
        private LoadoutStation activeLoadoutStation;
        private bool loadoutUiOpen;
        private bool forceLoadoutSelection;
        private int lastLoadoutSelectionDay = -1;
        private PlayerMovement playerMovement;
        private DayAdvanceTerminal detectedTerminal;
        private bool uiSuppressed;
        private CursorLockMode storedCursorLock;
        private bool storedCursorVisible;
        private bool hasStoredCursorState;
        private void Awake()
        {


            slotCount = Mathf.Max(2, slotCount);
            slotItems = new GameObject[slotCount];
            slotItemIds = new ulong[slotCount];
            if (pickupAudioSource == null)
            {
                pickupAudioSource = GetComponent<AudioSource>();
            }
            if (loadoutAudioSource == null)
            {
                loadoutAudioSource = pickupAudioSource != null ? pickupAudioSource : GetComponent<AudioSource>();
            }

            if (defaultItemAnchor == null)
            {
                Transform foundItemHolder = transform.Find("ItemHolder");
                if (foundItemHolder != null)
                {
                    defaultItemAnchor = foundItemHolder;
                }
            }

            if (worldViewAnchor == null)
            {
                Transform foundWorldHolder = transform.Find("WorldItemHolder");
                if (foundWorldHolder != null)
                {
                    worldViewAnchor = foundWorldHolder;
                }
            }

            if (autoResolveWorldAnchorFromHandBone && worldHandTransform != null)
            {
                Transform resolved = ResolveWorldAnchorFromHandBone();
                if (resolved != null)
                {
                    worldViewAnchor = resolved;
                }
            }
            else if (worldViewAnchor == null && autoResolveWorldAnchorFromHandBone)
            {
                worldViewAnchor = ResolveWorldAnchorFromHandBone();
            }

            if (localViewAnchor == null)
            {
                Transform camera = transform.Find("MainCamera");
                if (camera != null)
                {
                    Transform foundLocalHolder = camera.Find("ItemHolder");
                    if (foundLocalHolder != null)
                    {
                        localViewAnchor = foundLocalHolder;
                    }
                }
            }

            selectedSlot = 0;
            RefreshSlotVisibility(-1);

            if (crosshairPixel == null)
            {
                crosshairPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                crosshairPixel.SetPixel(0, 0, Color.white);
                crosshairPixel.Apply();
            }

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }
        }

        private void Update()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (uiSuppressed)
            {
                return;
            }

            UpdatePickupDetection();

            if (loadoutUiOpen)
            {
                return;
            }

            if (Keyboard.current == null || slotItems == null)
            {
                return;
            }

            bool ctrlHeld = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (ctrlHeld)
            {
                return;
            }

            HandleMouseWheelSelectionInput();

            Key[] numberKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            int maxKey = Mathf.Min(numberKeys.Length, slotItems.Length);
            for (int i = 0; i < maxKey; i++)
            {
                if (Keyboard.current[numberKeys[i]].wasPressedThisFrame)
                {
                    HandleSlotSelectionInput(i);
                    break;
                }
            }

        }
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                PushInventoryState();
            }
        }

        public void ClearInventoryForLobby()
        {
            if (slotItems == null)
            {
                return;
            }

            int previousSlot = selectedSlot;
            selectedSlot = 0;

            bool networkActive = NetworkManager != null && NetworkManager.IsListening;
            bool canDespawn = !networkActive || IsServer;

            for (int i = 0; i < slotItems.Length; i++)
            {
                GameObject item = slotItems[i];
                if (item != null)
                {
                    if (canDespawn && IsNetworkedItem(item))
                    {
                        NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                        {
                            netObj.Despawn(true);
                        }
                    }
                    else
                    {
                        Destroy(item);
                    }
                }

                if (IsServer)
                {
                    ClearSlotServer(i);
                }
                else
                {
                    slotItems[i] = null;
                    slotItemIds[i] = 0;
                }
            }

            CleanupLocalViewProxy();
            RefreshSlotVisibility(previousSlot);

            if (IsServer)
            {
                PushInventoryState();
            }
        }

        public bool HasSelectedLoadoutForCurrentDay()
        {
            int day = GetCurrentDaySafe();
            return day > 0 && lastLoadoutSelectionDay == day;
        }

        public void RequestOpenLoadoutUi(LoadoutStation station, bool forceSelection)
        {
            if (!IsLocallyControlled() || !enableLoadoutUi)
            {
                return;
            }

            if (station == null)
            {
                return;
            }

            if (HasSelectedLoadoutForCurrentDay())
            {
                return;
            }

            OpenLoadoutUi(station);
            forceLoadoutSelection = forceSelection;
        }

        public void RequestCloseLoadoutUi()
        {
            if (!loadoutUiOpen)
            {
                return;
            }

            forceLoadoutSelection = false;
            CloseLoadoutUi();
        }

        private int GetCurrentDaySafe()
        {
            GameManager manager = GameManager.Instance;
            if (manager != null)
            {
                return Mathf.Max(1, manager.currentDay);
            }

            return 1;
        }

        private void PushInventoryState()
        {
            if (!IsServer || slotItemIds == null)
            {
                return;
            }

            ulong[] snapshot = new ulong[slotItemIds.Length];
            System.Array.Copy(slotItemIds, snapshot, slotItemIds.Length);
            SyncInventoryClientRpc(snapshot, selectedSlot);
        }

        [ClientRpc]
        private void SyncInventoryClientRpc(ulong[] ids, int selected)
        {
            ApplyInventoryStateLocally(ids, selected);
        }

        private void ApplyInventoryStateLocally(ulong[] ids, int selected)
        {
            if (ids == null)
            {
                return;
            }

            if (slotItems == null || slotItems.Length != ids.Length)
            {
                slotItems = new GameObject[ids.Length];
            }

            if (slotItemIds == null || slotItemIds.Length != ids.Length)
            {
                slotItemIds = new ulong[ids.Length];
            }

            int previousSlot = selectedSlot;

            for (int i = 0; i < ids.Length; i++)
            {
                slotItemIds[i] = ids[i];
                slotItems[i] = ResolveNetworkObject(ids[i]);
            }

            selectedSlot = Mathf.Clamp(selected, 0, Mathf.Max(0, ids.Length - 1));

            CleanupLocalViewProxy();
            RefreshSlotVisibility(previousSlot);

            if (useSeparateViewAnchors && localViewAnchor != null)
            {
                localViewSource = null;
                UpdateLocalViewProxy();
            }
        }

        private GameObject ResolveNetworkObject(ulong networkObjectId)
        {
            if (networkObjectId == 0 || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }

            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                return netObj != null ? netObj.gameObject : null;
            }

            return null;
        }

        private void SetSlotServer(int slotIndex, GameObject item)
        {
            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                return;
            }

            slotItems[slotIndex] = item;

            if (item == null)
            {
                slotItemIds[slotIndex] = 0;
                return;
            }

            NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
            slotItemIds[slotIndex] = netObj != null ? netObj.NetworkObjectId : 0;
        }

        private void ClearSlotServer(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                return;
            }

            slotItems[slotIndex] = null;
            slotItemIds[slotIndex] = 0;
        }

        private void HandleMouseWheelSelectionInput()
        {
            if (!enableMouseWheelSelection || Mouse.current == null || slotItems == null || slotItems.Length <= 1)
            {
                return;
            }

            if (Time.unscaledTime < nextMouseWheelSwitchTime)
            {
                return;
            }

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f)
            {
                return;
            }

            int dir = scrollY > 0f ? -1 : 1;
            if (invertMouseWheel)
            {
                dir *= -1;
            }

            int targetSlot = WrapSlotIndex(selectedSlot + dir);
            if (targetSlot != selectedSlot)
            {
                HandleSlotSelectionInput(targetSlot);
            }

            nextMouseWheelSwitchTime = Time.unscaledTime + Mathf.Max(0.01f, mouseWheelSwitchCooldown);
        }


        private int WrapSlotIndex(int index)
        {
            int count = slotItems != null ? slotItems.Length : 0;
            if (count <= 0)
            {
                return 0;
            }

            int wrapped = index % count;
            if (wrapped < 0)
            {
                wrapped += count;
            }

            return wrapped;
        }

        private void LateUpdate()
        {
            bool isLocal = IsLocallyControlled();
            bool isServer = IsServer;
            if (slotItems == null)
            {
                return;
            }

            if (selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return;
            }

            GameObject selectedItem = slotItems[selectedSlot];
            if (selectedItem == null || !selectedItem.activeInHierarchy)
            {
                if (isLocal)
                {
                    CleanupLocalViewProxy();
                }
                return;
            }

            bool isNetworkedItem = IsNetworkedItem(selectedItem);
            Transform anchor = isNetworkedItem ? GetWorldAnchorForNetworkedItem() : GetSlotAnchor(selectedSlot);

            if (!isNetworkedItem && isLocal && selectedItem.transform.parent != anchor)
            {
                selectedItem.transform.SetParent(anchor, true);
            }

            bool useLocalView = !isNetworkedItem && isLocal;
            Vector3 localPos = GetTargetLocalPosition(selectedItem, useLocalView);
            Quaternion localRot = GetTargetLocalRotation(selectedItem, useLocalView);

            if (!isNetworkedItem)
            {
                if (isLocal)
                {
                    selectedItem.transform.localPosition = localPos;
                    selectedItem.transform.localRotation = localRot;
                    ApplyTargetLocalScale(selectedItem);
                }
            }
            else if (isServer && anchor != null)
            {
                selectedItem.transform.position = anchor.TransformPoint(localPos);
                selectedItem.transform.rotation = anchor.rotation * localRot;
                ApplyTargetLocalScale(selectedItem);
            }

            UpdateLocalViewProxy();

            ApplyRemoteVisualFollow();
        }

        private void OnDisable()
        {
            UpdateHoverHighlight(null);
            CleanupLocalViewProxy();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (crosshairPixel != null)
            {
                Destroy(crosshairPixel);
            }
            CleanupLocalViewProxy();
        }

        public bool TryStoreItem(GameObject itemPrefab, out GameObject storedItem)
        {
            storedItem = null;
            if (itemPrefab == null)
            {
                return false;
            }

            int targetSlot = FindFirstFreeDataSlot();
            if (targetSlot < 0)
            {
                return false;
            }

            GameObject instance = Instantiate(itemPrefab);
            bool hadNetworkComponents = StripNetworkComponents(instance);
            CacheOriginalScale(instance);
            slotItems[targetSlot] = instance;
            storedItem = instance;

            int previousSlot = selectedSlot;
            selectedSlot = targetSlot;
            RefreshSlotVisibility(previousSlot);
            if (hadNetworkComponents)
            {
                StartCoroutine(AttachItemToSlotDeferred(instance, targetSlot));
            }
            else
            {
                AttachItemToSlot(instance, targetSlot, true);
            }
            return true;
        }

        private void PlayPickupSound()
        {
            if (pickupAudioSource == null || pickupClips == null || pickupClips.Length == 0)
            {
                return;
            }

            int index = Random.Range(0, pickupClips.Length);
            AudioClip clip = pickupClips[index];
            if (clip == null)
            {
                return;
            }

            pickupAudioSource.PlayOneShot(clip, Mathf.Clamp01(pickupVolume));
        }

        public bool TryPickupWorldItem(GameObject worldItem, out GameObject storedItem)
        {
            storedItem = null;
            if (worldItem == null)
            {
                return false;
            }

            NetworkObject networkObject = worldItem.GetComponentInParent<NetworkObject>();
            bool isNetworked = networkObject != null && NetworkManager != null && NetworkManager.IsListening;
            if (isNetworked && !IsServer)
            {
                return false;
            }

            int targetSlot = FindFirstFreeDataSlot();
            if (targetSlot < 0)
            {
                return false;
            }

            if (IsAlreadyInInventory(worldItem))
            {
                return false;
            }

            CacheOriginalScale(worldItem);
            PrepareWorldItemForInventory(worldItem);

            SetSlotServer(targetSlot, worldItem);
            storedItem = worldItem;

            int previousSlot = selectedSlot;
            selectedSlot = targetSlot;

            AttachItemToSlot(worldItem, targetSlot, false);
            RefreshSlotVisibility(previousSlot);

            PlayPickupSound();
            return true;
        }
        public bool TryThrowSelectedItem(Camera throwCamera)
        {
            if (throwCamera == null)
            {
                return false;
            }

            if (Time.unscaledTime < nextThrowTime)
            {
                return false;
            }

            Transform throwOrigin = throwCamera.transform;
            Vector3 spawnPosition = throwOrigin.position + throwOrigin.forward * throwSpawnForward + Vector3.up * throwSpawnUp;
            Vector3 forward = throwOrigin.forward;

            bool didThrow = TryThrowSelectedItemWithDirection(spawnPosition, forward);
            if (didThrow)
            {
                nextThrowTime = Time.unscaledTime + Mathf.Max(0.01f, throwCooldownSeconds);
            }

            return didThrow;
        }

        public bool TryThrowSelectedItemNetworked(Camera throwCamera)
        {
            if (throwCamera == null)
            {
                return false;
            }

            if (Time.unscaledTime < nextThrowTime)
            {
                return false;
            }

            if (IsSpawned && NetworkManager != null)
            {
                int slotToThrow = selectedSlot;
                bool canThrow = slotToThrow >= 0
                    && slotToThrow < slotItems.Length
                    && slotItems[slotToThrow] != null;

                if (!canThrow)
                {
                    return false;
                }

                Transform throwOrigin = throwCamera.transform;
                Vector3 spawnPosition = throwOrigin.position + throwOrigin.forward * throwSpawnForward + Vector3.up * throwSpawnUp;
                Vector3 forward = throwOrigin.forward;

                RequestThrowServerRpc(slotToThrow, spawnPosition, forward);
                nextThrowTime = Time.unscaledTime + Mathf.Max(0.01f, throwCooldownSeconds);
                return true;
            }

            Transform throwOriginLocal = throwCamera.transform;
            Vector3 spawnPositionLocal = throwOriginLocal.position + throwOriginLocal.forward * throwSpawnForward + Vector3.up * throwSpawnUp;
            Vector3 forwardLocal = throwOriginLocal.forward;

            return TryThrowSelectedItemWithDirection(spawnPositionLocal, forwardLocal);
        }

        public bool TryDropSelectedItemNetworked(Transform dropOrigin)
        {
            if (Time.unscaledTime < nextThrowTime)
            {
                return false;
            }

            int slotToDrop = selectedSlot;
            bool canDrop = slotToDrop >= 0
                && slotToDrop < slotItems.Length
                && slotItems[slotToDrop] != null;

            if (!canDrop)
            {
                return false;
            }

            if (dropOrigin == null)
            {
                dropOrigin = transform;
            }

            Vector3 spawnPosition = ResolveDropSpawnPosition(dropOrigin);
            Vector3 forward = dropOrigin.forward;
            if (IsSpawned && NetworkManager != null)
            {
                RequestThrowServerRpc(slotToDrop, spawnPosition, forward);
                nextThrowTime = Time.unscaledTime + Mathf.Max(0.01f, throwCooldownSeconds);
                return true;
            }

            bool didDrop = TryThrowSelectedItemWithDirection(spawnPosition, forward);
            if (didDrop)
            {
                nextThrowTime = Time.unscaledTime + Mathf.Max(0.01f, throwCooldownSeconds);
            }
            return didDrop;
        }

        public bool TryConsumeSelectedItemNetworked()
        {
            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length || slotItems[selectedSlot] == null)
            {
                return false;
            }

            if (IsSpawned && NetworkManager != null)
            {
                RequestConsumeSelectedServerRpc(selectedSlot);
                return true;
            }

            return TryConsumeSlot(selectedSlot);
        }

        public bool TryConsumeSelectedItemServer()
        {
            if (!IsServer)
            {
                return false;
            }

            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length || slotItems[selectedSlot] == null)
            {
                return false;
            }

            GameObject item = slotItems[selectedSlot];
            if (TryConsumeSlot(selectedSlot))
            {
                if (item != null && IsNetworkedItem(item))
                {
                    NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
                    if (netObj != null && netObj.IsSpawned)
                    {
                        netObj.Despawn(true);
                    }
                }

                PushInventoryState();
                return true;
            }

            return false;
        }

        private Vector3 ResolveDropSpawnPosition(Transform dropOrigin)
        {
            Vector3 originPos = dropOrigin.position;
            float startHeight = Mathf.Max(0f, dropRayStartHeight);
            Vector3 rayStart = originPos + Vector3.up * startHeight;
            float maxDistance = Mathf.Max(0.05f, dropRayDistance + startHeight);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * dropGroundOffset;
            }

            return originPos + Vector3.down * Mathf.Max(0f, dropFallbackDown);
        }

        private bool TryThrowSelectedItemWithDirection(Vector3 spawnPosition, Vector3 forward)
        {
            int slotToThrow = selectedSlot;
            if (slotToThrow < 0 || slotToThrow >= slotItems.Length)
            {
                return false;
            }

            if (slotItems[slotToThrow] == null)
            {
                return false;
            }

            GameObject item = slotItems[slotToThrow];
            slotItems[slotToThrow] = null;
            CleanupLocalViewProxy();

            item.SetActive(true);
            SetItemRenderersEnabled(item, true);
            SetItemCollidersEnabled(item, true);

            if (!IsServer)
            {
                RefreshSlotVisibility(selectedSlot);
                return true;
            }

            item.transform.SetParent(null, true);

            if (restoreOriginalScaleOnThrow)
            {
                RestoreOriginalScale(item);
            }

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = item.AddComponent<Rigidbody>();
            }

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            item.transform.SetPositionAndRotation(spawnPosition, Quaternion.LookRotation(safeForward, Vector3.up));

            RefreshSlotVisibility(selectedSlot);
            return true;
        }

        [ServerRpc]
        private void RequestThrowServerRpc(int slotToThrow, Vector3 spawnPosition, Vector3 forward)
        {
            if (debugThrowFlow)
            {
                Debug.Log($"InventorySystem: server received throw request slot {slotToThrow} from {name}.");
            }

            ulong droppedNetworkId = 0;
            GameObject item = null;
            if (slotToThrow >= 0 && slotToThrow < slotItems.Length)
            {
                item = slotItems[slotToThrow];
                if (item != null)
                {
                    NetworkObject netObj = item.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        droppedNetworkId = netObj.NetworkObjectId;
                    }
                }
            }

            bool didThrow = TryThrowSlotServer(slotToThrow, spawnPosition, forward);
            if (didThrow)
            {
                ClearSlotServer(slotToThrow);
                PushInventoryState();
                if (droppedNetworkId != 0)
                {
                    ApplyDropClientRpc(droppedNetworkId);
                }
            }
        }

        [ClientRpc]
        private void ApplyDropClientRpc(ulong droppedNetworkId)
        {
            if (NetworkManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droppedNetworkId, out NetworkObject netObj))
            {
                return;
            }

            GameObject item = netObj.gameObject;
            item.SetActive(true);
            SetItemRenderersEnabled(item, true);
            SetItemCollidersEnabled(item, true);
        }


        private bool TryThrowSlotServer(int slotToThrow, Vector3 spawnPosition, Vector3 forward)
        {
            if (slotToThrow < 0 || slotToThrow >= slotItems.Length)
            {
                return false;
            }

            if (slotItems[slotToThrow] == null)
            {
                if (debugThrowFlow)
                {
                    Debug.LogWarning($"InventorySystem: client throw failed, slot {slotToThrow} empty on {name}.");
                }
                return false;
            }

            int previousSelectedSlot = selectedSlot;
            selectedSlot = slotToThrow;
            bool didThrow = TryThrowSelectedItemWithDirection(spawnPosition, forward);
            if (didThrow)
            {
                selectedSlot = Mathf.Clamp(previousSelectedSlot, 0, slotItems.Length - 1);
            }
            else
            {
                selectedSlot = previousSelectedSlot;
            }
            return didThrow;
        }


        private bool TryConsumeSlot(int slotToConsume)
        {
            if (slotToConsume < 0 || slotToConsume >= slotItems.Length)
            {
                return false;
            }

            if (slotItems[slotToConsume] == null)
            {
                return false;
            }

            GameObject item = slotItems[slotToConsume];

            if (IsServer)
            {
                ClearSlotServer(slotToConsume);
            }
            else
            {
                slotItems[slotToConsume] = null;
            }

            item.SetActive(false);

            if (!IsNetworkedItem(item) || IsServer)
            {
                item.transform.SetParent(null, true);
            }

            SetItemCollidersEnabled(item, false);
            SetItemRenderersEnabled(item, false);

            RefreshSlotVisibility(selectedSlot);
            return true;
        }

        [ServerRpc]
        private void RequestConsumeSelectedServerRpc(int slotToConsume)
        {
            if (TryConsumeSlot(slotToConsume))
            {
                PushInventoryState();
            }
        }

        public bool TrySelectSlot(int slotIndex)
        {
            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return false;
            }

            int previousSlot = selectedSlot;
            selectedSlot = slotIndex;
            RefreshSlotVisibility(previousSlot);
            return true;
        }

        public void SetUiSuppressed(bool suppressed)
        {
            uiSuppressed = suppressed;
        }

        private void HandleSlotSelectionInput(int slotIndex)
        {
            bool changed = TrySelectSlot(slotIndex);
            if (!changed)
            {
                return;
            }

            if (IsSpawned && NetworkManager != null)
            {
                SubmitSelectedSlotServerRpc(slotIndex);
            }
        }

        [ServerRpc]
        private void SubmitSelectedSlotServerRpc(int slotIndex)
        {
            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return;
            }

            selectedSlot = slotIndex;
            PushInventoryState();
        }

        public GameObject GetSelectedItem()
        {
            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return null;
            }

            return slotItems[selectedSlot];
        }

        public int GetSelectedSlotIndex()
        {
            return selectedSlot;
        }

        public bool ReplaceSelectedItemWithPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            if (!IsLocallyControlled())
            {
                return false;
            }

            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return false;
            }

            GameObject current = slotItems[selectedSlot];
            if (current == null)
            {
                return false;
            }

            bool isNetworked = IsNetworkedItem(current);
            if (isNetworked)
            {
                NetworkObject netObj = current.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned && IsServer)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    current.SetActive(false);
                }
            }
            else
            {
                Destroy(current);
            }

            GameObject instance = Instantiate(prefab);
            bool hadNetworkComponents = StripNetworkComponents(instance);
            CacheOriginalScale(instance);
            slotItems[selectedSlot] = instance;
            slotItemIds[selectedSlot] = 0;

            CleanupLocalViewProxy();
            RefreshSlotVisibility(-1);

            if (useSeparateViewAnchors && localViewAnchor != null)
            {
                localViewSource = null;
                UpdateLocalViewProxy();
            }

            if (hadNetworkComponents)
            {
                StartCoroutine(AttachItemToSlotDeferred(instance, selectedSlot));
            }
            else
            {
                AttachItemToSlot(instance, selectedSlot, true);
            }

            return true;
        }

        public bool ReplaceSlotItemWithPrefabLocal(int slotIndex, GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            if (!IsLocallyControlled())
            {
                return false;
            }

            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return false;
            }

            GameObject current = slotItems[slotIndex];
            if (current == null)
            {
                return false;
            }

            bool isNetworked = IsNetworkedItem(current);
            if (isNetworked)
            {
                NetworkObject netObj = current.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned && IsServer)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    current.SetActive(false);
                }
            }
            else
            {
                Destroy(current);
            }

            GameObject instance = Instantiate(prefab);
            bool hadNetworkComponents = StripNetworkComponents(instance);
            CacheOriginalScale(instance);
            slotItems[slotIndex] = instance;
            slotItemIds[slotIndex] = 0;

            CleanupLocalViewProxy();
            RefreshSlotVisibility(-1);

            if (useSeparateViewAnchors && localViewAnchor != null)
            {
                localViewSource = null;
                UpdateLocalViewProxy();
            }

            if (hadNetworkComponents)
            {
                StartCoroutine(AttachItemToSlotDeferred(instance, slotIndex));
            }
            else
            {
                AttachItemToSlot(instance, slotIndex, true);
            }

            return true;
        }

        public bool ServerGrantNetworkedItem(GameObject prefab, int count)
        {
            if (!IsServer || prefab == null || count <= 0)
            {
                return false;
            }

            int added = 0;
            for (int i = 0; i < count; i++)
            {
                int targetSlot = FindFirstFreeDataSlot();
                if (targetSlot < 0)
                {
                    break;
                }

                if (!TrySpawnNetworkedItem(prefab, out GameObject instance))
                {
                    break;
                }

                PrepareWorldItemForInventory(instance);
                SetSlotServer(targetSlot, instance);
                AttachItemToSlot(instance, targetSlot, true);
                added++;
            }

            if (added > 0)
            {
                PushInventoryState();
            }

            return added > 0;
        }

        public bool ServerReplaceSelectedItemWithPrefab(GameObject prefab)
        {
            if (!IsServer || prefab == null)
            {
                return false;
            }

            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return false;
            }

            GameObject current = slotItems[selectedSlot];
            if (current == null)
            {
                return false;
            }

            if (IsNetworkedItem(current))
            {
                NetworkObject netObj = current.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
            else
            {
                Destroy(current);
            }

            ClearSlotServer(selectedSlot);

            if (!TrySpawnNetworkedItem(prefab, out GameObject instance))
            {
                return false;
            }

            PrepareWorldItemForInventory(instance);
            SetSlotServer(selectedSlot, instance);
            AttachItemToSlot(instance, selectedSlot, true);

            PushInventoryState();
            return true;
        }

        public bool ServerReplaceSlotItemWithPrefab(int slotIndex, GameObject prefab)
        {
            if (!IsServer || prefab == null)
            {
                return false;
            }

            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return false;
            }

            GameObject current = slotItems[slotIndex];
            if (current == null)
            {
                return false;
            }

            if (IsNetworkedItem(current))
            {
                NetworkObject netObj = current.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
            else
            {
                Destroy(current);
            }

            ClearSlotServer(slotIndex);

            if (!TrySpawnNetworkedItem(prefab, out GameObject instance))
            {
                return false;
            }

            PrepareWorldItemForInventory(instance);
            SetSlotServer(slotIndex, instance);
            AttachItemToSlot(instance, slotIndex, true);

            PushInventoryState();
            return true;
        }

        public bool TryFindLuckyCharmSlot(out int slotIndex, out GameObject bonusPrefab)
        {
            bonusPrefab = null;
            slotIndex = -1;

            if (slotItems == null)
            {
                return false;
            }

            for (int i = 0; i < slotItems.Length; i++)
            {
                GameObject item = slotItems[i];
                if (item == null)
                {
                    continue;
                }

                LuckyCharmItem charm = item.GetComponentInChildren<LuckyCharmItem>(true);
                if (charm == null || charm.bonusPrefab == null)
                {
                    continue;
                }

                slotIndex = i;
                bonusPrefab = charm.bonusPrefab;
                return true;
            }

            return false;
        }

        private bool TrySpawnNetworkedItem(GameObject prefab, out GameObject instance)
        {
            instance = Instantiate(prefab);
            NetworkObject netObj = instance.GetComponentInParent<NetworkObject>();
            if (netObj == null)
            {
                Destroy(instance);
                instance = null;
                return false;
            }

            if (!netObj.IsSpawned)
            {
                try
                {
                    netObj.SpawnWithOwnership(OwnerClientId, true);
                }
                catch
                {
                    Destroy(instance);
                    instance = null;
                    return false;
                }
            }

            return true;
        }

        public bool TryConsumeSelectedItem()
        {
            if (!IsLocallyControlled())
            {
                return false;
            }

            if (slotItems == null || selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return false;
            }

            if (slotItems[selectedSlot] == null)
            {
                return false;
            }

            if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
            {
                if (!IsOwner)
                {
                    return false;
                }

                RequestConsumeSelectedItemServerRpc(selectedSlot);
                return true;
            }

            return ConsumeSelectedItemLocal(selectedSlot);
        }

        [ServerRpc]
        private void RequestConsumeSelectedItemServerRpc(int slotIndex, ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return;
            }

            GameObject item = slotItems[slotIndex];
            if (item == null)
            {
                return;
            }

            bool isNetworked = IsNetworkedItem(item);
            if (isNetworked)
            {
                NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
            else
            {
                Destroy(item);
            }

            ClearSlotServer(slotIndex);
            CleanupLocalViewProxy();
            RefreshSlotVisibility(-1);
            PushInventoryState();
        }

        private bool ConsumeSelectedItemLocal(int slotIndex)
        {
            if (slotItems == null || slotIndex < 0 || slotIndex >= slotItems.Length)
            {
                return false;
            }

            GameObject item = slotItems[slotIndex];
            if (item == null)
            {
                return false;
            }

            bool isNetworked = IsNetworkedItem(item);
            if (isNetworked)
            {
                NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned && IsServer)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    item.SetActive(false);
                }
            }
            else
            {
                Destroy(item);
            }

            slotItems[slotIndex] = null;
            slotItemIds[slotIndex] = 0;
            CleanupLocalViewProxy();
            RefreshSlotVisibility(-1);
            return true;
        }

        public bool IsSelectedItemNamed(string requiredName)
        {
            if (string.IsNullOrWhiteSpace(requiredName))
            {
                return true;
            }

            GameObject selected = GetSelectedItem();
            if (selected == null)
            {
                return false;
            }

            string cleanSelected = CleanItemName(selected.name);
            string cleanRequired = CleanItemName(requiredName);
            return string.Equals(cleanSelected, cleanRequired, System.StringComparison.OrdinalIgnoreCase);
        }

        public bool IsSelectedItemMatchingPrefab(GameObject requiredPrefab)
        {
            if (requiredPrefab == null)
            {
                return false;
            }

            GameObject selected = GetSelectedItem();
            if (selected == null)
            {
                return false;
            }

            if (ReferenceEquals(selected, requiredPrefab))
            {
                return true;
            }

            string cleanSelected = CleanItemName(selected.name);
            string cleanRequired = CleanItemName(requiredPrefab.name);
            return string.Equals(cleanSelected, cleanRequired, System.StringComparison.OrdinalIgnoreCase);
        }

        private int FindFirstFreeDataSlot()
        {
            for (int i = 0; i < slotItems.Length; i++)
            {
                if (slotItems[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsAlreadyInInventory(GameObject item)
        {
            for (int i = 0; i < slotItems.Length; i++)
            {
                if (slotItems[i] == item)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetSlotIndexForItem(GameObject item)
        {
            if (item == null || slotItems == null)
            {
                return -1;
            }

            for (int i = 0; i < slotItems.Length; i++)
            {
                if (slotItems[i] == item)
                {
                    return i;
                }
            }

            return -1;
        }


        private void AttachItemToSlot(GameObject item, int slotIndex, bool disableColliders)
        {
            Transform anchor = GetSlotAnchor(slotIndex);
            item.SetActive(true);
            bool isNetworkedItem = IsNetworkedItem(item);

            Vector3 localPos = GetTargetLocalPosition(item);
            Quaternion localRot = GetTargetLocalRotation(item);

            if (!isNetworkedItem)
            {
                if (anchor != null)
                {
                    item.transform.SetParent(anchor, true);
                    item.transform.localPosition = localPos;
                    item.transform.localRotation = localRot;
                }
            }
            else
            {
                if (IsServer && anchor != null)
                {
                    Transform worldAnchor = GetWorldAnchorForNetworkedItem();
                    if (worldAnchor != null)
                    {
                        item.transform.SetPositionAndRotation(
                            worldAnchor.TransformPoint(localPos),
                            worldAnchor.rotation * localRot);
                    }
                }
            }
            ApplyTargetLocalScale(item);
            SetItemRenderersEnabled(item, true);

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (disableColliders)
            {
                SetItemCollidersEnabled(item, false);
            }
        }

        private void PrepareWorldItemForInventory(GameObject item)
        {
            if (!IsNetworkedItem(item) || IsServer)
            {
                item.transform.SetParent(null, true);
            }

            item.SetActive(true);
            SetItemCollidersEnabled(item, false);
            SetItemRenderersEnabled(item, true);

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void PrepareWorldItemForRemoteHold(GameObject item)
        {
            if (item == null)
            {
                return;
            }

            item.SetActive(true);
            SetItemCollidersEnabled(item, false);
            SetItemRenderersEnabled(item, true);

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }


        // private bool TryPickupWorldItemClientProxy(GameObject worldItem, out GameObject storedItem)
        // {
        //     storedItem = null;

        //     int targetSlot = FindFirstFreeDataSlot();
        //     if (targetSlot < 0)
        //     {
        //         return false;
        //     }

        //     if (IsAlreadyInInventory(worldItem))
        //     {
        //         return false;
        //     }

        //     PrepareWorldItemForRemoteHold(worldItem);
        //     slotItems[targetSlot] = worldItem;
        //     storedItem = worldItem;
        //     int previousSlot = selectedSlot;
        //     selectedSlot = targetSlot;

        //     if (useSeparateViewAnchors && IsLocallyControlled() && localViewAnchor != null && IsNetworkedItem(worldItem))
        //     {
        //         SetItemRenderersEnabled(worldItem, false);
        //     }

        //     RefreshSlotVisibility(previousSlot);

        //     return true;
        // }

        private bool IsNetworkedItem(GameObject item)
        {
            if (item == null)
            {
                return false;
            }

            if (NetworkManager == null || !NetworkManager.IsListening)
            {
                return false;
            }

            NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned;
        }

        void UpdateLocalViewProxy()
        {
            if (!IsOwner)
            {
                CleanupLocalViewProxy();
                return;
            }
            if (!useSeparateViewAnchors) return;
            if (localViewAnchor == null) return;

            var selected = GetSelectedItem();
            if (selected == null)
            {
                CleanupLocalViewProxy();
                return;
            }

            if (localViewProxy == null || localViewSource != selected)
            {
                CleanupLocalViewProxy();

                var mesh = selected.GetComponentInChildren<MeshFilter>();
                var renderer = selected.GetComponentInChildren<MeshRenderer>();

                if (mesh == null || renderer == null) return;

                localViewProxy = new GameObject(selected.name + "_LocalViewProxy");
                localViewSource = selected;

                localViewProxy.transform.SetParent(localViewAnchor, false);

                var mf = localViewProxy.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh.sharedMesh;

                var mr = localViewProxy.AddComponent<MeshRenderer>();
                mr.sharedMaterials = renderer.sharedMaterials;
            }

            var holdPose = selected.GetComponent<ItemHoldPose>();
            if (holdPose != null)
            {
                localViewProxy.transform.localPosition = holdPose.localPosition;
                localViewProxy.transform.localRotation = Quaternion.Euler(holdPose.localEulerAngles);

                if (holdPose.useCustomHoldScale)
                    localViewProxy.transform.localScale = holdPose.customHoldScale;
                else
                    localViewProxy.transform.localScale = Vector3.one;
            }
            else
            {
                localViewProxy.transform.localPosition = Vector3.zero;
                localViewProxy.transform.localRotation = Quaternion.identity;
                localViewProxy.transform.localScale = Vector3.one;
            }
        }
        private void CleanupLocalViewProxy()
        {
            if (localViewSource != null)
            {
                SetItemRenderersEnabled(localViewSource, true);
            }

            if (localViewProxy != null)
            {
                Destroy(localViewProxy);
            }

            localViewProxy = null;
            localViewSource = null;
        }

        private bool StripNetworkComponents(GameObject item)
        {
            if (item == null)
            {
                return false;
            }

            bool hadAny = false;

            NetworkRigidbody[] netBodies = item.GetComponentsInChildren<NetworkRigidbody>(true);
            for (int i = 0; i < netBodies.Length; i++)
            {
                hadAny = true;
                Destroy(netBodies[i]);
            }

            NetworkTransform[] netTransforms = item.GetComponentsInChildren<NetworkTransform>(true);
            for (int i = 0; i < netTransforms.Length; i++)
            {
                hadAny = true;
                Destroy(netTransforms[i]);
            }

            NetworkObject[] netObjects = item.GetComponentsInChildren<NetworkObject>(true);
            for (int i = 0; i < netObjects.Length; i++)
            {
                hadAny = true;
                Destroy(netObjects[i]);
            }

            NetworkPlayerOwnership[] ownerships = item.GetComponentsInChildren<NetworkPlayerOwnership>(true);
            for (int i = 0; i < ownerships.Length; i++)
            {
                hadAny = true;
                Destroy(ownerships[i]);
            }

            NetworkAnimator[] netAnimators = item.GetComponentsInChildren<NetworkAnimator>(true);
            for (int i = 0; i < netAnimators.Length; i++)
            {
                hadAny = true;
                Destroy(netAnimators[i]);
            }

            Rigidbody[] bodies = item.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }

            return hadAny;
        }

        private IEnumerator AttachItemToSlotDeferred(GameObject item, int slotIndex)
        {
            yield return null;
            if (item == null)
            {
                yield break;
            }

            AttachItemToSlot(item, slotIndex, true);
        }

        private Transform GetSlotAnchor(int slotIndex)
        {
            if (slotAnchors != null && slotIndex >= 0 && slotIndex < slotAnchors.Length && slotAnchors[slotIndex] != null)
            {
                return slotAnchors[slotIndex];
            }

            if (useSeparateViewAnchors)
            {
                bool isNetworkedSelected = IsNetworkedItem(GetSelectedItem());
                if (IsLocallyControlled() && localViewAnchor != null && !isNetworkedSelected)
                {
                    return localViewAnchor;
                }

                if (worldViewAnchor != null)
                {
                    return worldViewAnchor;
                }

                if (autoResolveWorldAnchorFromHandBone)
                {
                    Transform resolved = ResolveWorldAnchorFromHandBone();
                    if (resolved != null)
                    {
                        worldViewAnchor = resolved;
                        return worldViewAnchor;
                    }
                }
            }

            if (defaultItemAnchor != null)
            {
                return defaultItemAnchor;
            }

            return transform;
        }

        private Transform GetWorldAnchorForNetworkedItem()
        {
            if (worldViewAnchor != null)
            {
                return worldViewAnchor;
            }

            if (autoResolveWorldAnchorFromHandBone)
            {
                Transform resolved = ResolveWorldAnchorFromHandBone();
                if (resolved != null)
                {
                    worldViewAnchor = resolved;
                    return worldViewAnchor;
                }
            }

            if (defaultItemAnchor != null)
            {
                return defaultItemAnchor;
            }

            return transform;
        }

        private Transform ResolveWorldAnchorFromHandBone()
        {
            Transform handBone = worldHandTransform;
            Animator[] animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length && handBone == null; i++)
            {
                Animator animator = animators[i];
                if (animator == null || !animator.isHuman)
                {
                    continue;
                }

                Transform bone = animator.GetBoneTransform(worldHandBone);
                if (bone != null)
                {
                    handBone = bone;
                    break;
                }
            }

            if (handBone == null && !string.IsNullOrWhiteSpace(worldHandBoneNameFallback))
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name == worldHandBoneNameFallback)
                    {
                        handBone = all[i];
                        break;
                    }
                }
            }

            if (handBone == null)
            {
                return null;
            }

            if (!createWorldHandHolderIfMissing)
            {
                return handBone;
            }

            string holderName = string.IsNullOrWhiteSpace(worldHandHolderName) ? "WorldItemHolder" : worldHandHolderName;
            Transform existing = handBone.Find(holderName);
            if (existing != null)
            {
                return existing;
            }

            GameObject holder = new GameObject(holderName);
            holder.transform.SetParent(handBone, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;
            return holder.transform;
        }

        private void RefreshSlotVisibility(int previousSlot)
        {
            if (slotItems == null)
            {
                return;
            }

            for (int i = 0; i < slotItems.Length; i++)
            {
                GameObject item = slotItems[i];
                if (item == null)
                {
                    continue;
                }

                bool shouldBeVisible = i == selectedSlot;
                bool isNetworkedItem = IsNetworkedItem(item);

                if (isNetworkedItem)
                {
                    item.SetActive(true);
                    SetItemCollidersEnabled(item, false);

                    bool showWorldItem = !IsOwner && shouldBeVisible;
                    SetItemRenderersEnabled(item, showWorldItem);

                    if (shouldBeVisible && IsServer)
                    {
                        ApplyOrAnimateItemPose(item, i, previousSlot);
                    }
                }
                else
                {
                    if (shouldBeVisible)
                    {
                        item.SetActive(true);
                        ApplyOrAnimateItemPose(item, i, previousSlot);
                    }
                    else
                    {
                        item.SetActive(false);
                    }
                }
            }
        }

        private void ApplyOrAnimateItemPose(GameObject item, int slotIndex, int previousSlot)
        {
            if (item == null)
            {
                return;
            }

            Vector3 targetPosition = GetTargetLocalPosition(item);
            Quaternion targetRotation = GetTargetLocalRotation(item);
            ApplyTargetLocalScale(item);
            item.transform.localPosition = targetPosition;
            item.transform.localRotation = targetRotation;
        }

        private Vector3 GetTargetLocalPosition(GameObject item)
        {
            return GetTargetLocalPosition(item, IsUsingLocalViewForItem(item));
        }

        private Vector3 GetTargetLocalPosition(GameObject item, bool useLocalView)
        {
            if (IsNetworkedItem(item) && !IsServer)
            {
                return Vector3.zero;
            }

            Vector3 position = Vector3.zero;
            if (TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (pose.useThirdPersonPose && !useLocalView)
                {
                    position = pose.thirdPersonLocalPosition;
                }
                else
                {
                    position = pose.localPosition;
                }
            }
            if (useLocalView && ShouldApplyLocalViewOffset())
            {
                position += localViewPositionOffset;
            }

            return position;
        }

        private Quaternion GetTargetLocalRotation(GameObject item)
        {
            return GetTargetLocalRotation(item, IsUsingLocalViewForItem(item));
        }

        private Quaternion GetTargetLocalRotation(GameObject item, bool useLocalView)
        {
            if (IsNetworkedItem(item) && !IsServer)
            {
                return Quaternion.identity;
            }

            Quaternion rotation = Quaternion.identity;
            if (TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (pose.useThirdPersonPose && !useLocalView)
                {
                    rotation = Quaternion.Euler(pose.thirdPersonLocalEulerAngles);
                }
                else
                {
                    rotation = Quaternion.Euler(pose.localEulerAngles);
                }
            }
            if (useLocalView && ShouldApplyLocalViewOffset())
            {
                rotation *= Quaternion.Euler(localViewEulerOffset);
            }

            return rotation;
        }

        private bool TryGetItemHoldPose(GameObject item, out ItemHoldPose pose)
        {
            pose = null;
            if (item == null)
            {
                return false;
            }

            pose = item.GetComponent<ItemHoldPose>();
            if (pose == null)
            {
                pose = item.GetComponentInChildren<ItemHoldPose>(true);
            }

            return pose != null;
        }

        private void ApplyTargetLocalScale(GameObject item)
        {
            if (IsNetworkedItem(item) && !IsServer)
            {
                return;
            }

            Vector3 baseScale;
            if (!TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (normalizeHeldItemScale && item != null)
                {
                    baseScale = Vector3.one;
                }
                else
                {
                    return;
                }
            }
            else if (pose.useCustomHoldScale)
            {
                baseScale = pose.customHoldScale;
            }
            else if (normalizeHeldItemScale)
            {
                baseScale = Vector3.one;
            }
            else
            {
                return;
            }

            Vector3 resultScale = baseScale;
            if (useDifferentHeldScalePerView)
            {
                Vector3 multiplier = GetHeldScaleMultiplierByView(item);
                resultScale = new Vector3(
                    baseScale.x * multiplier.x,
                    baseScale.y * multiplier.y,
                    baseScale.z * multiplier.z);
            }

            item.transform.localScale = new Vector3(
                Mathf.Max(0.0001f, resultScale.x),
                Mathf.Max(0.0001f, resultScale.y),
                Mathf.Max(0.0001f, resultScale.z));
        }

        private Vector3 GetHeldScaleMultiplierByView(GameObject item)
        {
            if (item == null)
            {
                return Vector3.one;
            }

            if (TryGetItemHoldPose(item, out ItemHoldPose pose) && pose.usePerViewScaleMultipliers)
            {
                if (useSeparateViewAnchors)
                {
                    Transform parentFromPose = item.transform.parent;
                    if (IsLocallyControlled() && localViewAnchor != null && parentFromPose == localViewAnchor)
                    {
                        return pose.firstPersonScaleMultiplier;
                    }

                    if (worldViewAnchor != null && parentFromPose == worldViewAnchor)
                    {
                        return pose.thirdPersonScaleMultiplier;
                    }
                }

                if (IsUsingLocalViewForItem(item))
                {
                    return pose.firstPersonScaleMultiplier;
                }

                return pose.thirdPersonScaleMultiplier;
            }

            Transform parent = item.transform.parent;
            if (useSeparateViewAnchors)
            {
                if (IsLocallyControlled() && localViewAnchor != null && parent == localViewAnchor)
                {
                    return localHeldScaleMultiplier;
                }

                if (worldViewAnchor != null && parent == worldViewAnchor)
                {
                    return worldHeldScaleMultiplier;
                }
            }

            if (IsUsingLocalViewForItem(item))
            {
                return localHeldScaleMultiplier;
            }

            return worldHeldScaleMultiplier;
        }

        private void SetItemCollidersEnabled(GameObject item, bool enabled)
        {
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabled;
            }
        }

        private void SetItemRenderersEnabled(GameObject item, bool enabled)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = enabled;
            }
        }

        private void CacheOriginalScale(GameObject item)
        {
            if (item == null || originalScales.ContainsKey(item))
            {
                return;
            }

            originalScales[item] = item.transform.localScale;
        }

        private void RestoreOriginalScale(GameObject item)
        {
            if (item == null)
            {
                return;
            }

            if (originalScales.TryGetValue(item, out Vector3 original))
            {
                item.transform.localScale = original;
            }
        }

        private bool ShouldApplyLocalViewOffset()
        {
            return applyLocalViewOffset
                && useSeparateViewAnchors
                && IsLocallyControlled()
                && localViewAnchor != null;
        }

        private bool IsUsingLocalViewForItem(GameObject item)
        {
            if (item == null)
            {
                return ShouldApplyLocalViewOffset();
            }

            bool isNetworked = IsNetworkedItem(item);
            Transform parent = item.transform.parent;
            if (useSeparateViewAnchors)
            {
                if (IsLocallyControlled() && localViewAnchor != null && parent == localViewAnchor)
                {
                    return true;
                }

                if (worldViewAnchor != null && parent == worldViewAnchor)
                {
                    return false;
                }

                if (isNetworked)
                {
                    return false;
                }
            }

            return ShouldApplyLocalViewOffset();
        }

        private void ApplyRemoteVisualFollow()
        {
            if (IsServer || slotItems == null)
            {
                return;
            }

            if (selectedSlot < 0 || selectedSlot >= slotItems.Length)
            {
                return;
            }

            GameObject item = slotItems[selectedSlot];
            if (item == null || !IsNetworkedItem(item))
            {
                return;
            }

            if (IsOwner)
            {
                return;
            }

            Transform anchor = GetWorldAnchorForNetworkedItem();
            if (anchor == null)
            {
                return;
            }

            Vector3 localPos = GetRemoteVisualLocalPosition(item);
            Quaternion localRot = GetRemoteVisualLocalRotation(item);
            item.transform.SetPositionAndRotation(
                anchor.TransformPoint(localPos),
                anchor.rotation * localRot);

            ApplyRemoteVisualScale(item);
        }

        private Vector3 GetRemoteVisualLocalPosition(GameObject item)
        {
            if (item == null)
            {
                return Vector3.zero;
            }

            if (TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (pose.useThirdPersonPose)
                {
                    return pose.thirdPersonLocalPosition;
                }

                return pose.localPosition;
            }

            return Vector3.zero;
        }

        private Quaternion GetRemoteVisualLocalRotation(GameObject item)
        {
            if (item == null)
            {
                return Quaternion.identity;
            }

            if (TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (pose.useThirdPersonPose)
                {
                    return Quaternion.Euler(pose.thirdPersonLocalEulerAngles);
                }

                return Quaternion.Euler(pose.localEulerAngles);
            }

            return Quaternion.identity;
        }

        private void ApplyRemoteVisualScale(GameObject item)
        {
            if (item == null)
            {
                return;
            }

            Vector3 baseScale = Vector3.one;
            if (TryGetItemHoldPose(item, out ItemHoldPose pose))
            {
                if (pose.useCustomHoldScale)
                {
                    baseScale = pose.customHoldScale;
                }
                else if (!normalizeHeldItemScale)
                {
                    return;
                }
            }
            else if (!normalizeHeldItemScale)
            {
                return;
            }

            if (useDifferentHeldScalePerView && TryGetItemHoldPose(item, out ItemHoldPose poseForView) && poseForView.usePerViewScaleMultipliers)
            {
                Vector3 mult = poseForView.thirdPersonScaleMultiplier;
                baseScale = new Vector3(baseScale.x * mult.x, baseScale.y * mult.y, baseScale.z * mult.z);
            }

            item.transform.localScale = new Vector3(
                Mathf.Max(0.0001f, baseScale.x),
                Mathf.Max(0.0001f, baseScale.y),
                Mathf.Max(0.0001f, baseScale.z));
        }

        private string GetDisplayItemName(GameObject item)
        {
            if (item == null)
            {
                return "EMPTY";
            }

            string name = CleanItemName(item.name);

            return string.IsNullOrWhiteSpace(name) ? "ITEM" : name;
        }

        private string CleanItemName(string rawName)
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

        private bool IsLocallyControlled()
        {
            return CoopGuard.IsLocalOwnerOrOffline(this);
        }
    }
}
