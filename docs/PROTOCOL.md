# HomeEasy local protocol (verified on CH-S12FTXN-E2wf, 2026-11-15)

## Discovery
- Client binds UDP **2415**, broadcasts to `255.255.255.255:12414`: `AA AA 06 02 FF FF FF 00 59`
- Reply (real): `AA AA 0C 03 01 02 02 08 BC 20 00 02 A0 00 EE`
  - `[2]=0x0C` length, `[3]=0x03` discovery reply, `[4..6]` = industry 01 / vendor 02 / type 02, `[7..12]` = MAC

## Control – TCP 12416, 21-byte frames
- Header `AA AA 12`, byte 3 = message type (`A0` status request, `00` status reply, `01` command)
- Last byte = sum(bytes 0..19) & 0xFF
- Status request: `AA AA 12 A0 0A 0A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1A`
- Real reply: `AA AA 12 00 0A 0A 00 0C 05 00 D4 00 00 00 00 19 05 00 00 00 7D`
  = power on, heat, 21 °C, fan auto, display/health/aux on, indoor 25.5 °C
- Command = last reply with byte3 = `01`, fields changed, checksum recomputed. AC replies with the new state.
  Real: setpoint 22 → `AA AA 12 01 0A 0A 00 0C 06 00 D4 00 00 00 00 19 05 00 00 00 7F`

## Field map (payload byte N = frame byte N+4; bit 0 = MSB)
| Field | Byte.bit (len) | Values |
|---|---|---|
| turbo | 3.0 | bool |
| fan speed | 3.1 (3) | 0 auto, 1–6 |
| power | 3.4 | bool |
| mode | 3.5 (3) | 0 auto, 1 cool, 2 dry, 3 fan, 4 heat |
| quiet | 4.1 | bool |
| °F scale | 4.2 | bool |
| setpoint | 4.3 (5) | °C − 16 |
| horizontal swing | 5.0 (4) | 0 stop, 1 swing, 2–6 positions, 12 left-right, 13 wide |
| vertical swing | 5.4 (4) | 0 stop, 1 swing, 2–6 positions |
| display / health / timer / aux(dry) / sleep / eco | 6.0 / 6.1 / 6.2 / 6.3 / 6.6 / 6.7 | bool |
| off-timer enable / minutes hi | 7.0 / 7.1 (3) | minutes **remaining**; the AC counts down every minute and powers off at 0 (verified) |
| on-timer enable / minutes hi | 7.4 / 7.5 (3) | minutes = hi<<8 \| byte 9 |
| indoor temp | byte 11 + byte 12 / 10 | °C |

Credit: field layout first published by ki0ki0/homeeasylib (GPLv3); this repo is an independent implementation.
