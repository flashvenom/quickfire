namespace Quickfire.Blazor.Infrastructure.Bridge;

public sealed record BridgeConnection(Guid DeviceId, string UserId, string ConnectionId, Action Abort);

public sealed class BridgeConnections
{
    private readonly Dictionary<Guid, BridgeConnection> _connections = new();
    private readonly object _sync = new();

    public BridgeConnection? Find(Guid deviceId) { lock (_sync) return _connections.GetValueOrDefault(deviceId); }
    public IReadOnlyCollection<BridgeConnection> Snapshot() { lock (_sync) return _connections.Values.ToArray(); }

    public void Register(BridgeConnection connection)
    {
        BridgeConnection? previous;
        lock (_sync)
        {
            previous = _connections.GetValueOrDefault(connection.DeviceId);
            _connections[connection.DeviceId] = connection;
        }
        if (previous != null && previous.ConnectionId != connection.ConnectionId)
            previous.Abort();
    }

    public void Remove(Guid deviceId, string connectionId)
    {
        lock (_sync)
            if (_connections.TryGetValue(deviceId, out var current) && current.ConnectionId == connectionId)
                _connections.Remove(deviceId);
    }

    public void Abort(Guid deviceId)
    {
        BridgeConnection? connection;
        lock (_sync) _connections.Remove(deviceId, out connection);
        connection?.Abort();
    }
}
