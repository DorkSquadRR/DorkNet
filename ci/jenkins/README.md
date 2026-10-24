# Client builds on Jenkins

The `Jenkinsfile` at the repo root builds the DorkNet clients on your existing
Jenkins.

It runs right on the Jenkins container, so that container's image needs the
build toolchain: Python with LIEF, the .NET SDK, the Android NDK and build-tools,
apktool, restic and rclone. `ci/jenkins/Dockerfile` is the stock
Jenkins image with that toolchain added. Your jobs, plugins and settings live
in the `jenkins_home` volume, not in the image, so switching images leaves
them alone.

| Platform | Output (Jenkins artifacts) | How |
|---|---|---|
| PC | `out/pc/dorknet-clientpatch-march_2023_03_21.zip`: MelonLoader, mod, config template, Steam stub, `manifest.json`; no game files | `tools/build_pc.py patcher` |
| PC | `out/pc/dorknet-client-march_2023_03_21.zip`: the stock Steam build with the patcher applied and your config; ready to run | `tools/build_pc.py client` |
| Quest | `out/quest/dorknet-recroom-<code>.apk` and `out/quest/DorkNet-Quest-<ver>/` (`.xapk` + installers) | `DorkNet.Quest/scripts/build.py build` |

The full PC client and the Quest packages contain game files, so keep them on
Jenkins. Don't publish them anywhere public.

## Where the stock builds come from

The stock builds live in restic repositories on the archive FTP. They are the
same repositories as the local archive's `v2/steam-v2` and `v2/quest-v2`. restic
reaches the FTP through its rclone backend (`ci/jenkins/archive-restore.sh`).

A build is picked by **snapshot tags**:

| Repository | Tags (examples) |
|---|---|
| Steam | `manifest=<depot manifest id>`, `date=…`, `branch=public` |
| Quest | `version=20230317`, `version-code=2128210` |

The job defaults to the builds this branch targets: Steam manifest
`7490748483298966814` (2023-03-21) and Quest `version-code=2128210`. Any other
build can be chosen per run. Tick **LIST_SNAPSHOTS** to print the snapshots
matching a filter without building anything.

A restored snapshot is cached in `jenkins_home/dorknet-cache`, so each stock
build is downloaded once.

A Quest build needs a resolved hook table for its versionCode, committed under
`DorkNet.Quest/targets/`. Only `20230317-2128210.json` exists today. The build
checks the table against the APK's `libil2cpp.so` and stops on a mismatch.
Resolve a table for another build locally with `build.py build --dump-cs`.

## Triggers

- **Push** to the branch (GitHub webhook) rebuilds only what changed:
  - `DorkNet.ClientMod/` and the PC scripts rebuild PC;
  - `DorkNet.Quest/` rebuilds Quest;
  - `Jenkinsfile` and `ci/jenkins/` rebuild both.
- **Build with Parameters** runs a manual build:
  - `PLATFORMS` (both / pc / quest), and `PC_FULL_CLIENT` for the full PC client;
  - snapshot tags, and per-run repository overrides.

## Setup

### 1. Switch your Jenkins to this image

On your Jenkins service in Dokploy:

1. **Build from this repo.**
   - For an Application service: provider GitHub, branch `march-2023-03-21`,
     build type Dockerfile, Dockerfile path `ci/jenkins/Dockerfile`, build
     context `ci/jenkins`.
   - For a Compose service: change its `jenkins` service to `build:` from
     `ci/jenkins` as in `ci/jenkins/docker-compose.yml`.
   - Either way, **keep the same volume on `/var/jenkins_home`**.
2. **Match the Jenkins version.** Set the build argument `JENKINS_IMAGE` to the
   image the service runs today (e.g. `jenkins/jenkins:2.492.3-lts-jdk17`).
   The default is `jenkins/jenkins:lts-jdk17`.
3. **Add the environment:**

   | Variable | Value |
   |---|---|
   | `ARCHIVE_PC_REPO` | Steam repository folder on the FTP, e.g. `Rec Room Versions/steam-v2` (plain text, not URL-encoded) |
   | `ARCHIVE_QUEST_REPO` | Quest repository folder, e.g. `Rec Room Versions/quest-v2` |
   | `ARCHIVE_FTP_TLS` | `explicit` (default), `implicit` or `none` |

4. **Redeploy.** The first image build downloads the Android NDK (~1.5 GB) and
   takes a while.

### 2. Plugins

The job needs these plugins:

- Pipeline
- Git
- GitHub (for the push trigger)
- Credentials Binding
- Timestamper
- AnsiColor
- Workspace Cleanup

Most are part of the suggested set.

### 3. Credentials

Add these under **Manage Jenkins → Credentials**, with exactly these IDs:

| ID | Kind | Value |
|---|---|---|
| `archive-ftp` | Username with password | the FTP login |
| `archive-ftp-host` | Secret text | FTP host name only (no `ftp://`, no user, no path) |
| `archive-restic-password` | Secret text | the archive's restic password |
| `quest-keystore` | Secret file | the Quest signing keystore |
| `quest-keystore-password` | Secret text | its store/key password |
| `clientmod-config` | Secret file | your `dorknet-clientmod.json` |
| `quest-config` | Secret file | your `dorknet-quest.cfg` |

**Use the keystore your players' installs were signed with**, the
`dorknet-debug.keystore` from your earlier local builds (password `dorknet`). A
different key means every player has to uninstall before installing a CI
build.

### 4. The job

1. Create a **New Item → Pipeline** (e.g. `dorknet-client`).
2. Under **Pipeline**, choose **Pipeline script from SCM**:
   - Git URL `https://github.com/DorkSquadRR/DorkNet.git`
   - branch `*/march-2023-03-21`
   - script path `Jenkinsfile`
3. Under **Triggers**, tick **GitHub hook trigger for GITScm polling**.
4. Run it once with **Build** so Jenkins learns the parameters. Later runs
   offer **Build with Parameters**.

### 5. GitHub webhook

In the repository's **Settings → Webhooks**:

- Payload URL: `https://<your jenkins>/github-webhook/`
- Content type: `application/json`
- Events: just the push event

### Disk

The restored stock builds are cached in `jenkins_home/dorknet-cache`, several
GB each. Jenkins keeps the artifacts of the last 3 runs.
