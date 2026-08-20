using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using NLog.Extensions.Logging;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.McpServer;
using SapAbapProject.RfcExtractor;
using System.Text.Json;
using System.Text.Json.Serialization;

// Per MCP best practice for stdio transport: stdout is reserved for JSON-RPC framing.
// Logs MUST go to stderr or to a file. NLog handles both — see nlog.config.

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// Load native SAP RFC SDK before any RFC call. Fail fast at startup with a clear message.
var sdkPath = SapMcpConfiguration.GetSdkPath();
if (string.IsNullOrWhiteSpace(sdkPath))
{
    Console.Error.WriteLine($"FATAL: {SapMcpConfiguration.EnvSdkPath} is not set. Configure the path to the SAP NetWeaver RFC SDK directory.");
    return 1;
}
if (!SapRfcSdkManager.EnsureSdkLoaded(sdkPath))
{
    Console.Error.WriteLine($"FATAL: SAP RFC SDK not found or incomplete at '{sdkPath}'. Required: sapnwrfc.dll, icudt50.dll, icuin50.dll, icuuc50.dll, libsapucum.dll.");
    return 1;
}

var connectionSettings = SapMcpConfiguration.BuildConnectionSettings();
builder.Services.AddSingleton(connectionSettings);
builder.Services.AddSingleton<IAbapExtractor>(sp => new AbapObjectExtractor(connectionSettings));

var toolJsonOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
};

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(serializerOptions: toolJsonOptions);

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SAP MCP Server starting. Target: {Target}", connectionSettings.ToDisplayString());

await app.RunAsync();
return 0;
