using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    [RequireComponent(typeof(NetworkObject))]
    public class BloodSampleSubmitter : NetworkBehaviour
    {
        [Header("Submit")]
        [SerializeField] private Key submitKey = Key.F;
        [SerializeField] private int samplesPerSubmit = 1;
        [SerializeField] private GameObject bloodSamplePrefab;
        [SerializeField] private InventorySystem inventorySystem;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float interactDistance = 3f;
        [SerializeField] private float interactRadius = 0.22f;
        [SerializeField] private LayerMask submitLayers = ~0;

        [Header("UI - Prompt")]
        [SerializeField] private bool showSubmitPrompt = true;
        [SerializeField] private Vector2 promptSize = new Vector2(360f, 36f);
        [SerializeField] private string promptText = "Press F to submit blood sample";

        private int submittedCount;
        private bool isSubmitTargetInFront;
        private readonly NetworkVariable<int> submittedCountNet = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public int SubmittedCount => submittedCount;
        public int SharedSubmittedCount => GetDisplayCount();

        private void Update()
        {
            ResolveInventoryIfNeeded();
            if (!IsLocalInventory())
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            isSubmitTargetInFront = TryGetSubmitTargetInFront();

            if (Keyboard.current[submitKey].wasPressedThisFrame && isSubmitTargetInFront && CanSubmitCurrentItem())
            {
                RequestSubmitServerRpc();
            }
        }

        public void AddSamples(int amount)
        {
            int add = Mathf.Max(0, amount);
            submittedCount += add;
        }

        private void OnGUI()
        {
            if (showSubmitPrompt && IsLocalInventory() && CanSubmitCurrentItem() && isSubmitTargetInFront)
            {
                GUIStyle centered = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 20
                };

                float x = (Screen.width - promptSize.x) * 0.5f;
                float y = (Screen.height * 0.5f) + 24f;
                Rect promptArea = new Rect(x, y, promptSize.x, promptSize.y);
                GUI.Label(promptArea, promptText, centered);
            }
        }

        private void ResolveInventoryIfNeeded()
        {
            if (inventorySystem != null)
            {
                return;
            }

            InventorySystem[] systems = Object.FindObjectsByType<InventorySystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < systems.Length; i++)
            {
                InventorySystem system = systems[i];
                if (system == null)
                {
                    continue;
                }

                if (!CoopGuard.IsLocalOwnerOrOffline(system))
                {
                    continue;
                }

                inventorySystem = system;
                break;
            }
        }

        private bool IsLocalInventory()
        {
            return inventorySystem != null && CoopGuard.IsLocalOwnerOrOffline(inventorySystem);
        }


        private bool TryGetSubmitTargetInFront()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }

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
                submitLayers,
                QueryTriggerInteraction.Ignore
            );

            if (!hit)
            {
                return false;
            }

            BloodSampleSubmitter target = hitInfo.collider.GetComponentInParent<BloodSampleSubmitter>();
            return target == this;
        }

        private bool CanSubmitCurrentItem()
        {
            if (inventorySystem == null || bloodSamplePrefab == null)
            {
                return false;
            }

            GameObject selected = inventorySystem.GetSelectedItem();
            if (selected == null)
            {
                return false;
            }

            return IsBloodSample(selected);
        }

        private int GetDisplayCount()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                return submittedCountNet.Value;
            }

            return submittedCount;
        }

        private bool IsBloodSample(GameObject item)
        {
            if (item == null || bloodSamplePrefab == null)
            {
                return false;
            }

            string prefabName = bloodSamplePrefab.name;
            string itemName = item.name;
            if (item == bloodSamplePrefab)
            {
                return true;
            }

            if (itemName == prefabName)
            {
                return true;
            }

            return itemName.StartsWith(prefabName);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSubmitServerRpc(ServerRpcParams rpcParams = default)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return;
            }

            NetworkObject player = nm.SpawnManager.GetPlayerNetworkObject(rpcParams.Receive.SenderClientId);
            if (player == null)
            {
                return;
            }

            InventorySystem inv = player.GetComponentInChildren<InventorySystem>(true);
            if (inv == null)
            {
                return;
            }

            GameObject selected = inv.GetSelectedItem();
            if (!IsBloodSample(selected))
            {
                return;
            }

            if (inv.TryConsumeSelectedItemServer())
            {
                submittedCountNet.Value += Mathf.Max(0, samplesPerSubmit);
                Multiplayer.GameManager manager = Multiplayer.GameManager.Instance;
                if (manager != null)
                {
                    for (int i = 0; i < Mathf.Max(0, samplesPerSubmit); i++)
                    {
                        manager.AddSample();
                    }
                }
            }
        }
    }
}
