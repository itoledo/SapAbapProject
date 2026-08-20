# SAPABAPProject

Tools for working with SAP ABAP source code outside of the SAP GUI.

English is the official language for project documentation, user-facing text,
issues, pull requests, and future contributions.

This repo contains:

- **`SapAbapProject.Extension`** — A Visual Studio 2022+ extension that defines a
  `.sapproj` project type. Imports SAP repository objects (function modules,
  tables, data elements, domains, structures, table types) over RFC and
  materializes each as an `.abap` file in your project so you can browse, search
  and version-control them. Inspired by SQL Server Database Projects.
- **`SapAbapProject.McpServer`** — A [Model Context Protocol](https://modelcontextprotocol.io/)
  server that exposes the same SAP-querying capabilities to LLM agents like
  Claude Desktop or GitHub Copilot. See
  [`src/SapAbapProject.McpServer/README.md`](src/SapAbapProject.McpServer/README.md)
  and [`USAGE.md`](USAGE.md).

Both share a core (`SapAbapProject.Core` + `SapAbapProject.RfcExtractor`) that
holds the SAP RFC logic.

## Features

- Import ABAP source from a live SAP system into a Visual Studio project.
- Syntax coloring for `.abap` files.
- MCP server so AI agents can query function modules, tables, etc. as tools,
  with both rendered ABAP source and structured JSON metadata.

## Requirements

- Visual Studio 2022+ (for the extension).
- .NET 8 SDK (for the MCP server).
- SAP NetWeaver RFC SDK installed locally — required for any RFC connection,
  not redistributed with this repo.
