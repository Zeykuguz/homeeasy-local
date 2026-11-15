"""HomeEasy local LAN protocol (Cooper&Hunter / Adentech Wi-Fi modules).

Clean-room implementation based on the protocol description in docs/PLAN.md.
Payload byte N lives at frame offset N+4; bit 0 is the MSB of a byte.
"""
from __future__ import annotations

import socket
import time
from dataclasses import dataclass, field
from enum import IntEnum

DISCOVERY_PORT = 12414
DISCOVERY_LOCAL_PORT = 2415
CONTROL_PORT = 12416
FRAME_LEN = 21

DISCOVERY_REQUEST = bytes.fromhex("AAAA0602FFFFFF0059")
STATUS_REQUEST = bytes.fromhex("AAAA12A00A0A00000000000000000000000000001A")


class Mode(IntEnum):
    AUTO = 0
    COOL = 1
    DRY = 2
    FAN = 3
    HEAT = 4


class HSwing(IntEnum):
    STOP = 0
    SWING = 1
    LEFT = 2
    LEFT_CENTER = 3
    CENTER = 4
    RIGHT_CENTER = 5
    RIGHT = 6
    LEFT_RIGHT = 12
    SWING_WIDE = 13


class VSwing(IntEnum):
    STOP = 0
    SWING = 1
    TOP = 2
    TOP_CENTER = 3
    CENTER = 4
    BOTTOM_CENTER = 5
    BOTTOM = 6


def checksum(data: bytes) -> int:
    return sum(data[:FRAME_LEN - 1]) & 0xFF


