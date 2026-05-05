using SapAbapProject.Core.Models;

namespace SapAbapProject.Cli;

/// <summary>
/// Parsed command-line invocation. <see cref="Command"/> + <see cref="Subcommand"/>
/// identify what to do; the remaining fields hold positional names and flags.
/// </summary>
internal sealed record CliOptions
{
    public string Command { get; init; } = "";
    public string? Subcommand { get; init; }
    public IReadOnlyList<string> Positionals { get; init; } = [];
    public string OutputDir { get; init; } = ".";
    public bool Overwrite { get; init; }
    public bool IncludeSignature { get; init; } = true;
    public string? Pattern { get; init; }
    public string? Package { get; init; }
    public string? FunctionGroup { get; init; }
    public bool IncludeStructures { get; init; }
    public int MaxRows { get; init; } = 200;
    public IReadOnlyList<AbapObjectType> Types { get; init; } = [];
    public CliOverrides Overrides { get; init; } = new();
    public bool ShowHelp { get; init; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
            return new CliOptions { ShowHelp = true };

        var positionals = new List<string>();
        string? outputDir = null;
        bool overwrite = false;
        bool includeSignature = true;
        string? pattern = null;
        string? package = null;
        string? fugr = null;
        bool includeStructures = false;
        int maxRows = 200;
        IReadOnlyList<AbapObjectType> types = [];

        string? optHost = null, optSysNr = null, optClient = null, optUser = null,
                optPassword = null, optLang = null, optSapRouter = null, optSdkPath = null;

        string command = args[0];
        string? subcommand = null;
        int idx = 1;

        // For multi-word commands ("download fugr"), consume the second positional as subcommand.
        if (idx < args.Length && !args[idx].StartsWith('-')
            && (command is "download" or "list"))
        {
            subcommand = args[idx];
            idx++;
        }

        for (; idx < args.Length; idx++)
        {
            var a = args[idx];
            switch (a)
            {
                case "-h":
                case "--help":
                    return new CliOptions { Command = command, Subcommand = subcommand, ShowHelp = true };

                case "-o":
                case "--output":
                    outputDir = NextValue(args, ref idx, a);
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--no-signature":
                    includeSignature = false;
                    break;

                case "--pattern":
                    pattern = NextValue(args, ref idx, a);
                    break;
                case "--package":
                    package = NextValue(args, ref idx, a);
                    break;
                case "--fugr":
                    fugr = NextValue(args, ref idx, a);
                    break;
                case "--include-structures":
                    includeStructures = true;
                    break;
                case "--max":
                    var maxStr = NextValue(args, ref idx, a);
                    if (!int.TryParse(maxStr, out maxRows) || maxRows <= 0)
                        throw new ArgumentException($"--max requires a positive integer, got '{maxStr}'.");
                    break;
                case "--types":
                    types = ParseTypes(NextValue(args, ref idx, a));
                    break;

                case "--host":         optHost      = NextValue(args, ref idx, a); break;
                case "--sysnr":        optSysNr     = NextValue(args, ref idx, a); break;
                case "--client":       optClient    = NextValue(args, ref idx, a); break;
                case "--user":         optUser      = NextValue(args, ref idx, a); break;
                case "--password":     optPassword  = NextValue(args, ref idx, a); break;
                case "--language":     optLang      = NextValue(args, ref idx, a); break;
                case "--sap-router":   optSapRouter = NextValue(args, ref idx, a); break;
                case "--sdk-path":     optSdkPath   = NextValue(args, ref idx, a); break;

                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option '{a}'. Run `sap-cli --help`.");
                    positionals.Add(a);
                    break;
            }
        }

        return new CliOptions
        {
            Command = command,
            Subcommand = subcommand,
            Positionals = positionals,
            OutputDir = outputDir ?? ".",
            Overwrite = overwrite,
            IncludeSignature = includeSignature,
            Pattern = pattern,
            Package = package,
            FunctionGroup = fugr,
            IncludeStructures = includeStructures,
            MaxRows = maxRows,
            Types = types,
            Overrides = new CliOverrides
            {
                Host = optHost,
                SystemNumber = optSysNr,
                Client = optClient,
                User = optUser,
                Password = optPassword,
                Language = optLang,
                SapRouter = optSapRouter,
                SdkPath = optSdkPath,
            },
        };
    }

    private static string NextValue(string[] args, ref int idx, string flag)
    {
        if (idx + 1 >= args.Length)
            throw new ArgumentException($"Option '{flag}' requires a value.");
        return args[++idx];
    }

    private static IReadOnlyList<AbapObjectType> ParseTypes(string csv)
    {
        var result = new List<AbapObjectType>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(raw.ToLowerInvariant() switch
            {
                "fm" or "function-module" or "functionmodule" => AbapObjectType.FunctionModule,
                "table" or "transparent-table"                => AbapObjectType.TransparentTable,
                "structure"                                   => AbapObjectType.Structure,
                "data-element" or "dataelement" or "dtel"     => AbapObjectType.DataElement,
                "domain" or "doma"                            => AbapObjectType.Domain,
                "table-type" or "tabletype" or "ttyp"         => AbapObjectType.TableType,
                _ => throw new ArgumentException(
                    $"Unknown object type '{raw}' in --types. Valid: fm, table, structure, data-element, domain, table-type."),
            });
        }
        return result;
    }
}
