import { useEffect, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, RefreshCw, Trash } from '../components/Icons';

interface ServerSettings {
  signupsDisabled: boolean;
  globalFriendsEnabled: boolean;
  weeklyChallengesCompletedRequired: boolean;
  profanityFilterDisabled: boolean;
  allAvatarItemsOwned: boolean;
  roomBlobVersionClampDisabled: boolean;
  updatedAt: string;
}

interface WeeklyChallenge {
  index: number;
  name: string;
  config: string;
  description: string;
  tooltip: string;
}

interface WeeklyChallengeSettings {
  completedRequired: boolean;
  challenges: WeeklyChallenge[];
  reward: WeeklyReward;
  updatedAt: string;
}

interface WeeklyReward {
  giftDropId: number;
  xp: number;
  tokens: number;
  level: number;
  giftContext: number;
  giftRarity: number;
  avatarItemDesc: string;
  consumableItemDesc: string;
  equipmentPrefabName: string;
  equipmentModificationGuid: string;
}

interface RewardOption {
  kind: 'avatar' | 'consumable' | 'equipment';
  label: string;
  giftDropId: number;
  avatarItemDesc: string;
  consumableItemDesc: string;
  equipmentPrefabName: string;
  equipmentModificationGuid: string;
}

interface RewardOptions {
  avatarItems: RewardOption[];
  consumables: RewardOption[];
  equipment: RewardOption[];
}

interface WeeklyChallengeTypeOption {
  value: number;
  label: string;
  note: string;
}

const WEEKLY_CHALLENGE_TYPES: WeeklyChallengeTypeOption[] = [
  { value: 0, label: 'Any challenge event', note: 'Base Challenge' },
  { value: 2, label: 'Timed buffer', note: 'TimedBufferChallenge' },
  { value: 3, label: 'Dynamic float arithmetic', note: 'DynamicFloatArithmeticChallenge' },
  { value: 4, label: 'Dynamic int arithmetic', note: 'DynamicIntArithmeticChallenge' },
  { value: 6, label: 'Required event type', note: 'RequiredEventTypeChallenge' },
  { value: 7, label: 'Required room scene location', note: 'RequiredRoomSceneLocationChallenge' },
  { value: 8, label: 'Required enemy type', note: 'RequiredEnemyTypeChallenge' },
  { value: 9, label: 'Bool var equals', note: 'BoolVarEqualsChallenge' },
  { value: 11, label: 'Disc golf under par', note: 'DiscGolfFinishUnderParChallenge' },
  { value: 12, label: 'Required game mode activity', note: 'RequiredGameModeActivityChallenge' },
  { value: 13, label: 'Complete game without', note: 'CompleteGameWithoutChallenge' },
  { value: 14, label: 'Required gesture', note: 'RequiredGestureChallenge' },
  { value: 15, label: 'Hitstreak', note: 'HitstreakChallenge' },
  { value: 16, label: 'Hitstreak count', note: 'HitstreakCountChallenge' },
];

const WEEKLY_TYPE_BY_VALUE = new Map(WEEKLY_CHALLENGE_TYPES.map((type) => [type.value, type]));
const DEFAULT_WEEKLY_CHALLENGE_TYPE = 0;
const MAX_WEEKLY_CHALLENGE_AMOUNT = 1000;

function clampWeeklyAmount(value: number): number {
  if (!Number.isFinite(value)) return 1;
  return Math.min(MAX_WEEKLY_CHALLENGE_AMOUNT, Math.max(1, Math.trunc(value)));
}

function buildWeeklyChallengeConfig(type: number, amount: number): string {
  const safeType = WEEKLY_TYPE_BY_VALUE.has(type) ? type : DEFAULT_WEEKLY_CHALLENGE_TYPE;
  const target = clampWeeklyAmount(amount);
  const child = { ct: safeType };
  if (target <= 1) return JSON.stringify(child);
  return JSON.stringify({ ct: 1, ctc: [child], t: target, cc: 0 });
}

function parseWeeklyChallengeConfig(config: string): { type: number; amount: number } {
  try {
    const parsed = JSON.parse(config) as unknown;
    if (!parsed || typeof parsed !== 'object') throw new Error('not an object');
    const root = parsed as { ct?: unknown; ctc?: unknown; t?: unknown };
    const rootType = typeof root.ct === 'number' ? root.ct : DEFAULT_WEEKLY_CHALLENGE_TYPE;
    if (rootType === 1 && Array.isArray(root.ctc) && root.ctc.length > 0) {
      const child = root.ctc[0] as { ct?: unknown };
      const childType = typeof child?.ct === 'number' && WEEKLY_TYPE_BY_VALUE.has(child.ct)
        ? child.ct
        : DEFAULT_WEEKLY_CHALLENGE_TYPE;
      const amount = typeof root.t === 'number' ? root.t : 1;
      return { type: childType, amount: clampWeeklyAmount(amount) };
    }
    return {
      type: WEEKLY_TYPE_BY_VALUE.has(rootType) ? rootType : DEFAULT_WEEKLY_CHALLENGE_TYPE,
      amount: 1,
    };
  } catch {
    return { type: DEFAULT_WEEKLY_CHALLENGE_TYPE, amount: 1 };
  }
}

