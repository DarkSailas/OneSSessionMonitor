namespace OneSSessionMonitor.Core.Models;

public sealed record OneCServerEndpoint(
    string Host,
    int RasPort = 1545,
    int ClusterPort = 1540,
    string? ClusterAdminUser = null,
    string? ClusterAdminPassword = null,
    string? RacPath = null
)
{
    public static OneCServerEndpoint Parse(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var parts = endpoint.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return new OneCServerEndpoint(parts[0].Trim());
        }
        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var port))
        {
            return new OneCServerEndpoint(parts[0].Trim(), RasPort: port);
        }
        return new OneCServerEndpoint(endpoint.Trim());
    }

    public string DisplayAddress => RasPort == 1545 ? Host : $"{Host}:{RasPort}";
}
