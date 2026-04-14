using UnityEngine;

namespace WalaPaNameHehe
{
    public class FrameRateCap : MonoBehaviour
    {
        [SerializeField] private int targetFps = 144;
        [SerializeField] private bool disableVSync = true;

        private void Awake()
        {
            if (disableVSync)
            {
                QualitySettings.vSyncCount = 0;
            }

            Application.targetFrameRate = Mathf.Max(30, targetFps);
        }
    }
}
