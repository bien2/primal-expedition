using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public class PlayerItemPickup : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private InventorySystem inventorySystem;

        [Header("Pickup")]
        [SerializeField] private float interactDistance = 3f;
        [SerializeField] private float interactRadius = 0.22f;
        [SerializeField] private LayerMask pickupLayers = ~0;
        [SerializeField] private bool showPickupPrompt = true;

        private GameObject detectedPickupObject;
        private Outline detectedOutline;
        private Outline lastHighlightedOutline;

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
        }

        private void Update()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (playerCamera == null || inventorySystem == null || Keyboard.current == null)
            {
                return;
            }

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

        private void OnDisable()
        {
            UpdateHoverHighlight(null);
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

        private void OnGUI()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (detectedPickupObject == null)
            {
                return;
            }

            if (!showPickupPrompt)
            {
                return;
            }

            GUIStyle centered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };

            string prompt = $"Press F to pick up {GetDisplayName(detectedPickupObject)}";
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, (Screen.height * 0.5f) + 24f, 520f, 30f), prompt, centered);
        }

        private string GetDisplayName(GameObject obj)
        {
            if (obj == null)
            {
                return "Item";
            }

            string n = obj.name;
            int cloneIndex = n.IndexOf("(Clone)");
            if (cloneIndex >= 0)
            {
                n = n.Substring(0, cloneIndex).Trim();
            }

            return string.IsNullOrWhiteSpace(n) ? "Item" : n;
        }

        private bool TryPickupDetectedObject()
        {
            if (detectedPickupObject == null || inventorySystem == null)
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

            if (IsSpawned && NetworkManager != null)
            {
                NetworkObject itemNetworkObject = pickupRoot.GetComponentInParent<NetworkObject>();
                if (itemNetworkObject != null && itemNetworkObject.IsSpawned)
                {
                    RequestPickupServerRpc(itemNetworkObject.NetworkObjectId);
                    return true;
                }

                RequestPickupBySnapshotServerRpc(pickupRoot.transform.position, GetCleanName(pickupRoot.name));
                return true;
            }

            return inventorySystem.TryPickupWorldItem(pickupRoot, out _);
        }

        [ServerRpc]
        private void RequestPickupServerRpc(ulong itemNetworkObjectId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null || inventorySystem == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkObjectId, out NetworkObject itemNetworkObject))
            {
                return;
            }

            bool pickedUp = inventorySystem.TryPickupWorldItem(itemNetworkObject.gameObject, out _);
            if (pickedUp)
            {
                ApplyPickupClientRpc(itemNetworkObjectId);
            }
        }

        [ServerRpc]
        private void RequestPickupBySnapshotServerRpc(Vector3 worldPosition, string cleanName)
        {
            if (inventorySystem == null)
            {
                return;
            }

            GameObject candidate = FindLocalPickupCandidate(worldPosition, cleanName);
            if (candidate == null)
            {
                return;
            }

            bool pickedUp = inventorySystem.TryPickupWorldItem(candidate, out _);
            if (pickedUp)
            {
                ApplyPickupBySnapshotClientRpc(worldPosition, cleanName);
            }
        }

        [ClientRpc]
        private void ApplyPickupClientRpc(ulong itemNetworkObjectId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null || inventorySystem == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkObjectId, out NetworkObject itemNetworkObject))
            {
                return;
            }

            if (detectedOutline != null)
            {
                detectedOutline.enabled = false;
            }

            inventorySystem.TryPickupWorldItem(itemNetworkObject.gameObject, out _);
        }

        [ClientRpc]
        private void ApplyPickupBySnapshotClientRpc(Vector3 worldPosition, string cleanName)
        {
            if (inventorySystem == null)
            {
                return;
            }

            GameObject candidate = FindLocalPickupCandidate(worldPosition, cleanName);
            if (candidate == null)
            {
                return;
            }

            Outline outline = candidate.GetComponentInParent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }

            inventorySystem.TryPickupWorldItem(candidate, out _);
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
                    string rootName = GetCleanName(root.name);
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

            // Fallback A: scene-wide Outline search in case physics positions diverged between virtual players.
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
                    string rootName = GetCleanName(root.name);
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

            // Fallback B: scene-wide collider search for pickups without Outline.
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
                    string rootName = GetCleanName(root.name);
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

        private string GetCleanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            int cloneIndex = rawName.IndexOf("(Clone)");
            if (cloneIndex >= 0)
            {
                rawName = rawName.Substring(0, cloneIndex);
            }

            return rawName.Trim();
        }

        private bool IsLocallyControlled()
        {
            return CoopGuard.IsLocalOwnerOrOffline(this);
        }
    }
}