interface DiscoveredGameConfigSettings {
  friendsPostGamePromptUnderFriendCount: number;
  friendsSuggestFriendCodeOnFriendsScreenCount: number;
  screensForceVerification: boolean;
  vrForceVerification: boolean;
  rewardsUseRewardSelection: boolean;
  rewardsSelectionTimeout: number;
  roomDetailsPhotoRollEnabled: boolean;
  loadingNetworkTimeout: number;
  runningNetworkTimeout: number;
  synchronizedFieldRemoveDefaultEntries: boolean;
  renderingDisableSrpBatcher: boolean;
  splitTestSoftOverrides: string;
  splitTestHardOverrides: string;
  splitTestSegmentProbabilities: string;
  updatedAt: string;
}

interface CircuitsV2NetworkLimit {
  numPlayersForActivation: number;
  bytes: number;
  bytesTimeWindowFrame60s: number;
  rpcs: number;
  rpcTimeWindowFrame60s: number;
}

interface CircuitsV2LimitSettings {
  network: CircuitsV2NetworkLimit[];
  compute: number;
  chips: number;
  cloudValues: number;
  cloudValuesAutosaveSeconds: number;
  unculledChips: number;
  visibleChips: number;
  inkPerChip: number;
  updatedAt: string;
}

export function Settings({ embedded }: { embedded?: boolean } = {}) {
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [weekly, setWeekly] = useState<WeeklyChallengeSettings | null>(null);
  const [gameConfigs, setGameConfigs] = useState<DiscoveredGameConfigSettings | null>(null);
  const [circuits, setCircuits] = useState<CircuitsV2LimitSettings | null>(null);
  const [rewardOptions, setRewardOptions] = useState<RewardOptions>({
    avatarItems: [],
    consumables: [],
    equipment: [],
  });
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [weeklyBusy, setWeeklyBusy] = useState(false);
  const [gameConfigBusy, setGameConfigBusy] = useState(false);
  const [circuitsBusy, setCircuitsBusy] = useState(false);
  const toast = useToast();

  const load = async () => {
    try {
      const [s, w, rewards, configs, limits] = await Promise.all([
        get<ServerSettings>('/settings'),
        get<WeeklyChallengeSettings>('/settings/weekly-challenges'),
        get<RewardOptions>('/settings/weekly-challenges/reward-options'),
        get<DiscoveredGameConfigSettings>('/settings/gameconfigs'),
        get<CircuitsV2LimitSettings>('/settings/circuits-limits'),
      ]);
      setSettings(s);
      setWeekly(w);
      setRewardOptions(rewards);
      setGameConfigs(configs);
      setCircuits(limits);
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  useEffect(() => { void load(); }, []);

  const toggleSignups = async () => {
    if (!settings) return;
    const next = !settings.signupsDisabled;
    const verb = next ? 'disable' : 'enable';
    if (!confirm(`Really ${verb} new account creation? Existing players keep their access either way.`)) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/signups', {
        method: 'POST',
        body: { Disabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'New signups blocked.' : 'New signups allowed.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const toggleGlobalFriends = async () => {
    if (!settings) return;
    const next = !settings.globalFriendsEnabled;
    const verb = next ? 'make everyone friends' : 'turn off all-friends';
    if (!confirm(
      next
        ? 'Make every account friends with every other account? No friend rows are written — it\'s synthesized live, and connected players refresh instantly. Toggle off to revert.'
        : `Really ${verb}? Players will revert to their real friends list.`,
    )) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/global-friends', {
        method: 'POST',
        body: { Enabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'Everyone is now friends — players refreshing live.' : 'All-friends turned off.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const toggleAllAvatarItems = async () => {
    if (!settings) return;
    const next = !settings.allAvatarItemsOwned;
    if (!confirm(
      next
        ? 'Give every account every avatar item? The whole wardrobe catalog (plus all hair dyes) shows as owned for everyone. Nothing is written to inventories — it\'s synthesized live, and reverts instantly when you toggle it off. Players see it next time the avatar menu opens.'
        : 'Turn off all-items? Players revert to only the avatar items they actually own.',
    )) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/all-avatar-items', {
        method: 'POST',
        body: { Enabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'Everyone now owns all avatar items.' : 'All-items turned off.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const toggleRoomBlobVersionClamp = async () => {
    if (!settings) return;
    const next = !settings.roomBlobVersionClampDisabled;
    if (!confirm(
      next
        ? 'Turn off the room version clamp? Rooms imported from modern RecNet exports will be served with their original version stamps, and the client will refuse them with "update Rec Room to visit this room". Only turn this off if a specific room misbehaves with the clamp on.'
        : 'Turn the room version clamp back on? Imported modern rooms become joinable again on the next blob download.',
    )) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/room-blob-version-clamp', {
        method: 'POST',
        body: { Disabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'Room version clamp disabled.' : 'Room version clamp enabled.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const toggleProfanity = async () => {
    if (!settings) return;
    const next = !settings.profanityFilterDisabled;
    const verb = next ? 'turn OFF the profanity filter' : 'turn the profanity filter back on';
    if (!confirm(
      next
        ? 'Turn off the profanity filter? Room names, invention names and chat will no longer be censored by the server. Toggle back on any time.'
        : `Really ${verb}? Text will be filtered again server-side.`,
    )) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/profanity', {
        method: 'POST',
        body: { Disabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'Profanity filter disabled.' : 'Profanity filter enabled.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const updateWeeklyChallenge = (index: number, patch: Partial<WeeklyChallenge>) => {
    setWeekly((current) => {
      if (!current) return current;
      return {
        ...current,
        challenges: current.challenges.map((challenge, i) =>
          i === index ? { ...challenge, ...patch } : challenge),
      };
    });
  };

  const addWeeklyChallenge = () => {
    setWeekly((current) => {
      if (!current) return current;
      return {
        ...current,
        challenges: [
          ...current.challenges,
          {
            index: current.challenges.length,
            name: 'New weekly challenge',
            config: buildWeeklyChallengeConfig(DEFAULT_WEEKLY_CHALLENGE_TYPE, 1),
            description: '',
            tooltip: '',
          },
        ],
      };
    });
  };

  const removeWeeklyChallenge = (index: number) => {
    setWeekly((current) => {
      if (!current) return current;
      const next = current.challenges.filter((_, i) => i !== index);
      return { ...current, challenges: next.map((challenge, i) => ({ ...challenge, index: i })) };
    });
  };

  const updateWeeklyChallengeConfig = (
    index: number,
    patch: Partial<{ type: number; amount: number }>,
  ) => {
    const challenge = weekly?.challenges[index];
    if (!challenge) return;
    const current = parseWeeklyChallengeConfig(challenge.config);
    const next = { ...current, ...patch };
    updateWeeklyChallenge(index, {
      config: buildWeeklyChallengeConfig(next.type, next.amount),
    });
  };

  const updateWeeklyReward = (patch: Partial<WeeklyReward>) => {
    setWeekly((current) => current ? {
      ...current,
      reward: { ...current.reward, ...patch },
    } : current);
  };

  const rewardNumber = (key: keyof WeeklyReward, value: string) => {
    const n = Number.parseInt(value, 10);
    updateWeeklyReward({ [key]: Number.isFinite(n) ? Math.max(0, n) : 0 } as Partial<WeeklyReward>);
  };

  const allRewardOptions = [
    ...rewardOptions.avatarItems.map((option, index) => ({ option, value: `avatar:${index}` })),
    ...rewardOptions.consumables.map((option, index) => ({ option, value: `consumable:${index}` })),
    ...rewardOptions.equipment.map((option, index) => ({ option, value: `equipment:${index}` })),
  ];

  const selectedRewardValue = weekly
    ? allRewardOptions.find(({ option }) =>
      option.avatarItemDesc === weekly.reward.avatarItemDesc &&
      option.consumableItemDesc === weekly.reward.consumableItemDesc &&
      option.equipmentPrefabName === weekly.reward.equipmentPrefabName &&
      option.equipmentModificationGuid === weekly.reward.equipmentModificationGuid)?.value
      ?? (
        weekly.reward.avatarItemDesc ||
        weekly.reward.consumableItemDesc ||
        weekly.reward.equipmentPrefabName ||
        weekly.reward.equipmentModificationGuid
          ? 'current'
          : 'none'
      )
    : 'none';

  const selectedRewardOption = selectedRewardValue === 'current'
    ? weekly?.reward
    : allRewardOptions.find((entry) => entry.value === selectedRewardValue)?.option ?? null;

  const selectWeeklyReward = (value: string) => {
    if (value === 'none') {
      updateWeeklyReward({
        giftDropId: 0,
        avatarItemDesc: '',
        consumableItemDesc: '',
        equipmentPrefabName: '',
        equipmentModificationGuid: '',
      });
      return;
    }

    const selected = allRewardOptions.find((entry) => entry.value === value)?.option;
    if (!selected) return;
    updateWeeklyReward({
      giftDropId: selected.giftDropId,
      avatarItemDesc: selected.avatarItemDesc,
      consumableItemDesc: selected.consumableItemDesc,
      equipmentPrefabName: selected.equipmentPrefabName,
      equipmentModificationGuid: selected.equipmentModificationGuid,
    });
  };

  const resetWeeklyDefaults = async () => {
    if (!confirm('Reset weekly challenges to the built-in defaults?')) return;
    setWeeklyBusy(true);
    try {
      const updated = await api<WeeklyChallengeSettings>('/settings/weekly-challenges', {
        method: 'POST',
        body: { CompletedRequired: true, Challenges: [] },
      });
      setWeekly(updated);
      setSettings((current) => current ? {
        ...current,
        weeklyChallengesCompletedRequired: updated.completedRequired,
        updatedAt: updated.updatedAt,
      } : current);
      toast.push('Weekly challenges reset.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setWeeklyBusy(false);
    }
  };

  const saveWeeklyChallenges = async () => {
    if (!weekly) return;
    setWeeklyBusy(true);
    try {
      const updated = await api<WeeklyChallengeSettings>('/settings/weekly-challenges', {
        method: 'POST',
        body: {
          CompletedRequired: weekly.completedRequired,
          Challenges: weekly.challenges,
          Reward: weekly.reward,
        },
      });
      setWeekly(updated);
      setSettings((current) => current ? {
        ...current,
        weeklyChallengesCompletedRequired: updated.completedRequired,
        updatedAt: updated.updatedAt,
      } : current);
      toast.push('Weekly challenges saved.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setWeeklyBusy(false);
    }
  };

  const updateGameConfigs = (patch: Partial<DiscoveredGameConfigSettings>) => {
    setGameConfigs((current) => current ? { ...current, ...patch } : current);
  };

  const saveGameConfigs = async (reset = false) => {
    if (!gameConfigs && !reset) return;
    setGameConfigBusy(true);
    try {
      const body = reset ? defaultGameConfigs() : gameConfigs;
      const updated = await api<DiscoveredGameConfigSettings>('/settings/gameconfigs', {
        method: 'POST',
        body,
      });
      setGameConfigs(updated);
      toast.push(reset ? 'GameConfig settings reset.' : 'GameConfig settings saved.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setGameConfigBusy(false);
    }
  };

  const updateCircuits = (patch: Partial<CircuitsV2LimitSettings>) => {
    setCircuits((current) => current ? { ...current, ...patch } : current);
  };

  const updateCircuitsNetwork = (index: number, patch: Partial<CircuitsV2NetworkLimit>) => {
    setCircuits((current) => current
      ? {
          ...current,
          network: current.network.map((entry, i) => i === index ? { ...entry, ...patch } : entry),
        }
      : current);
  };

  const saveCircuits = async (reset = false) => {
    if (!circuits && !reset) return;
    setCircuitsBusy(true);
    try {
      const body = reset ? defaultCircuitsLimits() : circuits;
      const updated = await api<CircuitsV2LimitSettings>('/settings/circuits-limits', {
        method: 'POST',
        body,
      });
      setCircuits(updated);
      toast.push(
        reset ? 'CircuitsV2 limits reset.' : 'CircuitsV2 limits saved. Players must rejoin their room.',
        'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setCircuitsBusy(false);
    }
  };

  const refreshBtn = (
    <button onClick={load} className="btn-secondary text-xs" disabled={busy}>
      <RefreshCw className={busy ? 'animate-spin' : ''} />
      Refresh
    </button>
  );

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{refreshBtn}</div>
      ) : (
        <PageHeader
          title="Server settings"
          blurb="Runtime toggles applied across every replica without a redeploy."
          actions={refreshBtn}
        />
      )}

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{err}</div>
      )}

      {!settings && !weekly && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading…</div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">In-game signups</h2>
              <p className="mt-1 text-xs text-ink-400">
                When disabled, the watch's account-creation flow returns an error to the player instead of minting a new account.
                Existing logins keep working — only brand-new signups are blocked.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleSignups}
              disabled={busy}
              className={(settings.signupsDisabled ? 'btn-primary' : 'btn-danger') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.signupsDisabled ? 'Enable signups' : 'Disable signups'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.signupsDisabled
              ? <span className="badge-banned">Signups disabled</span>
              : <span className="badge-online">Signups allowed</span>}
          </div>
        </div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl mt-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">Everyone is friends</h2>
              <p className="mt-1 text-xs text-ink-400">
                When on, every account is treated as a friend of every other account, so players
                don't have to search and friend-request each other on a small server. Nothing is
                written to the database — it's synthesized live, and connected players get a refresh
                signal so their friends list updates without relogging. Turn it off to instantly
                revert to real friend lists. Blocks are still respected.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleGlobalFriends}
              disabled={busy}
              className={(settings.globalFriendsEnabled ? 'btn-danger' : 'btn-primary') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.globalFriendsEnabled ? 'Turn off' : 'Make everyone friends'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.globalFriendsEnabled
              ? <span className="badge-online">Everyone is friends</span>
              : <span className="badge-neutral">Normal friend lists</span>}
          </div>
        </div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl mt-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">Everyone owns all avatar items</h2>
              <p className="mt-1 text-xs text-ink-400">
                When on, every account is reported as owning the entire avatar wardrobe catalog
                (plus all permanent hair dyes), so the store shows everything as "owned" and every
                item is equippable. Nothing is written to inventories — it's synthesized live, and
                turning it off instantly reverts each player to the items they actually own. Players
                pick up the change the next time the watch loads unlocked items (avatar menu open or
                next boot). Color variants and player-made custom items aren't included.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleAllAvatarItems}
              disabled={busy}
              className={(settings.allAvatarItemsOwned ? 'btn-danger' : 'btn-primary') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.allAvatarItemsOwned ? 'Turn off' : 'Give everyone all items'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.allAvatarItemsOwned
              ? <span className="badge-online">Everyone owns everything</span>
              : <span className="badge-neutral">Normal wardrobes</span>}
          </div>
        </div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl mt-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">Profanity filter</h2>
              <p className="mt-1 text-xs text-ink-400">
                The server-side filter behind <span className="font-mono">api/sanitize/*</span> that
                the watch calls before letting a player submit a room name, invention name, or chat
                message. Turn it off to stop censoring text entirely — every check returns "clean"
                and input is passed through unchanged. Takes effect immediately.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleProfanity}
              disabled={busy}
              className={(settings.profanityFilterDisabled ? 'btn-primary' : 'btn-danger') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.profanityFilterDisabled ? 'Enable filter' : 'Disable filter'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.profanityFilterDisabled
              ? <span className="badge-banned">Filter off — nothing censored</span>
              : <span className="badge-online">Filter active</span>}
          </div>
        </div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl mt-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">Imported-room version clamp</h2>
              <p className="mt-1 text-xs text-ink-400">
                Rooms imported from modern RecNet exports carry room-data version stamps far newer
                than the 2023 client understands, so it refuses to load them with "update Rec Room
                to visit this room". While the clamp is on, the CDN serves those blobs with their
                version stamps lowered to the client's maximum — nothing stored is modified, and
                already-compatible blobs pass through byte-for-byte. Leave it on unless a specific
                room misbehaves with the clamp active.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleRoomBlobVersionClamp}
              disabled={busy}
              className={(settings.roomBlobVersionClampDisabled ? 'btn-primary' : 'btn-danger') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.roomBlobVersionClampDisabled ? 'Enable clamp' : 'Disable clamp'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.roomBlobVersionClampDisabled
              ? <span className="badge-banned">Clamp off — modern imports will demand an update</span>
              : <span className="badge-online">Clamp active — modern imports joinable</span>}
          </div>
        </div>
      )}

      {gameConfigs && (
        <DiscoveredGameConfigsPanel
          settings={gameConfigs}
          busy={gameConfigBusy}
          onChange={updateGameConfigs}
          onSave={() => saveGameConfigs(false)}
          onReset={() => saveGameConfigs(true)}
        />
      )}

      {circuits && (
        <CircuitsV2LimitsPanel
          settings={circuits}
          busy={circuitsBusy}
          onChange={updateCircuits}
          onNetworkChange={updateCircuitsNetwork}
          onSave={() => saveCircuits(false)}
          onReset={() => saveCircuits(true)}
        />
      )}

      {weekly && (
        <div className="card !p-5 max-w-5xl mt-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">Weekly challenges</h2>
              <p className="mt-1 text-xs text-ink-400">
                These are sent to the watch as the current challenge map. Players start each weekly challenge incomplete until their own progress is reported.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(weekly.updatedAt).toLocaleString()}.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <button onClick={addWeeklyChallenge} disabled={weeklyBusy} className="btn-secondary text-xs">
                <Plus />
                Add
              </button>
              <button onClick={resetWeeklyDefaults} disabled={weeklyBusy} className="btn-secondary text-xs">
                <RefreshCw />
                Defaults
              </button>
              <button onClick={saveWeeklyChallenges} disabled={weeklyBusy} className="btn-primary text-xs">
                {weeklyBusy ? 'Saving…' : 'Save'}
              </button>
            </div>
          </div>

          <label className="mt-5 flex items-center gap-3 text-sm text-ink-100">
            <input
              type="checkbox"
              checked={weekly.completedRequired}
              onChange={(e) => setWeekly({ ...weekly, completedRequired: e.target.checked })}
              className="size-4 rounded border-ink-700 bg-ink-900 text-brand-500 focus:ring-brand-500/30"
            />
            CompletedRequired
          </label>

          <div className="mt-5 rounded-lg border border-ink-800 bg-ink-950/30 p-4">
            <div className="mb-3 flex items-center justify-between gap-3">
              <h3 className="text-sm font-semibold text-ink-50">Weekly reward</h3>
              <span className="badge-admin">{weekly.reward.xp} XP · {weekly.reward.tokens} tokens</span>
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              <label className="block">
                <span className="label">XP</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.xp}
                  onChange={(e) => rewardNumber('xp', e.target.value)}
                />
              </label>
              <label className="block">
                <span className="label">Tokens</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.tokens}
                  onChange={(e) => rewardNumber('tokens', e.target.value)}
                />
              </label>
              <label className="block">
                <span className="label">Gift drop id</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.giftDropId}
                  onChange={(e) => rewardNumber('giftDropId', e.target.value)}
                />
              </label>
              <label className="block">
                <span className="label">Level</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.level}
                  onChange={(e) => rewardNumber('level', e.target.value)}
                />
              </label>
              <label className="block">
                <span className="label">Gift context</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.giftContext}
                  onChange={(e) => rewardNumber('giftContext', e.target.value)}
                />
              </label>
              <label className="block">
                <span className="label">Gift rarity</span>
                <input
                  className="input mt-1"
                  type="number"
                  min={0}
                  value={weekly.reward.giftRarity}
                  onChange={(e) => rewardNumber('giftRarity', e.target.value)}
                />
              </label>
              <label className="block md:col-span-3">
                <span className="label">Reward item</span>
                <select
                  className="input mt-1"
                  value={selectedRewardValue}
                  onChange={(e) => selectWeeklyReward(e.target.value)}
                >
                  <option value="none">None</option>
                  {selectedRewardValue === 'current' && (
                    <option value="current">Current configured item</option>
                  )}
                  <optgroup label="Avatar items">
                    {rewardOptions.avatarItems.map((option, index) => (
                      <option key={`avatar:${index}`} value={`avatar:${index}`}>{option.label}</option>
                    ))}
                  </optgroup>
                  <optgroup label="Consumables">
                    {rewardOptions.consumables.map((option, index) => (
                      <option key={`consumable:${index}`} value={`consumable:${index}`}>{option.label}</option>
                    ))}
                  </optgroup>
                  <optgroup label="Weapon skins">
                    {rewardOptions.equipment.map((option, index) => (
                      <option key={`equipment:${index}`} value={`equipment:${index}`}>{option.label}</option>
                    ))}
                  </optgroup>
                </select>
              </label>
              {selectedRewardOption && selectedRewardValue !== 'none' && (
                <div className="md:col-span-3 rounded border border-ink-800 bg-ink-950/40 px-3 py-2 text-[11px] text-ink-400">
                  <div className="font-mono break-all">
                    {selectedRewardOption.avatarItemDesc && `avatar: ${selectedRewardOption.avatarItemDesc}`}
                    {selectedRewardOption.consumableItemDesc && `consumable: ${selectedRewardOption.consumableItemDesc}`}
                    {selectedRewardOption.equipmentPrefabName && `equipment: ${selectedRewardOption.equipmentPrefabName} / ${selectedRewardOption.equipmentModificationGuid}`}
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="mt-5 grid gap-3">
            {weekly.challenges.map((challenge, index) => (
              <div key={index} className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
                <div className="mb-3 flex items-center justify-between gap-3">
                  <span className="badge-neutral">Challenge {index + 1}</span>
                  <button
                    onClick={() => removeWeeklyChallenge(index)}
                    disabled={weeklyBusy || weekly.challenges.length <= 1}
                    className="btn-ghost !px-2 !py-1 text-danger"
                    title="Remove challenge"
                  >
                    <Trash />
                  </button>
                </div>
                <div className="grid gap-3 md:grid-cols-2">
                  <label className="block">
                    <span className="label">Name</span>
                    <input
                      className="input mt-1"
                      value={challenge.name}
                      onChange={(e) => updateWeeklyChallenge(index, { name: e.target.value })}
                    />
                  </label>
                  <label className="block">
                    <span className="label">Tooltip</span>
                    <input
                      className="input mt-1"
                      value={challenge.tooltip}
                      onChange={(e) => updateWeeklyChallenge(index, { tooltip: e.target.value })}
                    />
                  </label>
                  <label className="block md:col-span-2">
                    <span className="label">Description</span>
                    <input
                      className="input mt-1"
                      value={challenge.description}
                      onChange={(e) => updateWeeklyChallenge(index, { description: e.target.value })}
                    />
                  </label>
                  <WeeklyChallengeConfigForm
                    challenge={challenge}
                    onChange={(patch) => updateWeeklyChallengeConfig(index, patch)}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function WeeklyChallengeConfigForm({
  challenge,
  onChange,
}: {
  challenge: WeeklyChallenge;
  onChange: (patch: Partial<{ type: number; amount: number }>) => void;
}) {
  const parsed = parseWeeklyChallengeConfig(challenge.config);
  const selected = WEEKLY_TYPE_BY_VALUE.get(parsed.type) ?? WEEKLY_TYPE_BY_VALUE.get(DEFAULT_WEEKLY_CHALLENGE_TYPE)!;

  return (
    <div className="md:col-span-2 rounded-lg border border-ink-800 bg-ink-950/40 p-3">
      <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_160px]">
        <label className="block">
          <span className="label">Challenge type</span>
          <select
            className="input mt-1"
            value={parsed.type}
            onChange={(e) => onChange({ type: Number.parseInt(e.target.value, 10) })}
          >
            {WEEKLY_CHALLENGE_TYPES.map((type) => (
              <option key={type.value} value={type.value}>{type.label}</option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="label">Amount</span>
          <input
            className="input mt-1"
            type="number"
            min={1}
            max={MAX_WEEKLY_CHALLENGE_AMOUNT}
            value={parsed.amount}
            onChange={(e) => onChange({ amount: Number.parseInt(e.target.value, 10) })}
          />
        </label>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2 text-[11px] text-ink-400">
        <span className="badge-neutral">ct {selected.value}</span>
        <span>{selected.note}</span>
        {parsed.amount > 1 && <span className="badge-admin">wrapped by ct 1 count</span>}
      </div>
      <div className="mt-3 rounded border border-ink-800 bg-ink-950 px-3 py-2 font-mono text-[11px] text-ink-400 break-all">
        {challenge.config}
      </div>
    </div>
  );
}

function DiscoveredGameConfigsPanel({
  settings,
  busy,
  onChange,
  onSave,
  onReset,
}: {
  settings: DiscoveredGameConfigSettings;
  busy: boolean;
  onChange: (patch: Partial<DiscoveredGameConfigSettings>) => void;
  onSave: () => void;
  onReset: () => void;
}) {
  const number = (key: keyof DiscoveredGameConfigSettings, value: string) => {
    const n = Number.parseFloat(value);
    onChange({ [key]: Number.isFinite(n) ? n : 0 } as Partial<DiscoveredGameConfigSettings>);
  };

  return (
    <div className="card !p-5 max-w-5xl mt-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-semibold text-ink-50">GameConfig</h2>
          <p className="mt-1 text-xs text-ink-400">
            Typed keys confirmed in the 2020.12 client decomp.
          </p>
          <p className="mt-2 text-[11px] text-ink-500">
            Last changed {new Date(settings.updatedAt).toLocaleString()}.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button onClick={onReset} disabled={busy} className="btn-secondary text-xs">
            <RefreshCw />
            Defaults
          </button>
          <button onClick={onSave} disabled={busy} className="btn-primary text-xs">
            {busy ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>

      <div className="mt-5 grid gap-4 lg:grid-cols-2">
        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Social</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <NumberConfig
              label="Friends.PostGamePromptUnderFriendCount"
              value={settings.friendsPostGamePromptUnderFriendCount}
              onChange={(v) => number('friendsPostGamePromptUnderFriendCount', v)}
            />
            <NumberConfig
              label="Friends.SuggestFriendCodeOnFriendsScreenCount"
              value={settings.friendsSuggestFriendCodeOnFriendsScreenCount}
              onChange={(v) => number('friendsSuggestFriendCodeOnFriendsScreenCount', v)}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Account gates</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <BoolConfig
              label="Screens.ForceVerification"
              checked={settings.screensForceVerification}
              onChange={(v) => onChange({ screensForceVerification: v })}
            />
            <BoolConfig
              label="VR.ForceVerification"
              checked={settings.vrForceVerification}
              onChange={(v) => onChange({ vrForceVerification: v })}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Rewards</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <BoolConfig
              label="Rewards.UseRewardSelection"
              checked={settings.rewardsUseRewardSelection}
              onChange={(v) => onChange({ rewardsUseRewardSelection: v })}
            />
            <NumberConfig
              label="Rewards.SelectionTimeout"
              value={settings.rewardsSelectionTimeout}
              onChange={(v) => number('rewardsSelectionTimeout', v)}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Room details</h3>
          <div className="mt-3">
            <BoolConfig
              label="RoomDetails.PhotoRollEnabled"
              checked={settings.roomDetailsPhotoRollEnabled}
              onChange={(v) => onChange({ roomDetailsPhotoRollEnabled: v })}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Networking</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <NumberConfig
              label="loadingNetworkTimeout"
              value={settings.loadingNetworkTimeout}
              onChange={(v) => number('loadingNetworkTimeout', v)}
            />
            <NumberConfig
              label="runningNetworkTimeout"
              value={settings.runningNetworkTimeout}
              onChange={(v) => number('runningNetworkTimeout', v)}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Rendering / sync</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <BoolConfig
              label="SynchronizedField.RemoveDefaultEntries"
              checked={settings.synchronizedFieldRemoveDefaultEntries}
              onChange={(v) => onChange({ synchronizedFieldRemoveDefaultEntries: v })}
            />
            <BoolConfig
              label="Rendering.DisableSrpBatcher"
              checked={settings.renderingDisableSrpBatcher}
              onChange={(v) => onChange({ renderingDisableSrpBatcher: v })}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4 lg:col-span-2">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Split tests</h3>
          <div className="mt-3 grid gap-3 lg:grid-cols-3">
            <JsonConfig
              label="splitTestSoftOverrides"
              value={settings.splitTestSoftOverrides}
              onChange={(v) => onChange({ splitTestSoftOverrides: v })}
            />
            <JsonConfig
              label="splitTestHardOverrides"
              value={settings.splitTestHardOverrides}
              onChange={(v) => onChange({ splitTestHardOverrides: v })}
            />
            <JsonConfig
              label="splitTestSegmentProbabilities"
              value={settings.splitTestSegmentProbabilities}
              onChange={(v) => onChange({ splitTestSegmentProbabilities: v })}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

function CircuitsV2LimitsPanel({
  settings,
  busy,
  onChange,
  onNetworkChange,
  onSave,
  onReset,
}: {
  settings: CircuitsV2LimitSettings;
  busy: boolean;
  onChange: (patch: Partial<CircuitsV2LimitSettings>) => void;
  onNetworkChange: (index: number, patch: Partial<CircuitsV2NetworkLimit>) => void;
  onSave: () => void;
  onReset: () => void;
}) {
  const number = (key: keyof CircuitsV2LimitSettings, value: string) => {
    const n = Number.parseFloat(value);
    onChange({ [key]: Number.isFinite(n) ? n : 0 } as Partial<CircuitsV2LimitSettings>);
  };

  const netNumber = (index: number, key: keyof CircuitsV2NetworkLimit, value: string) => {
    const n = Number.parseInt(value, 10);
    onNetworkChange(index, { [key]: Number.isFinite(n) ? n : 0 } as Partial<CircuitsV2NetworkLimit>);
  };

  return (
    <div className="card !p-5 max-w-5xl mt-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-semibold text-ink-50">CircuitsV2 limits</h2>
          <p className="mt-1 text-xs text-ink-400">
            Served as the <span className="font-mono">UGC.CircuitsV2.Limit.*</span> gameconfig keys.
            Retail budgets a circuit 5 RPCs per frame and 1024 bytes per second and silently drops
            everything past that; DorkNet ships both effectively unlimited, and raises the room-size
            limits below about 10x past retail. Clients read these once
            when circuits start, so players must rejoin their room to pick up a change.
          </p>
          <p className="mt-2 text-[11px] text-ink-500">
            Last changed {new Date(settings.updatedAt).toLocaleString()}.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button onClick={onReset} disabled={busy} className="btn-secondary text-xs">
            <RefreshCw />
            Defaults
          </button>
          <button onClick={onSave} disabled={busy} className="btn-primary text-xs">
            {busy ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>

      <div className="mt-5 grid gap-4 lg:grid-cols-2">
        {settings.network.map((entry, index) => (
          <div key={index} className="rounded-lg border border-ink-800 bg-ink-950/30 p-4 lg:col-span-2">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">
              Network budget{settings.network.length > 1 ? ` — from ${entry.numPlayersForActivation} players` : ''}
            </h3>
            <p className="mt-1 text-[11px] text-ink-500">
              Each budget is spent over a rolling window measured in frames, so a window of 1 means
              per frame and 60 means roughly per second at 60fps. Widening a window makes the limit
              stricter, not looser — raise the budget instead.
            </p>
            <div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <NumberConfig
                label="Rpcs"
                value={entry.rpcs}
                onChange={(v) => netNumber(index, 'rpcs', v)}
              />
              <NumberConfig
                label="RpcTimeWindowFrame60s"
                value={entry.rpcTimeWindowFrame60s}
                onChange={(v) => netNumber(index, 'rpcTimeWindowFrame60s', v)}
              />
              <NumberConfig
                label="Bytes"
                value={entry.bytes}
                onChange={(v) => netNumber(index, 'bytes', v)}
              />
              <NumberConfig
                label="BytesTimeWindowFrame60s"
                value={entry.bytesTimeWindowFrame60s}
                onChange={(v) => netNumber(index, 'bytesTimeWindowFrame60s', v)}
              />
            </div>
          </div>
        ))}

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Circuit budget</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <NumberConfig
              label="Compute"
              value={settings.compute}
              hint="Per-frame CPU budget for all circuits in the room. Retail 50000. Keep it finite: it is what stops a runaway loop. Max 10,000,000."
              onChange={(v) => number('compute', v)}
            />
            <NumberConfig
              label="Chips"
              value={settings.chips}
              hint="Room chip cap. Retail 3000. Max 1,000,000."
              onChange={(v) => number('chips', v)}
            />
            <NumberConfig
              label="UnculledChips"
              value={settings.unculledChips}
              hint="Chips past this count get culled. Retail 500. Higher costs frame time."
              onChange={(v) => number('unculledChips', v)}
            />
            <NumberConfig
              label="VisibleChips"
              value={settings.visibleChips}
              hint="Visible-chip limit. Retail 500. Above Chips, adding a chip also charges InkPerChip."
              onChange={(v) => number('visibleChips', v)}
            />
            <NumberConfig
              label="InkPerChip"
              value={settings.inkPerChip}
              hint="Ink COST per chip against the fixed 30000-ink room cap. Retail 15; 0 = chips use no ink. Max 30,000."
              onChange={(v) => number('inkPerChip', v)}
            />
          </div>
        </div>

        <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-ink-400">Cloud variables</h3>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <NumberConfig
              label="CloudValues"
              value={settings.cloudValues}
              hint="Room cloud-variable cap. Retail 100. Max 10,000."
              onChange={(v) => number('cloudValues', v)}
            />
            <NumberConfig
              label="CloudValuesAutosaveSeconds"
              value={settings.cloudValuesAutosaveSeconds}
              hint="Seconds between player save-data autosaves. Retail 300. Lower means more save traffic."
              onChange={(v) => number('cloudValuesAutosaveSeconds', v)}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

function NumberConfig({
  label,
  value,
  hint,
  onChange,
}: {
  label: string;
  value: number;
  hint?: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block">
      <span className="label font-mono normal-case tracking-normal">{label}</span>
      <input className="input mt-1" type="number" value={value} onChange={(e) => onChange(e.target.value)} />
      {hint && <span className="mt-1 block text-[11px] text-ink-500">{hint}</span>}
    </label>
  );
}

function BoolConfig({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex min-h-10 items-center justify-between gap-3 rounded-md border border-ink-800 bg-ink-900/50 px-3 py-2">
      <span className="font-mono text-xs text-ink-100">{label}</span>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="size-4 rounded border-ink-700 bg-ink-950 text-brand-500 focus:ring-brand-500/30"
      />
    </label>
  );
}

function JsonConfig({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block">
      <span className="label font-mono normal-case tracking-normal">{label}</span>
      <textarea
        className="input mt-1 min-h-28 font-mono text-xs"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}

function defaultCircuitsLimits(): CircuitsV2LimitSettings {
  return {
    network: [{
      numPlayersForActivation: 0,
      bytes: 1_000_000_000,
      bytesTimeWindowFrame60s: 60,
      rpcs: 1_000_000_000,
      rpcTimeWindowFrame60s: 1,
    }],
    compute: 500_000,
    chips: 30_000,
    cloudValues: 1_000,
    cloudValuesAutosaveSeconds: 300,
    unculledChips: 3_000,
    visibleChips: 3_000,
    inkPerChip: 0,
    updatedAt: new Date().toISOString(),
  };
}

function defaultGameConfigs(): DiscoveredGameConfigSettings {
  return {
    friendsPostGamePromptUnderFriendCount: 3,
    friendsSuggestFriendCodeOnFriendsScreenCount: 5,
    screensForceVerification: false,
    vrForceVerification: false,
    rewardsUseRewardSelection: false,
    rewardsSelectionTimeout: 30,
    roomDetailsPhotoRollEnabled: false,
    loadingNetworkTimeout: 30,
    runningNetworkTimeout: 30,
    synchronizedFieldRemoveDefaultEntries: false,
    renderingDisableSrpBatcher: false,
    splitTestSoftOverrides: '{}',
    splitTestHardOverrides: '{}',
    splitTestSegmentProbabilities: '{}',
    updatedAt: new Date().toISOString(),
  };
}
