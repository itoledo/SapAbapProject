using SapAbapProject.Cli;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.RfcExtractor;

CliOptions opts;
try
{
    opts = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (opts.ShowHelp || string.IsNullOrEmpty(opts.Command))
{
    PrintHelp();
    return opts.ShowHelp ? 0 : 2;
}

var sdkPath = CliConfig.ResolveSdkPath(opts.Overrides.SdkPath);
if (!SapRfcSdkManager.EnsureSdkLoaded(sdkPath))
{
    Console.Error.WriteLine(
        "FATAL: SAP NetWeaver RFC SDK not found. " +
        "Set --sdk-path, the SAP_RFC_SDK_PATH env var, or configure it once via the VS extension. " +
        "Required DLLs in that directory: sapnwrfc.dll, icudt50.dll, icuin50.dll, icuuc50.dll, libsapucum.dll.");
    return 1;
}

SapAbapProject.Core.Models.SapConnectionSettings settings;
try
{
    settings = CliConfig.BuildConnectionSettings(opts.Overrides);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using IAbapExtractor extractor = new AbapObjectExtractor(settings);
var writer = new ScriptFileWriter();
var commands = new Commands(extractor, writer, opts);

try
{
    return await commands.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
"""
sap-cli — download SAP ABAP repository objects to disk

Usage:
  sap-cli <command> [args] [options]

Commands:
  test
      Test the SAP connection (RFC_PING).

  download fugr <name>...
      Download all function modules in one or more function groups.

  download fm <name>...
      Download one or more function modules by name.

  download table <name>...
  download structure <name>...
  download data-element <name>...
  download domain <name>...
  download table-type <name>...
      Download specific objects by name.

  download package <name>...
      Download all repository objects in one or more packages.
      Use --types to restrict object types (default: all supported).

  list packages [<pattern>]
      List packages matching pattern (default '*').

  list fugrs [<package>]
      List function groups, optionally filtered by package.

  list fms [--package X] [--fugr Y] [--pattern Z] [--max N]
      List function modules.

  list tables [--package X] [--pattern Y] [--include-structures] [--max N]
      List tables (and optionally structures).

Output options:
  -o, --output <path>            Output folder (default: current directory)
      --overwrite                Overwrite existing .abap files
      --no-signature             Skip the signature header in function modules
      --types <csv>              Object types for `download package`
                                  (fm, table, structure, data-element, domain, table-type)

List options:
      --package <name>           Package filter
      --fugr <name>              Function-group filter (list fms)
      --pattern <name-pattern>   Name pattern (use '*' wildcard)
      --include-structures       Include structures in `list tables`
      --max <n>                  Max rows (default 200)

SAP connection (env vars; CLI flags override per-call):
  SAP_HOST                       App server host (required)
  SAP_USER                       User (required)
  SAP_PASSWORD                   Password (required)
  SAP_SYSNR                      System number (default 00)
  SAP_CLIENT                     Client (default 100)
  SAP_LANGUAGE                   Logon language (default EN)
  SAP_ROUTER                     Optional SAProuter
  SAP_MSHOST/SAP_GROUP/SAP_SYSID Message-server logon
  SAP_SNC_MODE/SAP_SNC_PARTNERNAME  SNC config
  SAP_DESCRIPTION_LANGUAGES      CSV of DDIC langs (default: derived from SAP_LANGUAGE)
  SAP_RFC_SDK_PATH               Path to SAP NW RFC SDK directory

Connection overrides (take precedence over env):
      --host, --sysnr, --client, --user, --password, --language,
      --sap-router, --sdk-path

Examples:
  sap-cli test
  sap-cli download fugr ZSW_APP -o ./extracted
  sap-cli download fm BAPI_USER_GET_DETAIL RFC_READ_TABLE -o ./extracted
  sap-cli download package ZSW_APP --types table,structure -o ./extracted
  sap-cli list fms --fugr ZSW_APP --max 1000
""");
}
