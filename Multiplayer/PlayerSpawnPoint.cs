using UnityEngine;

namespace WalaPaNameHehe
{
    public class PlayerSpawnPoint : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.75f);
            Gizmos.DrawSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
        }
#endif
    }
}
