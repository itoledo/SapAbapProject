# Using the SAP ABAP MCP Server from AI Agents

This guide explains how to connect `SapAbapProject.McpServer` to GitHub Copilot,
Claude, and any other client compatible with the Model Context Protocol (MCP).

The server enables an agent to:

- Test connectivity to an SAP system.
- List packages, function groups, RFCs, and TADIR repository objects.
- Inspect tables, structures, domains, data elements, and table types.
- Read ABAP source code.
- Read table contents through `RFC_READ_TABLE`.
- Inspect a live RFC signature and execute it with JSON parameters.

> [!WARNING]
> `sap_execute_rfc` can call functions that modify data, start business processes,
> or commit transactions. Use a dedicated least-privilege SAP account and connect
> only to development or test systems.

## 1. Requirements

- .NET 8 SDK, or a published server executable.
- SAP NetWeaver RFC SDK 7.50 installed locally.
- Network connectivity to the SAP system.
- An SAP user authorized for the required RFCs and tables.
- An MCP client that supports the `stdio` transport.

The directory configured through `SAP_RFC_SDK_PATH` must contain:

- `sapnwrfc.dll`
- `icudt50.dll`
- `icuin50.dll`
- `icuuc50.dll`
- `libsapucum.dll`

## 2. Configure the SAP System

The MCP server acts as an **external RFC client** that logs on directly to SAP. It
is not a registered RFC Server, so you **do not need to create an SM59 destination**
to receive calls.

### 2.1 Obtain the connection details

The SAP Basis team can obtain these values from an existing SAP Logon entry or from
the application instance configuration.

For a direct application-server connection:

| SAP value | MCP variable | Example |
|---|---|---|
| Application Server | `SAP_HOST` | `sapdev01.example.com` |
| System Number | `SAP_SYSNR` | `00` |
| Client | `SAP_CLIENT` | `100` |
| SAProuter, when required | `SAP_ROUTER` | `/H/router.example.com/H/sapdev01.example.com` |

For a load-balanced Message Server connection:

| SAP value | MCP variable | Example |
|---|---|---|
| Message Server | `SAP_MSHOST` | `sapms.example.com` |
| System ID | `SAP_SYSID` | `DEV` |
| Logon Group | `SAP_GROUP` | `PUBLIC` |
| Client | `SAP_CLIENT` | `100` |
| SAProuter, when required | `SAP_ROUTER` | `/H/router.example.com/H/sapms.example.com` |

Message Server mode requires `SAP_MSHOST`, `SAP_SYSID`, and `SAP_GROUP` together.
`SAP_HOST` is not required in this mode.

### 2.2 Open network connectivity

The machine running `sap-mcp.exe` must be able to resolve the SAP hostname and open
a TCP connection to:

- The instance dispatcher for a direct connection: normally `32NN`, where `NN` is
  `SAP_SYSNR`; for example, system number `00` normally uses port `3200`.
- The Message Server for load-balanced connections: normally `36NN` or the
  `sapms<SID>` service, depending on the Basis configuration.
- SAProuter: normally port `3299`, when a router string is required.

Example checks from Windows:

```powershell
Test-NetConnection sapdev01.example.com -Port 3200
Test-NetConnection router.example.com -Port 3299
```

Ports can differ between environments. Confirm the exact values with SAP Basis and
the corporate firewall team.

### 2.3 Create the technical user

In `SU01`, create or assign a dedicated user for the agent:

- Use the **Communication** user type for stable technical integration, or
  **Dialog** during controlled development tests.
- Change the initial password and ensure it is not expired.
- Permit logon to the configured client.
- Do not assign broad roles such as `SAP_ALL`.
- Do not grant production access without explicit approval.

The user configured in the MCP server is the identity recorded in SAP auditing for
all reads and RFC executions.

### 2.4 Assign least-privilege authorizations

The exact role depends on the SAP release and local security policy. SAP Basis
should grant only the required objects and values:

| Capability | Common SAP authorization |
|---|---|
| Execute an RFC | `S_RFC`, normally activity `16`, restricted by function module or function group. |
| Read tables | `S_TABU_NAM` by table and/or `S_TABU_DIS` by authorization group. |
| Read code or metadata | Authorizations required by `RFC_READ_REPORT`, `RPY_*` functions, DDIC tables, and local system policies. |

The basic tools can require access to:

