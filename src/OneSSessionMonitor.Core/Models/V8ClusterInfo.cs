namespace OneSSessionMonitor.Core.Models;

public sealed record V8ClusterInfo(
    string ClusterId,
    string ClusterName,
    string Host,
    int Port,
    string? AdminUser = null,
    string? AdminPassword = null
);