def _get(buf: bytes, byte: int, bit: int, count: int = 1) -> int:
    val = 0
    for i in range(count):
        pos = (byte + 4) * 8 + bit + i
        val = (val << 1) | ((buf[pos // 8] >> (7 - pos % 8)) & 1)
    return val


def _set(buf: bytearray, byte: int, bit: int, count: int, value: int) -> None:
    for i in range(count):
        pos = (byte + 4) * 8 + bit + i
        mask = 1 << (7 - pos % 8)
        if (value >> (count - 1 - i)) & 1:
            buf[pos // 8] |= mask
        else:
            buf[pos // 8] &= ~mask & 0xFF


@dataclass
class State:
    raw: bytearray = field(default_factory=lambda: bytearray(FRAME_LEN))

    @classmethod
    def parse(cls, frame: bytes) -> "State":
        if len(frame) != FRAME_LEN or frame[0:2] != b"\xAA\xAA":
            raise ValueError(f"bad frame: {frame.hex()}")
        return cls(bytearray(frame))

    def _b(self, byte, bit, count=1):
        return _get(self.raw, byte, bit, count)

    def _s(self, byte, bit, count, value):
        _set(self.raw, byte, bit, count, int(value))

    power = property(lambda s: bool(s._b(3, 4)), lambda s, v: s._s(3, 4, 1, v))
    mode = property(lambda s: Mode(s._b(3, 5, 3)), lambda s, v: s._s(3, 5, 3, v))
    fan = property(lambda s: s._b(3, 1, 3), lambda s, v: s._s(3, 1, 3, v))
    turbo = property(lambda s: bool(s._b(3, 0)), lambda s, v: s._s(3, 0, 1, v))
    quiet = property(lambda s: bool(s._b(4, 1)), lambda s, v: s._s(4, 1, 1, v))
    fahrenheit = property(lambda s: bool(s._b(4, 2)))
    hswing = property(lambda s: s._b(5, 0, 4), lambda s, v: s._s(5, 0, 4, v))
    vswing = property(lambda s: s._b(5, 4, 4), lambda s, v: s._s(5, 4, 4, v))
    display = property(lambda s: bool(s._b(6, 0)), lambda s, v: s._s(6, 0, 1, v))
    health = property(lambda s: bool(s._b(6, 1)), lambda s, v: s._s(6, 1, 1, v))
    aux = property(lambda s: bool(s._b(6, 3)), lambda s, v: s._s(6, 3, 1, v))
    sleep = property(lambda s: bool(s._b(6, 6)), lambda s, v: s._s(6, 6, 1, v))
    eco = property(lambda s: bool(s._b(6, 7)), lambda s, v: s._s(6, 7, 1, v))
    timer = property(lambda s: bool(s._b(6, 2)), lambda s, v: s._s(6, 2, 1, v))
    off_timer = property(lambda s: bool(s._b(7, 0)), lambda s, v: s._s(7, 0, 1, v))
    on_timer = property(lambda s: bool(s._b(7, 4)), lambda s, v: s._s(7, 4, 1, v))

    @property
    def off_minutes(self) -> int:
        return (self._b(7, 1, 3) << 8) | self._b(9, 0, 8)

    @off_minutes.setter
    def off_minutes(self, minutes: int) -> None:
        self._s(7, 1, 3, minutes >> 8)
        self._s(9, 0, 8, minutes & 0xFF)

    @property
    def setpoint(self) -> int:
        v = self._b(4, 3, 5)
        if v >= 16:
            v -= 16
        return v + 16

    @setpoint.setter
    def setpoint(self, celsius: int) -> None:
        if not 16 <= celsius <= 30:
            raise ValueError("setpoint must be 16..30 °C")
        self._s(4, 3, 5, celsius - 16)

    @property
    def indoor(self) -> float:
        return self._b(11, 0, 8) + self._b(12, 0, 8) / 10

    def command(self) -> bytes:
        out = bytearray(self.raw)
        out[3] = 0x01
        out[-1] = checksum(out)
        return bytes(out)

    def __str__(self) -> str:
        fan = "turbo" if self.turbo else "quiet" if self.quiet else ("auto" if self.fan == 0 else str(self.fan))
        return (f"power={'on' if self.power else 'off'} mode={self.mode.name.lower()} set={self.setpoint}°C "
                f"indoor={self.indoor:.1f}°C fan={fan} hswing={self.hswing} vswing={self.vswing} "
                f"eco={self.eco} sleep={self.sleep} display={self.display} health={self.health} aux={self.aux} "
                f"timer={self.timer} off_timer={self.off_timer} off_min={self.off_minutes} on_timer={self.on_timer}")


def discover(timeout: float = 4.0, target: str = "255.255.255.255") -> list[dict]:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.bind(("", DISCOVERY_LOCAL_PORT))
    sock.settimeout(0.5)
    found: dict[str, dict] = {}
    end = time.time() + timeout
    next_send = 0.0
    try:
        while time.time() < end:
            if time.time() >= next_send:
                sock.sendto(DISCOVERY_REQUEST, (target, DISCOVERY_PORT))
                next_send = time.time() + 1.0
            try:
                data, (ip, _) = sock.recvfrom(1024)
            except socket.timeout:
                continue
            if len(data) >= 13 and data[2] == 0x0C and data[3] == 0x03 and ip not in found:
                found[ip] = {"ip": ip, "mac": data[7:13].hex(), "industry": data[4],
                             "vendor": data[5], "type": data[6], "raw": data.hex()}
    finally:
        sock.close()
    return list(found.values())


class Client:
    def __init__(self, host: str, port: int = CONTROL_PORT, timeout: float = 5.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.settimeout(timeout)

    def close(self) -> None:
        self.sock.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    def _read_frame(self) -> bytes:
        buf = b""
        while True:
            chunk = self.sock.recv(256)
            if not chunk:
                raise ConnectionError("connection closed by AC")
            buf += chunk
            i = buf.find(b"\xAA\xAA")
            if i >= 0 and len(buf) - i >= FRAME_LEN:
                return buf[i:i + FRAME_LEN]

    def status(self) -> State:
        self.sock.sendall(STATUS_REQUEST)
        return State.parse(self._read_frame())

    def send(self, state: State) -> State:
        self.sock.sendall(state.command())
        try:
            return State.parse(self._read_frame())
        except socket.timeout:
            return self.status()
