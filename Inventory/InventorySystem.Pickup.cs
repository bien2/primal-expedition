using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WalaPaNameHehe
{
    public partial class InventorySystem
    {
        // --- PlayerItemPickup (merged) ---
        private void UpdatePickupDetection()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }

            if (playerCamera == null || Keyboard.current == null)
            {
                return;
            }

            if (loadoutUiOpen)
            {
                if (!forceLoadoutSelection && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CloseLoadoutUi();
                }
                return;
            }

            if (enableLoadoutUi && TryGetLoadoutStationInFront(out detectedLoadoutStation))
            {
                ClearTerminalDetection();
                detectedPickupObject = null;
                detectedOutline = null;
                UpdateHoverHighlight(null);

                if (Keyboard.current.fKey.wasPressedThisFrame)
                {
                    OpenLoadoutUi(detectedLoadoutStation);
                }

                return;
            }
            detectedLoadoutStation = null;

            if (TryGetTerminalInFront(out DayAdvanceTerminal terminal))
            {
                UpdateTerminalDetection(terminal);
                detectedPickupObject = null;
                detectedOutline = null;
                UpdateHoverHighlight(null);
                return;
            }

            ClearTerminalDetection();

            bool hasTarget = TryGetPickupInFront(out detectedPickupObject, out detectedOutline);
            if (!hasTarget)
            {
                detectedPickupObject = null;
                detectedOutline = null;
            }

            UpdateHoverHighlight(detectedOutline);

            if (detectedPickupObject != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                bool handled = TryPickupDetectedObject();
                if (!handled)
                {
                    Debug.Log("Could not pick up item (inventory may be full).");
                }
            }
        }

        private bool TryGetTerminalInFront(out DayAdvanceTerminal terminal)
        {
            terminal = null;

            if (playerCamera == null)
            {
                return false;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool hit = Physics.SphereCast(
                ray,
                interactRadius,
                out RaycastHit hitInfo,
                interactDistance,
                loadoutLayers,
                QueryTriggerInteraction.Ignore
            );

            if (!hit)
            {
                return false;
            }

            terminal = hitInfo.collider.GetComponentInParent<DayAdvanceTerminal>();
            return terminal != null;
        }

        private void UpdateTerminalDetection(DayAdvanceTerminal terminal)
        {
            if (terminal == null)
            {
                ClearTerminalDetection();
                return;
            }

            if (detectedTerminal != terminal)
            {
                ClearTerminalDetection();
                detectedTerminal = terminal;
                detectedTerminal.EnableExternalDetection(true);
            }

            detectedTerminal.SetExternalLooking(true);
        }

        private void ClearTerminalDetection()
        {
            if (detectedTerminal == null)
            {
                return;
            }

            detectedTerminal.SetExternalLooking(false);
            detectedTerminal.EnableExternalDetection(false);
            detectedTerminal = null;
        }

        private bool TryGetPickupInFront(out GameObject pickupObject, out Outline outline)
        {
            pickupObject = null;
            outline = null;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool hit = Physics.SphereCast(
                ray,
                interactRadius,
                out RaycastHit hitInfo,
                interactDistance,
                pickupLayers,
                QueryTriggerInteraction.Ignore
            );
            if (!hit)
            {
                return false;
            }

            outline = hitInfo.collider.GetComponentInParent<Outline>();
            if (outline != null)
            {
                pickupObject = outline.gameObject;
                return true;
            }

            pickupObject = hitInfo.collider.attachedRigidbody != null
                ? hitInfo.collider.attachedRigidbody.gameObject
                : hitInfo.collider.gameObject;
            return true;
        }

        private bool TryGetLoadoutStationInFront(out LoadoutStation station)
        {
            station = null;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool hit = Physics.SphereCast(
                ray,
                interactRadius,
                out RaycastHit hitInfo,
                interactDistance,
                loadoutLayers,
                QueryTriggerInteraction.Ignore
            );

            if (!hit)
            {
                return false;
            }

            station = hitInfo.collider.GetComponentInParent<LoadoutStation>();
            return station != null;
        }

        private void UpdateHoverHighlight(Outline current)
        {
            if (lastHighlightedOutline == current)
            {
                return;
            }

            if (lastHighlightedOutline != null)
            {
                lastHighlightedOutline.enabled = false;
            }

            if (current != null)
            {
                current.enabled = true;
            }

            lastHighlightedOutline = current;
        }

        private void DrawPickupPrompt()
        {
            if (detectedPickupObject == null || !showPickupPrompt)
            {
                return;
            }

            GUIStyle centered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };

            string prompt = $"Press F to pick up {GetDisplayItemName(detectedPickupObject)}";
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, (Screen.height * 0.5f) + 24f, 520f, 30f), prompt, centered);
        }

        private void OpenLoadoutUi(LoadoutStation station)
        {
            if (station == null)
            {
                return;
            }

            activeLoadoutStation = station;
            loadoutUiOpen = true;

            if (!hasStoredCursorState)
            {
                storedCursorLock = Cursor.lockState;
                storedCursorVisible = Cursor.visible;
                hasStoredCursorState = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (playerMovement != null)
            {
                playerMovement.SetMovementSuppressed(true);
            }
        }

        private void CloseLoadoutUi()
        {
            loadoutUiOpen = false;
            activeLoadoutStation = null;
            forceLoadoutSelection = false;

            if (hasStoredCursorState)
            {
                Cursor.lockState = storedCursorLock;
                Cursor.visible = storedCursorVisible;
                hasStoredCursorState = false;
            }

            if (playerMovement != null)
            {
                playerMovement.SetMovementSuppressed(false);
            }
        }

        private void ApplyLoadout(LoadoutEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
            {
                if (!IsOwner)
                {
                    return;
                }

                int index = activeLoadoutStation != null
                    ? activeLoadoutStation.IndexOf(entry)
                    : -1;
                if (index >= 0)
                {
                    RequestLoadoutServerRpc(activeLoadoutStation.transform.position, index);
                }
                lastLoadoutSelectionDay = GetCurrentDaySafe();
                forceLoadoutSelection = false;
                return;
            }

            ClearInventoryLocal();
            TryAddLoadoutItem(entry.dronePrefab);
            TryAddLoadoutItem(entry.extractorPrefab);
            TryAddLoadoutItem(entry.specialPrefab);

            selectedSlot = 0;
            RefreshSlotVisibility(-1);
            PlayLoadoutSelectSound();
            lastLoadoutSelectionDay = GetCurrentDaySafe();
            forceLoadoutSelection = false;
        }

        private void TryAddLoadoutItem(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            TryStoreItem(prefab, out _);
        }

        private void ClearInventoryLocal()
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

                bool isNetworked = IsNetworkedItem(item);
                if (isNetworked && IsServer)
                {
                    NetworkObject netObj = item.GetComponentInParent<NetworkObject>();
                    if (netObj != null && netObj.IsSpawned)
                    {
                        netObj.Despawn(true);
                    }
                }
                else if (!isNetworked)
                {
                    Destroy(item);
                }
                else
                {
                    item.SetActive(false);
                }

                slotItems[i] = null;
                slotItemIds[i] = 0;
            }

            CleanupLocalViewProxy();
        }

        [ServerRpc]
        private void RequestLoadoutServerRpc(Vector3 stationPosition, int loadoutIndex, ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            LoadoutStation station = FindNearestLoadoutStation(stationPosition, interactDistance * 1.5f);
            if (station == null)
            {
                return;
            }

            IReadOnlyList<LoadoutEntry> loadouts = station.Loadouts;
            if (loadouts == null || loadoutIndex < 0 || loadoutIndex >= loadouts.Count)
            {
                return;
            }

            if (HasSelectedLoadoutForCurrentDay())
            {
                return;
            }

            ApplyLoadoutServer(loadouts[loadoutIndex]);
            lastLoadoutSelectionDay = GetCurrentDaySafe();
        }

        private LoadoutStation FindNearestLoadoutStation(Vector3 position, float maxDistance)
        {
            LoadoutStation[] stations = FindObjectsByType<LoadoutStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (stations == null || stations.Length == 0)
            {
                return null;
            }

            LoadoutStation best = null;
            float bestSqr = maxDistance * maxDistance;
            for (int i = 0; i < stations.Length; i++)
            {
                LoadoutStation s = stations[i];
                if (s == null)
                {
                    continue;
                }

                float sqr = (s.transform.position - position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }

            return best;
        }

        private void ApplyLoadoutServer(LoadoutEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            ClearInventoryServer();
            SpawnLoadoutItem(entry.dronePrefab);
            SpawnLoadoutItem(entry.extractorPrefab);
            SpawnLoadoutItem(entry.specialPrefab);

            selectedSlot = 0;
            RefreshSlotVisibility(-1);
            PushInventoryState();
            PlayLoadoutSelectSoundClientRpc();
        }

        private void SpawnLoadoutItem(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            int targetSlot = FindFirstFreeDataSlot();
            if (targetSlot < 0)
            {
                return;
            }

            GameObject instance = Instantiate(prefab);
            NetworkObject netObj = instance.GetComponentInParent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn(true);
            }

            PrepareWorldItemForInventory(instance);
            SetSlotServer(targetSlot, instance);
            AttachItemToSlot(instance, targetSlot, false);
        }

        private void ClearInventoryServer()
        {
            if (!IsServer || slotItems == null)
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

                if (IsNetworkedItem(item))
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

                slotItems[i] = null;
                slotItemIds[i] = 0;
            }

            CleanupLocalViewProxy();
        }

        private void PlayLoadoutSelectSound()
        {
            if (loadoutAudioSource == null || loadoutSelectClip == null)
            {
                return;
            }

            loadoutAudioSource.PlayOneShot(loadoutSelectClip, Mathf.Clamp01(loadoutSelectVolume));
        }

        [ClientRpc]
        private void PlayLoadoutSelectSoundClientRpc()
        {
            if (!IsOwner)
            {
                return;
            }

            PlayLoadoutSelectSound();
        }

        private bool TryPickupDetectedObject()
        {
            if (detectedPickupObject == null)
            {
                return false;
            }

            if (detectedOutline != null)
            {
                detectedOutline.enabled = false;
            }

            GameObject pickupRoot = ResolvePickupRoot(detectedPickupObject);
            if (pickupRoot == null)
            {
                return false;
            }

            if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
            {
                NetworkObject itemNetworkObject = pickupRoot.GetComponentInParent<NetworkObject>();
                if (itemNetworkObject != null && itemNetworkObject.IsSpawned)
                {
                    RequestPickupServerRpc(itemNetworkObject.NetworkObjectId);
                    return true;
                }
                Debug.LogWarning($"InventorySystem: pickup skipped for non-networked item '{pickupRoot.name}' in multiplayer.");
                return false;
            }

            return TryPickupWorldItem(pickupRoot, out _);
        }

        [ServerRpc]
        private void RequestPickupServerRpc(ulong itemNetworkObjectId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkObjectId, out NetworkObject itemNetworkObject))
            {
                return;
            }

            bool pickedUp = TryPickupWorldItem(itemNetworkObject.gameObject, out _);
            if (pickedUp)
            {
                int slotIndex = GetSlotIndexForItem(itemNetworkObject.gameObject);
                ApplyPickupClientRpc(itemNetworkObjectId, slotIndex);
            }
        }

        [ServerRpc]
        private void RequestPickupBySnapshotServerRpc(Vector3 worldPosition, string cleanName)
        {
            GameObject candidate = FindLocalPickupCandidate(worldPosition, cleanName);
            if (candidate == null)
            {
                return;
            }

            bool pickedUp = TryPickupWorldItem(candidate, out _);
            if (pickedUp)
            {
                ApplyPickupBySnapshotClientRpc(worldPosition, cleanName);
            }
        }

        [ClientRpc]
        private void ApplyPickupClientRpc(ulong itemNetworkObjectId, int slotIndex)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkObjectId, out NetworkObject itemNetworkObject))
            {
                return;
            }

            PrepareWorldItemForRemoteHold(itemNetworkObject.gameObject);

            if (slotIndex < 0)
            {
                slotIndex = FindFirstFreeDataSlot();
            }

            if (slotItems[slotIndex] != null && slotItems[slotIndex] != itemNetworkObject.gameObject)
            {
                slotItems[slotIndex].SetActive(false);
            }

            int previousSlot = selectedSlot;

            slotItems[slotIndex] = itemNetworkObject.gameObject;
            selectedSlot = slotIndex;
            AttachItemToSlot(itemNetworkObject.gameObject, slotIndex, false);
            CleanupLocalViewProxy();
            RefreshSlotVisibility(previousSlot);

            if (useSeparateViewAnchors && localViewAnchor != null)
            {
                localViewSource = null;
                UpdateLocalViewProxy();
            }
        }

        [ClientRpc]
        private void ApplyPickupBySnapshotClientRpc(Vector3 worldPosition, string cleanName)
        {
            // Snapshot-based pickup not supported in multiplayer for inventory sync.
        }

        private GameObject ResolvePickupRoot(GameObject source)
        {
            if (source == null)
            {
                return null;
            }

            NetworkObject networkObject = source.GetComponentInParent<NetworkObject>();
            if (networkObject != null)
            {
                return networkObject.gameObject;
            }

            Rigidbody rb = source.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                return rb.gameObject;
            }

            return source;
        }

        private GameObject FindLocalPickupCandidate(Vector3 worldPosition, string cleanName)
        {
            float searchRadius = Mathf.Max(0.35f, interactRadius * 3f);
            Collider[] hits = Physics.OverlapSphere(worldPosition, searchRadius, pickupLayers, QueryTriggerInteraction.Ignore);

            GameObject best = null;
            float bestSqrDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i];
                if (col == null)
                {
                    continue;
                }

                GameObject root = ResolvePickupRoot(col.gameObject);
                if (root == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    string rootName = CleanItemName(root.name);
                    if (!string.Equals(rootName, cleanName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                float sqrDist = (root.transform.position - worldPosition).sqrMagnitude;
                if (sqrDist < bestSqrDistance)
                {
                    bestSqrDistance = sqrDist;
                    best = root;
                }
            }

            if (best != null)
            {
                return best;
            }

            Outline[] outlines = FindObjectsByType<Outline>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < outlines.Length; i++)
            {
                Outline outline = outlines[i];
                if (outline == null)
                {
                    continue;
                }

                GameObject root = ResolvePickupRoot(outline.gameObject);
                if (root == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    string rootName = CleanItemName(root.name);
                    if (!string.Equals(rootName, cleanName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                float sqrDist = (root.transform.position - worldPosition).sqrMagnitude;
                if (sqrDist < bestSqrDistance)
                {
                    bestSqrDistance = sqrDist;
                    best = root;
                }
            }

            if (best != null)
            {
                return best;
            }

            Collider[] allColliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < allColliders.Length; i++)
            {
                Collider col = allColliders[i];
                if (col == null)
                {
                    continue;
                }

                int layerBit = 1 << col.gameObject.layer;
                if ((pickupLayers.value & layerBit) == 0)
                {
                    continue;
                }

                GameObject root = ResolvePickupRoot(col.gameObject);
                if (root == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    string rootName = CleanItemName(root.name);
                    if (!string.Equals(rootName, cleanName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                float sqrDist = (root.transform.position - worldPosition).sqrMagnitude;
                if (sqrDist < bestSqrDistance)
                {
                    bestSqrDistance = sqrDist;
                    best = root;
                }
            }

            return best;
        }
    }
}
