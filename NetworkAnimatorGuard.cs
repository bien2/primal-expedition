using UnityEngine;
using Unity.Netcode.Components;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkAnimator))]
    [DefaultExecutionOrder(-10000)]
    public class NetworkAnimatorGuard : MonoBehaviour
    {
        [SerializeField] private bool autoAssignAnimator = true;
        [SerializeField] private bool disableIfInvalid = true;

        private void Awake()
        {
            NetworkAnimator netAnimator = GetComponent<NetworkAnimator>();
            if (netAnimator == null)
            {
                return;
            }

            if (autoAssignAnimator && netAnimator.Animator == null)
            {
                Animator found = GetComponent<Animator>();
                if (found != null)
                {
                    netAnimator.Animator = found;
                }
            }

            Animator animator = netAnimator.Animator;
            bool hasController = animator != null && animator.runtimeAnimatorController != null;
            if (!hasController && disableIfInvalid)
            {
                Debug.LogWarning($"NetworkAnimator disabled on '{name}' because Animator/Controller is missing.", this);
                netAnimator.enabled = false;
            }
        }
    }
}
