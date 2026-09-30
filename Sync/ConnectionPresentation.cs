namespace HuntHelperEvolved.Sync;

internal enum ConnectionDisplayState { Off, Connecting, Connected, Reconnecting, Disconnected }

internal static class ConnectionPresentation
{
    public static ConnectionDisplayState State(bool enabled, SyncClient.ConnectionState state, bool fatalError) =>
        !enabled ? ConnectionDisplayState.Off : state switch
        {
            SyncClient.ConnectionState.Connected => ConnectionDisplayState.Connected,
            SyncClient.ConnectionState.Connecting => ConnectionDisplayState.Connecting,
            SyncClient.ConnectionState.Failed when !fatalError => ConnectionDisplayState.Reconnecting,
            _ => ConnectionDisplayState.Disconnected
        };

    public static string Label(ConnectionDisplayState state) => state switch
    {
        ConnectionDisplayState.Off => "Sync off",
        ConnectionDisplayState.Connected => "Connected",
        ConnectionDisplayState.Connecting => "Connecting",
        ConnectionDisplayState.Reconnecting => "Reconnecting",
        _ => "Disconnected"
    };

    public static string TrainScope(bool enabled, bool connected, bool shareTrain) =>
        !enabled || !shareTrain ? "Local train" : connected ? "Shared train" : "Shared train (disconnected)";
}
