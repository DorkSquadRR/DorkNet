# DorkNet build agent: a Jenkins inbound agent with everything the PC and
# Quest client builds need. It runs next to Jenkins on Coolify, connects to
# the controller over WebSocket, and runs the jobs labelled "dorknet-builder"
# (the Jenkinsfile at the repo root). Jenkins itself stays on the stock image.
#
#   PC      Python + .NET SDK          tools/build_pc.py
#   Quest   Python/LIEF + JDK + Android NDK, build-tools, apktool
#                                      DorkNet.Quest/scripts/build.py
#   Builds  restic + rclone            stock builds restored from the restic
#                                      archive on the FTP (archive-restore.sh)
#
# Java 25: Jenkins supports it until Sep 2029 (Java 21 only until Sep 2027).
# Keep the agent's Java the same major version as the controller's.
#
# See ci/jenkins/README.md.

ARG AGENT_IMAGE=jenkins/inbound-agent:latest-jdk25
FROM ${AGENT_IMAGE}

USER root

# ---- base tools + .NET SDK (Microsoft's Debian feed for the image's own
# Debian release) -----------------------------------------------------------
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl wget unzip zip bzip2 python3 python3-venv \
 && . /etc/os-release \
 && wget -q "https://packages.microsoft.com/config/debian/${VERSION_ID}/packages-microsoft-prod.deb" -O /tmp/ms.deb \
 && dpkg -i /tmp/ms.deb && rm /tmp/ms.deb \
 && apt-get update \
 && apt-get install -y --no-install-recommends dotnet-sdk-10.0 \
 && rm -rf /var/lib/apt/lists/*

# ---- Python with LIEF (DT_NEEDED injection into libmain.so) ---------------
# build.py runs its helpers with -I (isolated), which ignores PYTHONPATH and
# user site-packages, so LIEF goes into the interpreter that is first on PATH.
RUN python3 -m venv /opt/py \
 && /opt/py/bin/pip install --no-cache-dir lief==0.15.1
ENV PATH="/opt/py/bin:${PATH}"

# ---- Android SDK: build-tools (zipalign, apksigner, aapt2) + NDK ----------
ENV ANDROID_SDK_ROOT=/opt/android-sdk
ARG CMDLINE_TOOLS=11076708
ARG BUILD_TOOLS=34.0.0
ARG NDK=27.2.12479018
RUN mkdir -p ${ANDROID_SDK_ROOT}/cmdline-tools \
 && wget -q https://dl.google.com/android/repository/commandlinetools-linux-${CMDLINE_TOOLS}_latest.zip -O /tmp/clt.zip \
 && unzip -q /tmp/clt.zip -d ${ANDROID_SDK_ROOT}/cmdline-tools && rm /tmp/clt.zip \
 && mv ${ANDROID_SDK_ROOT}/cmdline-tools/cmdline-tools ${ANDROID_SDK_ROOT}/cmdline-tools/latest \
 && (yes || true) | ${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin/sdkmanager --licenses > /dev/null \
 && ${ANDROID_SDK_ROOT}/cmdline-tools/latest/bin/sdkmanager --install \
      "build-tools;${BUILD_TOOLS}" "ndk;${NDK}" > /dev/null \
 && chown -R jenkins:jenkins ${ANDROID_SDK_ROOT}
ENV ANDROID_NDK=${ANDROID_SDK_ROOT}/ndk/${NDK}

# ---- apktool ---------------------------------------------------------------
ARG APKTOOL=2.9.3
RUN wget -q https://github.com/iBotPeaches/Apktool/releases/download/v${APKTOOL}/apktool_${APKTOOL}.jar \
      -O ${ANDROID_SDK_ROOT}/apktool.jar
ENV APKTOOL_JAR=${ANDROID_SDK_ROOT}/apktool.jar

# ---- restic + rclone (restic reaches the FTP through rclone) --------------
ARG RESTIC=0.18.1
ARG RCLONE=1.68.2
RUN wget -q https://github.com/restic/restic/releases/download/v${RESTIC}/restic_${RESTIC}_linux_amd64.bz2 -O /tmp/restic.bz2 \
 && bunzip2 /tmp/restic.bz2 \
 && install -m 0755 /tmp/restic /usr/local/bin/restic && rm /tmp/restic \
 && wget -q https://downloads.rclone.org/v${RCLONE}/rclone-v${RCLONE}-linux-amd64.zip -O /tmp/rclone.zip \
 && unzip -q /tmp/rclone.zip -d /tmp/rclone \
 && install -m 0755 /tmp/rclone/rclone-v${RCLONE}-linux-amd64/rclone /usr/local/bin/rclone \
 && rm -rf /tmp/rclone /tmp/rclone.zip

COPY archive-restore.sh /usr/local/bin/archive-restore
RUN chmod +x /usr/local/bin/archive-restore

# Restored stock builds and the restic cache. Mount a volume here so each
# multi-GB stock build is downloaded once, not after every restart.
ENV DORKNET_CACHE=/home/jenkins/dorknet-cache
RUN mkdir -p ${DORKNET_CACHE} && chown jenkins:jenkins ${DORKNET_CACHE}

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

USER jenkins
