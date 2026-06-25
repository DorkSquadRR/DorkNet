#!/usr/bin/env python3
"""Decode a DorkNet ProtoDumper manifest into .proto text.

Usage:
    python decode_dump.py manifest.tsv  [out_dir]

manifest.tsv has one  "<proto path>\\t<base64 FileDescriptorProto>"  line per
file (as written by the ProtoDumper MelonLoader mod). This emits one .proto
file per descriptor under out_dir/ (default: ./proto_out), plus a combined
recroom_2023.proto with every rec_room/circuits message in one file for an
easy diff against DorkNet.Server/Protos/recroom_2020.proto.

Requires:  pip install protobuf
"""
import sys, os, base64, re
from google.protobuf import descriptor_pb2 as D

FDP = D.FileDescriptorProto

# proto3 scalar type names keyed by FieldDescriptorProto.Type
SCALAR = {
    1: "double", 2: "float", 3: "int64", 4: "uint64", 5: "int32",
    6: "fixed64", 7: "fixed32", 8: "bool", 9: "string", 12: "bytes",
    13: "uint32", 15: "sfixed32", 16: "sfixed64", 17: "sint32", 18: "sint64",
}
LABEL_REPEATED = 3


def type_name(f):
    if f.type in (11, 14):  # message / enum -> use type_name (strip leading dot)
        return f.type_name.lstrip(".")
    return SCALAR.get(f.type, f"type{f.type}")


def emit_enum(e, indent):
    pad = "  " * indent
    out = [f"{pad}enum {e.name} {{"]
    for v in e.value:
        out.append(f"{pad}  {v.name} = {v.number};")
    out.append(f"{pad}}}")
    return out


def emit_message(m, indent=0):
    pad = "  " * indent
    out = [f"{pad}message {m.name} {{"]

    # map<> detection: synthetic nested *Entry types with map_entry option
    map_entries = {}
    for nt in m.nested_type:
        if nt.options.map_entry:
            kt = type_name(nt.field[0])
            vt = type_name(nt.field[1])
            map_entries[nt.name] = (kt, vt)

    # which oneofs are real (not proto3 optional synthetic)
    for nt in m.nested_type:
        if nt.name in map_entries:
            continue
        out += emit_message(nt, indent + 1)
    for en in m.enum_type:
        out += emit_enum(en, indent + 1)

    # group fields by oneof
    printed_oneof = set()
    for f in m.field:
        # map field?
        if f.type == 11 and f.type_name.split(".")[-1] in map_entries:
            kt, vt = map_entries[f.type_name.split(".")[-1]]
            out.append(f"{pad}  map<{kt}, {vt}> {f.name} = {f.number};")
            continue

        if f.HasField("oneof_index") and not f.proto3_optional:
            idx = f.oneof_index
            if idx not in printed_oneof:
                printed_oneof.add(idx)
                out.append(f"{pad}  oneof {m.oneof_decl[idx].name} {{")
                for g in m.field:
                    if g.HasField("oneof_index") and g.oneof_index == idx and not g.proto3_optional:
                        out.append(f"{pad}    {type_name(g)} {g.name} = {g.number};")
                out.append(f"{pad}  }}")
            continue

        label = "repeated " if f.label == LABEL_REPEATED else ""
        opt = "optional " if f.proto3_optional else ""
        out.append(f"{pad}  {label}{opt}{type_name(f)} {f.name} = {f.number};")

    out.append(f"{pad}}}")
    return out


def render_file(fdp):
    out = []
    syntax = fdp.syntax or "proto2"
    out.append(f'syntax = "{syntax}";')
    out.append("")
    if fdp.package:
        out.append(f"package {fdp.package};")
    for dep in fdp.dependency:
        out.append(f'import "{dep}";')
    if fdp.package or fdp.dependency:
        out.append("")
    for en in fdp.enum_type:
        out += emit_enum(en, 0)
        out.append("")
    for m in fdp.message_type:
        out += emit_message(m, 0)
        out.append("")
    return "\n".join(out).rstrip() + "\n"


def main():
    if len(sys.argv) < 2:
        print(__doc__); sys.exit(1)
    manifest = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else "proto_out"
    os.makedirs(out_dir, exist_ok=True)

    files = []
    with open(manifest, "r", encoding="utf-8") as fh:
        for ln in fh:
            ln = ln.rstrip("\n")
            if not ln or "\t" not in ln:
                continue
            name, b64 = ln.split("\t", 1)
            try:
                raw = base64.b64decode(b64)
                fdp = FDP(); fdp.ParseFromString(raw)
            except Exception as e:
                print(f"  ! failed to parse {name}: {e}")
                continue
            files.append((name, fdp))

    print(f"parsed {len(files)} descriptors")

    # per-file .proto
    for name, fdp in files:
        safe = re.sub(r"[^A-Za-z0-9_.\-]", "_", name)
        with open(os.path.join(out_dir, safe), "w", encoding="utf-8") as w:
            w.write(render_file(fdp))

    # combined rec_room/circuits view (skip google/* well-known imports)
    rr = [ (n,f) for n,f in files if not n.startswith("google/") ]
    seen = set(); combo = ['syntax = "proto3";', "", "package recroom;", ""]
    total_msgs = 0
    for name, fdp in sorted(rr, key=lambda x: x[0]):
        combo.append(f"// ===== {name}  (package {fdp.package or '-'}) =====")
        for m in fdp.message_type:
            if m.name in seen:
                continue
            seen.add(m.name)
            combo += emit_message(m, 0)
            combo.append("")
            total_msgs += 1
        for en in fdp.enum_type:
            combo += emit_enum(en, 0)
            combo.append("")
    with open(os.path.join(out_dir, "recroom_2023.proto"), "w", encoding="utf-8") as w:
        w.write("\n".join(combo).rstrip() + "\n")
    print(f"wrote {out_dir}/recroom_2023.proto with {total_msgs} unique messages "
          f"from {len(rr)} rec_room/circuits descriptors")


if __name__ == "__main__":
    main()
