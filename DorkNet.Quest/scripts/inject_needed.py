#!/usr/bin/env python3
"""Add a DT_NEEDED entry to libmain.so so the Android loader pulls in
libdorknet.so before Unity starts.

libmain.so is tiny (the Unity bootstrap shim) and lists libil2cpp etc. as
DT_NEEDED; adding one more makes the loader map our hook library first. This is
the same mechanism RecFlare uses on Quest. Idempotent: a second run is a no-op.

Usage: inject_needed.py <libmain.so> <needed-soname>   (e.g. libdorknet.so)
"""
import sys

import lief


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: inject_needed.py <libmain.so> <soname>")
    path, soname = sys.argv[1], sys.argv[2]
    binary = lief.ELF.parse(path)
    if binary is None:
        raise SystemExit(f"could not parse {path}")

    existing = [e.name for e in binary.dynamic_entries
                if e.tag == lief.ELF.DynamicEntry.TAG.NEEDED]
    if soname in existing:
        print(f"{soname} already a DT_NEEDED of {path}; nothing to do")
        return

    binary.add_library(soname)
    binary.write(path)
    print(f"added DT_NEEDED {soname} to {path}")

    check = lief.ELF.parse(path)
    names = [e.name for e in check.dynamic_entries
             if e.tag == lief.ELF.DynamicEntry.TAG.NEEDED]
    if soname not in names:
        raise SystemExit("injection did not stick")
    print("verified:", ", ".join(names))


if __name__ == "__main__":
    main()
