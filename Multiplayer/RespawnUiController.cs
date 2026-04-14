using UnityEngine;
using UnityEngine.UI;

namespace WalaPaNameHehe
{
    public class RespawnUiController : MonoBehaviour
    {
        [SerializeField] private PlayerMovement player;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Button respawnButton;
        [SerializeField] private bool autoFindPlayer = true;

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponentInChildren<CanvasGroup>(true);
            }

            if (respawnButton == null)
            {
                respawnButton = GetComponentInChildren<Button>(true);
            }

            if (respawnButton != null)
            {
                respawnButton.onClick.AddListener(OnRespawnClicked);
            }
        }

        private void OnDestroy()
        {
            if (respawnButton != null)
            {
                respawnButton.onClick.RemoveListener(OnRespawnClicked);
            }
        }

        private void Update()
        {
            if (player == null && autoFindPlayer)
            {
                player = FindLocalPlayer();
            }

            bool shouldShow = player != null && player.IsDead && player.IsOwner;
            SetVisible(shouldShow);
        }

        private void OnRespawnClicked()
        {
            if (player == null)
            {
                return;
            }

            player.RequestRespawn();
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.blocksRaycasts = visible;
                canvasGroup.interactable = visible;
                return;
            }

            gameObject.SetActive(visible);
        }

        private static PlayerMovement FindLocalPlayer()
        {
            PlayerMovement[] players = Object.FindObjectsByType<PlayerMovement>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement candidate = players[i];
                if (candidate != null && candidate.IsOwner)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
