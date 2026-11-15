# HomeEasy Local

Local-only (no cloud) control of the Cooper&Hunter **CH-S12FTXN-E2wf** (and other HomeEasy / Adentech Wi-Fi ACs)
from Windows. The PC talks directly to the AC over your home network.

## Your setup (verified 2026-11-15)
| | |
|---|---|
| Network | `KODI` (2.4 GHz). PC and AC must be on the same home network (same router). |
| AC IP / MAC | `192.168.0.5` / `08:bc:20:00:02:a0`. Give it a DHCP reservation in the router. |
| Protocol | UDP 12414 for discovery, TCP 12416 for control (21-byte frames). See `docs/PROTOCOL.md`. |

## Run it on another Windows 11 PC

### Option 1 – just copy the exe (recommended, no install)
1. Get `HomeEasyLocal.exe` (about 70 MB, self-contained, no .NET needed):
   - from the GitHub repo → **Releases** → latest → download `HomeEasyLocal.exe`, or
   - copy `publish\HomeEasyLocal.exe` from this PC (USB stick or network share).
2. Put it in a permanent folder, e.g. `C:\Apps\HomeEasyLocal\HomeEasyLocal.exe`.
3. Connect that PC to the same home network as the AC (Wi-Fi `KODI`, `KODI 5G` or a cable to the same router).
4. Double-click the exe.
   - If Windows SmartScreen says *"Windows protected your PC"*: click **More info → Run anyway**.
     This happens because the exe is unsigned.
   - If the Windows Firewall asks: allow **Private networks**.
5. The app searches for the AC and connects (or type `192.168.0.5` and click **Connect**).
6. Optional:
   - **Start with Windows:** press `Win+R`, type `shell:startup`, and put a shortcut to the exe there.
   - **Start-menu entry:** right-click the exe → *Pin to Start*.

### Option 2 – build from source
```powershell
winget install Microsoft.DotNet.SDK.8 Git.Git
git clone https://github.com/Zeykuguz/homeeasy-local.git
cd homeeasy-local
.\publish.ps1            # creates publish\HomeEasyLocal.exe
```

### If the AC is not found
- The PC is on a **guest** network, or the router has **AP/client isolation** turned on. Use the main network.
- The AC got a new IP. Check the router's device list and type the new IP into the app.
- A VPN on the PC can block local traffic. Disconnect it, or allow LAN access in the VPN.

## Using the app
- **Power, mode, target temperature, fan, swing, Eco/Sleep/Display/Health/Aux.** The app refreshes every 5 s.
- **Off timer:** choose a preset (15 min to 12 h), or type minutes (e.g. `90`) or hours (e.g. `1.5 h`). Then click **Start**.
  The timer runs **inside the AC**, so the PC can be shut down afterwards. **Cancel** removes it.
  The app shows the remaining time.
- **Minimize** = normal taskbar minimize.
- **Close (X)** = keeps running in the tray (blue **AC** icon; it may be hidden under `^`).
  - Click the icon, or just start the app again, to reopen the window.
  - Right-click the icon for Power ON/OFF and **Exit**.
- Settings: `%AppData%\HomeEasyLocal\settings.json`.

## Development
```powershell
dotnet run --project src\HomeEasy.Desktop   # run from source
dotnet test                                 # unit tests (use real frames captured from the AC)
```
Python tools (`tools/`, Python 3.10+):
```powershell
python tools\he.py discover
python tools\he.py status 192.168.0.5
python tools\he.py set 192.168.0.5 power=on mode=cool temp=22 fan=auto vswing=1
python tools\he.py set 192.168.0.5 timer=on off_timer=on off_minutes=90
```
`probe_ap.py` / `wifi_join.ps1` are for re-pairing research (AC hotspot `SMART_xxxx`, key `88888888`).

## Privacy
The AC module also tries to reach the vendor MQTT cloud (91.196.132.126:1883), which has no authentication.
Block that IP (or the AC's internet access) in the router; local control keeps working.
