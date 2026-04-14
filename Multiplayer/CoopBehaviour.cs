using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    // Base class for co-op scripts: local input/UI on owner, shared gameplay on server.
    public abstract class CoopBehaviour : NetworkBehaviour
    {
        [Header("Co-op Safety")]
        [SerializeField] private bool allowOfflineWhenNoNetworkManager = true;

        protected bool CanRunLocal
        {
            get
            {
                if (NetworkManager == null)
                {
                    return allowOfflineWhenNoNetworkManager;
                }

                return IsSpawned && IsOwner;
            }
        }

        protected bool CanRunServer
        {
            get
            {
                if (NetworkManager == null)
                {
                    return allowOfflineWhenNoNetworkManager;
                }

                return IsSpawned && IsServer;
            }
        }
    }
}
