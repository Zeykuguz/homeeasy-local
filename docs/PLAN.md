# HomeEasy AC (Cooper&Hunter CH-S12FTXN-E2wf) – Local-Only Windows Control App

## 1. Goal
A Windows 11 desktop app that talks **directly to the AC over the home LAN** – no vendor cloud,
no remote server, no internet dependency. (iOS later, optional.)

Current state: AC powered on, **not yet joined to Wi-Fi**. Home Wi-Fi: `KODI` and `KODI 5G`.

### Status update (2026-11-15)
- **DONE: the AC was already joined to KODI** (the earlier o3). Discovered at **192.168.0.5**, MAC `08:bc:20:00:02:a0`, type 01/02/02.
- **Local protocol verified**: status read + setpoint write (21→22→21 °C) over TCP 12416. No cloud used.
- Windows "Public" profile did NOT block discovery – no firewall/admin change needed.
- .NET 8 SDK installed; solution `HomeEasy.sln` builds; 11 tests pass; WPF app `HomeEasyLocal.exe` auto-discovers and connects.
- Remaining (optional): DHCP reservation for 192.168.0.5, block 91.196.132.126, iOS app.
- DONE (21:40): off-timer (AC built-in countdown, verified), single-instance + tray fixes, self-contained publish, private GitHub repo Zeykuguz/homeeasy-local with release exe.
- Previous notes below are kept for reference.
- PC verified: Wi-Fi on **KODI, 2.4 GHz, ch 2, WPA2-Personal**, IP 192.168.0.69; Ethernet also on 192.168.0.68 (same LAN, GW 192.168.0.1).
- Both Windows network profiles are **Public** → must switch to **Private** (blocks broadcast replies otherwise).
- Python 3.13 installed; **.NET SDK NOT installed** → install .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).
- **Option A (official app pairing) does NOT work** → Option B (PC-based pairing) is now the primary path.
- Wi-Fi password is provided by the user at runtime only (prompt / env var `HE_WIFI_PASS`); never written to files in the repo.

