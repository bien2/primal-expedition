using UnityEngine;

namespace WalaPaNameHehe
{
    public class DroneSpawnerItemUses : MonoBehaviour
    {
        [SerializeField] private int remainingUses = -1;

        public int RemainingUses => remainingUses;

        public void InitializeIfNeeded(int defaultUses)
        {
            if (remainingUses >= 0)
            {
                return;
            }

            remainingUses = Mathf.Max(1, defaultUses);
        }

        public int ConsumeUse()
        {
            if (remainingUses < 0)
            {
                return remainingUses;
            }

            remainingUses = Mathf.Max(0, remainingUses - 1);
            return remainingUses;
        }
    }
}
