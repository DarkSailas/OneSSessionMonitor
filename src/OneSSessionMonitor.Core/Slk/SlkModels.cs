using System;
using System.Collections.Generic;

namespace OneSSessionMonitor.Core.Slk;

public record SlkProductInfo(
    string ProductCode,
    string ProductName,
    int TotalLicenses,
    int InUseLicenses,
    int FreeLicenses
);

public record SlkSessionHolder(
    int? SessionId,
    string? ProductCode,
    string? ProductName,
    string? ClientHost,
    string? ClientIp,
    string? UserName,
    DateTime? ConnectedAt = null,
    string? IdleTime = null
);

public record SlkServerStatus(
    bool IsConnected,
    string ServerAddress,
    int TotalKeys,
    int TotalLicenses,
    int InUseLicenses,
    int FreeLicenses,
    IReadOnlyList<SlkProductInfo> Products,
    IReadOnlyList<SlkSessionHolder> ActiveSessions,
    string? ErrorMessage = null
)
{
    public bool IsAvailable => IsConnected;
}