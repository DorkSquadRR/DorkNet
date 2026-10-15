// DorkNet.Quest native hook library (libdorknet.so).
//
// Loaded via a DT_NEEDED entry added to libmain.so, so it runs before Unity.
// It waits for libil2cpp.so, then installs arm64 inline hooks (And64InlineHook)
// at RVAs resolved from the build's dump.cs by scripts/resolve_targets.py and
// baked into targets_generated.h at compile time. It is the Quest counterpart
// of DorkNet.ClientMod (the PC MelonLoader mod) and does the same jobs:
//
//   * rewrite <service>.rec.net hosts to <service>.<ServerHost> (the ns
//     response then supplies every other service URL) -- like ClientMod's
//     UriPatches,
//   * inject the operator's Photon app ids -- like the PC byte patch
//     tools/patch-client.ps1,
//   * optionally set the operator's Meta (Oculus) app id,
//   * accept the self-hosted TLS chain and the image signature the operator's
//     server produces -- like ClientMod's TlsPatches / the PC ImageSigning,
//   * neutralise the integrity callbacks that would reject a re-signed,
//     hook-loaded client -- like ClientMod's AntiTamper/ToxMod/FileHash patches,
//   * when no Meta app id is set, steer the Oculus platform path past its
//     entitlement/login checks to the client's own credential screen.
//
// Every RVA is checked against a 20-byte FNV-1a fingerprint from the target
// table before it is hooked, so the wrong build is refused rather than
// miswritten. Obfuscated Assembly-CSharp names are re-randomised per release
// but shared with the PC build of the same release; this build (20230317 /
// versionCode 2128210) shares its names with the PC 2023-03-21 decompile.
//
// No Rec Room code or assets are included. Third-party inline hooker in
// src/third_party/And64InlineHook (MIT, (c) Rprop).

#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <unistd.h>

#include "third_party/And64InlineHook/And64InlineHook.hpp"
#include "config.h"
#include "targets_generated.h"

extern "C" int __android_log_print(int prio, const char *tag, const char *fmt, ...);
#define LOGI(...) __android_log_print(4, "DORKNET", __VA_ARGS__)
#define LOGE(...) __android_log_print(6, "DORKNET", __VA_ARGS__)

// --------------------------------------------------------------- config -----

static const char *kOfficialSuffix = ".rec.net";

static char g_server_host[160]   = DN_SERVER_HOST;     // <service>.rec.net -> <service>.<this>
static char g_oculus_appid[64]   = DN_OCULUS_APPID;    // "" = keep the client's own
static char g_photon_realtime[64] = DN_PHOTON_REALTIME;
static char g_photon_voice[64]   = DN_PHOTON_VOICE;
static char g_photon_chat[64]    = DN_PHOTON_CHAT;
static char g_photon_server[128] = DN_PHOTON_SERVER;   // "" = Photon Cloud
static int  g_photon_port        = DN_PHOTON_PORT;
static char g_photon_region[16]  = DN_PHOTON_REGION;
static bool g_tls_bypass         = DN_TLS_TRUST_BYPASS;
static bool g_accept_images      = DN_ACCEPT_UNSIGNED_IMAGES;
static bool g_log_http           = DN_LOG_HTTP;
static bool g_debug_http         = DN_DEBUG_HTTP;
static int  g_body_max           = DN_LOG_BODY_MAX;

// AppSettings field offsets (dump.cs; unchanged across the 2023 builds).
static const size_t kAppIdRealtimeOff = 0x10;
static const size_t kAppIdChatOff     = 0x18;
static const size_t kAppIdVoiceOff    = 0x20;
static const size_t kUseNameServerOff = 0x30;  // bool
static const size_t kFixedRegionOff   = 0x38;
static const size_t kServerOff        = 0x48;
static const size_t kPortOff          = 0x50;  // int
static const size_t kProtocolOff      = 0x60;  // int (Udp = 0)

