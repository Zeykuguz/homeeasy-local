# Builds a single self-contained HomeEasyLocal.exe (no .NET install needed on the target PC).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet publish src\HomeEasy.Desktop -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o publish
Get-Item publish\HomeEasyLocal.exe | Select-Object FullName, @{n='SizeMB'; e={[math]::Round($_.Length / 1MB, 1)}}
