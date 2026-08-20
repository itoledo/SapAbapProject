# SAP ABAP MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io/) 2.1 server that exposes
a live SAP system through the SAP NetWeaver RFC SDK. It lets an LLM agent discover
repository and Dictionary objects, inspect RFC signatures, read ABAP source and
tables, and execute remote-enabled function modules with structured JSON input and
output.

This is a thin wrapper over the same `SapAbapProject.RfcExtractor` core that powers
the Visual Studio extension, so anything you can extract into an `.abap` file you
can also fetch via these tools.

> **Intended for development environments.** `sap_read_table` can read any table
> visible to the SAP user. `sap_execute_rfc` can invoke any RFC authorized for that
> user, including functions that modify business data or commit transactions. Use a
> dedicated least-privilege SAP account and do not point this server at production.

## Tools exposed

| Tool | Purpose |
|------|---------|
| `sap_test_connection` | RFC_PING — verify connectivity. |
| `sap_list_packages` | List `DEVCLASS` matching a wildcard pattern. |
| `sap_list_function_groups` | List FUGRs, optionally per package. |
| `sap_list_rfcs` | List remote-enabled function modules. |
| `sap_list_function_modules` | Search all FMs, optionally restricted to RFC-enabled modules. |
| `sap_get_rfc_definition` | Read live RFC parameter, structure, table, and exception metadata. |
| `sap_execute_rfc` | Execute an RFC from arbitrary structured JSON parameters. |
| `sap_list_repository_objects` | Query any TADIR object type such as PROG, CLAS, INTF, FUGR, TABL, or DDLS. |
| `sap_list_dictionary_object_types` | List supported DDIC categories and TADIR codes. |
| `sap_list_dictionary_objects` | List tables, structures, views, data elements, domains, table types, search helps, lock objects, type groups, and CDS DDL sources. |
| `sap_list_tables` | Search transparent tables (and optionally INTTAB structures). |
| `sap_get_function_module` | Source + structured signature for one FM. |
| `sap_get_table` | DDL + field metadata for a transparent table. |
| `sap_get_structure` | DDL + fields for an INTTAB structure. |
| `sap_get_data_element` | Definition + domain reference + labels. |
| `sap_get_domain` | Definition + fixed values. |
| `sap_get_table_type` | Row type + access mode + key fields. |
| `sap_get_abap_source` | Source for a function module, program, include, or function-group main program. |
| `sap_read_table` | Paginated `RFC_READ_TABLE` access for ad-hoc reads. |

Object-detail tools return both the rendered ABAP source (`source` field) and a
structured `metadata` object — pick whichever the agent finds easier to reason about.

## Executing an RFC

First call `sap_get_rfc_definition` so the agent knows the exact parameter names,
directions, RFC scalar types, structure fields, and table row fields. Then call
`sap_execute_rfc` with an object such as:

```json
{
  "functionName": "BAPI_USER_GET_DETAIL",
  "parameters": {
    "USERNAME": "DEVELOPER"
  },
  "maxTableRows": 500
}
```

Structures are JSON objects and RFC table parameters are arrays of objects. Dates
accept `yyyyMMdd` or `yyyy-MM-dd`, times accept `HH:mm:ss`, booleans sent to
character fields become `X`/blank, and binary values are base64. Output table
parameters are bounded by `maxTableRows`; truncated parameter names are returned in
`truncatedTableParameters`.

The generic repository catalog intentionally accepts any TADIR object code, while
the dedicated Dictionary catalog provides friendly names for the most common DDIC
types. Detailed rendered definitions are currently provided for tables, structures,
data elements, domains, and table types; other definitions can be queried through
their standard SAP RFCs using `sap_get_rfc_definition` and `sap_execute_rfc`.

## Prerequisites

1. **.NET 8 SDK** on the machine that runs the MCP server.
2. **SAP NetWeaver RFC SDK** installed locally (binary download from SAP — not
   redistributable). Required DLLs: `sapnwrfc.dll`, `icudt50.dll`, `icuin50.dll`,
   `icuuc50.dll`, `libsapucum.dll`.