// HTTPRequest / HTTPResponse field offsets (dump.cs).
static const size_t kUriFieldOffset     = 0x10;  // HTTPRequest.<Uri>
static const size_t kReqMethodOffset    = 0x18;  // HTTPRequest.<MethodType> (byte)
static const size_t kReqResponseOffset  = 0x70;  // HTTPRequest.<Response>
static const size_t kReqExceptionOffset = 0x80;  // HTTPRequest.<Exception>
static const size_t kReqStateOffset     = 0xB4;  // HTTPRequest.<State> (int)
static const size_t kRespStatusOffset   = 0x18;  // HTTPResponse.<StatusCode> (int)
static const size_t kRespDataOffset     = 0x48;  // HTTPResponse.<Data> (byte[])

// Il2CppArray data layout on 64-bit.
static const size_t kArrayLengthOff = 0x18;
static const size_t kArrayDataOff   = 0x20;

// Oculus platform message types (dump.cs ONGCNIAMKCG.DJCEHCFKHME).
static const unsigned kMsgEntitlementGetIsViewerEntitled = 409688241u;
static const unsigned kMsgUserGetUserProof = 578880643u;
static const char *kPlaceholderNonce = "dorknet-no-platform-account";
static const unsigned long long kNoPlatformUserId = 1ULL;

static const size_t kLogChunk = 1500;
static const uint32_t kUnityTlsSuccess = 0;  // UNITYTLS_X509VERIFY_SUCCESS / UNITYTLS_SUCCESS
static const size_t kErrorStateCodeOff = 0x4;

// --------------------------------------------------------------- il2cpp ------

typedef void *(*il2cpp_object_get_class_t)(void *obj);
typedef void *(*il2cpp_class_get_method_from_name_t)(void *klass, const char *name, int argc);
typedef void *(*il2cpp_object_new_t)(void *klass);
typedef void *(*il2cpp_string_new_t)(const char *str);
typedef void *(*il2cpp_runtime_invoke_t)(void *method, void *obj, void **params, void **exc);

static il2cpp_object_get_class_t il2cpp_object_get_class;
static il2cpp_class_get_method_from_name_t il2cpp_class_get_method_from_name;
static il2cpp_object_new_t il2cpp_object_new;
static il2cpp_string_new_t il2cpp_string_new;
static il2cpp_runtime_invoke_t il2cpp_runtime_invoke;

static uintptr_t g_il2cpp_base;

// Il2CppString: [klass][monitor][int32 length][char16 chars...]
static void str_to_utf8(void *s, char *out, size_t cap) {
    out[0] = '\0';
    if (!s || cap == 0) return;
    int32_t len = *(int32_t *)((char *)s + 0x10);
    if (len < 0) return;
    const uint16_t *chars = (const uint16_t *)((char *)s + 0x14);
    size_t i = 0;
    for (; i < (size_t)len && i + 1 < cap; i++)
        out[i] = chars[i] < 0x80 ? (char)chars[i] : '?';
    out[i] = '\0';
}

static const unsigned char *invoke_bytes(void *obj, const char *method, size_t *len) {
    *len = 0;
    if (!obj) return nullptr;
    void *cls = il2cpp_object_get_class(obj);
    if (!cls) return nullptr;
    void *m = il2cpp_class_get_method_from_name(cls, method, 0);
    if (!m) return nullptr;
    void *exc = nullptr;
    void *arr = il2cpp_runtime_invoke(m, obj, nullptr, &exc);
    if (!arr || exc) return nullptr;
    *len = (size_t) * (uint64_t *)((char *)arr + kArrayLengthOff);
    return (const unsigned char *)((char *)arr + kArrayDataOff);
}

static void invoke_str(void *obj, const char *method, char *out, size_t cap) {
    out[0] = '\0';
    if (!obj) return;
    void *cls = il2cpp_object_get_class(obj);
    if (!cls) return;
    void *m = il2cpp_class_get_method_from_name(cls, method, 0);
    if (!m) return;
    void *exc = nullptr;
    void *s = il2cpp_runtime_invoke(m, obj, nullptr, &exc);
    if (s && !exc) str_to_utf8(s, out, cap);
}

