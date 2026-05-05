namespace SapAbapProject.Core.Models;

public sealed record SapConnectionSettings
{
    public required string AppServerHost { get; init; }
    public string SystemNumber { get; init; } = "00";
    public string Client { get; init; } = "100";
    public required string User { get; init; }
    public required string Password { get; init; }
    public string Language { get; init; } = "EN";
    public string? SystemId { get; init; }
    public string? MessageServerHost { get; init; }
    public string? Group { get; init; }
    public string? SncPartnerName { get; init; }
    public bool UseSncConnection { get; init; }
    public string? SapRouter { get; init; }

    /// <summary>
    /// SAP DDIC language codes (single-char, e.g. <c>"E"</c>, <c>"S"</c>) tried in order
    /// when looking up object descriptions. The first language with a non-empty result
    /// wins. If null, defaults to a cascade derived from <see cref="Language"/> with
    /// English as the final fallback.
    /// </summary>
    public IReadOnlyList<string>? DescriptionLanguages { get; init; }

    public Dictionary<string, string> ToRfcParameters()
    {
        var parameters = new Dictionary<string, string>
        {
            ["USER"] = User,
            ["PASSWD"] = Password,
            ["CLIENT"] = Client,
            ["LANG"] = Language,
        };

        if (!string.IsNullOrEmpty(MessageServerHost) && !string.IsNullOrEmpty(Group))
        {
            parameters["MSHOST"] = MessageServerHost!;
            parameters["GROUP"] = Group!;
            if (!string.IsNullOrEmpty(SystemId))
                parameters["SYSID"] = SystemId!;
        }
        else
        {
            parameters["ASHOST"] = AppServerHost;
            parameters["SYSNR"] = SystemNumber;
        }

        if (UseSncConnection && !string.IsNullOrEmpty(SncPartnerName))
        {
            parameters["SNC_MODE"] = "1";
            parameters["SNC_PARTNERNAME"] = SncPartnerName!;
        }

        if (!string.IsNullOrEmpty(SapRouter))
            parameters["SAPROUTER"] = SapRouter!;

        return parameters;
    }

    public string ToDisplayString() => $"{User}@{AppServerHost} [{Client}]";
}
