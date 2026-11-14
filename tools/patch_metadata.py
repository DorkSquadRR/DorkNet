#!/usr/bin/env python3
"""Point a Rec Room 2023.03.21 PC client at a DorkNet server by rewriting
string literals in its IL2CPP global-metadata.dat.

The client hardcodes these values as C# string literals:

  NameServerUrl          https://ns.rec.net     where the client asks for every
                                                other service URL
  AuthUrl                https://auth.rec.net   the login host (not in the ns
                                                response)
  PhotonAppId            9372aa8d-...           Photon Realtime AppId
  PhotonVoiceAppId       e93ae440-...           Photon Voice AppId
  ImageSigningPublicKey  X07yXkxa...            RSA modulus (base64) the client
                                                checks image Content-Signatures
                                                against

ClientMod alone can't redirect the name server lookup, so a client without
this patch keeps asking ns.rec.net and fails with "RecNet name server query
failed (error code: summer)".

The patch doesn't overwrite a literal's bytes in place. It appends the new
string to the end of the file and repoints that literal's table entry
(length + data index) at it, so a replacement can be any length and literal
data that other entries share is never clobbered. Only literals whose whole
value equals a known original are touched, and the patch is idempotent.

The two Photon AppIds are also stored as field/parameter default values
(each twice). Those are overwritten in place; a GUID always has the same
length, so nothing moves.

Values come from a JSON file. Leave a key out (or blank) to keep the client's
own value:

    {
      "NameServerUrl": "https://ns.example.com",
      "AuthUrl": "https://auth.example.com",
      "PhotonAppId": "00000000-0000-0000-0000-000000000000",
      "PhotonVoiceAppId": "00000000-0000-0000-0000-000000000000",
      "ImageSigningPublicKey": "<base64 RSA modulus, or a PEM public key>"
    }

The ImageSigningPublicKey must be the public half of the server's
ImageSigning:PrivateKeyPem.

    py -3 tools/patch_metadata.py apply RecRoom_Data/il2cpp_data/Metadata/global-metadata.dat \\
        --config dorknet-client-metadata.json
    py -3 tools/patch_metadata.py show path/to/patched/global-metadata.dat \\
        --stock path/to/stock/global-metadata.dat --output current.json

`show` writes the values an already-patched metadata file holds, in the same
JSON shape, so a working client's values can become a config file. It needs
the same build's stock metadata to find the literals.
"""
from __future__ import annotations

import argparse
import base64
import json
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import NoReturn
from urllib.parse import urlsplit

SANITY = 0xFAB11BAF
# v24 to v28 share what this script reads: the header's string-literal table
# (offset, size) and literal data blob (offset, size), each table entry being
# {uint32 length, int32 dataIndex} with dataIndex relative to the blob, and
# default-value strings stored as an int32 length + UTF-8 bytes. (v29 moved
# those lengths to a compressed encoding.) Rec Room 2023.03.21 is v27.
SUPPORTED_VERSIONS = range(24, 29)
ENTRY = struct.Struct("<Ii")
DEFAULTS_PAIR = 8
METADATA_RELPATH = "RecRoom_Data/il2cpp_data/Metadata/global-metadata.dat"


@dataclass(frozen=True)
class Slot:
    key: str
    original: str
    kind: str  # "url" | "guid" | "modulus"


# The 2023.03.21 client's own values (checked against its stock metadata).
SLOTS = (
    Slot("NameServerUrl", "https://ns.rec.net", "url"),
    Slot("AuthUrl", "https://auth.rec.net", "url"),
    Slot("PhotonAppId", "9372aa8d-d3f4-44a0-986d-419e145a2b83", "guid"),
    Slot("PhotonVoiceAppId", "e93ae440-f238-4b6c-848f-1df89faf14f5", "guid"),
    Slot("ImageSigningPublicKey",
         "X07yXkxaaLcZ1wVXfkWjgFkkqdoLhDFm0GPODsF+Q47pSUlbLvtXGqStnyEJEIrQmgDiicAvCdGRq4lovr2l5sIPMaoy"
         "izsbVHBdwLUrCsji0RvSBnmvN+8KqQ8STnB4DP4pAsPilfD35def4WuX/xMCXB5+hQUVhv27HPV8Dj9XzHuJAijIM9Uw"
         "DZmvUcECyiO4wv+TaZi2+ELBtaLCQR8Gm1ZPeDEwP62Ch6MJy0jx5pkvvD0KdF9Wye+3/Wx31Zn/Trdo9HL4sGFWPDM9"
         "H9kQhZd5wkTHuxpwGIIhlzIwvY2/pBGdZKP6fi1D2jROEmVkBDyhmYY9nO+s3/bndQ==",
         "modulus"),
)
SLOT_BY_KEY = {s.key: s for s in SLOTS}
GUID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")