// --------------------------------------------------------------- logging ----

static void log_chunked(const char *prefix, const char *text) {
    size_t n = strlen(text);
    if (n <= kLogChunk) { LOGI("%s%s", prefix, text); return; }
    char buf[kLogChunk + 1];
    for (size_t off = 0, part = 1; off < n; off += kLogChunk, part++) {
        size_t take = n - off < kLogChunk ? n - off : kLogChunk;
        memcpy(buf, text + off, take);
        buf[take] = '\0';
        LOGI("%s[%zu] %s", prefix, part, buf);
    }
}

static bool looks_binary(const unsigned char *d, size_t n) {
    if (n == 0) return false;
    size_t sample = n < 4096 ? n : 4096, nontext = 0;
    for (size_t i = 0; i < sample; i++) {
        unsigned char b = d[i];
        if (b == 0) return true;
        if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D) nontext++;
    }
    return nontext * 100 / sample > 10;
}

static void format_body(const unsigned char *d, size_t n, char *out, size_t cap) {
    if (!d || n == 0) { snprintf(out, cap, "<empty>"); return; }
    size_t limit = (size_t)g_body_max;
    if (limit > cap - 64) limit = cap - 64;
    if (!looks_binary(d, n)) {
        size_t take = n < limit ? n : limit;
        memcpy(out, d, take);
        out[take] = '\0';
        if (take < n) snprintf(out + take, cap - take, "... <truncated %zu bytes>", n - take);
        return;
    }
    int w = snprintf(out, cap, "<binary %zu bytes>", n);
    out[w < 0 ? 0 : (size_t)w] = '\0';
}

// --------------------------------------------------------------- hooks -------

static void *(*orig_SendRequest)(void *request);

static void *hk_SendRequest(void *request) {
    if (!request) return orig_SendRequest(request);
    void *uri = *(void **)((char *)request + kUriFieldOffset);
    if (!uri) return orig_SendRequest(request);
    void *uri_class = il2cpp_object_get_class(uri);
    if (!uri_class) return orig_SendRequest(request);

    void *m_abs = il2cpp_class_get_method_from_name(uri_class, "get_AbsoluteUri", 0);
    if (!m_abs) return orig_SendRequest(request);
    void *exc = nullptr;
    void *s = il2cpp_runtime_invoke(m_abs, uri, nullptr, &exc);
    if (!s || exc) return orig_SendRequest(request);

    char url[2048];
    str_to_utf8(s, url, sizeof(url));

    if (g_log_http) {
        int method = *(unsigned char *)((char *)request + kReqMethodOffset);
        static const char *methods[] = {"GET", "HEAD", "POST", "PUT", "DELETE",
                                        "PATCH", "MERGE", "OPTIONS", "CONNECT"};
        const char *mname = (method >= 0 && method < 9) ? methods[method] : "?";
        LOGI("[http] %s %s", mname, url);
        if (g_debug_http) {
            size_t blen = 0;
            const unsigned char *body = invoke_bytes(request, "GetEntityBody", &blen);
            if (body && blen) {
                size_t cap = (size_t)g_body_max + 128;
                char *text = (char *)malloc(cap);
                if (text) { format_body(body, blen, text, cap); log_chunked("[http]   body=", text); free(text); }
            }
        }
    }

    // Rewrite any "<service>.rec.net" host to "<service>.<ServerHost>".
    const char *scheme_end = strstr(url, "://");
    if (!scheme_end) return orig_SendRequest(request);
    const char *host = scheme_end + 3;
    const char *host_end = host;
    while (*host_end && *host_end != '/' && *host_end != ':' && *host_end != '?') host_end++;
    size_t host_len = (size_t)(host_end - host);
    size_t suf = strlen(kOfficialSuffix);
    if (host_len <= suf || strncasecmp(host_end - suf, kOfficialSuffix, suf) != 0)
        return orig_SendRequest(request);

    char newurl[1024];
    size_t head = (size_t)(host - url);                 // "https://"
    size_t service_len = host_len - suf;                // "api", "auth", ...
    // ServerHost is either a plain apex ("dork.sh" -> "api.dork.sh") or a
    // template with "{service}" for servers on DorkNet's hyphen host style
    // ("{service}-dev.dork.sh" -> "ns-dev.dork.sh").
    static const char kServiceToken[] = "{service}";
    const char *token = strstr(g_server_host, kServiceToken);
    size_t new_host_len = token ? strlen(g_server_host) - (sizeof(kServiceToken) - 1) + service_len
                                : service_len + 1 + strlen(g_server_host);
    if (head + new_host_len + strlen(host_end) + 1 >= sizeof(newurl))
        return orig_SendRequest(request);
    memcpy(newurl, url, head);
    size_t p = head;
    if (token) {
        size_t before = (size_t)(token - g_server_host);
        memcpy(newurl + p, g_server_host, before);
        p += before;
        memcpy(newurl + p, host, service_len);
        p += service_len;
        strcpy(newurl + p, token + sizeof(kServiceToken) - 1);
    } else {
        memcpy(newurl + p, host, service_len);
        p += service_len;
        newurl[p++] = '.';
        strcpy(newurl + p, g_server_host);
    }
    strcat(newurl + head, host_end);

    void *new_str = il2cpp_string_new(newurl);
    void *new_uri = il2cpp_object_new(uri_class);
    void *ctor = il2cpp_class_get_method_from_name(uri_class, ".ctor", 1);
    if (!new_str || !new_uri || !ctor) return orig_SendRequest(request);
    void *args[1] = {new_str};
    exc = nullptr;
    il2cpp_runtime_invoke(ctor, new_uri, args, &exc);
    if (exc) return orig_SendRequest(request);
    *(void **)((char *)request + kUriFieldOffset) = new_uri;
    LOGI("intercepted %s -> %s", url, newurl);
    return orig_SendRequest(request);
}

