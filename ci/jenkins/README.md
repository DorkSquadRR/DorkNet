# Client builds on Jenkins

The `Jenkinsfile` at the repo root builds the DorkNet clients on your existing
Jenkins.

The builds run on a **build agent**, not on Jenkins itself:
- **The agent:** a container built from `ci/jenkins/Dockerfile`, which is the
  Jenkins inbound agent plus Python with LIEF, the .NET SDK, the Android NDK
  and build-tools, apktool, restic and rclone. It runs on Coolify next to
  Jenkins and connects to it over WebSocket on Docker's internal network. The
  job runs on it through the `dorknet-builder` label.
- **Jenkins itself:** stays on the stock image and runs no builds.

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

A restored snapshot is cached on the agent's `dorknet_cache` volume, so each
stock build is downloaded once.

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

### 1. Jenkins itself

Jenkins runs as a Coolify service from a Compose file:

- **Use the Java 25 image**, e.g. `jenkins/jenkins:latest-jdk25`, or
  `2.587-jdk25` to pin. Jenkins supports Java 25 until September 2029, Java 21
  only until September 2027, and Java 17 not at all. Keep the agent on the same
  Java major version; its image defaults to Java 25.
- **Add `restart: unless-stopped`** to the `jenkins` service. A restart from
  the Jenkins UI exits the container with code 5 and relies on Docker to
  start it again. Without a restart policy it stays stopped.
- **Turn on "Connect to predefined network"** (Advanced) so the agent can
  reach Jenkins by its container name, `jenkins-<service uuid>`.
- **Run no builds on it:** **Manage Jenkins → Nodes → Built-In Node →
  Configure**, set **Number of executors** to `0`.

#### Upgrading from the old setup

`ci/jenkins/docker-compose.yml` used to define the Jenkins **controller**
(its `jenkins` service, built from `ci/jenkins/Dockerfile`, with the
`jenkins_home` volume). It now defines only the build agent. If your Jenkins
was deployed from that file, **don't redeploy that application** from this
version: it would replace the controller with an agent, and Jenkins stops
until you rebuild it. The data volume survives, but nothing serves it.

Instead:

1. Create a new service for the controller as above, on the stock Java 25
   image, mounting the **same** data volume: reference the old volume by its
   full name and mark it `external: true`, or Jenkins starts empty.
2. Stop the old application, start the new service, and check that your jobs
   and credentials are there.
3. Deploy the agent (step 3) as a **new** application, then delete the old
   one without deleting its volume.

### 2. The agent node

**Manage Jenkins → Nodes → New Node**, a permanent agent:

| Setting | Value |
|---|---|
| Name | `dorknet-builder` |
| Labels | `dorknet-builder` |
| Remote root directory | `/home/jenkins/agent` |
| Launch method | Launch agent by connecting it to the controller, with **Use WebSocket** ticked |
| Number of executors | 1 |

Save, open the node, and copy its **secret**.

### 3. Deploy the agent on Coolify

1. **Create a Docker Compose application** from this repository: build pack
   Docker Compose, base directory **`/ci/jenkins`**, compose location
   `/docker-compose.yml`. The base directory is the build context, so with
   `/` the build would pick up the server's `Dockerfile` at the repo root. It
   needs no domain. Under Advanced, turn on **Connect to predefined network**.
2. **Set its environment:**

   | Variable | Value |
   |---|---|
   | `JENKINS_URL` | Jenkins' **internal** address, `http://jenkins-<service uuid>:8080/`. Not the public URL, which is behind Cloudflare Access. |
   | `JENKINS_SECRET` | the node secret from step 2 |
   | `JENKINS_AGENT_NAME` | optional, default `dorknet-builder` |
   | `ARCHIVE_PC_REPO` | Steam repository folder on the FTP, e.g. `Rec Room Versions/steam-v2` (plain text, not URL-encoded) |
   | `ARCHIVE_QUEST_REPO` | Quest repository folder, e.g. `Rec Room Versions/quest-v2` |
   | `ARCHIVE_FTP_TLS` | `explicit` (default), `implicit` or `none` |

3. **Deploy.** The first image build downloads the Android NDK (~1.5 GB) and
   takes a while. The node shows as connected in Jenkins once the agent is up.

Both Jenkins and the agent join the server's predefined Coolify network, so
the agent reaches Jenkins by container name. Check the name under the Jenkins
service's containers before using it.

### 4. Plugins

The job needs these plugins:

- Pipeline
- Git
- GitHub (for the push trigger)
- Credentials Binding
- Timestamper
- AnsiColor
- Workspace Cleanup

Most are part of the suggested set.

### 5. Credentials

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

### 6. The job

1. Create a **New Item → Pipeline** (e.g. `dorknet-client`).
2. Under **Pipeline**, choose **Pipeline script from SCM**:
   - Git URL `https://github.com/DorkSquadRR/DorkNet.git`
   - branch `*/march-2023-03-21`
   - script path `Jenkinsfile`
3. Under **Triggers**, tick **GitHub hook trigger for GITScm polling**.
4. Run it once with **Build** so Jenkins learns the parameters. Later runs
   offer **Build with Parameters**.

### 7. GitHub webhook

In the repository's **Settings → Webhooks**:

- Payload URL: `https://<your jenkins>/github-webhook/`
- Content type: `application/json`
- Events: just the push event

### Disk

The agent's `dorknet_cache` volume holds the restored stock builds, several GB
each. Jenkins keeps the artifacts of the last 3 runs, on its own volume.
