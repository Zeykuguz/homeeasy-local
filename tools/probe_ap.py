"""Probe the AC Wi-Fi module while it is in AP/config mode (display 'o2', hotspot SMART_xxxx).

Usage: python probe_ap.py [ap_ip]   (default: Wi-Fi default gateway)
Writes a log to probe_ap_<time>.log next to this script.
"""
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import heproto as hp

TCP_PORTS = [23, 80, 81, 443, 1883, 5000, 6000, 6001, 6002, 6666, 7000, 8000, 8080, 8266, 8888, 8899,
             9000, 9999, 10000, 12414, 12415, 12416, 12417, 48899, 49999]
UDP_PROBES = {
    12414: [hp.DISCOVERY_REQUEST],
    12415: [hp.DISCOVERY_REQUEST],
    12416: [hp.DISCOVERY_REQUEST, hp.STATUS_REQUEST],
    48899: [b"HF-A11ASSISTHREAD", b"www.usr.cn", b"smartlinkfind"],
    7000: [b'{"t":"scan"}'],
    6666: [b"\x00"],
    8266: [b"\x00"],
}

LOG = Path(__file__).with_name(f"probe_ap_{time.strftime('%Y%m%d_%H%M%S')}.log")


def log(*a):
    line = " ".join(str(x) for x in a)
    print(line)
    with LOG.open("a", encoding="utf-8") as f:
        f.write(line + "\n")


def wifi_gateway() -> str | None:
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          "(Get-NetIPConfiguration -InterfaceAlias 'Wi-Fi').IPv4DefaultGateway.NextHop"],
                         capture_output=True, text=True).stdout.strip()
    return out or None


def wifi_ip() -> str | None:
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          "(Get-NetIPAddress -InterfaceAlias 'Wi-Fi' -AddressFamily IPv4).IPAddress"],
                         capture_output=True, text=True).stdout.strip()
    return out or None


def tcp_scan(ip):
    open_ports = []
    for p in TCP_PORTS:
        s = socket.socket()
        s.settimeout(0.8)
        try:
            if s.connect_ex((ip, p)) == 0:
                open_ports.append(p)
                s.settimeout(1.5)
                try:
                    banner = s.recv(256)
                    log(f"  TCP {p} OPEN banner={banner!r}")
                except socket.timeout:
                    log(f"  TCP {p} OPEN (no banner)")
        finally:
            s.close()
    return open_ports


def udp_probe(ip, local_ip):
    for port, payloads in UDP_PROBES.items():
        for payload in payloads:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            try:
                s.bind((local_ip or "", 0))
                s.settimeout(1.5)
                s.sendto(payload, (ip, port))
                data, src = s.recvfrom(2048)
                log(f"  UDP {port} <- {payload!r}: from {src} {data.hex()} {data!r}")
            except (socket.timeout, ConnectionResetError):
                pass
            except OSError as e:
                log(f"  UDP {port} error {e}")
            finally:
                s.close()


def http_probe(ip, port):
    for path in ["/", "/index.html", "/config", "/wifi", "/setting.html", "/EN/home.html"]:
        try:
            req = urllib.request.Request(f"http://{ip}:{port}{path}")
            with urllib.request.urlopen(req, timeout=3) as r:
                body = r.read(4000)
                log(f"  HTTP {port}{path} {r.status} headers={dict(r.headers)}\n{body[:2000]!r}")
        except urllib.error.HTTPError as e:
            log(f"  HTTP {port}{path} -> {e.code} {dict(e.headers)}")
        except Exception as e:
            log(f"  HTTP {port}{path} -> {e}")


def main():
    ip = sys.argv[1] if len(sys.argv) > 1 else wifi_gateway()
    local_ip = wifi_ip()
    log(f"== probe {ip} from {local_ip} at {time.ctime()}")
    if not ip:
        log("No gateway found; pass AP IP explicitly")
        return 1

    log("-- TCP scan")
    open_ports = tcp_scan(ip)
    log(f"open: {open_ports}")

    log("-- UDP probes")
    udp_probe(ip, local_ip)

    log("-- HomeEasy discovery (broadcast)")
    for dev in hp.discover(3, "255.255.255.255"):
        log(f"  {dev}")

    if hp.CONTROL_PORT in open_ports:
        log("-- HomeEasy status on TCP 12416")
        try:
            with hp.Client(ip) as cl:
                st = cl.status()
                log(f"  raw={st.raw.hex()}\n  {st}")
        except Exception as e:
            log(f"  status failed: {e}")

    for p in (80, 81, 8080, 8000):
        if p in open_ports:
            log(f"-- HTTP {p}")
            http_probe(ip, p)

    log(f"log saved to {LOG}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
