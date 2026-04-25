using UnityEngine;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    public class LadderClimbTrigger : MonoBehaviour
    {
        [Min(0f)]
        [SerializeField] private float climbSpeed = 3.5f;

        private void OnTriggerEnter(Collider other)
        {
            TrySetLadderState(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            TrySetLadderState(other, false);
        }

        private void TrySetLadderState(Collider other, bool isInside)
        {
            if (other == null)
            {
                return;
            }

            PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
            if (player == null)
            {
                return;
            }

            player.SetLadderClimb(isInside, climbSpeed);
        }
    }
}