- `RFC_PING`
- `RFC_READ_TABLE`
- `RFC_READ_REPORT`
- `RFC_GET_FUNCTION_INTERFACE`
- `RPY_FUNCTIONMODULE_READ`
- `RPY_FUNCTIONMODULE_READ_NEW`
- The BAPIs or custom RFCs that the agent must validate.

Repository discovery and DDIC definitions query tables including `TADIR`, `TDEVC`,
`TFDIR`, `TFTIT`, `DD01L`, `DD01T`, `DD02L`, `DD02T`, `DD03L`, `DD04L`,
`DD04T`, `DD07L`, `DD07T`, `DD40L`, `DD40T`, and `DD42V`. Restrict access to
these tables and to the specific business tables the agent must read.

`RFC_READ_TABLE` authorization behavior can vary by SAP release and installed SAP
Notes. Validate the effective behavior with the SAP security team.

### 2.5 Enable callable function modules

A function can be called from the MCP server only when it is configured as a
**Remote-Enabled Module** in `SE37`.

For custom function modules:

1. Open the function in `SE37`.
2. Review **Attributes → Processing Type**.
3. Select **Remote-Enabled Module** if the API is intended for remote use.
4. Use RFC-compatible types for every parameter.
5. Activate the function and its dependent objects.
6. Grant `S_RFC` only for that function or its function group.

Do not expose internal function modules only to avoid designing a stable API.
Prefer a dedicated, validated wrapper with a clear remote contract.

### 2.6 Direct connection configuration

```powershell
$env:SAP_RFC_SDK_PATH = "C:\SAP\nwrfcsdk\lib"
$env:SAP_HOST = "sapdev01.example.com"
$env:SAP_SYSNR = "00"
$env:SAP_CLIENT = "100"
$env:SAP_USER = "MCP_RFC_DEV"
$env:SAP_PASSWORD = "REPLACE_LOCALLY"
$env:SAP_LANGUAGE = "EN"
```

Remove Message Server variables inherited from another session:

```powershell
Remove-Item Env:SAP_MSHOST -ErrorAction SilentlyContinue
Remove-Item Env:SAP_GROUP -ErrorAction SilentlyContinue
Remove-Item Env:SAP_SYSID -ErrorAction SilentlyContinue
```

### 2.7 Load-balanced Message Server configuration

```powershell
$env:SAP_RFC_SDK_PATH = "C:\SAP\nwrfcsdk\lib"
$env:SAP_MSHOST = "sapms.example.com"
$env:SAP_SYSID = "DEV"
$env:SAP_GROUP = "PUBLIC"
$env:SAP_CLIENT = "100"
$env:SAP_USER = "MCP_RFC_DEV"
$env:SAP_PASSWORD = "REPLACE_LOCALLY"
$env:SAP_LANGUAGE = "EN"
Remove-Item Env:SAP_HOST -ErrorAction SilentlyContinue
```

### 2.8 SAProuter

When the network requires SAProuter, add the route provided by SAP Basis:

```powershell
$env:SAP_ROUTER = "/H/router.example.com/S/3299/H/sapdev01.example.com"
```

Do not invent the route string. Its hops, hosts, and ports must match the corporate
SAProuter configuration.

### 2.9 SNC

If the system requires Secure Network Communication:

```powershell
$env:SAP_SNC_MODE = "1"
$env:SAP_SNC_PARTNERNAME = "p:CN=SAPDEV, O=EXAMPLE, C=US"
$env:SAP_SNC_LIB = "C:\Program Files\SAP\Crypto\sapcrypto.dll"
$env:SAP_SNC_QOP = "8"
```

SAP Basis must provide the SNC partner name, quality of protection, cryptographic
library, PSE, and required credentials. Do not enable SNC partially: an incorrect
partner name, PSE, or QOP prevents logon.

### 2.10 Verify the SAP connection from an agent

After configuring the MCP client, ask:

```text
Use sap_test_connection to test the SAP logon. If it fails, do not run any other
tool and summarize the error without displaying credentials.
```

Then test a least-privilege discovery operation:

```text
List at most 10 packages matching Z*. Do not execute any business RFC.
```

## 3. Prepare the MCP Server

### Recommended: publish an executable

From the repository root:

```powershell
dotnet publish .\src\SapAbapProject.McpServer\SapAbapProject.McpServer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\out\sap-mcp
```

Configure the following command in the MCP client:

```text
D:\dev\SAPABAPProject\out\sap-mcp\sap-mcp.exe
```

### Development option: `dotnet run`

You can also start the server directly from the project:

```powershell
dotnet run --project .\src\SapAbapProject.McpServer
```

