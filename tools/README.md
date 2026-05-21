# tools/

CLI helpers. Most users don't need to touch these — the desktop app
wraps the same logic with a UI.

| Script               | Purpose                                                |
| -------------------- | ------------------------------------------------------ |
| `patch-client.ps1`   | Patch a Rec Room install to talk to your DorkNet server. The desktop app wraps this in a UI; CLI path is for advanced-mode hosts. |
| `dump-il2cpp.ps1`    | Run Cpp2IL against a Rec Room install to produce the decompiled dumps the project uses for reverse-engineering. Output stays on your machine — never committed. |
| `build-easy-app.ps1` | Build the unified desktop app (`dorknet.exe`). Self-contained .NET single-file publish. |
