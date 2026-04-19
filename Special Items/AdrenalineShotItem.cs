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
        public Key useKey = Key.E;
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

            bool keyHeld = false;
            if (Keyboard.current != null)
            {
                keyHeld = Keyboard.current[useKey].isPressed;
            }
            else
            {
                keyHeld = Input.GetKey(KeyCode.E);
            }

            if (!keyHeld)
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

            GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 15
            };

            float cx = (Screen.width * 0.5f) + holdUiOffset.x;
            float cy = (Screen.height * 0.5f) + holdUiOffset.y;
            DrawCircularProgressRing(
                cx,
                cy,
                34f,
                8f,
                progress,
                96,
                holdUiBackgroundColor,
                holdUiFillColor);
            GUI.Label(new Rect(cx - 180f, cy + 44f, 360f, 22f), "Using Adrenaline", infoStyle);
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