## Configuration (environment variables)

The server reads everything from environment variables — credentials never come
from the agent.

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `SAP_RFC_SDK_PATH` | yes | — | Directory containing `sapnwrfc.dll` and ICU DLLs. |
| `SAP_HOST` | conditional | — | Application server hostname for direct logon. |
| `SAP_USER` | yes | — | SAP user. |
| `SAP_PASSWORD` | yes | — | SAP password. |
| `SAP_SYSNR` | no | `00` | System number. |
| `SAP_CLIENT` | no | `100` | Client (mandant). |
| `SAP_LANGUAGE` | no | `EN` | Logon language (ISO 2-char or SAP 1-char). |
| `SAP_DESCRIPTION_LANGUAGES` | no | derived from `SAP_LANGUAGE` + `E` fallback | Comma-separated cascade for object descriptions, e.g. `ES,EN`. The first language with a non-empty description wins. ISO 2-char codes are mapped to SAP single-char codes (`ES`→`S`, `EN`→`E`, `DE`→`D`, ...). |
| `SAP_ROUTER` | no | — | SAProuter string, if needed. |
| `SAP_MSHOST` | conditional | — | Message server host; requires `SAP_GROUP` and `SAP_SYSID`. |
| `SAP_GROUP` | conditional | — | Logon group for message-server logon. |
| `SAP_SYSID` | conditional | — | System ID for message-server logon. |
| `SAP_SNC_MODE` | no | — | `1` to enable SNC. |
| `SAP_SNC_PARTNERNAME` | no | — | SNC partner name. |
| `SAP_SNC_LIB` | no | — | SNC library path, such as `sapcrypto.dll`. |
| `SAP_SNC_QOP` | no | — | SNC quality of protection agreed with SAP Basis. |
| `SAP_MCP_LOG_DIR` | no | `%APPDATA%\SapAbapProject\logs` | Where NLog writes the rolling log file. |

## Running locally

```powershell
dotnet run --project src/SapAbapProject.McpServer
```

You won't see anything on stdout — that's normal: stdout is reserved for the MCP
JSON-RPC frames. Diagnostics go to stderr and to the rolling log file under
`%APPDATA%\SapAbapProject\logs\sap-mcp.log`.

## Wiring it into a host

### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "sap-abap": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:/dev/SAPABAPProject/src/SapAbapProject.McpServer/SapAbapProject.McpServer.csproj",
        "--no-build"
      ],
      "env": {
        "SAP_RFC_SDK_PATH": "C:/SAP/nwrfcsdk/lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_USER": "DEVELOPER",
        "SAP_PASSWORD": "********",
        "SAP_CLIENT": "100"
      }
    }
  }
}
```

### VS Code (`.vscode/mcp.json`)

```json
{
  "servers": {
    "sap-abap": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/SapAbapProject.McpServer", "--no-build"],
      "env": {
        "SAP_RFC_SDK_PATH": "C:/SAP/nwrfcsdk/lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_USER": "DEVELOPER",
        "SAP_PASSWORD": "********"
      }
    }
  }
}
```

For a less brittle setup, publish the server as a self-contained executable
(`dotnet publish -r win-x64 --self-contained -o out/`) and point `command` at
`out/sap-mcp.exe` instead of using `dotnet run`.

## Logging

NLog writes to:

- **stderr** — visible to the MCP host; useful when debugging from the host's
  output panel.
- **`%APPDATA%/SapAbapProject/logs/sap-mcp.log`** — rolling file (10 MB ×
  5 archives). Override the directory with `SAP_MCP_LOG_DIR`.

Stdout is **never** used for logs — the MCP protocol owns it.

## Troubleshooting

- **"FATAL: SAP_RFC_SDK_PATH is not set"** — set the env var and restart.
- **"SAP RFC SDK not found or incomplete"** — the directory must contain all
  five DLLs listed above.
- **Connection times out** — check `SAP_HOST`, `SAP_SYSNR`, and any SAProuter
  string. The server logs the connection target at startup.
- **`sap_get_*` returns null** — the object doesn't exist or isn't visible to
  the configured SAP user.
