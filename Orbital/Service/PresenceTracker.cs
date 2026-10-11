namespace Orbital.Service;

public class PresenceTracker
{
    // UserId -> Set of active ConnectionIds (handles multiple tabs / devices per user)
    private readonly Dictionary<string, HashSet<string>> _onlineUsers = new();
    private readonly object _lock = new();

    public Task<bool> UserConnected(string userId, string connectionId)
    {
        bool isFirstConnection = false;

        lock (_lock)
        {
            if (!_onlineUsers.TryGetValue(userId, out var connections))
            {
                connections = new HashSet<string>();
                _onlineUsers[userId] = connections;
                isFirstConnection = true;
            }

            connections.Add(connectionId);
        }

        return Task.FromResult(isFirstConnection);
    }

    public Task<bool> UserDisconnected(string userId, string connectionId)
    {
        bool isCompletelyOffline = false;

        lock (_lock)
        {
            if (!_onlineUsers.TryGetValue(userId, out var connections))
            {
                return Task.FromResult(false);
            }

            connections.Remove(connectionId);

            if (connections.Count == 0)
            {
                _onlineUsers.Remove(userId);
                isCompletelyOffline = true;
            }
        }

        return Task.FromResult(isCompletelyOffline);
    }

    public Task<string[]> GetOnlineUsers()
    {
        string[] onlineUserIds;

        lock (_lock)
        {
            onlineUserIds = _onlineUsers.Keys.ToArray();
        }

        return Task.FromResult(onlineUserIds);
    }

    public Task<bool> IsUserOnline(string userId)
    {
        bool isOnline;

        lock (_lock)
        {
            isOnline = _onlineUsers.ContainsKey(userId);
        }

        return Task.FromResult(isOnline);
    }

    public Task<List<string>> GetConnectionsForUser(string userId)
    {
        List<string> connectionIds;

        lock (_lock)
        {
            if (_onlineUsers.TryGetValue(userId, out var connections))
            {
                connectionIds = connections.ToList();
            }
            else
            {
                connectionIds = new List<string>();
            }
        }

        return Task.FromResult(connectionIds);
    }
}