static void (*orig_CallCallback)(void *thiz, void *method);
static void hk_CallCallback(void *thiz, void *method) {
    if (!thiz || !g_log_http) { orig_CallCallback(thiz, method); return; }
    char url[2048];
    invoke_str(*(void **)((char *)thiz + kUriFieldOffset), "get_AbsoluteUri", url, sizeof(url));
    int state = *(int *)((char *)thiz + kReqStateOffset);
    void *resp = *(void **)((char *)thiz + kReqResponseOffset);
    if (!resp) {
        void *ex = *(void **)((char *)thiz + kReqExceptionOffset);
        if (ex) {
            char msg[900];
            invoke_str(ex, "ToString", msg, sizeof(msg));
            LOGI("[http<-] NO-RESPONSE state=%d %s | %s", state, url, msg);
        } else {
            LOGI("[http<-] NO-RESPONSE state=%d %s | (no exception)", state, url);
        }
    } else {
        int status = *(int *)((char *)resp + kRespStatusOffset);
        LOGI("[http<-] %d %s", status, url);
        if (g_debug_http || status < 200 || status >= 300) {
            size_t blen = 0;
            const unsigned char *data = invoke_bytes(resp, "get_Data", &blen);
            size_t cap = (size_t)g_body_max + 128;
            char *text = (char *)malloc(cap);
            if (text) { format_body(data, blen, text, cap); log_chunked("[http<-]   body=", text); free(text); }
        }
    }
    orig_CallCallback(thiz, method);
}

static void *hk_get_AppID(void *method) {
    (void)method;
    return il2cpp_string_new(g_oculus_appid);
}

