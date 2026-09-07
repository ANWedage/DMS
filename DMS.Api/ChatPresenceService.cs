using System.Collections.Concurrent;

namespace DMS.Api;

public sealed class ChatPresenceService
{
    private readonly ConcurrentDictionary<string, int> _connections = new(StringComparer.Ordinal);

    public void Connect(string userId, string role)
    {
        _connections.AddOrUpdate(Key(userId, role), 1, (_, count) => count + 1);
    }

    public bool Disconnect(string userId, string role)
    {
        var key = Key(userId, role);
        while (_connections.TryGetValue(key, out var count))
        {
            if (count > 1 && _connections.TryUpdate(key, count - 1, count))
                return false;
            if (_connections.TryRemove(new KeyValuePair<string, int>(key, count)))
                return true;
        }

        return false;
    }

    public bool IsOnline(string userId, string role) => _connections.ContainsKey(Key(userId, role));

    private static string Key(string userId, string role) => $"{role}:{userId}";
}