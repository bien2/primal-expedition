using UnityEngine;

namespace WalaPaNameHehe
{
    public class PlayerCrosshair : MonoBehaviour
    {
        [SerializeField] private bool showCrosshair = true;
        [SerializeField] private float size = 16f;
        [SerializeField] private float thickness = 2f;
        [SerializeField] private Color color = Color.white;

        private Texture2D pixel;

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void OnGUI()
        {
            if (!showCrosshair || pixel == null)
            {
                return;
            }

            Color prevColor = GUI.color;
            GUI.color = color;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float half = size * 0.5f;
            float halfThickness = thickness * 0.5f;

            // Horizontal line
            GUI.DrawTexture(new Rect(cx - half, cy - halfThickness, size, thickness), pixel);
            // Vertical line
            GUI.DrawTexture(new Rect(cx - halfThickness, cy - half, thickness, size), pixel);

            GUI.color = prevColor;
        }

        private void OnDestroy()
        {
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }
    }
}