static void *(*orig_Photon)(void *thiz, void *method);
static void *hk_Photon(void *thiz, void *method) {
    void *s = orig_Photon(thiz, method);
    if (s) {
        if (g_photon_realtime[0]) *(void **)((char *)s + kAppIdRealtimeOff) = il2cpp_string_new(g_photon_realtime);
        if (g_photon_chat[0])     *(void **)((char *)s + kAppIdChatOff)     = il2cpp_string_new(g_photon_chat);
        if (g_photon_voice[0])    *(void **)((char *)s + kAppIdVoiceOff)    = il2cpp_string_new(g_photon_voice);
        *(unsigned char *)((char *)s + kUseNameServerOff) = 1;
        *(void **)((char *)s + kFixedRegionOff) = il2cpp_string_new(g_photon_region);
        *(int *)((char *)s + kProtocolOff) = 0;
        if (g_photon_server[0]) {
            *(void **)((char *)s + kServerOff) = il2cpp_string_new(g_photon_server);
            *(int *)((char *)s + kPortOff) = g_photon_port ? g_photon_port : 4533;
        }
        static int logged = 0;
        if (logged < 2) {
            logged++;
            LOGI("photon appsettings: RT=%s Voice=%s Chat=%s region=%s server=%s port=%d",
                 g_photon_realtime[0] ? g_photon_realtime : "(unset)",
                 g_photon_voice[0] ? g_photon_voice : "(unset)",
                 g_photon_chat[0] ? g_photon_chat : "(unset)", g_photon_region,
                 g_photon_server[0] ? g_photon_server : "(cloud)", g_photon_port);
            if (!g_photon_realtime[0])
                LOGE("photon app ids are not configured; multiplayer will try Rec Room's dead apps");
        }
    }
    return s;
}

static uint32_t (*orig_UnityTls)(void *thiz, uint64_t chain, void *es, void *method);
static uint32_t hk_UnityTls(void *thiz, uint64_t chain, void *es, void *method) {
    uint32_t r = orig_UnityTls(thiz, chain, es, method);
    if (r != kUnityTlsSuccess) {
        static int logged = 0;
        if (logged < 3) { logged++; LOGI("unitytls verify: forcing 0x%x -> SUCCESS", r); }
        if (es) *(uint32_t *)((char *)es + kErrorStateCodeOff) = kUnityTlsSuccess;
    }
    return kUnityTlsSuccess;
}

// void NotifyServerCertificate(Certificate): a no-op accepts the chain.
static void hk_NotifyServerCertificate(void *thiz, void *cert, void *method) {
    (void)thiz; (void)cert; (void)method;
}

// bool pin verifiers: accept unconditionally (replace-only, so no mismatch
// report is ever built and the client never quits).
static bool hk_pin_true4(void *a, void *b, void *c, void *d) { (void)a;(void)b;(void)c;(void)d; return true; }
static bool hk_pin_true4b(void *a, void *b, void *c, void *d) { (void)a;(void)b;(void)c;(void)d; return true; }

static bool hk_image_true(void *data, void *sig, void *method) {
    (void)data; (void)sig; (void)method;
    return true;
}

// void callbacks the client runs on an integrity rejection -- no-op them so a
// re-signed, hook-loaded sideload is not warned/quit (as DorkNet.ClientMod does
// on PC). The quit funnels and the report are left to run but are logged.
static void hk_void0(void *thiz, void *method) { (void)thiz; (void)method; }
static void hk_void1(void *thiz, void *arg, void *method) { (void)thiz; (void)arg; (void)method; }

static bool hk_toxmod_false(void *method) { (void)method; return false; }

