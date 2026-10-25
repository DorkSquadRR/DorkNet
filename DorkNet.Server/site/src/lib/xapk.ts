// Read a DorkNet .xapk (zip) without loading it into memory, either from a
// File the player picked or from a URL that answers HTTP Range requests (the
// server's latest published build). DorkNet.Quest/scripts/build.py stores the
// APK and the OBB uncompressed, so those entries are plain byte ranges that
// stream straight through; manifest.json is deflated and goes through
// DecompressionStream. Handles zip64 so packages over 4 GB still open.

/** Random access to the package's bytes; `end` is exclusive. */
export interface ByteSource {
  name: string;
  size: number;
  read(start: number, end: number): Promise<ArrayBuffer>;
  stream(start: number, end: number): Promise<ReadableStream<Uint8Array<ArrayBuffer>>>;
}

export function fileSource(file: File): ByteSource {
  return {
    name: file.name,
    size: file.size,
    read: (start, end) => file.slice(start, end).arrayBuffer(),
    stream: async (start, end) => file.slice(start, end).stream(),
  };
}

/** A package on the server. `size` comes from the downloads manifest; every
 * read is one Range request. */
export function urlSource(url: string, name: string, size: number): ByteSource {
  const get = async (start: number, end: number): Promise<Response> => {
    let res: Response;
    try {
      res = await fetch(url, { headers: { Range: `bytes=${start}-${end - 1}` }, cache: 'no-store' });
    } catch {
      // A cross-origin redirect (an expired sign-in at an access proxy) also lands here.
      throw new Error("Couldn't reach the server. Reload the page (you may need to sign in again) and try again.");
    }
    if (res.status !== 206) {
      throw new Error(res.status === 200
        ? "The server doesn't support partial downloads, so the package can't be read in place."
        : `Downloading the package failed (HTTP ${res.status}).`);
    }
    return res;
  };
  const empty = () => new ReadableStream<Uint8Array<ArrayBuffer>>({ start: (c) => c.close() });
  return {
    name,
    size,
    read: async (start, end) => (end <= start ? new ArrayBuffer(0) : (await get(start, end)).arrayBuffer()),
    stream: async (start, end) => {
      if (end <= start) return empty();
      const body = (await get(start, end)).body;
      if (!body) throw new Error('The server sent an empty response.');
      return body;
    },
  };
}

export interface XapkEntry {
  name: string;
  method: number; // 0 = stored, 8 = deflate
  compressedSize: number;
  size: number;
  localHeaderOffset: number;
}

export interface XapkExpansion {
  file: string;
  install_path: string;
}

export interface XapkManifest {
  package_name: string;
  name?: string;
  version_code?: string;
  version_name?: string;
  expansions?: XapkExpansion[];
}

export interface Xapk {
  source: ByteSource;
  manifest: XapkManifest;
  entries: Map<string, XapkEntry>;
  apk: XapkEntry;
  expansions: { entry: XapkEntry; installPath: string }[];
}

const EOCD = 0x06054b50;
const ZIP64_LOCATOR = 0x07064b50;
const ZIP64_EOCD = 0x06064b50;
const CENTRAL = 0x02014b50;
const LOCAL = 0x04034b50;

async function bytes(src: ByteSource, start: number, end: number): Promise<DataView> {
  return new DataView(await src.read(start, end));
}

function u64(v: DataView, at: number): number {
  return Number(v.getBigUint64(at, true));
}

