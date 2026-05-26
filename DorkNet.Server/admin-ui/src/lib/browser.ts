// Chromium gate. The zip importer uploads multi-hundred-megabyte
// FormData bodies and uses File / Blob slicing patterns that are
// effectively only stable on Chromium right now:
//   * Firefox's xhr.upload.progress + multipart streaming has buffer
//     issues over ~500 MB.
//   * Safari has a long-standing bug where the FormData clone for
//     large bodies hits a memory ceiling and aborts mid-upload.
//
// Easier to gate the importer than to chase per-browser workarounds.
// Other admin pages don't have this constraint, so the warning only
// shows on the import flow.

interface NavigatorWithUaData extends Navigator {
  userAgentData?: { brands: Array<{ brand: string; version: string }> };
}

export function isChromium(): boolean {
  if (typeof navigator === 'undefined') return true; // SSR / tests
  // userAgentData is currently only implemented in Chromium-based
  // browsers — Firefox/Safari haven't shipped it. If it's present,
  // we're on Chromium.
  if ((navigator as NavigatorWithUaData).userAgentData) return true;
  // Fallback UA sniff for Chromium-derived browsers that haven't
  // exposed userAgentData yet (e.g. older Edge/Brave builds).
  const ua = navigator.userAgent;
  return /Chrome\/|Chromium\/|Edg\//.test(ua) && !/Firefox\/|FxiOS\//.test(ua);
}
