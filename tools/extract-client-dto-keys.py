#!/usr/bin/env python3
"""Extract every generated JSON reader's key set from the 2023-03-21 ISIL dump.

Why this exists
---------------
When one of our responses carries the wrong keys the client does NOT report a
bad request. It answers HTTP 200, the client's generated reader throws while
deserialising, and the failure surfaces much later as a bare "Failed to copy
room" with no URL attached. Neither side logs the payload, so the only way to
know what the client actually wants is to read the readers out of the binary.

Cpp2IL emits each generated reader as a `.ctor` that registers its property
names as string literals, three spellings per field:

    262 Move rcx, "DataBlob"
    273 Move rcx, "dataBlob"
    281 Move rcx, "datablob"

The PascalCase spelling is the wire name; the other two are the reader's
case-insensitive aliases. A run of >= MIN_FIELDS such triples is a DTO, and the
registration order is the field order.

Output is a JSON catalogue: {typeName: [field, ...]} in registration order, for
the parity test to assert our responses against.

    python tools/extract-client-dto-keys.py \
        C:/tmp/recroom-2023-03-21-isil/IsilDump \
        DorkNet.Server.Tests/data/client-dto-keys-2023.json
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

# A type with only a couple of fields is usually a wrapper or a false positive
# (enum name tables register short literal runs too). Real wire DTOs are wider.
MIN_FIELDS = 4

LITERAL = re.compile(r'Move \w+, "([A-Za-z][A-Za-z0-9_]*)"')


def fields_in(text: str) -> list[str]:
    """Ordered PascalCase field names, de-duplicated.

    A field is only counted when its lowercase alias also appears, which is what
    separates a reader's property registrations from incidental string literals
    (log messages, enum names) that happen to be capitalised.
    """
    literals = LITERAL.findall(text)
    seen_lower = {lit.lower() for lit in literals}

    fields: list[str] = []
    taken: set[str] = set()
    for lit in literals:
        if not lit[0].isupper():
            continue
        key = lit.lower()
        if key in taken:
            continue
        # The alias spellings are what mark this as a registered property.
        if key not in seen_lower or literals.count(lit) == 0:
            continue
        camel = lit[0].lower() + lit[1:]
        if camel not in literals and key not in literals:
            continue
        taken.add(key)
        fields.append(lit)
    return fields


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    dump_root, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    if not dump_root.is_dir():
        print(f"not a directory: {dump_root}", file=sys.stderr)
        return 1

    catalogue: dict[str, list[str]] = {}
    for path in sorted(dump_root.rglob("*.txt")):
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        if '"' not in text:
            continue
        fields = fields_in(text)
        if len(fields) >= MIN_FIELDS:
            catalogue[path.stem] = fields

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(catalogue, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"{len(catalogue)} DTO reader(s) -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