## 2. Answer: which Wi-Fi network?
- **AC → must join `KODI`** (2.4 GHz). The AC Wi-Fi module is 2.4 GHz only; it cannot see/join `KODI 5G`.
- **PC → join `KODI` as well (recommended)**. Strictly, PC and AC only need to be on the **same LAN/subnet**
  (same router, same IP range e.g. 192.168.1.x). On most routers KODI and KODI 5G are bridged, so it would
  work either way, BUT:
  - device discovery uses a **UDP broadcast**; some routers don't forward broadcast between bands or
    have "AP/client isolation" on one band → discovery silently fails (this may be exactly why the phone app
    can't see the AC!).
  - Using `KODI` for both removes that variable during testing. Once working, you can try the PC on 5G.
- The PC may also be on **Ethernet** to the same router – also fine (same subnet).
- Router settings to check: **AP/client isolation OFF**, **guest network NOT used** for either device,
  2.4 GHz security **WPA2-PSK (AES)** (not WPA3-only), avoid special chars in SSID/password if pairing fails.
  Note: SSID `KODI 5G` contains a space – irrelevant for AC since AC uses `KODI`.

## 3. Protocol knowledge (from public project ki0ki0/homeeasylib, GPLv3 – verified on CH-S09FTXN-PW, same FTXN family)
| Item | Value |
|---|---|
| Discovery | UDP broadcast to `255.255.255.255:12414`, reply comes back to local UDP port **2415** |
| Discovery request | `AA AA 06 02 FF FF FF 00 59` |
| Discovery reply | `data[2]==0x0C && data[3]==0x03`; `data[7..12]`=MAC |
| Control | **TCP 12416** on AC IP, plain/unencrypted, 21-byte frames |
| Checksum | last byte = sum(bytes[0..19]) & 0xFF |
| Status request | `AA AA 12 A0 0A 0A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1A` |
| Command | copy last status frame → modify bits → byte[3]=`0x01` → recompute checksum → send |
| Cloud (NOT used) | MQTT 91.196.132.126:1883 – we will **block it** in the router/firewall |

State bit map (payload byte N = frame byte N+4, bit 0 = MSB):
| Field | Byte.bit (len) | Values |
|---|---|---|
| turbo | 3.0 | bool |
| fanSpeed | 3.1 (3) | 0 Auto, 1–6 |
| power | 3.4 | bool |
| mode | 3.5 (3) | 0 Auto, 1 Cool, 2 Dry, 3 Fan, 4 Heat |
| quiet | 4.1 | bool |
| tempScale | 4.2 | 0 °C, 1 °F |
| setpoint | 4.3 (5) | °C = value+16 (16–30) |
| horizontal swing | 5.0 (4) | 0 Stop, 1 Swing, 2–6 pos, 12, 13 |
| vertical swing | 5.4 (4) | 0 Stop, 1 Swing, 2–6 pos |
| display/health/timer/dry-aux/sleep/eco | 6.0/6.1/6.2/6.3/6.6/6.7 | bool |
| shut timer en / hi | 7.0 / 7.1(3) | |
| boot timer en / hi | 7.4 / 7.5(3) | minutes = hi<<8 \| byte9 |
| indoor temp | byte 11 + byte 12/10 | °C |

## 4. Step-by-step guide (I will guide you interactively at each step)

### Step 1 – Prepare the router (you)
1. Log into router admin. Confirm `KODI` = 2.4 GHz, `KODI 5G` = 5 GHz, same LAN (same IP range).
2. Disable AP/client isolation on `KODI`.
3. Note the LAN range (e.g. 192.168.1.0/24).

### Step 2 – Prepare the PC (me + you)
1. Connect PC to `KODI` (or Ethernet on same router). I verify with `ipconfig` / `netsh wlan show interfaces`.
2. Set network profile to **Private** (Windows blocks discovery on "Public").
3. Add Windows Firewall rule: allow inbound **UDP 2415** (discovery replies) and outbound TCP 12416.
4. Install: Python 3.11+, Wireshark + Npcap, nmap (optional), .NET 8 SDK.

### Step 3 – Connect the AC to `KODI` (pairing)
The AC needs your SSID/password once. Two options:

**Option A (recommended first) – use the official HomeEasy app only for pairing, in LOCAL mode**
1. Phone: clear HomeEasy app data / reinstall; when asked for QR, scan any plain-text QR (e.g. "CooperHunter")
   → app runs in local mode (avoids cloud).
2. AC remote: press **WiFi** (indicator flashes slowly), then hold **WiFi 3 s** → 2 beeps, display **o2**, fast flash.
3. Phone: join hotspot `SMART_xxxx`, password `88888888`.
4. App → `+` → select `KODI`, enter password, choose **LOCAL** → Setting.
5. AC beeps 3×, display **o3** = joined KODI. Phone back to KODI.
   (Even if the app then doesn't list it – that's fine, our PC app will find it.)

**Option A – FAILED (user tested).**

**Option B (PRIMARY) – pair from the PC.** Keep the PC's Ethernet on the home LAN (192.168.0.68),
so the PC stays online and Copilot keeps working while the Wi-Fi adapter joins the AC hotspot.
1. User: AC remote → WiFi button (slow flash) → hold WiFi 3 s → 2 beeps, display **o2**, fast flash.
2. Me: `netsh wlan show networks` → find `SMART_xxxx`; create a WLAN profile (key 88888888) and
   `netsh wlan connect` on the Wi-Fi adapter only.
3. Me: `ipconfig` → AC IP = Wi-Fi gateway (typical: 192.168.4.1, 10.10.100.254, 192.168.1.1, 192.168.10.1).
4. Me: run `tools/probe_ap.py` (Python, standard library only), which:
   - runs a TCP connect scan of common ports (80, 8080, 12414, 12416, 48899, 8899, 6000-6002, 9999, 1883)
     and sends UDP probes;
   - sends HomeEasy discovery `AA AA 06 02 FF FF FF 00 59` to UDP 12414 and listens on 2415;
   - tries the HF-LPB/USR "HF-A11ASSISTHREAD" handshake on UDP 48899 (AT+WSSSID / AT+WSKEY / AT+WMODE=STA);
   - fetches `http://<ap-ip>/` (many modules have a web config page, often admin/admin);
   - tries status request on TCP 12416 (local control may already work over the hotspot).
5. Me: implement provisioning for whichever channel answers:
   - a. web page form → POST SSID/key;
   - b. AT commands on 48899;
   - c. HomeEasy binary frame. If needed, I'll find the exact frame by decompiling the APK
     (`cn.adentech.iot.he`, plus the older `net.conditioner.web`) with JADX and searching for
     `SMART_`, `88888888`, `ssid`, `12414`, `0xAA`, `DatagramSocket` and `Socket(`.
   Then send SSID `KODI` and the password (password is entered at runtime).
6. User: confirm AC beeps 3× / shows **o3**. Me: reconnect Wi-Fi to KODI, then go on to Step 4.
7. Fallback if every channel fails: capture a real phone pairing attempt. Turn on Windows Mobile Hotspot,
   connect the phone to it, capture with Wireshark, and run the official app's pairing while the phone is
   also seen joining SMART_xxxx (or use PCAPdroid on Android).

### Step 4 – Find the AC on the LAN
1. Router DHCP list → AC entry (MAC like `80:05:df:de:xx:xx`, last 4 hex = SMART_xxxx suffix).
2. Create **DHCP reservation** (fixed IP) for the AC.
3. **Block AC internet access / block 91.196.132.126** in router (optional, privacy – verify local control still works after).
4. From PC: `ping <ac-ip>`, `Test-NetConnection <ac-ip> -Port 12416`.

### Step 5 – Prove the protocol with Python (quick test scripts in this repo)
1. `discover.py` – UDP broadcast, print IP/MAC.
2. `status.py <ip>` – TCP 12416, send status request, decode and print state.
3. `cmd.py <ip> power on|off`, `temp 22`, `mode cool` – verify AC reacts.
4. Save real captured frames as test fixtures. If anything differs → Wireshark capture + APK decompile (JADX) to fix bit map; document in `PROTOCOL.md`.

### Repo layout (D:\IdeaProjects\CHAC)
```
tools/            Python research scripts (probe_ap.py, provision.py, discover.py, status.py, cmd.py)
docs/PROTOCOL.md  protocol findings + plan copy
src/HomeEasy.Protocol/         .NET 8 class library
src/HomeEasy.Protocol.Tests/   xUnit
src/HomeEasy.Desktop/          WPF app (WPF chosen: simplest, no packaging, runs on Win11)
src/HomeEasy.Cli/              optional
```
The protocol library and tests can be written now (spec known) in parallel with pairing.

### Step 6 – Windows desktop app (.NET 8, local-only)
- `HomeEasy.Protocol` (class library): `Frame` (21 bytes, checksum), `DeviceState` (bit map, enums),
  `Discovery` (UdpClient broadcast → port 12414, listen 2415, timeout), `LocalClient`
  (TcpClient 12416, read loop, auto-reconnect, status poll ~5 s, serialized commands).
- `HomeEasy.Protocol.Tests` (xUnit): golden frames from your AC, encode/decode round-trip, checksum.
- `HomeEasy.Desktop` (WinUI 3 or WPF): device list (auto-discover + manual IP), controls: power, mode,
  setpoint 16–30 °C, fan (Auto/1–6/Quiet/Turbo), swing H/V, eco/sleep/display/health, timers,
  indoor temp; tray icon with quick on/off; settings JSON in `%AppData%`.
- Optional `HomeEasy.Cli` (`he power on`, `he temp 22`) for scripts / Task Scheduler.
- No cloud code at all.

### Step 7 – iOS (optional, later)
- .NET MAUI reusing `HomeEasy.Protocol`, or Swift + Network.framework; needs Mac/Xcode;
  `NSLocalNetworkUsageDescription`; manual IP (broadcast needs multicast entitlement).
- Same local-only rule: phone must be on home Wi-Fi.

## 5. Notes
- Legal: personal interoperability RE is OK in EU (Directive 2009/24/EC art. 6). If we copy homeeasylib code,
  our code must be GPLv3; plan is to reimplement from the documented protocol.
- Safety: start in read-only mode (status only) before sending commands.
- Device ID `8005dfde88c3` format = module MAC.