export async function openXapk(src: ByteSource): Promise<Xapk> {
  // End of central directory: last 22 bytes plus up to 64 KiB of comment.
  const tailStart = Math.max(0, src.size - (22 + 0xffff + 20));
  const tail = await bytes(src, tailStart, src.size);
  let eocd = -1;
  for (let i = tail.byteLength - 22; i >= 0; i--) {
    if (tail.getUint32(i, true) === EOCD) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('This is not a zip/.xapk file.');

  let count = tail.getUint16(eocd + 10, true);
  let cdSize = tail.getUint32(eocd + 12, true);
  let cdOffset = tail.getUint32(eocd + 16, true);

  if (cdOffset === 0xffffffff || count === 0xffff || cdSize === 0xffffffff) {
    const loc = eocd - 20;
    if (loc < 0 || tail.getUint32(loc, true) !== ZIP64_LOCATOR) throw new Error('Unsupported zip64 layout.');
    const z64At = u64(tail, loc + 8);
    const z64 = await bytes(src, z64At, z64At + 56);
    if (z64.getUint32(0, true) !== ZIP64_EOCD) throw new Error('Unsupported zip64 layout.');
    count = u64(z64, 32);
    cdSize = u64(z64, 40);
    cdOffset = u64(z64, 48);
  }

  const cd = await bytes(src, cdOffset, cdOffset + cdSize);
  const entries = new Map<string, XapkEntry>();
  const decoder = new TextDecoder();
  let p = 0;
  for (let n = 0; n < count; n++) {
    if (cd.getUint32(p, true) !== CENTRAL) throw new Error('Corrupt zip directory.');
    const method = cd.getUint16(p + 10, true);
    let compressedSize = cd.getUint32(p + 20, true);
    let size = cd.getUint32(p + 24, true);
    const nameLen = cd.getUint16(p + 28, true);
    const extraLen = cd.getUint16(p + 30, true);
    const commentLen = cd.getUint16(p + 32, true);
    let localHeaderOffset = cd.getUint32(p + 42, true);
    const name = decoder.decode(new Uint8Array(cd.buffer, cd.byteOffset + p + 46, nameLen));

    // zip64 extended info: present fields appear in this order, only for
    // values saturated in the fixed header.
    let e = p + 46 + nameLen;
    const extraEnd = e + extraLen;
    while (e + 4 <= extraEnd) {
      const id = cd.getUint16(e, true);
      const len = cd.getUint16(e + 2, true);
      if (id === 0x0001) {
        let q = e + 4;
        if (size === 0xffffffff) { size = u64(cd, q); q += 8; }
        if (compressedSize === 0xffffffff) { compressedSize = u64(cd, q); q += 8; }
        if (localHeaderOffset === 0xffffffff) { localHeaderOffset = u64(cd, q); }
      }
      e += 4 + len;
    }

    entries.set(name, { name, method, compressedSize, size, localHeaderOffset });
    p += 46 + nameLen + extraLen + commentLen;
  }

  const manifestEntry = entries.get('manifest.json');
  if (!manifestEntry) throw new Error('Not an .xapk: manifest.json is missing.');
  const manifest = JSON.parse(await new Response(await entryStream(src, manifestEntry)).text()) as XapkManifest;
  if (!manifest.package_name) throw new Error('manifest.json has no package_name.');

  const apk = entries.get(`${manifest.package_name}.apk`)
    ?? [...entries.values()].find((x) => x.name.endsWith('.apk'));
  if (!apk) throw new Error('The .xapk contains no APK.');

  const expansions = (manifest.expansions ?? []).map((x) => {
    const entry = entries.get(x.file);
    if (!entry) throw new Error(`The .xapk is missing ${x.file}.`);
    return { entry, installPath: x.install_path };
  });

  return { source: src, manifest, entries, apk, expansions };
}

/** The entry's uncompressed bytes as a stream. */
export async function entryStream(src: ByteSource, entry: XapkEntry): Promise<ReadableStream<Uint8Array<ArrayBuffer>>> {
  const local = await bytes(src, entry.localHeaderOffset, entry.localHeaderOffset + 30);
  if (local.getUint32(0, true) !== LOCAL) throw new Error(`Corrupt entry ${entry.name}.`);
  const start = entry.localHeaderOffset + 30 + local.getUint16(26, true) + local.getUint16(28, true);
  const raw = await src.stream(start, start + entry.compressedSize);
  if (entry.method === 0) return raw;
  if (entry.method === 8) return raw.pipeThrough(new DecompressionStream('deflate-raw'));
  throw new Error(`Unsupported compression in ${entry.name}.`);
}
