# Disclaimer

DorkNet is an independent, fan-made, clean-room reimplementation of the
backend protocol used by the **2020 release of Rec Room** (build
`Recroom_Release` dated 2020.03.10).

It is **not** affiliated with, endorsed by, or sponsored by:

- Rec Room Inc.
- Against Gravity (the original developer)
- Photon (Exit Games GmbH)
- Steam / Valve Corporation
- Meta / Oculus

**Rec Room™**, the Rec Room logo, and all in-game art, audio, models,
locations, characters, and brand assets are the intellectual property of
Rec Room Inc. and are used here only for descriptive interoperability.

## What DorkNet does and doesn't ship

DorkNet's repository contains:
- Original server code (ASP.NET Core)
- Original admin tooling (React)
- Original MelonLoader mod (C#)
- Patcher scripts that operate on the user's own local Rec Room install

DorkNet's repository **does not** contain:
- The Rec Room game executable, assets, or any redistributable Rec Room IP
- Decompiled Rec Room source or IL2CPP dumps
- Patched copies of `Recroom_Release.exe`, `global-metadata.dat`, or any
  Rec Room game file

You must acquire the 2020.03.10 Rec Room client yourself through whatever
means are legal in your jurisdiction (e.g. SteamDB depot tools against an
account that owns Rec Room). The patcher runs on your machine, modifies
files locally, and never uploads anything anywhere.

## Use at your own risk

This is hobby software. There are no warranties of any kind. If running
DorkNet violates a license you agreed to (e.g. Steam Subscriber Agreement,
Rec Room Terms of Service), that's between you and the rights holder.
DorkNet's authors take no responsibility for consequences arising from
your use of the software.

## Takedown requests

If you represent Rec Room Inc. or another rights holder and wish to discuss
this project, please open an issue on the public repository and we will
respond promptly.
