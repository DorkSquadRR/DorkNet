import { useEffect, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, RefreshCw, Trash } from '../components/Icons';

interface ServerSettings {
  signupsDisabled: boolean;
  weeklyChallengesCompletedRequired: boolean;
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

export function Settings({ embedded }: { embedded?: boolean } = {}) {
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [weekly, setWeekly] = useState<WeeklyChallengeSettings | null>(null);
  const [gameConfigs, setGameConfigs] = useState<DiscoveredGameConfigSettings | null>(null);
  const [rewardOptions, setRewardOptions] = useState<RewardOptions>({
    avatarItems: [],
    consumables: [],
    equipment: [],
  });
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [weeklyBusy, setWeeklyBusy] = useState(false);
  const [gameConfigBusy, setGameConfigBusy] = useState(false);
  const toast = useToast();

  const load = async () => {
    try {
      const [s, w, rewards, configs] = await Promise.all([
        get<ServerSettings>('/settings'),
        get<WeeklyChallengeSettings>('/settings/weekly-challenges'),
        get<RewardOptions>('/settings/weekly-challenges/reward-options'),
        get<DiscoveredGameConfigSettings>('/settings/gameconfigs'),
      ]);
      setSettings(s);
      setWeekly(w);
      setRewardOptions(rewards);
      setGameConfigs(configs);
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
            config: '{}',
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

      {gameConfigs && (
        <DiscoveredGameConfigsPanel
          settings={gameConfigs}
          busy={gameConfigBusy}
          onChange={updateGameConfigs}
          onSave={() => saveGameConfigs(false)}
          onReset={() => saveGameConfigs(true)}
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
                  <label className="block md:col-span-2">
                    <span className="label">Config</span>
                    <textarea
                      className="input mt-1 min-h-24 font-mono text-xs"
                      value={challenge.config}
                      onChange={(e) => updateWeeklyChallenge(index, { config: e.target.value })}
                    />
                  </label>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
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

function NumberConfig({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block">
      <span className="label font-mono normal-case tracking-normal">{label}</span>
      <input className="input mt-1" type="number" value={value} onChange={(e) => onChange(e.target.value)} />
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
