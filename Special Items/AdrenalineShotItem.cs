using UnityEngine;
using UnityEngine.InputSystem;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public class AdrenalineShotItem : MonoBehaviour
    {
        [Header("Adrenaline Shot")]
        public float speedMultiplier = 1.5f;
        public float effectDurationSeconds = 5f;
        public float holdToUseSeconds = 2f;
        public bool showHoldUi = true;
        public Vector2 holdUiSize = new Vector2(240f, 16f);
        public Vector2 holdUiOffset = new Vector2(0f, 70f);
        public Color holdUiBackgroundColor = new Color(1f, 1f, 1f, 0.2f);
        public Color holdUiFillColor = new Color(0.2f, 0.9f, 0.35f, 0.95f);

        private InventorySystem inventorySystem;
        private PlayerMovement playerMovement;
        private float holdTimer;
        private Texture2D uiPixel;
        private void Awake()
        {
            ResolveOwners();
            EnsureUiPixel();
        }

        private void OnEnable()
        {
            holdTimer = 0f;
            ResolveOwners();
            EnsureUiPixel();
        }

        private void Update()
        {
            if (!IsUsableByLocalPlayer())
            {
                ResetHold();
                return;
            }

            bool rightHeld = false;
            if (Mouse.current != null)
            {
                rightHeld = Mouse.current.rightButton.isPressed;
            }
            else
            {
                rightHeld = Input.GetMouseButton(1);
            }

            if (!rightHeld)
            {
                ResetHold();
                return;
            }

            holdTimer += Time.deltaTime;

            if (holdTimer < holdToUseSeconds)
            {
                return;
            }

            UseItem();
            ResetHold();
        }

        private void UseItem()
        {
            if (playerMovement == null)
            {
                ResolveOwners();
            }

            if (playerMovement == null)
            {
                return;
            }

            playerMovement.ApplyTemporarySpeedMultiplier(speedMultiplier, effectDurationSeconds);

            if (inventorySystem != null)
            {
                inventorySystem.TryConsumeSelectedItem();
            }
        }

        private bool IsUsableByLocalPlayer()
        {
            ResolveOwnersIfNeeded();

            if (inventorySystem == null || playerMovement == null)
            {
                return false;
            }

            if (!CoopGuard.IsLocalOwnerOrOffline(playerMovement))
            {
                return false;
            }

            GameObject selected = inventorySystem.GetSelectedItem();
            if (selected == null)
            {
                return false;
            }

            if (selected == gameObject)
            {
                return true;
            }

            bool childOf = transform.IsChildOf(selected.transform);
            return childOf;
        }

        private void ResolveOwnersIfNeeded()
        {
            if (inventorySystem != null && playerMovement != null)
            {
                return;
            }

            ResolveOwners();
        }

        private void ResolveOwners()
        {
            inventorySystem = GetComponentInParent<InventorySystem>();
            playerMovement = GetComponentInParent<PlayerMovement>();

            if (inventorySystem != null && playerMovement != null)
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
                playerMovement = system.GetComponent<PlayerMovement>();
                if (playerMovement == null)
                {
                    playerMovement = system.GetComponentInParent<PlayerMovement>();
                }

                if (playerMovement != null)
                {
                    break;
                }
            }
        }

        private void ResetHold()
        {
            holdTimer = 0f;
        }

        private void EnsureUiPixel()
        {
            if (uiPixel != null)
            {
                return;
            }

            uiPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            uiPixel.SetPixel(0, 0, Color.white);
            uiPixel.Apply();
        }

        private void OnGUI()
        {
            if (!showHoldUi)
            {
                return;
            }

            if (!IsUsableByLocalPlayer())
            {
                return;
            }

            if (holdToUseSeconds <= 0f)
            {
                return;
            }

            if (holdTimer <= 0f)
            {
                return;
            }

            EnsureUiPixel();
            float progress = Mathf.Clamp01(holdTimer / holdToUseSeconds);

            float width = Mathf.Max(40f, holdUiSize.x);
            float height = Mathf.Max(6f, holdUiSize.y);
            float x = (Screen.width - width) * 0.5f + holdUiOffset.x;
            float y = (Screen.height * 0.5f) + holdUiOffset.y;

            GUI.color = holdUiBackgroundColor;
            GUI.DrawTexture(new Rect(x, y, width, height), uiPixel);
            GUI.color = holdUiFillColor;
            GUI.DrawTexture(new Rect(x, y, width * progress, height), uiPixel);
            GUI.color = Color.white;
        }
    }
}
