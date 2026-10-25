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

Each build is published to the **downloads page** under a name players can
read, and the same files are kept as Jenkins artifacts
(`out/downloads/2023.03/`):

| Download | What it is | Built from |
|---|---|---|
| `DorkNet-2023.03-PC-Full.zip` | the stock Steam build with the patcher applied and your config; ready to run | `tools/build_pc.py client` → `dorknet-client-march_2023_03_21.zip` |
| `DorkNet-2023.03-PC-Patch.zip` | MelonLoader, mod, config template, Steam stub, `manifest.json`; no game files | `tools/build_pc.py patcher` → `dorknet-clientpatch-march_2023_03_21.zip` |
| `DorkNet-2023.03-Quest.xapk` | patched APK + OBB, for the browser installer or SideQuest | `DorkNet.Quest/scripts/build.py build` → `DorkNet-Quest-<ver>/DorkNet-<ver>.xapk` |
| `DorkNet-2023.03-Quest-Installer.zip` | `install.cmd` / `install.ps1` / `install.sh` + README, unzipped next to the `.xapk` | the same package folder |

The build scripts keep their own names: the launcher downloads
`dorknet-clientpatch-<VersionKey>.zip` from GitHub Releases by that exact
name. `ci/downloads/publish.py` renames only the published copies.

The full PC client and the Quest packages contain game files. The downloads
page is behind Cloudflare Access for that reason; don't publish them
anywhere public.

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
  - snapshot tags, and per-run repository overrides;
  - `PUBLISH_DOWNLOADS` (on by default) to publish to the downloads page.

Push builds publish too, but only the platform they rebuilt: the page keeps
the other platform's last files.

## Setup

### 1. Jenkins itself

The controller is `ci/jenkins/docker-compose.yml`: stock Jenkins, no build
tools. On Coolify, deploy it as a Docker Compose application from this
repository (base directory `/ci/jenkins`), or paste it into a Compose service.

- **Java 25.** The file defaults to `jenkins/jenkins:lts-jdk25`; set
  `JENKINS_IMAGE` to pin a version, e.g. `jenkins/jenkins:2.587-jdk25`.
  Jenkins supports Java 25 until September 2029, Java 21 only until September
  2027, and Java 17 not at all. Keep the agent on the same Java major version;
  its image defaults to Java 25.
- **`restart: unless-stopped`** is in the file: a restart from the Jenkins UI
  exits the container with code 5 and relies on Docker to start it again.
- **Turn on "Connect to predefined network"** (Advanced) so the agent can
  reach Jenkins by its container name, `jenkins-<uuid>`.
- **Run no builds on it:** **Manage Jenkins → Nodes → Built-In Node →
  Configure**, set **Number of executors** to `0`.

Earlier versions of this file built the controller from `ci/jenkins/Dockerfile`,
which is now the agent's image. The service and data volume keep their names,
so an existing Jenkins deployed from this file keeps its jobs and credentials
when redeployed; it switches to the stock image, and its builds wait until
the agent (step 3) is connected.

That redeploy also upgrades Jenkins, and from Java 17 (the old default) to
Java 25, unless `JENKINS_IMAGE` pins your current image. Before it:

1. **Back up the data volume** (`jenkins_home`), e.g. with a Coolify volume
   backup or `docker run --rm -v <volume>:/v -v "$PWD":/b alpine tar czf
   /b/jenkins_home.tgz -C /v .`, so you can go back to the old image.
2. After it starts, open **Manage Jenkins**. Plugins that need a newer
   version show up there as failed to load; update them under **Plugins →
   Updates** and restart.
3. Run the job once (**LIST_SNAPSHOTS** is a quick check) before relying on it.

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
   **`/docker-compose.agent.yml`**. The base directory is the build context, so with
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
   | `DOWNLOADS_S3_ENDPOINT` | the downloads bucket's S3 API, internal address, e.g. `http://garage-<service uuid>:3900` |
   | `DOWNLOADS_BUCKET` | its name, e.g. `downloads.example.com` |
   | `DORKNET_SITE_URL` | optional: the DorkNet server's website (e.g. `https://dorknet.example.com`); the page links its Quest installer |

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
| `downloads-s3` | Username with password | S3 key for the downloads bucket: access key ID / secret. Give it read and write on that bucket only. |

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

### 8. The downloads page

The page (`ci/downloads/index.html`) is a static file in an S3 bucket. It
renders the bucket's `manifest.json`, which `publish.py upload` rewrites after
each build. With Garage (the S3 store on the Coolify server):

1. **Bucket and key.** Create the bucket, serve it as a website, and give
   Jenkins a key for that bucket only:

   ```sh
   garage bucket create downloads.example.com
   garage bucket website --allow downloads.example.com
   garage key create jenkins-downloads
   garage bucket allow --read --write --key <key id> downloads.example.com
   ```

   Store the key as the `downloads-s3` credential. Garage's web endpoint (port
   `3902`) serves a bucket whose name is the request's host name, read-only.
2. **Route.** Point the hostname at the web endpoint with a Coolify dynamic
   proxy configuration (**Servers → Proxy → Dynamic Configurations**):

   ```yaml
   http:
     routers:
       downloads:
         entryPoints: [http]
         service: downloads
         rule: Host(`downloads.example.com`)
     services:
       downloads:
         loadBalancer:
           passHostHeader: true
           servers:
             - url: "http://garage-<service uuid>:3902"
   ```

3. **Access.** Put the hostname behind a Cloudflare Access application
   **before** the first publish, with the policies of the people who may
   download. That group needs no other application.

Files are uploaded with `Cache-Control: no-cache`, so Cloudflare never serves
an old build after a new one replaces it. Each release keeps one copy of each
file; a new build overwrites it.

### Disk

The agent's `dorknet_cache` volume holds the restored stock builds, several GB
each. Jenkins keeps the artifacts of the last 3 runs, on its own volume.
