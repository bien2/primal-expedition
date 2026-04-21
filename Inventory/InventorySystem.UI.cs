using System.Collections.Generic;
using UnityEngine;

namespace WalaPaNameHehe
{
    public partial class InventorySystem
    {
        private void OnGUI()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (playerMovement != null && playerMovement.IsInteractionLocked)
            {
                return;
            }

            DrawCrosshair();

            if (uiSuppressed)
            {
                return;
            }

            DrawLoadoutPrompt();
            DrawLoadoutUi();
            DrawPickupPrompt();
            DrawInventoryUi();
        }

        // --- Inventory UI ---
        private void DrawInventoryUi()
        {
            if (!showInventoryUi || slotItems == null)
            {
                return;
            }

            float uiScale = Mathf.Clamp(inventoryUiScale, 0.8f, 1.5f);
            float slotWidth = 96f * uiScale;
            float slotHeight = 64f * uiScale;
            float spacing = 8f * uiScale;
            float totalWidth = (slotWidth * slotItems.Length) + (spacing * (slotItems.Length - 1));
            float startX = (Screen.width - totalWidth) * 0.5f;
            float y = Screen.height - (92f * uiScale);

            Rect panelRect = new Rect(startX - (12f * uiScale), y - (20f * uiScale), totalWidth + (24f * uiScale), slotHeight + (28f * uiScale));
            DrawSolidRect(panelRect, inventoryPanelColor);

            GUIStyle centered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(14f * uiScale),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };

            GUIStyle topCentered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(11f * uiScale),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1f, 1f, 1f, 0.86f) }
            };

            for (int i = 0; i < slotItems.Length; i++)
            {
                float x = startX + i * (slotWidth + spacing);
                Rect slotRect = new Rect(x, y, slotWidth, slotHeight);
                bool selected = i == selectedSlot;
                DrawSolidRect(slotRect, selected ? selectedSlotColor : slotColor);
                DrawRectBorder(slotRect, selected ? selectedSlotBorderColor : slotBorderColor, Mathf.Max(1f, uiScale));

                string itemName = slotItems[i] != null ? GetDisplayItemName(slotItems[i]) : "EMPTY";
                string nameLabel = $"{i + 1}: {ShortUiName(itemName, 12)}";

                GUI.Label(new Rect(x + 2f, y - (18f * uiScale), slotWidth - 4f, 18f * uiScale), nameLabel, topCentered);
                DrawSlotItemVisual(new Rect(x + 4f, y + 4f, slotWidth - 8f, slotHeight - 8f), slotItems[i], centered);
            }
        }

        private void DrawSlotItemVisual(Rect rect, GameObject item, GUIStyle centered)
        {
            if (item == null)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(rect, "-", centered);
                GUI.color = previous;
                return;
            }

            string label = ShortUiName(GetDisplayItemName(item), 8).ToUpperInvariant();
            GUI.Label(rect, label, centered);
        }

        private void DrawLoadoutPrompt()
        {
            if (!showLoadoutPrompt || detectedLoadoutStation == null || loadoutUiOpen)
            {
                return;
            }

            GUIStyle centered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };

            string prompt = "Press F to open loadouts";
            GUI.Label(new Rect((Screen.width - 520f) * 0.5f, (Screen.height * 0.5f) + 24f, 520f, 30f), prompt, centered);
        }

        private void DrawLoadoutUi()
        {
            if (!loadoutUiOpen || activeLoadoutStation == null)
            {
                return;
            }

            IReadOnlyList<LoadoutEntry> loadouts = activeLoadoutStation.Loadouts;
            if (loadouts == null || loadouts.Count == 0)
            {
                return;
            }

            float uiScale = Mathf.Clamp(inventoryUiScale, 0.8f, 1.5f);
            float cardWidth = 220f * uiScale;
            float cardHeight = 140f * uiScale;
            float spacing = 16f * uiScale;
            float totalWidth = (cardWidth * loadouts.Count) + (spacing * (loadouts.Count - 1));
            float startX = (Screen.width - totalWidth) * 0.5f;
            float startY = (Screen.height - cardHeight) * 0.5f;

            Rect panelRect = new Rect(startX - (20f * uiScale), startY - (28f * uiScale), totalWidth + (40f * uiScale), cardHeight + (56f * uiScale));
            DrawSolidRect(panelRect, inventoryPanelColor);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(14f * uiScale),
                normal = { textColor = Color.white }
            };

            GUIStyle itemStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Normal,
                fontSize = Mathf.RoundToInt(12f * uiScale),
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };

            for (int i = 0; i < loadouts.Count; i++)
            {
                LoadoutEntry entry = loadouts[i];
                float x = startX + i * (cardWidth + spacing);
                Rect cardRect = new Rect(x, startY, cardWidth, cardHeight);
                DrawSolidRect(cardRect, slotColor);
                DrawRectBorder(cardRect, slotBorderColor, Mathf.Max(1f, uiScale));

                string title = string.IsNullOrWhiteSpace(entry.name) ? $"Loadout {i + 1}" : entry.name;
                GUI.Label(new Rect(x + 6f, startY + 6f, cardWidth - 12f, 20f * uiScale), title, titleStyle);

                float listY = startY + (32f * uiScale);
                GUI.Label(new Rect(x + 10f, listY, cardWidth - 20f, 18f * uiScale), $"Drone: {GetLoadoutItemName(entry.dronePrefab)}", itemStyle);
                GUI.Label(new Rect(x + 10f, listY + (20f * uiScale), cardWidth - 20f, 18f * uiScale), $"Extractor: {GetLoadoutItemName(entry.extractorPrefab)}", itemStyle);
                GUI.Label(new Rect(x + 10f, listY + (40f * uiScale), cardWidth - 20f, 18f * uiScale), $"Special: {GetLoadoutItemName(entry.specialPrefab)}", itemStyle);

                Rect buttonRect = new Rect(x + (cardWidth - (120f * uiScale)) * 0.5f, startY + cardHeight - (34f * uiScale), 120f * uiScale, 26f * uiScale);
                if (GUI.Button(buttonRect, "Select"))
                {
                    ApplyLoadout(entry);
                    CloseLoadoutUi();
                }
            }
        }

        private string GetLoadoutItemName(GameObject prefab)
        {
            return prefab != null ? ShortUiName(GetDisplayItemName(prefab), 12) : "None";
        }

        private void DrawSolidRect(Rect rect, Color color)
        {
            if (crosshairPixel == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, crosshairPixel);
            GUI.color = previous;
        }

        private void DrawRectBorder(Rect rect, Color color, float thickness)
        {
            thickness = Mathf.Max(1f, thickness);
            DrawSolidRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private static string ShortUiName(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.Length <= maxChars)
            {
                return trimmed;
            }

            return trimmed.Substring(0, Mathf.Max(1, maxChars - 3)) + "...";
        }

        // --- PlayerCrosshair (merged) ---
        private void DrawCrosshair()
        {
            if (!showCrosshair || crosshairPixel == null)
            {
                return;
            }

            Color prevColor = GUI.color;
            GUI.color = crosshairColor;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float half = crosshairSize * 0.5f;
            float halfThickness = crosshairThickness * 0.5f;

            GUI.DrawTexture(new Rect(cx - half, cy - halfThickness, crosshairSize, crosshairThickness), crosshairPixel);
            GUI.DrawTexture(new Rect(cx - halfThickness, cy - half, crosshairThickness, crosshairSize), crosshairPixel);

            GUI.color = prevColor;
        }
    }
}
