# HomeEasy Local

Local-only (no cloud) control of the Cooper&Hunter **CH-S12FTXN-E2wf** (and other HomeEasy / Adentech Wi-Fi ACs)
from Windows. The PC talks directly to the AC over your home network.

## Your setup (verified 2026-11-15)
| | |
|---|---|
| Network | (2.4 GHz). PC and AC must be on the same home network (same router). |
| AC IP / MAC | `192.168.0.5` / `08:bc:20:00:02:a0`. Give it a DHCP reservation in the router. |
| Protocol | UDP 12414 for discovery, TCP 12416 for control (21-byte frames). See `docs/PROTOCOL.md`. |

## Run it on another Windows 11 PC

### Option 1 – just copy the exe (recommended, no install)
1. Get `HomeEasyLocal.exe` (about 70 MB, self-contained, no .NET needed):
   - from the GitHub repo → **Releases** → latest → download `HomeEasyLocal.exe`, or
   - copy `publish\HomeEasyLocal.exe` from this PC (USB stick or network share).
2. Put it in a permanent folder, e.g. `C:\Apps\HomeEasyLocal\HomeEasyLocal.exe`.
3. Connect that PC to the same home network as the AC.
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
- **Leftover timers are cleared automatically.** After the off timer fires, the AC keeps the timer armed (0 min).
  Turning the AC on would re-arm it, so the app (PC and iPhone) cancels it at app start, after the AC turns off, and when the AC turns on.
  Timers started from the same app keep running. Turning on from the app always starts without a timer.
- **Light ON/OFF** (next to Power) switches the AC display light. The AC accepts it only while it is running.
- **Minimize** = normal taskbar minimize.
- **Close (X)** = keeps running in the tray (blue **AC** icon; it may be hidden under `^`).
  - Click the icon, or just start the app again, to reopen the window.
  - Right-click the icon for Power ON/OFF and **Exit**.
- Settings: `%AppData%\HomeEasyLocal\settings.json`.

## iPhone app (local only, no App Store)
Source: `ios/` (SwiftUI, iOS 15+). GitHub Actions (`.github/workflows/ios.yml`) builds an **unsigned** IPA on a Mac runner.
It is attached to the release [`ios-v1.1.0`](https://github.com/Zeykuguz/homeeasy-local/releases/tag/ios-v1.1.0)
as `HomeEasyLocal.ipa`. You sign and install it from this Windows PC with **your own free Apple ID**, using **Sideloadly**.

### One-time preparation (Windows PC)
1. Install **iTunes** and **iCloud** from apple.com, the "Windows without Microsoft Store" download links.
   The Microsoft Store versions do not work with Sideloadly.
2. Install **Sideloadly** from https://sideloadly.io.
3. Download `HomeEasyLocal.ipa` from the release (or `gh release download ios-v1.1.0 -R Zeykuguz/homeeasy-local`).

### Install
1. Connect the iPhone by USB cable, unlock it and tap **Trust this computer**.
2. Open Sideloadly:
   - drag `HomeEasyLocal.ipa` into it;
   - choose your iPhone;
   - type your **Apple ID** e-mail;
   - click **Start**.
3. Enter the Apple ID password and the 2FA code when asked.
   Sideloadly sends them only to Apple. You can use a secondary Apple ID if you prefer.
4. On the iPhone, trust the developer profile: **Settings → General → VPN & Device Management** →
   your Apple ID → **Trust**.
5. iOS 16 or newer: turn on **Settings → Privacy & Security → Developer Mode**. The iPhone restarts; confirm **Turn On**.
6. Open **AC Control** and tap **Allow** when asked for *Local Network* access. Without it the app cannot reach the AC.
   You can change it later in **Settings → Privacy & Security → Local Network**.
7. Enter the AC IP (default `192.168.0.5`) and tap **Connect**, or tap **Scan network**.
   The iPhone must be on the home Wi-Fi.

### Limits of a free Apple ID
- The app stops opening after **7 days**. Re-sign it: connect the iPhone and run Sideloadly again with the same IPA;
  your settings are kept. You can also enable Sideloadly's *Automatic refresh* (iPhone and PC on the same Wi-Fi).
- A free Apple ID can have at most 3 sideloaded apps.
- With a paid Apple Developer account ($99/year) the signature lasts 1 year.

### Rebuild after changes
Push changes under `ios/`, or run the workflow by hand: `gh workflow run ios.yml`.
Then download the artifact with `gh run download <run-id>`.
The protocol package can also be tested on a Mac with `cd ios/HomeEasyKit && swift test`.

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
