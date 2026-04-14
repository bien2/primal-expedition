namespace WalaPaNameHehe.Multiplayer.Core
{
    public interface INetworkSessionService
    {
        bool IsReady { get; }
        bool IsClient { get; }
        bool IsServer { get; }
        bool IsHost { get; }
        ulong LocalClientId { get; }

        event System.Action<ulong> ClientConnected;
        event System.Action<bool> SessionStopped;

        bool TryStartHost();
        bool TryStartClient();
        bool TryStartServer();
        void Shutdown();
    }
}
