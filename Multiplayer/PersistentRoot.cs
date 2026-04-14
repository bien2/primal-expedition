using UnityEngine;
using Unity.Netcode;

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class PersistentRoot : MonoBehaviour
    {
        [Header("Persistent Objects")]
        [SerializeField] private GameObject[] persistentObjects;

        private void Awake()
        {
            PersistentRoot existing = FindExistingRoot();
            if (existing != null && existing != this)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            ReparentPersistentObjects();
        }

        private PersistentRoot FindExistingRoot()
        {
            PersistentRoot[] roots = FindObjectsOfType<PersistentRoot>(true);
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != this)
                {
                    return roots[i];
                }
            }
            return null;
        }

        private void ReparentPersistentObjects()
        {
            if (persistentObjects == null)
            {
                return;
            }

            for (int i = 0; i < persistentObjects.Length; i++)
            {
                GameObject obj = persistentObjects[i];
                if (obj == null)
                {
                    continue;
                }

                if (obj.GetComponent<NetworkManager>() != null)
                {
                    obj.transform.SetParent(null, true);
                    continue;
                }

                // NetworkObjects can throw errors if their parent changes before being spawned.
                // Don't touch them here; they should manage their own hierarchy.
                if (obj.GetComponent<NetworkObject>() != null)
                {
                    continue;
                }

                obj.transform.SetParent(transform, true);
            }
        }
    }
}
