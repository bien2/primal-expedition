using System.Collections.Generic;
using UnityEngine;

namespace WalaPaNameHehe
{
    [System.Serializable]
    public class LoadoutEntry
    {
        public string name = "Loadout";
        public GameObject dronePrefab;
        public GameObject extractorPrefab;
        public GameObject specialPrefab;
    }

    public class LoadoutStation : MonoBehaviour
    {
        [SerializeField] private int minimumLoadoutCount = 4;
        [SerializeField] private List<LoadoutEntry> loadouts = new();

        public IReadOnlyList<LoadoutEntry> Loadouts => loadouts;

        public int IndexOf(LoadoutEntry entry)
        {
            if (entry == null || loadouts == null)
            {
                return -1;
            }

            return loadouts.IndexOf(entry);
        }

        public void Configure(List<LoadoutEntry> entries, int minimumCount)
        {
            minimumLoadoutCount = Mathf.Max(1, minimumCount);
            loadouts = entries != null ? new List<LoadoutEntry>(entries) : new List<LoadoutEntry>();

            while (loadouts.Count < minimumLoadoutCount)
            {
                loadouts.Add(new LoadoutEntry { name = $"Loadout {loadouts.Count + 1}" });
            }
        }

        private void OnValidate()
        {
            minimumLoadoutCount = Mathf.Max(1, minimumLoadoutCount);
            if (loadouts == null)
            {
                loadouts = new List<LoadoutEntry>();
            }

            while (loadouts.Count < minimumLoadoutCount)
            {
                loadouts.Add(new LoadoutEntry { name = $"Loadout {loadouts.Count + 1}" });
            }
        }
    }
}