static const char *cheat_reason(int r) {
    switch (r) {
        case 0: return "Obscured"; case 1: return "Time"; case 2: return "Inject";
        case 3: return "GiftCount"; case 4: return "Engine"; case 5: return "UnknownDll";
        case 6: return "ImageSignature"; case 7: return "AvatarHack";
        case 100: return "NetworkCertificatePublicKey"; case 101: return "NetworkCertificateIssuer";
        case 102: return "NetworkCertificateMissing"; case 103: return "NetworkCertificateMismatch";
        case 150: return "AutosaveChecksumMismatch"; case 151: return "AutosaveSubRoomIdMismatch";
        case 152: return "AutosaveChecksumException"; case 200: return "Photon_MissingHash";
        case 201: return "Photon_CorruptHash"; case 203: return "Photon_DifferentHash";
        case 300: return "AppData_Runtime_LengthMismatch"; case 301: return "AppData_Runtime_LastWriteTimeMismatch";
        case 302: return "AppData_Runtime_FileModified"; case 310: return "AppData_Boot_InvalidSignature";
        case 311: return "AppData_Boot_UnableToVerifySignatures"; case 320: return "Config_MissingHash";
        case 321: return "Config_DifferentHash"; case 400: return "Photon_InstantiateTool";
        case 500: return "Memory_Hash_Mismatch"; case 600: return "Driver_Invalid_Signature";
        default: return "?";
    }
}

static void log_caller(const char *what, void *ret) {
    uintptr_t a = (uintptr_t)ret;
    if (g_il2cpp_base && a > g_il2cpp_base)
        LOGI("[caller] %s <- libil2cpp rva 0x%lx", what, (unsigned long)(a - g_il2cpp_base));
    else
        LOGI("[caller] %s <- %p", what, ret);
}

static void *(*orig_CheatReport)(int reason, void *msg, uint64_t code, void *method);
static void *hk_CheatReport(int reason, void *msg, uint64_t code, void *method) {
    char text[512];
    str_to_utf8(msg, text, sizeof(text));
    LOGI("[integrity] report reason=%d (%s) msg=\"%s\"", reason, cheat_reason(reason), text);
    log_caller("report", __builtin_return_address(0));
    return orig_CheatReport(reason, msg, code, method);
}

static void (*orig_FatalQuit)(int code, void *reason, void *method);
static void hk_FatalQuit(int code, void *reason, void *method) {
    char msg[512];
    str_to_utf8(reason, msg, sizeof(msg));
    LOGI("[exit] FatalApplicationQuit(%d, \"%s\")", code, msg);
    log_caller("fatal quit", __builtin_return_address(0));
    orig_FatalQuit(code, reason, method);
}

static void (*orig_TryQuit)(int code, void *method);
static void hk_TryQuit(int code, void *method) {
    LOGI("[exit] TryApplicationQuit(%d)", code);
    log_caller("try quit", __builtin_return_address(0));
    orig_TryQuit(code, method);
}

// ------------------------------------------------------- oculus platform -----

typedef bool (*ovr_IsError_t)(void *m);
typedef unsigned (*ovr_GetType_t)(void *m);
static ovr_IsError_t orig_ovr_IsError;
static ovr_GetType_t ovr_GetType;

static bool hk_ovr_IsError(void *m) {
    bool real = orig_ovr_IsError(m);
    if (!real || !ovr_GetType) return real;
    unsigned t = ovr_GetType(m);
    if (t == kMsgEntitlementGetIsViewerEntitled || t == kMsgUserGetUserProof) return false;
    return real;
}

static void *(*orig_ovr_GetUserProof)(void *m);
static void *hk_ovr_GetUserProof(void *m) {
    void *p = orig_ovr_GetUserProof ? orig_ovr_GetUserProof(m) : nullptr;
    return p ? p : (void *)0x1;
}

static const char *(*orig_ovr_GetNonce)(void *proof);
static const char *hk_ovr_GetNonce(void *proof) {
    if (proof && proof != (void *)0x1 && orig_ovr_GetNonce) {
        const char *n = orig_ovr_GetNonce(proof);
        if (n && n[0]) return n;
    }
    return kPlaceholderNonce;
}

static unsigned long long (*orig_ovr_GetLoggedInUserID)();
static unsigned long long hk_ovr_GetLoggedInUserID() {
    unsigned long long real = orig_ovr_GetLoggedInUserID ? orig_ovr_GetLoggedInUserID() : 0;
    return real ? real : kNoPlatformUserId;
}

