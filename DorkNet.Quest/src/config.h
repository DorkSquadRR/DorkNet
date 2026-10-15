// Compile-time configuration for libdorknet.so.
//
// scripts/gen_config_header.py turns dorknet-quest.cfg into config_generated.h
// (put on the include path by the build), baking the operator's values into the
// APK. Any key left out of the cfg falls back to the built-in default below.
// The same cfg can also be pushed to
//   /sdcard/Android/data/<package>/files/dorknet-quest.cfg
// to override at runtime without a rebuild (dn_load_runtime_config).
#ifndef DORKNET_QUEST_CONFIG_H
#define DORKNET_QUEST_CONFIG_H

#include <stdbool.h>
#include <stddef.h>

#if defined(__has_include)
#  if __has_include("config_generated.h")
#    include "config_generated.h"
#  endif
#endif

// The apex the client's *.rec.net hosts are rewritten onto. "api.rec.net"
// becomes "api.<this>", "auth.rec.net" -> "auth.<this>", etc. No default: a
// build with no server configured would do nothing useful.
#ifndef DN_SERVER_HOST
#  define DN_SERVER_HOST ""
#endif
// Empty = keep the client's own (Rec Room's) Meta app id, which selects the
// no-Meta-account build (platform entitlement/login are steered past).
#ifndef DN_OCULUS_APPID
#  define DN_OCULUS_APPID ""
#endif
// Photon app ids: per-deployment, no default. Empty leaves the client's own
// (dead) value in place, so multiplayer will not connect until set.
#ifndef DN_PHOTON_REALTIME
#  define DN_PHOTON_REALTIME ""
#endif
#ifndef DN_PHOTON_VOICE
#  define DN_PHOTON_VOICE ""
#endif
#ifndef DN_PHOTON_CHAT
#  define DN_PHOTON_CHAT ""
#endif
#ifndef DN_PHOTON_SERVER
#  define DN_PHOTON_SERVER ""   // "" = Photon Cloud
#endif
#ifndef DN_PHOTON_PORT
#  define DN_PHOTON_PORT 0
#endif
// Photon Cloud fixed region. Must match the PC clients' region (ClientMod
// PhotonCloudRegion), or Quest and PC players land on different masters.
#ifndef DN_PHOTON_REGION
#  define DN_PHOTON_REGION "us"
#endif
#ifndef DN_TLS_TRUST_BYPASS
#  define DN_TLS_TRUST_BYPASS true   // accept a self-hosted TLS chain + skip pinning
#endif
#ifndef DN_ACCEPT_UNSIGNED_IMAGES
#  define DN_ACCEPT_UNSIGNED_IMAGES true
#endif
#ifndef DN_LOG_HTTP
#  define DN_LOG_HTTP true
#endif
#ifndef DN_DEBUG_HTTP
#  define DN_DEBUG_HTTP false        // also log request/response bodies (incl. tokens!)
#endif
#ifndef DN_LOG_BODY_MAX
#  define DN_LOG_BODY_MAX 10000
#endif

#ifndef DN_RUNTIME_CFG_NAME
#  define DN_RUNTIME_CFG_NAME "dorknet-quest.cfg"
#endif
// Package the runtime cfg is read from; the build's gen step overrides this.
#ifndef DN_PACKAGE
#  define DN_PACKAGE "net.dorknet.RecRoom"
#endif

// Apply /sdcard/Android/data/<DN_PACKAGE>/files/dorknet-quest.cfg over the
// baked defaults. Each out pointer already holds the compiled-in value.
void dn_load_runtime_config(char *server_host, size_t server_host_cap,
                            char *oculus_appid, size_t oculus_appid_cap,
                            char *photon_realtime, char *photon_voice, char *photon_chat,
                            char *photon_server, size_t photon_server_cap, int *photon_port,
                            char *photon_region, size_t photon_region_cap,
                            bool *tls_bypass, bool *accept_images,
                            bool *log_http, bool *debug_http, int *body_max);

#endif  // DORKNET_QUEST_CONFIG_H
