using UnityEngine;
using Unity.Netcode;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    public class PlayerDeathBlackout : MonoBehaviour
    {
        [SerializeField] private bool enableBlackout = true;
        [Min(0.01f)] [SerializeField] private float fadeToBlackSeconds = 2f;

        private PlayerMovement playerMovement;
        private NetworkObject networkObject;
        private bool blackoutActive;
        private bool sawDeadWhileBlackout;
        private float blackoutStartTime;

        private void Awake()
        {
            playerMovement = GetComponent<PlayerMovement>();
            if (playerMovement == null)
            {
                playerMovement = GetComponentInChildren<PlayerMovement>(true);
            }

            networkObject = GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = GetComponentInParent<NetworkObject>();
            }
        }

        private void OnGUI()
        {
            if (!enableBlackout)
            {
                return;
            }

            if (!blackoutActive)
            {
                return;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (networkObject != null && !networkObject.IsOwner)
                {
                    return;
                }
            }

            Color prev = GUI.color;
            float seconds = Mathf.Max(0.01f, fadeToBlackSeconds);
            float alpha = Mathf.Clamp01((Time.time - blackoutStartTime) / seconds);
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        public void BeginBlackoutDelay()
        {
            TriggerImmediateBlackout();
        }

        public void TriggerImmediateBlackout()
        {
            TriggerBlackout(true);
        }

        public void TriggerBlackout(bool useFade)
        {
            if (!enableBlackout)
            {
                return;
            }

            if (playerMovement == null)
            {
                return;
            }

            blackoutActive = true;
            sawDeadWhileBlackout = playerMovement.IsDead;
            float seconds = Mathf.Max(0.01f, fadeToBlackSeconds);
            blackoutStartTime = useFade ? Time.time : (Time.time - seconds);
        }

        private void LateUpdate()
        {
            if (playerMovement == null)
            {
                return;
            }

            if (!blackoutActive)
            {
                return;
            }

            if (playerMovement.IsDead)
            {
                sawDeadWhileBlackout = true;
                return;
            }

            if (sawDeadWhileBlackout)
            {
                blackoutActive = false;
                sawDeadWhileBlackout = false;
            }
        }
    }
}