In this case, configure `dotnet` as the MCP command with these arguments:

```json
[
  "run",
  "--project",
  "D:\\dev\\SAPABAPProject\\src\\SapAbapProject.McpServer\\SapAbapProject.McpServer.csproj",
  "--no-build"
]
```

## 4. Environment Variables

| Variable | Required | Default | Description |
|---|---:|---|---|
| `SAP_RFC_SDK_PATH` | Yes | — | SAP NetWeaver RFC SDK directory. |
| `SAP_HOST` | Conditional | — | Application Server Host for direct logon. |
| `SAP_USER` | Yes | — | SAP user. |
| `SAP_PASSWORD` | Yes | — | SAP password. |
| `SAP_SYSNR` | No | `00` | System number. |
| `SAP_CLIENT` | No | `100` | Client. |
| `SAP_LANGUAGE` | No | `EN` | Logon language. |
| `SAP_DESCRIPTION_LANGUAGES` | No | Logon language + English | Comma-separated fallback list, such as `EN,DE`. |
| `SAP_ROUTER` | No | — | SAProuter string. |
| `SAP_MSHOST` | Conditional | — | Message Server Host; requires `SAP_GROUP` and `SAP_SYSID`. |
| `SAP_GROUP` | Conditional | — | Message Server logon group. |
| `SAP_SYSID` | Conditional | — | Message Server system ID. |
| `SAP_SNC_MODE` | No | — | Set to `1` to enable SNC. |
| `SAP_SNC_PARTNERNAME` | No | — | SAP server SNC partner name. |
| `SAP_SNC_LIB` | No | — | SNC library path, such as `sapcrypto.dll`. |
| `SAP_SNC_QOP` | No | — | SNC quality of protection agreed with SAP Basis. |
| `SAP_MCP_LOG_DIR` | No | `%APPDATA%\SapAbapProject\logs` | Log directory. |

Do not place real credentials in version-controlled files. Use client secret inputs,
local environment variables, or a secret manager.

## 5. GitHub Copilot in Visual Studio Code

Create `.vscode\mcp.json` in the repository:

```json
{
  "servers": {
    "sap-abap": {
      "type": "stdio",
      "command": "D:\\dev\\SAPABAPProject\\out\\sap-mcp\\sap-mcp.exe",
      "env": {
        "SAP_RFC_SDK_PATH": "C:\\SAP\\nwrfcsdk\\lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_SYSNR": "00",
        "SAP_CLIENT": "100",
        "SAP_USER": "${input:sap-user}",
        "SAP_PASSWORD": "${input:sap-password}",
        "SAP_LANGUAGE": "EN",
        "SAP_DESCRIPTION_LANGUAGES": "EN"
      }
    }
  },
  "inputs": [
    {
      "type": "promptString",
      "id": "sap-user",
      "description": "SAP user"
    },
    {
      "type": "promptString",
      "id": "sap-password",
      "description": "SAP password",
      "password": true
    }
  ]
}
```

Then:

1. Open GitHub Copilot Chat.
2. Select **Agent** mode.
3. Confirm that the `sap-abap` server is running in the MCP server view.
4. Ask: `Test the SAP connection using sap_test_connection.`

VS Code uses the top-level `servers` key.

## 6. GitHub Copilot CLI

Copilot CLI can be configured interactively:

1. Run `copilot`.
2. Enter `/mcp add`.
3. Select the `stdio` transport.
4. Use `D:\dev\SAPABAPProject\out\sap-mcp\sap-mcp.exe` as the command.
5. Add the required `SAP_*` environment variables.
6. Run `/mcp show sap-abap` to inspect the configuration.

Alternatively, create `%USERPROFILE%\.copilot\mcp-config.json`:

```json
{
  "mcpServers": {
    "sap-abap": {
      "type": "stdio",
      "command": "D:\\dev\\SAPABAPProject\\out\\sap-mcp\\sap-mcp.exe",
      "args": [],
      "env": {
        "SAP_RFC_SDK_PATH": "C:\\SAP\\nwrfcsdk\\lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_SYSNR": "00",
        "SAP_CLIENT": "100",
        "SAP_USER": "DEVELOPER",
        "SAP_PASSWORD": "REPLACE_LOCALLY",
        "SAP_LANGUAGE": "EN"
      },
      "tools": ["*"]
    }
  }
}
```

Copilot CLI uses the top-level `mcpServers` key, unlike `.vscode\mcp.json`.

## 7. Claude Desktop

On Windows, open:

