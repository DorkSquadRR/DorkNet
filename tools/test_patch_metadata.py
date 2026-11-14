"""Tests for tools/patch_metadata.py on a synthetic IL2CPP v27 metadata file.

    python3 -m unittest discover -s tools -p "test_*.py"
"""
from __future__ import annotations

import base64
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import patch_metadata as pm  # noqa: E402

HEADER_SIZE = 8 + 8 * 32
SLOT = {s.key: s for s in pm.SLOTS}
NEW = {
    "NameServerUrl": "https://ns-dev.example.com",
    "AuthUrl": "https://auth-dev.example.com",
    "PhotonAppId": "11111111-2222-3333-4444-555555555555",
    "PhotonVoiceAppId": "66666666-7777-8888-9999-000000000000",
    "ImageSigningPublicKey": base64.b64encode(bytes(range(1, 256)) + b"\x7f").decode(),
}


def build_metadata(literals: list[str], defaults: list[str]) -> bytes:
    """Header, literal table, literal blob, then a default-value section of
    int32-length-prefixed strings, laid out like a real v27 file."""
    blob, table = bytearray(), bytearray()
    for text in literals:
        raw = text.encode()
        table += struct.pack("<Ii", len(raw), len(blob))
        blob += raw
    section = bytearray(b"\xaa" * 8)  # unrelated default data around the strings
    for text in defaults:
        raw = text.encode()
        section += struct.pack("<i", len(raw)) + raw + b"\xbb\xbb"
    table_off = HEADER_SIZE
    blob_off = table_off + len(table)
    defaults_off = blob_off + len(blob)
    pairs = [(0, 0)] * 32
    pairs[0] = (table_off, len(table))
    pairs[1] = (blob_off, len(blob))
    pairs[pm.DEFAULTS_PAIR] = (defaults_off, len(section))
    header = struct.pack("<Ii", pm.SANITY, 27) + b"".join(struct.pack("<ii", *p) for p in pairs)
    return header + bytes(table) + bytes(blob) + bytes(section)


def stock() -> bytes:
    literals = ["hello"] + [s.original for s in pm.SLOTS] + ["https://rec.net/shop"]
    photon = [SLOT["PhotonAppId"].original, SLOT["PhotonVoiceAppId"].original]
    return build_metadata(literals, photon * 2)


def values_of(data: bytes) -> dict[str, str]:
    return pm.current_values(data, stock())


