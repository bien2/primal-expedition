using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    public static class CoopGuard
    {
        // For MonoBehaviours on networked objects:
        // - offline / no NetworkObject: allow
        // - networked: owner only
        public static bool IsLocalOwnerOrOffline(Component component)
        {
            if (component == null)
            {
                return false;
            }

            NetworkObject networkObject = component.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                return true;
            }

            if (NetworkManager.Singleton == null)
            {
                return true;
            }

            return networkObject.IsOwner;
        }

        public static bool IsServerOrOffline()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return true;
            }

            return nm.IsServer;
        }

 
        public static bool ShouldRunServerGameplay(Component component)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return true;
            }

            NetworkObject networkObject = component != null ? component.GetComponent<NetworkObject>() : null;
            if (networkObject == null)
            {
                return nm.IsServer;
            }

  
            return nm.IsServer;
        }

        public static bool IsClientProxy(Component component)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return false;
            }

            if (nm.IsServer)
            {
                return false;
            }

            NetworkObject networkObject = component != null ? component.GetComponent<NetworkObject>() : null;
            if (networkObject == null)
            {
                return true;
            }

            return !networkObject.IsOwner;
        }
    }
}
