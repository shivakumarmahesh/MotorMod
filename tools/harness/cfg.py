#!/usr/bin/env python3
"""Edit the mod's config in the game folder for quick harness iteration.

    ./.venv/bin/python tools/harness/cfg.py show <bike>
    ./.venv/bin/python tools/harness/cfg.py set <bike> Key=value [Key=value ...]
    ./.venv/bin/python tools/harness/cfg.py reset            # delete the file; the game regenerates it from the mod's defaults at the next launch

The harness reloads the file at the start of every run, so "set" then shoot.py is the loop.
"""
import re, sys
from pathlib import Path

C = Path.home() / "Library/Application Support/Steam/steamapps/common/Valheim/BepInEx/config/com.shiva.valheim.motormod.cfg"


def section(text, name):
    m = re.search(rf"(?ms)^\[{re.escape(name)}\]\n(.*?)(?=^\[|\Z)", text)
    return m


def main(argv):
    if not argv:
        sys.exit(__doc__)
    cmd = argv[0]
    if cmd == "reset":
        if C.exists():
            C.unlink(); print("config deleted; the next launch regenerates it")
        return
    text = C.read_text()
    bike = argv[1]
    m = section(text, bike)
    if not m:
        sys.exit(f"no [{bike}] section in {C}")
    body = m.group(1)
    if cmd == "show":
        print("\n".join(l for l in body.splitlines() if l and not l.startswith("#")))
        return
    if cmd == "set":
        for kv in argv[2:]:
            k, v = kv.split("=", 1)
            body, n = re.subn(rf"(?m)^{re.escape(k)} = .*$", f"{k} = {v}", body)
            if n != 1:
                sys.exit(f"key {k} not found once in [{bike}] ({n})")
            print(f"[{bike}] {k} = {v}")
        text = text[: m.start(1)] + body + text[m.end(1):]
        C.write_text(text)
        return
    sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
