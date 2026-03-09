using System;

namespace SapAbapProject.Extension.Services;

public sealed class ConnectionManager
{
    private static readonly Lazy<ConnectionManager> _instance = new(() => new ConnectionManager());
    public static ConnectionManager Instance => _instance.Value;

    public string? ConnectionString { get; private set; }
    public string? SystemInfo { get; private set; }
    public bool IsConnected => ConnectionString is not null;

    public event EventHandler? ConnectionChanged;

    public void SetConnection(string connectionString, string systemInfo)
    {
        ConnectionString = connectionString;
        SystemInfo = systemInfo;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disconnect()
    {
        ConnectionString = null;
        SystemInfo = null;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