class PatchError(Exception):
    pass


def fail(msg: str) -> NoReturn:
    raise SystemExit(f"error: {msg}")


# ---- metadata -------------------------------------------------------------

class Metadata:
    def __init__(self, data: bytes) -> None:
        if len(data) < 24:
            raise PatchError("file is too small to be IL2CPP metadata")
        sanity, version = struct.unpack_from("<Ii", data, 0)
        if sanity != SANITY:
            raise PatchError("not an IL2CPP global-metadata.dat (bad magic)")
        if version not in SUPPORTED_VERSIONS:
            raise PatchError(f"unsupported metadata version {version}")
        self.data = bytearray(data)
        self.version = version
        (self.table_off, self.table_size,
         self.blob_off, self.blob_size) = struct.unpack_from("<iiii", data, 8)
        # Header pair 8: fieldAndParameterDefaultValueData, the constant and
        # default values of fields and parameters.
        self.defaults_off, self.defaults_size = struct.unpack_from("<ii", data, 8 + 8 * DEFAULTS_PAIR)
        if (self.table_size % ENTRY.size or self.table_off < 0 or self.blob_off < 0
                or self.table_off + self.table_size > len(data)
                or self.blob_off + self.blob_size > len(data)
                or self.defaults_off < 0 or self.defaults_size < 0
                or self.defaults_off + self.defaults_size > len(data)):
            raise PatchError("metadata section table is out of bounds")

    @property
    def count(self) -> int:
        return self.table_size // ENTRY.size

    def entry(self, i: int) -> tuple[int, int]:
        return ENTRY.unpack_from(self.data, self.table_off + i * ENTRY.size)

    def literal(self, i: int) -> bytes:
        length, index = self.entry(i)
        start = self.blob_off + index
        if index < 0 or start + length > len(self.data):
            raise PatchError(f"string literal {i} points outside the file")
        return bytes(self.data[start:start + length])

    def find(self, value: str) -> list[int]:
        """Indices of literals whose whole value is `value`."""
        want = value.encode("utf-8")
        hits = []
        for i in range(self.count):
            length, _ = self.entry(i)
            if length == len(want) and self.literal(i) == want:
                hits.append(i)
        return hits

    def find_defaults(self, value: str) -> list[int]:
        """File offsets of `value` stored as a default/constant string: an
        int32 byte length followed by the UTF-8 bytes (metadata < v29)."""
        raw = value.encode("utf-8")
        prefix = struct.pack("<i", len(raw)) + raw
        start, end = self.defaults_off, self.defaults_off + self.defaults_size
        hits, pos = [], self.data.find(prefix, start, end)
        while pos != -1:
            hits.append(pos + 4)
            pos = self.data.find(prefix, pos + 1, end)
        return hits

    def overwrite(self, offsets: list[int], old: str, new: str) -> None:
        raw = new.encode("utf-8")
        if len(raw) != len(old.encode("utf-8")):
            raise PatchError("a default-value string can only be replaced by one of the same length")
        for off in offsets:
            self.data[off:off + len(raw)] = raw

    def repoint(self, indices: list[int], value: str) -> None:
        raw = value.encode("utf-8")
        # Literal data is read through the int32 index; keep every appended
        # string 4-byte aligned, like il2cpp's own sections.
        pad = (-len(self.data)) % 4
        self.data.extend(b"\0" * pad)
        index = len(self.data) - self.blob_off
        if index > 0x7FFFFFFF:
            raise PatchError("metadata would exceed the int32 data index range")
        self.data.extend(raw)
        for i in indices:
            ENTRY.pack_into(self.data, self.table_off + i * ENTRY.size, len(raw), index)


# ---- values ---------------------------------------------------------------

