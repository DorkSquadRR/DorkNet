// DorkNet client builds — PC (patcher zip + full client) and Quest (.xapk).
//
// Runs on the Jenkins image from ci/jenkins (the stock image plus Python with
// LIEF, .NET, the Android NDK/build-tools, apktool, restic and rclone).
//
// Stock builds come out of the restic build archive on the FTP. Where it lives
// is server configuration, not part of the repo: the FTP host, login and the
// restic password are Jenkins credentials, and the repository folders come
// from the Jenkins container's environment (ARCHIVE_PC_REPO /
// ARCHIVE_QUEST_REPO), each overridable per run. A build is picked by
// snapshot tags.
//
// On a push, only the platforms whose code changed are rebuilt. A manual run
// picks the platforms with PLATFORMS. See ci/jenkins/README.md.

// The builds this branch targets. Any other archived build can be picked per
// run with the *_SNAPSHOT_TAGS parameters.
def PC_VERSION_KEY = 'march_2023_03_21'
def PC_DEFAULT_TAGS = 'manifest=7490748483298966814'   // Steam 2023-03-21 (appVersion 20230317)
def QUEST_DEFAULT_TAGS = 'version-code=2128210'        // Quest 20230317

def buildPc = false
def buildQuest = false

pipeline {
  agent any

  options {
    timestamps()
    ansiColor('xterm')
    disableConcurrentBuilds()
    // Full clients and xapks are several GB each: keep artifacts for the
    // last few runs only.
    buildDiscarder(logRotator(numToKeepStr: '20', artifactNumToKeepStr: '3'))
    timeout(time: 3, unit: 'HOURS')
  }

  parameters {
    choice(name: 'PLATFORMS', choices: ['both', 'pc', 'quest'],
      description: 'What to build on a manual run. Push builds rebuild whatever changed.')
    booleanParam(name: 'PC_FULL_CLIENT', defaultValue: true,
      description: 'Also build the full PC client (stock build + MelonLoader + mod + config). The patcher zip is always built.')
    string(name: 'PC_SNAPSHOT_TAGS', defaultValue: '',
      description: "restic tags picking the stock PC build, comma = AND. Empty: ${PC_DEFAULT_TAGS}")
    string(name: 'QUEST_SNAPSHOT_TAGS', defaultValue: '',
      description: "restic tags picking the stock Quest build, comma = AND. Empty: ${QUEST_DEFAULT_TAGS}")
    string(name: 'PC_REPO', defaultValue: '',
      description: 'PC restic repository folder on the FTP. Empty: ARCHIVE_PC_REPO from the Jenkins environment.')
    string(name: 'QUEST_REPO', defaultValue: '',
      description: 'Quest restic repository folder on the FTP. Empty: ARCHIVE_QUEST_REPO from the Jenkins environment.')
    booleanParam(name: 'LIST_SNAPSHOTS', defaultValue: false,
      description: 'Only list the snapshots matching the tags in both repositories (to find a build), then stop.')
  }

  stages {
    stage('Plan') {
      steps {
        script {
          def pushed = !currentBuild.getBuildCauses('com.cloudbees.jenkins.GitHubPushCause').isEmpty() ||
                       !currentBuild.getBuildCauses('hudson.triggers.SCMTrigger$SCMTriggerCause').isEmpty()
          if (params.LIST_SNAPSHOTS) {
            buildPc = false
            buildQuest = false
          } else if (pushed) {
            def changed = []
            for (cs in currentBuild.changeSets) {
              for (entry in cs.items) changed.addAll(entry.affectedPaths)
            }
            def touches = { prefixes -> changed.any { p -> prefixes.any { p.startsWith(it) } } }
            def common = ['Jenkinsfile', 'ci/jenkins/']
            buildPc = touches(common + ['DorkNet.ClientMod/', 'tools/build_pc.py'])
            buildQuest = touches(common + ['DorkNet.Quest/'])
            echo "Push build; ${changed.size()} changed file(s)"
          } else {
            buildPc = params.PLATFORMS in ['both', 'pc']
            buildQuest = params.PLATFORMS in ['both', 'quest']
          }
          echo "Build PC: ${buildPc}, Quest: ${buildQuest}"
        }
      }
    }

    stage('List snapshots') {
      when { expression { params.LIST_SNAPSHOTS } }
      steps {
        withArchive {
          withEnv(["PC_REPO_OVERRIDE=${params.PC_REPO}", "QUEST_REPO_OVERRIDE=${params.QUEST_REPO}",
                   "PC_TAGS=${params.PC_SNAPSHOT_TAGS}", "QUEST_TAGS=${params.QUEST_SNAPSHOT_TAGS}"]) {
            sh '''
              export RESTIC_CACHE_DIR="$DORKNET_CACHE/restic"
              pc="${PC_REPO_OVERRIDE:-${ARCHIVE_PC_REPO:?set ARCHIVE_PC_REPO on Jenkins or PC_REPO on the run}}"
              quest="${QUEST_REPO_OVERRIDE:-${ARCHIVE_QUEST_REPO:?set ARCHIVE_QUEST_REPO on Jenkins or QUEST_REPO on the run}}"
              echo "== PC: $pc";       archive-restore list "$pc" "$PC_TAGS"
              echo "== Quest: $quest"; archive-restore list "$quest" "$QUEST_TAGS"
            '''
          }
        }
      }
    }

    stage('PC patcher zip') {
      when { expression { buildPc } }
      steps {
        sh "python3 tools/build_pc.py patcher --version-key ${PC_VERSION_KEY} --output-dir out/pc"
      }
    }

    stage('PC full client') {
      when { expression { buildPc && params.PC_FULL_CLIENT } }
      steps {
        script {
          def base = withArchive {
            withEnv(["REPO_OVERRIDE=${params.PC_REPO}", "TAGS=${params.PC_SNAPSHOT_TAGS ?: PC_DEFAULT_TAGS}"]) {
              sh(returnStdout: true, script: '''
                export RESTIC_CACHE_DIR="$DORKNET_CACHE/restic"
                repo="${REPO_OVERRIDE:-${ARCHIVE_PC_REPO:?set ARCHIVE_PC_REPO on Jenkins or PC_REPO on the run}}"
                archive-restore restore "$repo" "$TAGS" "$DORKNET_CACHE/pc" --exclude /.DepotDownloader
              ''').trim().readLines().last()
            }
          }
          withCredentials([file(credentialsId: 'clientmod-config', variable: 'CLIENTMOD_CONFIG')]) {
            withEnv(["BASE_CLIENT=${base}"]) {
              sh """
                python3 tools/build_pc.py client \\
                  --base-client "\$BASE_CLIENT" \\
                  --patcher-zip out/pc/dorknet-clientpatch-${PC_VERSION_KEY}.zip \\
                  --config "\$CLIENTMOD_CONFIG" \\
                  --output-zip out/pc/dorknet-client-${PC_VERSION_KEY}.zip
              """
            }
          }
        }
      }
    }

    stage('Quest') {
      when { expression { buildQuest } }
      steps {
        script {
          def stock = withArchive {
            withEnv(["REPO_OVERRIDE=${params.QUEST_REPO}", "TAGS=${params.QUEST_SNAPSHOT_TAGS ?: QUEST_DEFAULT_TAGS}"]) {
              sh(returnStdout: true, script: '''
                export RESTIC_CACHE_DIR="$DORKNET_CACHE/restic"
                repo="${REPO_OVERRIDE:-${ARCHIVE_QUEST_REPO:?set ARCHIVE_QUEST_REPO on Jenkins or QUEST_REPO on the run}}"
                archive-restore restore "$repo" "$TAGS" "$DORKNET_CACHE/quest"
              ''').trim().readLines().last()
            }
          }
          withCredentials([
            file(credentialsId: 'quest-keystore', variable: 'QUEST_KEYSTORE'),
            string(credentialsId: 'quest-keystore-password', variable: 'QUEST_KEYSTORE_PASSWORD'),
            file(credentialsId: 'quest-config', variable: 'QUEST_CONFIG'),
          ]) {
            withEnv(["QUEST_STOCK=${stock}"]) {
              // The hook table for the build's versionCode must already be
              // resolved and committed under DorkNet.Quest/targets/; build.py
              // checks it against the APK's libil2cpp.so.
              sh '''
                set -eu
                obb="$(ls "$QUEST_STOCK"/obbs/main.*.obb | head -n1)"
                code="$(basename "$obb" | cut -d. -f2)"
                table="$(ls DorkNet.Quest/targets/*-"$code".json 2>/dev/null | head -n1 || true)"
                if [ -z "$table" ]; then
                  echo "No resolved hook table for versionCode $code in DorkNet.Quest/targets/." >&2
                  echo "Resolve one locally with build.py build --dump-cs and commit it." >&2
                  exit 1
                fi
                mkdir -p out/quest
                python3 DorkNet.Quest/scripts/build.py build \
                  --apk "$QUEST_STOCK/app.apk" --obb "$obb" \
                  --targets-json "$table" \
                  --config "$QUEST_CONFIG" \
                  --keystore "$QUEST_KEYSTORE" --keystore-pass "$QUEST_KEYSTORE_PASSWORD" \
                  --work-dir "$WORKSPACE/quest-work" \
                  --out-apk "$WORKSPACE/out/quest/dorknet-recroom-$code.apk"
              '''
            }
          }
        }
      }
    }
  }

  post {
    success {
      archiveArtifacts artifacts: 'out/**', allowEmptyArchive: true, fingerprint: true
    }
    cleanup {
      // Runs after archiving. Keeps the restic cache and restored stock
      // builds ($DORKNET_CACHE in jenkins_home, outside the workspace); drops
      // the workspace's build output and scratch.
      cleanWs(deleteDirs: true, notFailBuild: true)
    }
  }
}

// FTP + restic credentials for archive-restore.
def withArchive(Closure body) {
  withCredentials([
    usernamePassword(credentialsId: 'archive-ftp', usernameVariable: 'ARCHIVE_FTP_USER', passwordVariable: 'ARCHIVE_FTP_PASSWORD'),
    string(credentialsId: 'archive-ftp-host', variable: 'ARCHIVE_FTP_HOST'),
    string(credentialsId: 'archive-restic-password', variable: 'RESTIC_PASSWORD'),
  ]) {
    return body()
  }
}
