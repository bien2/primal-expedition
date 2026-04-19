using System.Collections.Generic;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class SessionHud : MonoBehaviour
    {
        public static SessionHud Instance { get; private set; }

        [Header("Layout")]
        [SerializeField] private Vector2 panelSize = new Vector2(220f, 28f);
        [SerializeField] private Vector2 panelMargin = new Vector2(20f, 12f);
        [SerializeField] private float panelSpacing = 6f;
        [SerializeField] private float logPanelHeight = 200f;
        [SerializeField] private float logPanelWidth = 420f;
        [SerializeField] private float logLineHeight = 20f;
        [SerializeField] private Color panelColor = new Color(0.08f, 0.08f, 0.12f, 0.85f);
        [SerializeField] private bool anchorTopLeft = true;
        [SerializeField] private float topOffset = 64f;
        [SerializeField] private bool showSpawnLogs = true;
        [Min(1)] [SerializeField] private int maxLogLines = 12;

        [Header("Labels")]
        [SerializeField] private string bloodSamplesPrefix = "Blood Samples: ";
        [SerializeField] private string hunterMeterPrefix = "Hunter Meter";
        [SerializeField] private string dayPrefix = "Day: ";
        [SerializeField] private string runStatePrefix = "Run: ";
        [SerializeField] private string extractionPrefix = "Extract: ";
        [SerializeField] private string huntedPrefix = "Hunted: ";

        [Header("Debug")]
        [SerializeField] private bool showGameManagerState = true;
        [SerializeField] private bool showKillSelfButton = true;
        [SerializeField] private string killSelfButtonText = "Kill Me";

        private BloodSampleSubmitter bloodSampleSubmitter;
        private DayNightTimer dayNightTimer;
        private PlayerMovement localPlayer;
        private readonly List<string> logLines = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void PostLog(string message)
        {
            if (Instance == null)
            {
                return;
            }

            Instance.AddLog(message);
        }

        public void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string line = $"{System.DateTime.Now:HH:mm:ss} {message}";
            logLines.Add(line);

            int limit = Mathf.Max(1, maxLogLines);
            while (logLines.Count > limit)
            {
                logLines.RemoveAt(0);
            }
        }

        private void OnGUI()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == ulong.MaxValue)
            {
                return;
            }

            ResolveReferences();

            float x = panelMargin.x;
            float y = anchorTopLeft
                ? panelMargin.y + Mathf.Max(0f, topOffset)
                : Screen.height - logPanelHeight - panelSize.y - panelMargin.y;

            List<string> lines = new();

            // Day
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                lines.Add($"{dayPrefix}{gm.currentDay}");
            }
            else if (dayNightTimer != null)
            {
                lines.Add($"{dayPrefix}{dayNightTimer.CurrentDay}");
            }

            if (showGameManagerState && gm != null)
            {
                lines.Add($"{runStatePrefix}{gm.CurrentState}");
                lines.Add($"{bloodSamplesPrefix}{gm.collectedSamples} / {gm.requiredSamples}");
                lines.Add($"{extractionPrefix}{(gm.isExtractionAvailable ? "Yes" : "No")}");
            }
            else
            {
                // Blood samples
                if (bloodSampleSubmitter != null)
                {
                    lines.Add($"{bloodSamplesPrefix}{bloodSampleSubmitter.SharedSubmittedCount}");
                }
            }

            // Hunter meter list
            List<(string label, float meter)> meters = GetHunterMeterList();
            if (meters.Count > 0)
            {
                lines.Add(hunterMeterPrefix);

                for (int i = 0; i < meters.Count; i++)
                {
                    (string label, float meter) = meters[i];
                    int percent = Mathf.RoundToInt(Mathf.Clamp01(meter) * 100f);
                    lines.Add($"{label} - {percent}%");
                }
            }

            // Hunted player (below meter list)
            HunterMeterManager manager = HunterMeterManager.Instance;
            if (manager != null && manager.HasActiveHunt)
            {
                string huntedLabel = ResolvePlayerLabel(manager.HuntTargetClientId);
                lines.Add($"{huntedPrefix}{huntedLabel}");
            }
            else
            {
                lines.Add($"{huntedPrefix}None");
            }

            if (lines.Count == 0)
            {
                return;
            }

            float lineHeight = panelSize.y;
            float totalHeight = (lineHeight * lines.Count) + (panelSpacing * (lines.Count - 1));
            Rect panelRect = new Rect(x, y, panelSize.x, totalHeight);
            DrawPanel(panelRect);

            float lineY = y;
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(x, lineY, panelSize.x, lineHeight), lines[i]);
                lineY += lineHeight + panelSpacing;
            }

            if (!showKillSelfButton)
            {
                DrawLogs(x, lineY + panelSpacing);
                return;
            }

            PlayerMovement player = ResolveLocalPlayer();
            if (player == null)
            {
                return;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !player.IsOwner)
            {
                return;
            }

            Rect buttonRect = new Rect(x, lineY + panelSpacing, panelSize.x, Mathf.Max(24f, panelSize.y));
            GUI.enabled = !player.IsDead;
            if (GUI.Button(buttonRect, string.IsNullOrWhiteSpace(killSelfButtonText) ? "Kill Me" : killSelfButtonText))
            {
                player.DebugRequestKillSelf();
            }
            GUI.enabled = true;

            DrawLogs(x, buttonRect.yMax + panelSpacing);
        }

        private void DrawLogs(float x, float y)
        {
            if (!showSpawnLogs || logLines == null || logLines.Count == 0)
            {
                return;
            }

            float height = Mathf.Max(0f, logPanelHeight);
            if (height <= 0f)
            {
                return;
            }

            float width = Mathf.Max(panelSize.x, logPanelWidth);
            Rect rect = new Rect(x, y, width, height);
            DrawPanel(rect);

            float lineHeight = Mathf.Max(12f, logLineHeight);
            float textY = y + 4f;
            for (int i = logLines.Count - 1; i >= 0; i--)
            {
                GUI.Label(new Rect(x + 6f, textY, width - 12f, lineHeight), logLines[i]);
                textY += lineHeight;
                if (textY > rect.yMax - lineHeight)
                {
                    break;
                }
            }
        }

        private void DrawPanel(Rect rect)
        {
            Color prev = GUI.color;
            GUI.color = panelColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void ResolveReferences()
        {
            if (bloodSampleSubmitter == null)
            {
                bloodSampleSubmitter = FindFirstObjectByType<BloodSampleSubmitter>(FindObjectsInactive.Exclude);
            }

            if (dayNightTimer == null)
            {
                dayNightTimer = FindFirstObjectByType<DayNightTimer>(FindObjectsInactive.Exclude);
            }
        }

        private PlayerMovement ResolveLocalPlayer()
        {
            if (localPlayer != null)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (!localPlayer.IsSpawned)
                    {
                        localPlayer = null;
                    }
                }
                else if (!localPlayer.gameObject.activeInHierarchy)
                {
                    localPlayer = null;
                }
            }

            if (localPlayer != null)
            {
                return localPlayer;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                ulong localId = nm.LocalClientId;
                if (nm.ConnectedClients != null &&
                    nm.ConnectedClients.TryGetValue(localId, out NetworkClient client) &&
                    client != null &&
                    client.PlayerObject != null)
                {
                    localPlayer = client.PlayerObject.GetComponentInChildren<PlayerMovement>(true);
                    return localPlayer;
                }
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement candidate = players[i];
                if (candidate == null)
                {
                    continue;
                }

                if (CoopGuard.IsLocalOwnerOrOffline(candidate))
                {
                    localPlayer = candidate;
                    return localPlayer;
                }
            }

            return null;
        }

        private List<(string label, float meter)> GetHunterMeterList()
        {
            List<(string label, float meter)> list = new();
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                List<ulong> ids = new List<ulong>();
                foreach (var kvp in nm.ConnectedClients)
                {
                    ids.Add(kvp.Key);
                }
                ids.Sort();

                for (int i = 0; i < ids.Count; i++)
                {
                    ulong clientId = ids[i];
                    if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                    {
                        continue;
                    }

                    PlayerMovement player = client.PlayerObject.GetComponentInChildren<PlayerMovement>(true);
                    if (player == null)
                    {
                        continue;
                    }

                    string label = $"P{i + 1}";
                    list.Add((label, player.HunterMeterValue));
                }
                return list;
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement player = players[i];
                if (player == null)
                {
                    continue;
                }

                list.Add(($"P{i + 1}", player.HunterMeterValue));
            }

            return list;
        }

        private string ResolvePlayerLabel(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                List<ulong> ids = new List<ulong>();
                foreach (var kvp in nm.ConnectedClients)
                {
                    ids.Add(kvp.Key);
                }
                ids.Sort();
                int index = ids.IndexOf(clientId);
                if (index >= 0)
                {
                    return $"P{index + 1}";
                }
            }

            return "P1";
        }
    }
}
