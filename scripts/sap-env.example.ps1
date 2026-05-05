# Template for the SAP_* environment variables consumed by sap-cli and the MCP server.
#
# How to use:
#   1. Copy this file to scripts\sap-env.ps1 (gitignored).
#   2. Fill in real values below.
#   3. Dot-source it in the shell where you'll run sap-cli:
#        . .\scripts\sap-env.ps1
#      (the leading dot is required — it imports the variables into the current shell).
#
# After dot-sourcing, run:
#   sap-cli test
#   sap-cli download fugr ZSW_APP -o .\extracted
#
# To remove these vars from the current shell, dot-source scripts\sap-env-clear.ps1
# or just open a new terminal.

# --- Required ---------------------------------------------------------------
$env:SAP_HOST     = "your.sap.host"   # Application server hostname or IP
$env:SAP_USER     = "your-user"
$env:SAP_PASSWORD = "your-password"

# --- Optional, with defaults ------------------------------------------------
$env:SAP_SYSNR    = "00"              # System number
$env:SAP_CLIENT   = "100"             # Client / mandant
$env:SAP_LANGUAGE = "EN"              # Logon language

# --- Path to the SAP NetWeaver RFC SDK (required for native DLL load) -------
# Directory containing sapnwrfc.dll, icudt50.dll, icuin50.dll, icuuc50.dll, libsapucum.dll.
$env:SAP_RFC_SDK_PATH = "C:\SAP\nwrfcsdk\lib"

# --- Optional: SAProuter / message-server logon -----------------------------
# $env:SAP_ROUTER  = "/H/saprouter.example.com/S/3299/H/"
# $env:SAP_MSHOST  = "msg.example.com"
# $env:SAP_GROUP   = "PUBLIC"
# $env:SAP_SYSID   = "PRD"

# --- Optional: SNC (single sign-on) -----------------------------------------
# $env:SAP_SNC_MODE        = "1"
# $env:SAP_SNC_PARTNERNAME = "p:CN=SAPSRV, O=Example, C=CL"

# --- Optional: description-language cascade ---------------------------------
# CSV of DDIC single-char codes; first non-empty wins. Defaults to derivation
# from SAP_LANGUAGE with English ("E") as final fallback.
# $env:SAP_DESCRIPTION_LANGUAGES = "S,E"

Write-Host "SAP env loaded for $($env:SAP_USER)@$($env:SAP_HOST) [$($env:SAP_CLIENT)]" -ForegroundColor Green