static unsigned long long (*orig_ovr_User_GetID)(void *u);
static unsigned long long hk_ovr_User_GetID(void *u) {
    unsigned long long real = orig_ovr_User_GetID ? orig_ovr_User_GetID(u) : 0;
    return real ? real : kNoPlatformUserId;
}

// ----------------------------------------------------------------- init ------

static uintptr_t module_base(const char *name) {
    FILE *f = fopen("/proc/self/maps", "r");
    if (!f) return 0;
    char line[512];
    uintptr_t base = 0;
    while (fgets(line, sizeof(line), f))
        if (strstr(line, name)) { base = (uintptr_t)strtoull(line, nullptr, 16); break; }
    fclose(f);
    return base;
}

static uint64_t fnv1a(const unsigned char *d, size_t n) {
    uint64_t h = 0xCBF29CE484222325ULL;
    for (size_t i = 0; i < n; i++) { h ^= d[i]; h *= 0x100000001B3ULL; }
    return h;
}

// Arm a target by id: verify the 20-byte fingerprint, then install the hook.
static bool arm(const DnTarget *t, void *hook, void **orig) {
    void *addr = (void *)(g_il2cpp_base + t->rva);
    uint64_t fp = fnv1a((const unsigned char *)addr, DN_HOOK_BYTES);
    if (fp != t->fingerprint) {
        LOGE("REFUSING %s: rva 0x%lx fingerprint 0x%llx != expected 0x%llx (wrong build?)",
             t->id, (unsigned long)t->rva, (unsigned long long)fp,
             (unsigned long long)t->fingerprint);
        return false;
    }
    void *dummy = nullptr;
    A64HookFunction(addr, hook, orig ? orig : &dummy);
    LOGI("armed %s @ rva 0x%lx", t->id, (unsigned long)t->rva);
    return true;
}

static bool resolve_il2cpp(void *h) {
#define SYM(n) n = (n##_t)dlsym(h, #n); if (!n) { LOGE("dlsym " #n " failed"); return false; }
    SYM(il2cpp_object_get_class)
    SYM(il2cpp_class_get_method_from_name)
    SYM(il2cpp_object_new)
    SYM(il2cpp_string_new)
    SYM(il2cpp_runtime_invoke)
#undef SYM
    return true;
}

static void arm_oculus_platform_bypass() {
    void *ovr = nullptr;
    for (int i = 0; i < 600; i++) {
        ovr = dlopen("libovrplatformloader.so", RTLD_NOLOAD | RTLD_NOW);
        if (ovr) break;
        usleep(100 * 1000);
    }
    if (!ovr) { LOGE("libovrplatformloader.so never appeared; platform bypass not armed"); return; }
    ovr_GetType = (ovr_GetType_t)dlsym(ovr, "ovr_Message_GetType");
    void *isErr = dlsym(ovr, "ovr_Message_IsError");
    if (ovr_GetType && isErr) {
        A64HookFunction(isErr, (void *)hk_ovr_IsError, (void **)&orig_ovr_IsError);
        LOGI("platform entitlement/proof bypass armed (no Meta app id configured)");
    } else {
        LOGE("ovr symbols missing; platform bypass not armed");
        return;
    }
    void *a;
    if ((a = dlsym(ovr, "ovr_GetLoggedInUserID")))
        A64HookFunction(a, (void *)hk_ovr_GetLoggedInUserID, (void **)&orig_ovr_GetLoggedInUserID);
    if ((a = dlsym(ovr, "ovr_User_GetID")))
        A64HookFunction(a, (void *)hk_ovr_User_GetID, (void **)&orig_ovr_User_GetID);
    if ((a = dlsym(ovr, "ovr_Message_GetUserProof")))
        A64HookFunction(a, (void *)hk_ovr_GetUserProof, (void **)&orig_ovr_GetUserProof);
    if ((a = dlsym(ovr, "ovr_UserProof_GetNonce")))
        A64HookFunction(a, (void *)hk_ovr_GetNonce, (void **)&orig_ovr_GetNonce);
}

