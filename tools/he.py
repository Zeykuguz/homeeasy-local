"""HomeEasy local CLI.

  python he.py discover
  python he.py status <ip>
  python he.py set <ip> power=on mode=cool temp=22 fan=auto|1-6|quiet|turbo hswing=1 vswing=1 eco=off
"""
import argparse
import sys

import heproto as hp

BOOL = {"on": True, "off": False, "1": True, "0": False, "true": True, "false": False}


def apply(st: hp.State, key: str, val: str) -> None:
    v = val.lower()
    if key == "power":
        st.power = BOOL[v]
    elif key == "mode":
        st.mode = hp.Mode[v.upper()]
    elif key in ("temp", "setpoint"):
        st.setpoint = int(v)
    elif key == "fan":
        st.turbo = v == "turbo"
        st.quiet = v == "quiet"
        if v not in ("turbo", "quiet"):
            st.fan = 0 if v == "auto" else int(v)
    elif key in ("hswing", "vswing"):
        setattr(st, key, int(v))
    elif key in ("eco", "sleep", "display", "health", "aux", "timer", "off_timer", "on_timer"):
        setattr(st, key, BOOL[v])
    elif key == "off_minutes":
        st.off_minutes = int(v)
    else:
        raise KeyError(key)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("discover")
    d.add_argument("--target", default="255.255.255.255")
    d.add_argument("--timeout", type=float, default=4)
    s = sub.add_parser("status")
    s.add_argument("ip")
    c = sub.add_parser("set")
    c.add_argument("ip")
    c.add_argument("pairs", nargs="+")
    a = p.parse_args()

    if a.cmd == "discover":
        devs = hp.discover(a.timeout, a.target)
        for dev in devs:
            print(dev)
        if not devs:
            print("no devices found")
        return 0 if devs else 1

    with hp.Client(a.ip) as cl:
        st = cl.status()
        print("raw:", st.raw.hex())
        if a.cmd == "set":
            for pair in a.pairs:
                k, _, v = pair.partition("=")
                apply(st, k.lower(), v)
            print("cmd:", st.command().hex())
            st = cl.send(st)
        print(st)
    return 0


if __name__ == "__main__":
    sys.exit(main())
