using System;

namespace SapAbapProject.Extension.Services;

public sealed class ConnectionEntry
{
    public string AppServerHost { get; set; } = string.Empty;
    public string SystemNumber { get; set; } = "00";
    public string Client { get; set; } = "100";
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Language { get; set; } = "EN";
    public string? SystemId { get; set; }
    public string? SapRouter { get; set; }

    public string DisplayName => $"{User}@{AppServerHost} [{Client}]";

    public string ToConnectionString()
    {
        var cs = $"ASHOST={AppServerHost}; SYSNR={SystemNumber}; CLIENT={Client}; USER={User}; PASSWD={Password}; LANG={Language}";
        if (!string.IsNullOrWhiteSpace(SapRouter))
            cs += $"; SAPROUTER={SapRouter}";
        return cs;
    }

    public static ConnectionEntry? ParseConnectionString(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
            return null;

        var entry = new ConnectionEntry();
        var foundHost = false;
        var parts = cs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var kv = part.Split(new[] { '=' }, 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToUpperInvariant();
            var value = kv[1].Trim();
            switch (key)
            {
                case "ASHOST":
                    entry.AppServerHost = value;
                    foundHost = true;
                    break;
                case "SYSNR":
                    entry.SystemNumber = value;
                    break;
                case "CLIENT":
                    entry.Client = value;
                    break;
                case "USER":
                    entry.User = value;
                    break;
                case "PASSWD":
                    entry.Password = value;
                    break;
                case "LANG":
                    entry.Language = value;
                    break;
                case "SYSID":
                    entry.SystemId = value;
                    break;
                case "SAPROUTER":
                    entry.SapRouter = value;
                    break;
            }
        }

        return foundHost ? entry : null;
    }

    public ConnectionEntry Clone() => new()
    {
        AppServerHost = AppServerHost,
        SystemNumber = SystemNumber,
        Client = Client,
        User = User,
        Password = Password,
        Language = Language,
        SystemId = SystemId,
        SapRouter = SapRouter
    };
}