class PatchMetadataTests(unittest.TestCase):
    def test_patches_every_slot(self) -> None:
        patched, report = pm.patch_bytes(stock(), dict(NEW))
        self.assertEqual(values_of(patched), NEW)
        md = pm.Metadata(patched)
        self.assertEqual(md.literal(0), b"hello")
        self.assertEqual(md.literal(len(pm.SLOTS) + 1), b"https://rec.net/shop")
        for slot in pm.SLOTS:
            self.assertEqual(md.find(slot.original), [])
            self.assertEqual(md.find_defaults(slot.original), [])
        self.assertEqual(len(md.find_defaults(NEW["PhotonAppId"])), 2)
        self.assertEqual(len(md.find_defaults(NEW["PhotonVoiceAppId"])), 2)
        self.assertIn("PhotonAppId: patched 1 literal(s), 2 default value(s)", report)

    def test_literal_blob_and_defaults_layout_untouched(self) -> None:
        before = stock()
        patched, _ = pm.patch_bytes(before, dict(NEW))
        md = pm.Metadata(before)
        blob = slice(md.blob_off, md.blob_off + md.blob_size)
        self.assertEqual(patched[blob], before[blob])  # strings are appended, not overwritten
        self.assertGreater(len(patched), len(before))

    def test_idempotent(self) -> None:
        once, _ = pm.patch_bytes(stock(), dict(NEW))
        twice, report = pm.patch_bytes(once, dict(NEW))
        self.assertEqual(once, twice)
        self.assertTrue(all(line.endswith("already set") for line in report))

    def test_swapped_stock_photon_ids_are_refused(self) -> None:
        swapped = {"PhotonAppId": SLOT["PhotonVoiceAppId"].original,
                   "PhotonVoiceAppId": SLOT["PhotonAppId"].original}
        with self.assertRaisesRegex(pm.PatchError, "stock"):
            pm.patch_bytes(stock(), swapped)

    def test_partial_config_leaves_other_slots(self) -> None:
        patched, _ = pm.patch_bytes(stock(), {"NameServerUrl": NEW["NameServerUrl"]})
        got = values_of(patched)
        self.assertEqual(got["NameServerUrl"], NEW["NameServerUrl"])
        self.assertEqual(got["PhotonAppId"], SLOT["PhotonAppId"].original)

    def test_other_build_fails(self) -> None:
        other = build_metadata(["hello", "https://ns.rec.net/?v=2"], [])
        with self.assertRaisesRegex(pm.PatchError, "original value"):
            pm.patch_bytes(other, {"NameServerUrl": NEW["NameServerUrl"]})

    def test_bad_magic_and_version(self) -> None:
        data = bytearray(stock())
        data[0] ^= 1
        with self.assertRaisesRegex(pm.PatchError, "magic"):
            pm.Metadata(bytes(data))
        data = bytearray(stock())
        struct.pack_into("<i", data, 4, 29)
        with self.assertRaisesRegex(pm.PatchError, "version"):
            pm.Metadata(bytes(data))

    def test_normalize(self) -> None:
        url, guid, key = SLOT["NameServerUrl"], SLOT["PhotonAppId"], SLOT["ImageSigningPublicKey"]
        self.assertEqual(pm.normalize(url, " https://ns.example.com/ "), "https://ns.example.com")
        for bad in ("ns.example.com", "https://ns.example.com/api", "https://ns.example.com?x=1"):
            with self.assertRaises(pm.PatchError):
                pm.normalize(url, bad)
        self.assertEqual(pm.normalize(guid, NEW["PhotonAppId"].upper()), NEW["PhotonAppId"])
        with self.assertRaises(pm.PatchError):
            pm.normalize(guid, "not-a-guid")
        with self.assertRaisesRegex(pm.PatchError, "2048-bit"):
            pm.normalize(key, base64.b64encode(b"\x01" * 128).decode())

    def test_pem_public_keys(self) -> None:
        modulus = b"\xc1" + bytes(255)

        def der(tag: int, body: bytes) -> bytes:
            n = len(body)
            length = bytes([n]) if n < 0x80 else bytes([0x82]) + n.to_bytes(2, "big")
            return bytes([tag]) + length + body

        rsa = der(0x30, der(0x02, b"\x00" + modulus) + der(0x02, b"\x01\x00\x01"))
        spki = der(0x30, der(0x30, der(0x06, bytes.fromhex("2a864886f70d010101")) + b"\x05\x00")
                   + der(0x03, b"\x00" + rsa))

        def pem(label: str, body: bytes) -> str:
            return f"-----BEGIN {label}-----\n{base64.b64encode(body).decode()}\n-----END {label}-----\n"

        want = base64.b64encode(modulus).decode()
        slot = SLOT["ImageSigningPublicKey"]
        self.assertEqual(pm.normalize(slot, pem("PUBLIC KEY", spki)), want)
        self.assertEqual(pm.normalize(slot, pem("RSA PUBLIC KEY", rsa)), want)
        with self.assertRaisesRegex(pm.PatchError, "PUBLIC"):
            pm.normalize(slot, pem("RSA PRIVATE KEY", rsa))

    def test_load_config_rejects_unknown_keys(self) -> None:
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "c.json"
            path.write_text('{"NameServerUrl": "https://ns.example.com", "Typo": "x"}', encoding="utf-8")
            with self.assertRaisesRegex(pm.PatchError, "Typo"):
                pm.load_config(path)
            path.write_text('{"NameServerUrl": "https://ns.example.com", "AuthUrl": ""}', encoding="utf-8")
            self.assertEqual(pm.load_config(path), {"NameServerUrl": "https://ns.example.com"})


if __name__ == "__main__":
    unittest.main()
