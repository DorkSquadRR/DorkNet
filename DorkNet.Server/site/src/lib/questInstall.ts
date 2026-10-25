// Install a DorkNet .xapk onto a Meta Quest from the browser over WebUSB, using
// Tango (@yume-chan/adb). Mirrors DorkNet.Quest/scripts/installer/install.ps1:
// install the APK, copy each OBB to its install path (skipped when an identical
// size is already there), then launch.
import { Adb, AdbDaemonTransport } from '@yume-chan/adb';
import { AdbDaemonWebUsbDevice, AdbDaemonWebUsbDeviceManager } from '@yume-chan/adb-daemon-webusb';
import AdbWebCredentialStore from '@yume-chan/adb-credential-web';
import { PackageManager } from '@yume-chan/android-bin';
import type { ReadableStream as TangoReadableStream } from '@yume-chan/stream-extra';
import { entryStream, type Xapk } from './xapk';

export const webUsbSupported = () => typeof navigator !== 'undefined' && 'usb' in navigator && !!AdbDaemonWebUsbDeviceManager.BROWSER;

export interface Headset {
  adb: Adb;
  model: string;
  serial: string;
}

export type Phase = 'app' | 'data' | 'launch';

export interface Progress {
  phase: Phase;
  label: string;
  done: number;
  total: number;
}

const credentialStore = new AdbWebCredentialStore('DorkNet Quest installer');

export async function connectHeadset(): Promise<Headset> {
  const manager = AdbDaemonWebUsbDeviceManager.BROWSER;
  if (!manager) throw new Error('This browser does not support WebUSB. Use Chrome or Edge on a computer.');
  const device = await manager.requestDevice();
  if (!device) throw new Error('No headset selected.');

  let connection;
  try {
    connection = await device.connect();
  } catch (e) {
    if (e instanceof AdbDaemonWebUsbDevice.DeviceBusyError) {
      throw new Error('The headset is in use by another program. Close SideQuest, Meta Quest Developer Hub or adb (run "adb kill-server"), then try again.');
    }
    throw e;
  }

  const transport = await AdbDaemonTransport.authenticate({
    serial: device.serial,
    connection,
    credentialStore,
  });
  const adb = new Adb(transport);
  const model = (await adb.getProp('ro.product.model')).trim() || device.name;
  return { adb, model, serial: device.serial };
}

function counted(stream: ReadableStream<Uint8Array>, onBytes: (n: number) => void): TangoReadableStream<Uint8Array> {
  const counter = new TransformStream<Uint8Array, Uint8Array>({
    transform(chunk, controller) {
      onBytes(chunk.byteLength);
      controller.enqueue(chunk);
    },
  });
  return stream.pipeThrough(counter) as unknown as TangoReadableStream<Uint8Array>;
}

async function remoteSize(adb: Adb, path: string): Promise<number | null> {
  const sync = await adb.sync();
  try {
    const st = await sync.lstat(path);
    return Number(st.size);
  } catch {
    return null;
  } finally {
    await sync.dispose();
  }
}

/**
 * Install the package. `confirmUninstall` is asked before removing an existing
 * copy signed with a different key (that clears its local data).
 */
export async function installXapk(
  headset: Headset,
  xapk: Xapk,
  onProgress: (p: Progress) => void,
  confirmUninstall: (pkg: string) => Promise<boolean>,
): Promise<void> {
  const { adb } = headset;
  const pkg = xapk.manifest.package_name;
  const pm = new PackageManager(adb);

  // ---- APK ----
  const installApk = async () => {
    let done = 0;
    const total = xapk.apk.size;
    onProgress({ phase: 'app', label: 'Installing the app', done, total });
    const stream = counted(await entryStream(xapk.source, xapk.apk), (n) => {
      done += n;
      onProgress({ phase: 'app', label: 'Installing the app', done, total });
    });
    await pm.installStream(total, stream);
  };
  try {
    await installApk();
  } catch (e) {
    const msg = String((e as Error)?.message ?? e);
    if (!/INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match/i.test(msg)) throw e;
    if (!(await confirmUninstall(pkg))) throw new Error('Cancelled.');
    await pm.uninstall(pkg);
    await installApk();
  }

  // ---- game data (OBB) ----
  for (const { entry, installPath } of xapk.expansions) {
    const dest = `/sdcard/${installPath}`;
    const total = entry.size;
    if ((await remoteSize(adb, dest)) === total) {
      onProgress({ phase: 'data', label: 'Game data already on the headset', done: total, total });
      continue;
    }
    const dir = dest.substring(0, dest.lastIndexOf('/'));
    await adb.subprocess.noneProtocol.spawnWaitText(['mkdir', '-p', dir]);

    let done = 0;
    onProgress({ phase: 'data', label: 'Copying game data', done, total });
    const stream = counted(await entryStream(xapk.source, entry), (n) => {
      done += n;
      onProgress({ phase: 'data', label: 'Copying game data', done, total });
    });
    const sync = await adb.sync();
    try {
      await sync.write({ filename: dest, file: stream, permission: 0o660 });
    } finally {
      await sync.dispose();
    }
  }

  // ---- launch ----
  onProgress({ phase: 'launch', label: 'Starting the game', done: 1, total: 1 });
  await adb.subprocess.noneProtocol.spawnWaitText(['monkey', '-p', pkg, '-c', 'android.intent.category.LAUNCHER', '1']);
}