def _der_read(buf: bytes, pos: int) -> tuple[int, bytes, int]:
    """Read one DER TLV at pos -> (tag, value, next_pos)."""
    if pos + 2 > len(buf):
        raise PatchError("truncated DER")
    tag, length = buf[pos], buf[pos + 1]
    pos += 2
    if length & 0x80:
        n = length & 0x7F
        if n == 0 or n > 4 or pos + n > len(buf):
            raise PatchError("bad DER length")
        length = int.from_bytes(buf[pos:pos + n], "big")
        pos += n
    if pos + length > len(buf):
        raise PatchError("truncated DER")
    return tag, buf[pos:pos + length], pos + length


def _rsa_modulus_from_pem(pem: str) -> bytes:
    m = re.search(r"-----BEGIN ([A-Z ]+)-----(.*?)-----END \1-----", pem, re.S)
    if not m:
        raise PatchError("no PEM block found")
    label, der = m.group(1), base64.b64decode("".join(m.group(2).split()))
    if "PRIVATE" in label:
        raise PatchError("give the PUBLIC key; a private key doesn't belong in the client config")
    tag, seq, _ = _der_read(der, 0)
    if tag != 0x30:
        raise PatchError("PEM is not a DER SEQUENCE")
    if label == "PUBLIC KEY":  # SubjectPublicKeyInfo: SEQ { algId SEQ, BIT STRING { RSAPublicKey } }
        _, _, pos = _der_read(seq, 0)
        tag, bits, _ = _der_read(seq, pos)
        if tag != 0x03 or not bits or bits[0] != 0:
            raise PatchError("unexpected SubjectPublicKeyInfo layout")
        tag, seq, _ = _der_read(bits[1:], 0)
        if tag != 0x30:
            raise PatchError("unexpected RSAPublicKey layout")
    elif label != "RSA PUBLIC KEY":
        raise PatchError(f"unsupported PEM type '{label}'")
    tag, modulus, _ = _der_read(seq, 0)  # RSAPublicKey: SEQ { modulus INTEGER, exponent INTEGER }
    if tag != 0x02:
        raise PatchError("RSA modulus is not an INTEGER")
    return modulus.lstrip(b"\0")


def normalize(slot: Slot, raw: object) -> str:
    if not isinstance(raw, str):
        raise PatchError(f"{slot.key} must be a string")
    value = raw.strip()
    if slot.kind == "url":
        parts = urlsplit(value)
        if parts.scheme not in ("https", "http") or not parts.hostname:
            raise PatchError(f"{slot.key} must be an absolute http(s) URL")
        if parts.path not in ("", "/") or parts.query or parts.fragment:
            raise PatchError(f"{slot.key} must be scheme + host only, like {slot.original}")
        return value.rstrip("/")
    if slot.kind == "guid":
        if not GUID_RE.match(value):
            raise PatchError(f"{slot.key} must be a GUID")
        return value.lower()
    # modulus: base64 of the raw modulus, or a PEM public key
    if "-----BEGIN" in value:
        modulus = _rsa_modulus_from_pem(value)
    else:
        try:
            modulus = base64.b64decode(value, validate=True)
        except ValueError as ex:
            raise PatchError(f"{slot.key} is not valid base64") from ex
        modulus = modulus.lstrip(b"\0")
    expected = len(base64.b64decode(slot.original))
    if len(modulus) != expected:
        raise PatchError(f"{slot.key} must be a {expected * 8}-bit RSA key (got {len(modulus) * 8} bits)")
    return base64.b64encode(modulus).decode("ascii")


def load_config(path: Path) -> dict[str, str]:
    try:
        raw = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as ex:
        raise PatchError(f"can't read {path}: {ex}") from ex
    if not isinstance(raw, dict):
        raise PatchError(f"{path} must hold a JSON object")
    unknown = sorted(set(raw) - set(SLOT_BY_KEY))
    if unknown:
        raise PatchError(f"unknown key(s) in {path}: {', '.join(unknown)}")
    values = {}
    for key, value in raw.items():
        if value is None or (isinstance(value, str) and not value.strip()):
            continue
        values[key] = normalize(SLOT_BY_KEY[key], value)
    return values


# ---- operations -----------------------------------------------------------

