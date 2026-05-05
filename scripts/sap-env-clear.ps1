# Clears the SAP_* environment variables from the current shell.
# Dot-source it:  . .\scripts\sap-env-clear.ps1

$names = @(
    'SAP_HOST', 'SAP_USER', 'SAP_PASSWORD',
    'SAP_SYSNR', 'SAP_CLIENT', 'SAP_LANGUAGE',
    'SAP_RFC_SDK_PATH', 'SAP_ROUTER',
    'SAP_MSHOST', 'SAP_GROUP', 'SAP_SYSID',
    'SAP_SNC_MODE', 'SAP_SNC_PARTNERNAME',
    'SAP_DESCRIPTION_LANGUAGES'
)

foreach ($n in $names) {
    if (Test-Path "env:$n") { Remove-Item "env:$n" }
}

Write-Host "SAP env cleared." -ForegroundColor Yellow
