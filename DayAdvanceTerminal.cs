using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    [RequireComponent(typeof(NetworkObject))]
    public class DayAdvanceTerminal : NetworkBehaviour
    {
        [Header("Interact")]
        [SerializeField] private Key interactKey = Key.F;
        [SerializeField] private float holdSeconds = 2f;
        [SerializeField] private float interactDistance = 3f;
        private Camera playerCamera;

        [Header("UI")]
        [SerializeField] private bool showUi = true;
        [SerializeField] private string promptText = "Hold F to start next day";
        [SerializeField] private Vector2 progressBarSize = new Vector2(320f, 12f);
        [SerializeField] private float progressBarYOffset = 58f;
        [SerializeField] private Color progressBarBackground = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        [SerializeField] private Color progressBarFill = new Color(0.9f, 0.9f, 0.95f, 0.9f);

        [Header("Availability Gate")]
        [SerializeField] private bool requireExtractionAvailable = false;
        [SerializeField] private string proceedPrompt = "Hold F to end run";
        [SerializeField] private string lockedPrompt = "Need more samples";
        [SerializeField] private bool endRunOnInteract = false;
        [SerializeField] private bool startRunOnInteract = false;
        [SerializeField] private string startRunPrompt = "Hold F to start run";

        private float holdTimer;
        private bool isLookingAtTerminal;
        private bool useExternalDetection;
        private bool externalIsLooking;

        private void Update()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.IsClient)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (useExternalDetection)
            {
                isLookingAtTerminal = externalIsLooking;
            }
            else
            {
                isLookingAtTerminal = IsTerminalInFront();
            }

            if (!isLookingAtTerminal)
            {
                holdTimer = 0f;
                return;
            }

            if (!IsInteractionAvailable())
            {
                holdTimer = 0f;
                return;
            }

            if (!Keyboard.current[interactKey].isPressed)
            {
                holdTimer = 0f;
                return;
            }

            holdTimer += Time.deltaTime;
            if (holdTimer >= Mathf.Max(0.1f, holdSeconds))
            {
                holdTimer = 0f;
                RequestAdvanceDayServerRpc();
            }
        }

        private void OnGUI()
        {
            if (!showUi)
            {
                return;
            }

            if (isLookingAtTerminal)
            {
                GUIStyle centered = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 20
                };

                float px = (Screen.width - 420f) * 0.5f;
                float py = (Screen.height * 0.5f) + 24f;
                GUI.Label(new Rect(px, py, 420f, 30f), GetCurrentPrompt(), centered);

                float progress = Mathf.Clamp01(holdTimer / Mathf.Max(0.1f, holdSeconds));
                if (progress > 0f)
                {
                    float barX = (Screen.width - progressBarSize.x) * 0.5f;
                    float barY = (Screen.height * 0.5f) + progressBarYOffset;
                    Rect barRect = new Rect(barX, barY, progressBarSize.x, progressBarSize.y);
                    DrawPanel(barRect, progressBarBackground);
                    Rect fillRect = new Rect(barX, barY, progressBarSize.x * progress, progressBarSize.y);
                    DrawPanel(fillRect, progressBarFill);
                }
            }

        }

        private void DrawPanel(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private bool IsTerminalInFront()
        {
            if (playerCamera == null)
            {
                ResolvePlayerCamera();
            }

            if (playerCamera == null)
            {
                return false;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            int mask = 1 << gameObject.layer;
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                interactDistance,
                mask,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null)
                {
                    continue;
                }

                DayAdvanceTerminal terminal = col.GetComponentInParent<DayAdvanceTerminal>();
                if (terminal == this)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolvePlayerCamera()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                if (nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                {
                    Camera ownedCam = nm.LocalClient.PlayerObject.GetComponentInChildren<Camera>(true);
                    if (ownedCam != null)
                    {
                        playerCamera = ownedCam;
                        return;
                    }
                }
            }

            Camera main = Camera.main;
            if (main != null)
            {
                playerCamera = main;
                return;
            }

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam != null && cam.enabled)
                {
                    playerCamera = cam;
                    return;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestAdvanceDayServerRpc(ServerRpcParams rpcParams = default)
        {
            if (startRunOnInteract)
            {
                GameManager.Instance?.StartExpedition();
                return;
            }

            if (endRunOnInteract)
            {
                GameManager.Instance?.CompleteExpedition();
                return;
            }

            GameManager.Instance?.StartExpedition();
        }

        public void SetPromptText(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                promptText = text;
            }
        }

        public void SetEndRunOnInteract(bool enabled)
        {
            endRunOnInteract = enabled;
        }

        private bool IsInteractionAvailable()
        {
            if (!requireExtractionAvailable || !endRunOnInteract)
            {
                return true;
            }

            GameManager manager = GameManager.Instance;
            return manager != null && manager.isExtractionAvailable;
        }

        private string GetCurrentPrompt()
        {
            if (!IsInteractionAvailable())
            {
                return lockedPrompt;
            }

            if (startRunOnInteract)
            {
                return startRunPrompt;
            }

            if (endRunOnInteract)
            {
                return proceedPrompt;
            }

            return promptText;
        }

        public void EnableExternalDetection(bool enabled)
        {
            useExternalDetection = enabled;
        }

        public void SetExternalLooking(bool isLooking)
        {
            externalIsLooking = isLooking;
        }
    }
}
