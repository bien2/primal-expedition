using UnityEngine;

public class DinoKillCamera : MonoBehaviour
{
    [SerializeField] private Camera killCamera;

    private void Awake()
    {
        if (killCamera == null)
        {
            killCamera = GetComponentInChildren<Camera>(true);
        }

        if (killCamera != null)
        {
            killCamera.enabled = false;
            AudioListener listener = killCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }
    }

    public Camera KillCamera
    {
        get
        {
            if (killCamera == null)
            {
                killCamera = GetComponentInChildren<Camera>(true);
            }

            return killCamera;
        }
    }
}