```text
%APPDATA%\Claude\claude_desktop_config.json
```

You can also use **Settings → Developer → Edit Config**.

```json
{
  "mcpServers": {
    "sap-abap": {
      "command": "D:\\dev\\SAPABAPProject\\out\\sap-mcp\\sap-mcp.exe",
      "args": [],
      "env": {
        "SAP_RFC_SDK_PATH": "C:\\SAP\\nwrfcsdk\\lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_SYSNR": "00",
        "SAP_CLIENT": "100",
        "SAP_USER": "DEVELOPER",
        "SAP_PASSWORD": "REPLACE_LOCALLY",
        "SAP_LANGUAGE": "EN",
        "SAP_DESCRIPTION_LANGUAGES": "EN"
      }
    }
  }
}
```

Fully restart Claude Desktop after saving the file.

Claude Desktop configuration can contain the password in plain text. Restrict file
permissions and never copy it into a repository.

## 8. Claude Code

Set the SAP variables in the session used to start Claude Code:

```powershell
$env:SAP_RFC_SDK_PATH = "C:\SAP\nwrfcsdk\lib"
$env:SAP_HOST = "sap-dev.example.com"
$env:SAP_SYSNR = "00"
$env:SAP_CLIENT = "100"
$env:SAP_USER = "DEVELOPER"
$env:SAP_PASSWORD = "REPLACE_LOCALLY"
$env:SAP_LANGUAGE = "EN"
```

Register the server; its process inherits the variables:

```powershell
claude mcp add --transport stdio --scope user sap-abap -- `
  D:\dev\SAPABAPProject\out\sap-mcp\sap-mcp.exe
