#!/usr/bin/env python3
"""Resolve DorkNet.Quest hook targets for one Rec Room Quest build.

Reads targets/spec.json, finds every target in an Il2CppDumper dump.cs by its
class header and method signature, and verifies each against the build's
libil2cpp.so:

  * exactly one method matches the signature,
  * the method body is at least 20 bytes (the inline hook rewrites the first
    five instructions),
  * the first instructions are disassembled (llvm-objdump, when provided) so
    the prologue can be eyeballed before a build.

Output is a per-build table (targets/<versionName>-<versionCode>.json) holding
each target's RVA, body length and an FNV-1a fingerprint of the first 20 code
bytes. The hook library refuses to arm a target whose fingerprint does not
match the running libil2cpp.so, so a table can never be applied to the wrong
build. Only RVAs, lengths and hashes are written -- no game code or metadata.

Usage:
  python resolve_targets.py --dump dump.cs --il2cpp libil2cpp.so \
      --version-name 20230317 --version-code 2128210 \
      [--objdump path/to/llvm-objdump] [--out targets/20230317-2128210.json]
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

HOOK_BYTES = 20
RVA_RE = re.compile(r"^\s*// RVA: 0x([0-9A-Fa-f]+) Offset: 0x([0-9A-Fa-f]+)")
TYPE_RE = re.compile(r"^(public|internal|private|protected)\b.*\b(class|struct|interface|enum)\b ")


def fnv1a64(data):
    h = 0xCBF29CE484222325
    for b in data:
        h ^= b
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return h


def sha256(path):
    d = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            d.update(chunk)
    return d.hexdigest()


def scan_dump(dump, spec):
    """One pass over dump.cs collecting (rva, offset) for each spec match and
    for the NEXT method after it (to bound the body length)."""
    wanted = {t["id"]: (t["class"], t["method"]) for t in spec["targets"]}
    hits = {}          # id -> {rva, offset, ...}
    all_rvas = set()   # every method RVA, for next-greater length bounding
    rva = off = None
    cur_class = None
    lines = dump.read_text(encoding="utf-8", errors="replace").splitlines()
    for ln in lines:
        m = RVA_RE.match(ln)
        if m:
            rva, off = int(m.group(1), 16), int(m.group(2), 16)
            if rva:
                all_rvas.add(rva)
            continue
        if TYPE_RE.match(ln):
            # dump.cs class headers end with "// TypeDefIndex: N"; drop it.
            cur_class = ln.split("// TypeDefIndex")[0].rstrip()
            continue
        # dump.cs method lines end with " { }" (or "; " for abstract); drop it.
        body = ln.strip()
        body = body.split("// RVA")[0].rstrip()
        if body.endswith("{ }"):
            body = body[:-3].rstrip()
        elif body.endswith(";"):
            body = body[:-1].rstrip()
        for tid, (cls, meth) in wanted.items():
            if tid in hits:
                continue
            # Class header may carry a base/interface list ("X : Base, IFoo").
            # Match the exact class, or that class followed by " : <bases>",
            # but never a nested "X.Y" (which continues with "." not " ").
            spec_class = cls.rstrip()
            class_ok = cur_class is not None and (
                cur_class == spec_class or cur_class.startswith(spec_class + " "))
            if class_ok and body == meth.rstrip():
                if rva is None:
                    raise SystemExit(f"{tid}: matched signature with no preceding RVA line")
                hits[tid] = {"rva": rva, "offset": off, "signature": meth,
                             "class": cls.rstrip()}
    # Bound each body by the next-greater RVA anywhere in the dump: dump.cs
    # lists methods in declaration order, so the following *line* is not the
    # following *function* in memory.
    ordered = sorted(all_rvas)
    import bisect
    for h in hits.values():
        i = bisect.bisect_right(ordered, h["rva"])
        h["end_rva"] = ordered[i] if i < len(ordered) else None
    return hits


def objdump_prologue(objdump, il2cpp, rva, count=6):
    try:
        out = subprocess.run(
            [objdump, "-d", f"--start-address={rva:#x}",
             f"--stop-address={rva + count * 4:#x}", str(il2cpp)],
            capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        return f"(objdump failed: {e})"
    return "\n".join(l for l in out.splitlines() if ":\t" in l)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True, type=Path)
    ap.add_argument("--il2cpp", required=True, type=Path)
    ap.add_argument("--version-name", required=True)
    ap.add_argument("--version-code", required=True)
    ap.add_argument("--objdump")
    ap.add_argument("--spec", type=Path,
                    default=Path(__file__).resolve().parent.parent / "targets" / "spec.json")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    spec = json.loads(args.spec.read_text(encoding="utf-8"))
    hits = scan_dump(args.dump, spec)

    code = args.il2cpp.read_bytes()
    missing = [t["id"] for t in spec["targets"] if t["id"] not in hits]
    if missing:
        raise SystemExit("targets not found in dump: " + ", ".join(missing))

    entries = {}
    ok = True
    for t in spec["targets"]:
        tid = t["id"]
        h = hits[tid]
        rva = h["rva"]
        # In this ELF Offset == VA == RVA, so the file offset is the RVA.
        length = (h["end_rva"] - rva) if h["end_rva"] else None
        fp = fnv1a64(code[rva:rva + HOOK_BYTES])
        short = length is not None and length < HOOK_BYTES
        if short:
            ok = False
        entries[tid] = {
            "rva": rva,
            "length": length,
            "fingerprint": f"{fp:#018x}",
            "why": t["why"],
        }
        flag = "  TOO SHORT" if short else ""
        print(f"{tid:28s} rva={rva:#010x} len={length}{flag}")
        if args.objdump:
            print(objdump_prologue(args.objdump, args.il2cpp, rva))

    table = {
        "versionName": args.version_name,
        "versionCode": args.version_code,
        "il2cppSha256": sha256(args.il2cpp),
        "hookBytes": HOOK_BYTES,
        "targets": entries,
    }
    out = args.out or (args.spec.parent / f"{args.version_name}-{args.version_code}.json")
    out.write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")
    print(f"\nwrote {out}")
    if not ok:
        raise SystemExit("one or more targets are shorter than the hook patch; refuse to build")


if __name__ == "__main__":
    main()
