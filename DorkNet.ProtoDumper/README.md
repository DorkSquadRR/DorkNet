# DorkNet ProtoDumper

Runtime extractor for Rec Room's Google.Protobuf schema (room save /
creation / circuits data model). Produces clean `.proto` files for any
client build — including obfuscated ones like 2023.03.21 where static
reconstruction from `global-metadata.dat` is unreliable (the descriptor
base64 is split into scattered string-literal chunks whose concat order
only lives in IL).

At runtime every `FileDescriptor` still carries its original,
un-obfuscated `FileDescriptorProto` (real message names, field names,
numbers and types intact). The catch: Rec Room builds these descriptors
**lazily and in a specific dependency order** as content loads. Trying to
force-build them by reading a message type's static descriptor out of
order throws inside the type's static constructor, and the failure is
cached — *poisoning* the type (and crashing the client). So this mod does
**not** enumerate types.

Instead it Harmony-postfixes the one library method every generated cctor
funnels through —
`Google.Protobuf.Reflection.FileDescriptor.BuildFrom` /
`.FromGeneratedCode` — and records each descriptor as the game builds it.
Completely passive: **play the client through the content you care about**
and every descriptor that loads is captured.

## Pipeline

1. **Build the mod**

   ```sh
   dotnet build DorkNet.ProtoDumper/DorkNet.ProtoDumper.csproj -c Release
   ```

   Output: `DorkNet.ProtoDumper/bin/Release/DorkNet.ProtoDumper.dll`.

2. **Run it on a *runnable* 2023.03.21 client (MelonLoader 0.6.6)**

   > The `GameAssembly.dll` + `il2cpp_data`-only copy under
   > `Recnet-old/dist/RecRoom-2023.03.21-il2cpp/…/staging/` is an
   > **offline-analysis payload, not a runnable build**. Use a full build
   > with the exe + UnityPlayer + MelonLoader (e.g. the devdork package).

   - Drop `DorkNet.ProtoDumper.dll` into `<RecRoom>\Mods\`. On launch it
     installs the descriptor-build hooks (see `dump.log` / MelonLoader
     console for "installed N descriptor-build hooks").
   - **Log in and play through the content whose schema you want:** your
     dorm, a created room, the Maker Pen, and a circuits (CV2) board. Each
     subsystem builds its protos the first time it loads. Descriptors are
     captured the instant they build — no scene/timer scanning, nothing is
     force-built, so the client stays stable.
   - Output appears under `<RecRoom>\DorkNet-ProtoDump\` and is flushed to
     disk ~1 s after new descriptors load, and again on quit:
     - `manifest.tsv`   — `<proto path>\t<base64>` per file
     - `files\*.b64`    — same, one file each
     - `dump.log`       — running capture count

3. **Decode to .proto**

   ```sh
   pip install protobuf
   python DorkNet.ProtoDumper/decode_dump.py <path>/manifest.tsv proto_out
   ```

   Emits one `.proto` per descriptor under `proto_out/`, plus a combined
   `proto_out/recroom_2023.proto` (every rec_room/circuits message in one
   file) for an easy diff against
   `DorkNet.Server/Protos/recroom_2020.proto`.

## Notes

- No game DLLs are referenced at compile time. `FileDescriptor` is the
  Google.Protobuf *library* type (not obfuscated); it's reached by its
  original name with the Il2Cpp namespace-prefix fallback, and its members
  are read via reflection. The same DLL works across builds and stays clean
  of derivative game code.
- The only Harmony patches are the two read-only descriptor-build
  postfixes above — safe to run alongside (or without) `DorkNet.ClientMod`.
- The descriptor base64 is the same `FileDescriptorProto` wire format
  Google's own tooling emits, so `decode_dump.py` (or `protoc
  --decode=google.protobuf.FileDescriptorProto descriptor.proto`) reads it
  directly.