```

Inspect the registration:

```powershell
claude mcp list
claude mcp get sap-abap
```

Use `--scope project` instead of `--scope user` to share the server definition with
the project. Do not include credentials in the shared configuration.

## 9. Other MCP Clients

For a generic MCP client, configure a local server with:

| Field | Value |
|---|---|
| Name | `sap-abap` |
| Transport | `stdio` |
| Command | Absolute path to `sap-mcp.exe` |
| Arguments | None |
| Environment | Required `SAP_*` variables |

Common configuration format:

```json
{
  "mcpServers": {
    "sap-abap": {
      "type": "stdio",
      "command": "D:\\dev\\SAPABAPProject\\out\\sap-mcp\\sap-mcp.exe",
      "args": [],
      "env": {
        "SAP_RFC_SDK_PATH": "C:\\SAP\\nwrfcsdk\\lib",
        "SAP_HOST": "sap-dev.example.com",
        "SAP_USER": "DEVELOPER",
        "SAP_PASSWORD": "REPLACE_LOCALLY"
      }
    }
  }
}
```

Some clients use `servers` instead of `mcpServers`. Follow the schema required by
the specific host.

## 10. Recommended Agent Workflow

### Query SAP

1. Run `sap_test_connection`.
2. Discover the object with a `sap_list_*` tool.
3. Inspect its definition or source.
4. Read only the required rows and fields.
5. Summarize results without exposing credentials or sensitive data.

### Execute an RFC

1. Find the function with `sap_list_rfcs`.
2. Always call `sap_get_rfc_definition`.
3. Prepare parameters according to the returned signature.
4. Explain possible side effects.
5. Request confirmation when the function can modify SAP.
6. Call `sap_execute_rfc`.
7. Interpret `RETURN`, `BAPIRET2`, exceptions, and output tables.

The server does not call `BAPI_TRANSACTION_COMMIT` automatically. An agent must not
execute that RFC unless the user explicitly requests a transaction commit.

## 11. Prompt Examples

### Test connectivity

```text
Use the sap-abap MCP server to test the SAP connection. Do not execute any other
RFC.
```

### Discover an API

```text
Find RFCs whose names match Z_ORDER*. Show the package, function group, and
description for each result. Do not execute them.
```

### Inspect an RFC before using it

```text
Inspect the definition of BAPI_USER_GET_DETAIL and explain its input parameters,
structures, output tables, and return messages. Do not execute it yet.
```

### Execute a read-only RFC

```text
First inspect the definition of BAPI_USER_GET_DETAIL. If USERNAME is a valid
parameter, execute it for user DEVELOPER and summarize the returned data. Limit
each output table to 100 rows.
```

### Validate an SAP API

```text
Validate RFC Z_API_GET_ORDER with order 4500001234:
1. Inspect its definition.
2. Build parameters using the declared SAP types.
3. Execute it with at most 200 rows per output table.
4. Review exceptions and RETURN/BAPIRET2 structures.
5. State whether the API worked and provide relevant evidence.
Do not commit a transaction or execute any other write RFC.
```

### Query a table

```text
Read T001 using sap_read_table. Return BUKRS, BUTXT, ORT01, and LAND1 for
BUKRS = '1000'. Limit the result to 20 rows.
```

### Explore the ABAP Dictionary

```text
Find tables and structures matching ZSD_* in package ZSD. Then inspect
ZSD_ORDER_HEADER and explain its keys, fields, data elements, and types.
```

### Inspect ABAP source

```text
Read the source code of function module Z_API_GET_ORDER. Identify queried tables,
called RFCs, and input validations. Do not execute the function.
```

### Compare contract and behavior

```text
Inspect the signature and source of Z_API_GET_ORDER. Then execute a read-only test
case and compare the actual response with the declared contract.
Do not execute BAPI_TRANSACTION_COMMIT.
```

## 12. JSON Parameters for `sap_execute_rfc`

Scalar example:

```json
{
  "functionName": "BAPI_USER_GET_DETAIL",
  "parameters": {
    "USERNAME": "DEVELOPER"
  },
  "maxTableRows": 100
}
```

Structure and table example:

```json
{
  "functionName": "Z_API_VALIDATE_ORDER",
  "parameters": {
    "IS_HEADER": {
      "VBELN": "4500001234",
      "TEST_RUN": true
    },
    "IT_ITEMS": [
      {
        "POSNR": "000010",
        "MATNR": "MAT-001",
        "MENGE": "2"
      }
    ]
  },
  "maxTableRows": 200
}
```

Conversion rules:

| SAP input type | JSON representation |
|---|---|
| `CHAR`, `STRING`, `NUM`, `BCD` | JSON string. |
| `INT`, `INT1`, `INT2`, `INT8` | JSON number. |
| `FLOAT`, `DECF16`, `DECF34` | JSON number. |
| `DATE` | `yyyyMMdd` or `yyyy-MM-dd`. |
| `TIME` | `HH:mm:ss`. |
| Indicator fields | `true` becomes `X`; `false` becomes blank. |
| `BYTE`, `XSTRING` | Base64 string. |
| `STRUCTURE` | JSON object. |
| `TABLE` | Array of JSON objects with the same fields in every row. |

Omit optional values that are not used. Do not send `null`.

## 13. Paginated Table Reads

`sap_read_table` accepts:

- `table`: table or view name.
- `fields`: requested fields.
- `where`: optional ABAP SQL predicate.
- `maxRows`: page size, up to 10,000.
- `rowSkip`: number of rows to skip.

Example agent instruction:

```text
Read MARA in pages of 100 rows using rowSkip. Request only MATNR, MTART, and MATKL.
Stop after 500 rows or when a page is empty.
```

`RFC_READ_TABLE` retains its standard SAP limitations, including the maximum
serialized row length. Request only the fields that are required.

## 14. Security and Permissions

- Use a technical user separate from personal and production users.
- Restrict authorization objects, RFCs, and visible tables.
- Never provide the SAP password to the model in a prompt.
- Never store credentials in Git.
- Review every execution request marked destructive by the MCP client.
- Bound `maxRows` and `maxTableRows`.
- Avoid personal, credential, or financial data unless explicitly authorized.
- Do not allow automatic commits during read-only validation.

Effective security is determined by both the MCP client and SAP authorizations. The
server does not attempt to bypass SAP authorization checks.

## 15. Troubleshooting

Logs are written to:

```text
%APPDATA%\SapAbapProject\logs\sap-mcp.log
```

Change the directory with `SAP_MCP_LOG_DIR`.

Common problems:

| Symptom | Action |
|---|---|
| Server does not appear | Validate the JSON and fully restart the client. |
| `SAP_RFC_SDK_PATH` is missing | Set the SDK path in the MCP process environment. |
| Required DLLs are missing | Verify that all five RFC SDK DLLs are present. |
| Logon error | Check host, system number, client, user, password, and SAProuter. |
| RFC not found | Confirm that it is remote-enabled and visible to the SAP user. |
| `NOT_AUTHORIZED` | Request the minimum required SAP authorization. |
| Output is truncated | Carefully increase `maxTableRows` or paginate the query. |
| No tools are available | Confirm that the host started the process over `stdio`. |

`stdout` is reserved for MCP JSON-RPC messages. Diagnostics are written to `stderr`
and to the log file.
