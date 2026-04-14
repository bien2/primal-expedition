using UnityEngine;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    public class AudioListenerGuard : MonoBehaviour
    {
        [SerializeField] private bool preferCameraListener = true;

        private void Awake()
        {
            EnforceSingleListener();
        }

        private void OnEnable()
        {
            EnforceSingleListener();
        }

        private void EnforceSingleListener()
        {
            AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
            if (listeners == null || listeners.Length <= 1)
            {
                return;
            }

            AudioListener keep = null;
            if (preferCameraListener)
            {
                Camera cam = GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    keep = cam.GetComponent<AudioListener>();
                }
            }

            if (keep == null)
            {
                keep = listeners[0];
            }

            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null)
                {
                    continue;
                }

                listener.enabled = listener == keep;
            }
        }
    }
}