def patch_bytes(data: bytes, values: dict[str, str]) -> tuple[bytes, list[str]]:
    """Return (patched bytes, report lines). Raises PatchError on any problem."""
    # A slot set to another slot's stock value (e.g. the two Photon AppIds
    # swapped) would be found again as that other slot's original on the next
    # run and flipped back, so the patch could never be idempotent. The stock
    # values are Rec Room's own dead ids/hosts anyway: refuse the config.
    originals = {s.original: s.key for s in SLOTS}
    for key, new in values.items():
        other = originals.get(new)
        if other is not None and other != key:
            raise PatchError(f"{key} is set to the client's stock {other} value")

    md = Metadata(data)
    report = []
    # Locate every slot in the unmodified file first, so one replacement can
    # never create matches for a later slot.
    plan = []
    for slot in SLOTS:
        if slot.key not in values:
            continue
        new = values[slot.key]
        if new == slot.original:
            report.append(f"{slot.key}: already set")
            continue
        hits = md.find(slot.original)
        # The Photon AppIds are also field/parameter default values; the
        # client builds its Photon settings from those too.
        defaults = md.find_defaults(slot.original)
        if hits or defaults:
            plan.append((slot, new, hits, defaults))
        elif md.find(new):
            report.append(f"{slot.key}: already set")
        else:
            raise PatchError(f"{slot.key}: the client's original value isn't in this metadata "
                             "(a different build, or patched to another value)")
    for slot, new, hits, defaults in plan:
        if hits:
            md.repoint(hits, new)
        if defaults:
            md.overwrite(defaults, slot.original, new)
        report.append(f"{slot.key}: patched {len(hits)} literal(s), {len(defaults)} default value(s)")

    # Re-read the result so a broken table never gets written out: every
    # located literal and default value must now hold the new value.
    check = Metadata(bytes(md.data))
    for slot, new, hits, defaults in plan:
        raw = new.encode("utf-8")
        if (any(check.literal(i) != raw for i in hits)
                or any(check.data[off:off + len(raw)] != raw for off in defaults)):
            raise PatchError(f"{slot.key}: verification failed after patching")
    return bytes(md.data), report


def current_values(data: bytes, stock: bytes) -> dict[str, str]:
    """Each slot's value in `data`, read at the literal indices where the
    unpatched `stock` metadata of the same build holds the original. That
    covers literals patched either way: repointed or overwritten in place."""
    md, ref = Metadata(data), Metadata(stock)
    if md.count != ref.count:
        raise PatchError("the stock metadata is from a different build (literal counts differ)")
    out = {}
    for slot in SLOTS:
        hits = ref.find(slot.original)
        if not hits:
            raise PatchError(f"{slot.key}: the original value isn't in the stock metadata")
        found = {md.literal(i) for i in hits}
        if len(found) != 1:
            raise PatchError(f"{slot.key}: the literals disagree ({len(found)} different values)")
        out[slot.key] = found.pop().decode("utf-8")
    return out


def cmd_apply(a: argparse.Namespace) -> None:
    path = Path(a.metadata)
    try:
        values = load_config(Path(a.config))
        if not values:
            print("nothing to patch: the config sets no values")
            return
        original = path.read_bytes()
        patched, report = patch_bytes(original, values)
    except (OSError, PatchError) as ex:
        fail(str(ex))
    for line in report:
        print(f"    {line}")
    if patched == original:
        print(f"    {path} already patched")
        return
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(patched)
    tmp.replace(path)
    print(f"    wrote {path}")


def cmd_show(a: argparse.Namespace) -> None:
    try:
        values = current_values(Path(a.metadata).read_bytes(), Path(a.stock).read_bytes())
    except (OSError, PatchError) as ex:
        fail(str(ex))
    text = json.dumps(values, indent=2) + "\n"
    if a.output:
        Path(a.output).write_text(text, encoding="utf-8")
        print(f"wrote {a.output}")
    else:
        sys.stdout.write(text)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    ap = sub.add_parser("apply", help="patch a global-metadata.dat in place")
    ap.add_argument("metadata", help=f"path to {METADATA_RELPATH}")
    ap.add_argument("--config", required=True, help="JSON file with the values to set")
    ap.set_defaults(func=cmd_apply)
    sp = sub.add_parser("show", help="write the values a global-metadata.dat holds now")
    sp.add_argument("metadata")
    sp.add_argument("--stock", required=True,
                    help="the same build's unpatched global-metadata.dat, to locate each literal")
    sp.add_argument("--output", help="write the JSON here instead of stdout")
    sp.set_defaults(func=cmd_show)
    a = p.parse_args()
    a.func(a)


if __name__ == "__main__":
    main()