static void *init_thread(void *) {
    void *h = nullptr;
    for (int i = 0; i < 600; i++) {
        h = dlopen("libil2cpp.so", RTLD_NOLOAD | RTLD_NOW);
        if (h) break;
        usleep(100 * 1000);
    }
    if (!h) { LOGE("libil2cpp.so never appeared; aborting"); return nullptr; }
    g_il2cpp_base = module_base("libil2cpp.so");
    if (!g_il2cpp_base) { LOGE("could not find libil2cpp.so base"); return nullptr; }
    if (!resolve_il2cpp(h)) return nullptr;
    dn_load_runtime_config(g_server_host, sizeof(g_server_host), g_oculus_appid, sizeof(g_oculus_appid),
                           g_photon_realtime, g_photon_voice, g_photon_chat,
                           g_photon_server, sizeof(g_photon_server), &g_photon_port,
                           g_photon_region, sizeof(g_photon_region),
                           &g_tls_bypass, &g_accept_images, &g_log_http, &g_debug_http, &g_body_max);

    LOGI("DorkNet.Quest init: build %s/%s il2cpp @ 0x%lx; host-rewrite -> *.%s",
         DN_TARGET_VERSION_NAME, DN_TARGET_VERSION_CODE, (unsigned long)g_il2cpp_base, g_server_host);

    arm(&dn_targets.http_send_request, (void *)hk_SendRequest, (void **)&orig_SendRequest);
    if (g_log_http)
        arm(&dn_targets.http_call_callback, (void *)hk_CallCallback, (void **)&orig_CallCallback);
    arm(&dn_targets.photon_app_settings, (void *)hk_Photon, (void **)&orig_Photon);

    if (g_oculus_appid[0]) {
        arm(&dn_targets.oculus_get_app_id, (void *)hk_get_AppID, nullptr);
        arm(&dn_targets.oculus_get_mobile_app_id, (void *)hk_get_AppID, nullptr);
        LOGI("oculus app id override: %s", g_oculus_appid);
    }

    if (g_tls_bypass) {
        arm(&dn_targets.tls_unity_verify, (void *)hk_UnityTls, (void **)&orig_UnityTls);
        arm(&dn_targets.tls_bc_notify_server_certificate, (void *)hk_NotifyServerCertificate, nullptr);
        arm(&dn_targets.pin_nameserver, (void *)hk_pin_true4, nullptr);
        arm(&dn_targets.pin_https, (void *)hk_pin_true4b, nullptr);
        arm(&dn_targets.pin_bc_verifier, (void *)hk_pin_true4, nullptr);
    }

    if (g_accept_images)
        arm(&dn_targets.image_signature_verify, (void *)hk_image_true, nullptr);

    // Integrity callbacks that would reject a re-signed, hook-loaded client.
    arm(&dn_targets.hile_warning_callback, (void *)hk_void1, nullptr);
    arm(&dn_targets.unknown_dll_callback, (void *)hk_void0, nullptr);
    arm(&dn_targets.file_hash_callback, (void *)hk_void1, nullptr);
    arm(&dn_targets.toxmod_can_initialize, (void *)hk_toxmod_false, nullptr);

    // Log-only diagnostics.
    arm(&dn_targets.cheat_report, (void *)hk_CheatReport, (void **)&orig_CheatReport);
    arm(&dn_targets.fatal_application_quit, (void *)hk_FatalQuit, (void **)&orig_FatalQuit);
    arm(&dn_targets.try_application_quit, (void *)hk_TryQuit, (void **)&orig_TryQuit);

    if (!g_oculus_appid[0]) arm_oculus_platform_bypass();
    LOGI("DorkNet.Quest init complete");
    return nullptr;
}

__attribute__((constructor)) static void on_load() {
    LOGI("DorkNet.Quest hooks loaded (build " __DATE__ " " __TIME__ ")");
    pthread_t t;
    pthread_create(&t, nullptr, init_thread, nullptr);
    pthread_detach(t);
}
