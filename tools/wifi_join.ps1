# Connect the Wi-Fi adapter to the AC hotspot (SMART_xxxx / 88888888) or back to home Wi-Fi.
#   .\wifi_join.ps1            -> lists SMART_* networks and joins the first one
#   .\wifi_join.ps1 -Ssid  -> reconnect to home Wi-Fi (existing profile)
param([string]$Ssid, [string]$Key = '88888888', [string]$Interface = 'Wi-Fi')

if ($Ssid -and (netsh wlan show profiles) -match [regex]::Escape($Ssid) -and $Ssid -notlike 'SMART_*') {
    netsh wlan connect name="$Ssid" interface="$Interface"
    return
}

if (-not $Ssid) {
    netsh wlan disconnect interface="$Interface" | Out-Null
    Start-Sleep 2
    $nets = netsh wlan show networks interface="$Interface" | Select-String '^SSID \d+ : (.+)$' |
        ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() }
    "Visible networks: $($nets -join ', ')"
    $Ssid = $nets | Where-Object { $_ -like 'SMART_*' } | Select-Object -First 1
    if (-not $Ssid) { Write-Error 'No SMART_* network visible. Is the AC in o2 (config) mode?'; exit 1 }
}

$xml = @"
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>$Ssid</name>
  <SSIDConfig><SSID><name>$Ssid</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM><security>
    <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
    <sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>$Key</keyMaterial></sharedKey>
  </security></MSM>
</WLANProfile>
"@
$file = Join-Path $env:TEMP "$Ssid.xml"
$xml | Set-Content -Encoding UTF8 $file
netsh wlan add profile filename="$file" interface="$Interface" user=current | Out-Null
Remove-Item $file
netsh wlan connect name="$Ssid" interface="$Interface"
Start-Sleep 6
netsh wlan show interfaces | Select-String 'SSID|State|Authentication'
Get-NetIPConfiguration -InterfaceAlias $Interface | Select-Object @{n='IP';e={$_.IPv4Address.IPAddress}}, @{n='Gateway';e={$_.IPv4DefaultGateway.NextHop}}
