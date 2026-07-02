import { useEffect, useMemo, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, Trash, RefreshCw } from '../components/Icons';

// Difficulty values mirror the client CNMMMNJJDMM enum (verified in the
// 2023.03.21 il2cpp dump). The Charades slot uses easy/hard/very-hard; the
// Icebreakers slot uses its own value so cards group correctly in-game.
const DIFFICULTIES = [
  { value: 0, label: 'Easy' },
  { value: 1, label: 'Hard' },
  { value: 10, label: 'Very hard' },
  { value: 20, label: 'Icebreaker' },
] as const;

// The three baked client card-source slots. Each is fed by whichever
// library list its binding points at.
const SLOTS = [
  {
    key: 'charades' as const,
    title: 'Charades',
    blurb: 'The main 3D Charades deck players draw from.',
  },
  {
    key: 'charadesAprilFoolsDay' as const,
    title: 'April Fools',
    blurb: 'The seasonal "impossible to explain" deck.',
  },
  {
    key: 'icebreakers' as const,
    title: 'Icebreakers',
    blurb: 'Prompt-style cards for the Icebreakers card box.',
  },
];

interface CharadesWord {
  text: string;
  difficulty: number;
}

interface WordList {
  id: number;
  name: string;
  words: CharadesWord[];
  isBuiltIn: boolean;
  updatedAt: string;
}

interface SlotBindings {
  charades: number;
  charadesAprilFoolsDay: number;
  icebreakers: number;
}

interface WordListsResponse {
  lists: WordList[];
  bindings: SlotBindings;
}

export function Charades({ embedded }: { embedded?: boolean } = {}) {
  const [lists, setLists] = useState<WordList[] | null>(null);
  const [bindings, setBindings] = useState<SlotBindings | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const load = async () => {
    try {
      const data = await get<WordListsResponse>('/charades/wordlists');
      setLists(data.lists);
      setBindings(data.bindings);
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  useEffect(() => { void load(); }, []);

  const updateLocal = (idx: number, next: WordList) => {
    setLists(cur => {
      if (!cur) return cur;
      const copy = cur.slice();
      copy[idx] = next;
      return copy;
    });
  };

  const addNew = async () => {
    setBusy(true);
    try {
      const created = await api<WordList>('/charades/wordlists', {
        method: 'POST',
        body: { Name: 'New list', Words: [] },
      });
      setLists([...(lists ?? []), created]);
      toast.push('List created.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const saveList = async (idx: number) => {
    if (!lists) return;
    const l = lists[idx];
    try {
      const saved = await api<WordList>(`/charades/wordlists/${l.id}`, {
        method: 'PUT',
        body: { Name: l.name, Words: l.words },
      });
      updateLocal(idx, saved);
      toast.push('Saved.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const removeList = async (idx: number) => {
    if (!lists) return;
    const l = lists[idx];
    const boundSlot = bindings && (
      bindings.charades === l.id ||
      bindings.charadesAprilFoolsDay === l.id ||
      bindings.icebreakers === l.id);
    const warn = boundSlot
      ? ` It's currently live on a slot — that slot will fall back to its built-in list.`
      : '';
    if (!confirm(`Delete list "${l.name || '(untitled)'}"?${warn}`)) return;
    try {
      await api(`/charades/wordlists/${l.id}`, { method: 'DELETE' });
      setLists(lists.filter((_, i) => i !== idx));
      // Reflect any binding fallback locally.
      setBindings(cur => cur ? {
        charades: cur.charades === l.id ? 0 : cur.charades,
        charadesAprilFoolsDay: cur.charadesAprilFoolsDay === l.id ? 0 : cur.charadesAprilFoolsDay,
        icebreakers: cur.icebreakers === l.id ? 0 : cur.icebreakers,
      } : cur);
      toast.push('Deleted.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const importInto = async (idx: number, text: string, defaultDifficulty: number, replace: boolean) => {
    if (!lists) return;
    const l = lists[idx];
    try {
      const saved = await api<WordList>(`/charades/wordlists/${l.id}/import`, {
        method: 'POST',
        body: { Text: text, DefaultDifficulty: defaultDifficulty, Replace: replace },
      });
      updateLocal(idx, saved);
      toast.push(replace ? 'List replaced from paste.' : 'Cards imported.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const saveBindings = async () => {
    if (!bindings) return;
    setBusy(true);
    try {
      const saved = await api<SlotBindings>('/charades/bindings', {
        method: 'PUT',
        body: {
          Charades: bindings.charades,
          CharadesAprilFoolsDay: bindings.charadesAprilFoolsDay,
          Icebreakers: bindings.icebreakers,
        },
      });
      setBindings(saved);
      toast.push('Live slots updated — effective on next card-box refresh.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const headerActions = (
    <div className="flex gap-2">
      <button onClick={load} className="btn-secondary text-xs" disabled={busy}>
        <RefreshCw className={busy ? 'animate-spin' : ''} /> Refresh
      </button>
      <button onClick={addNew} className="btn-primary text-xs" disabled={busy}>
        <Plus /> New list
      </button>
    </div>
  );

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{headerActions}</div>
      ) : (
        <PageHeader
          title="3D Charades words"
          blurb="Keep an unlimited library of word lists and pick which one is live for each in-game card box. The March 2023 client fetches the bound list at card-box spawn."
          actions={headerActions}
        />
      )}

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{err}</div>
      )}

      {lists === null && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading…</div>
      )}

      {lists && bindings && (
        <div className="card !p-5 mb-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold text-ink-50">Live card slots</h2>
              <p className="mt-1 text-xs text-ink-400">
                The client only ever requests these three card sources. Point each at any list in
                your library to switch decks — no redeploy. Changes apply the next time a card box
                spawns (room rejoin).
              </p>
            </div>
            <button onClick={saveBindings} disabled={busy} className="btn-primary text-xs shrink-0">
              {busy ? 'Saving…' : 'Save slots'}
            </button>
          </div>
          <div className="mt-4 grid gap-3 md:grid-cols-3">
            {SLOTS.map(slot => (
              <label key={slot.key} className="block rounded-lg border border-ink-800 bg-ink-950/30 p-3">
                <span className="text-sm font-semibold text-ink-100">{slot.title}</span>
                <span className="mt-0.5 block text-[11px] text-ink-500">{slot.blurb}</span>
                <select
                  className="input mt-2 text-xs"
                  value={bindings[slot.key]}
                  onChange={e => setBindings({ ...bindings, [slot.key]: parseInt(e.target.value) || 0 })}
                >
                  <option value={0}>— built-in fallback —</option>
                  {lists.map(l => (
                    <option key={l.id} value={l.id}>
                      {l.name} ({l.words.length})
                    </option>
                  ))}
                </select>
              </label>
            ))}
          </div>
        </div>
      )}

      {lists && lists.length === 0 && (
        <div className="card !p-6 text-center text-xs text-ink-400">
          No lists yet — add one with the button above.
        </div>
      )}

      {lists && (
        <div className="space-y-3">
          {lists.map((l, i) => (
            <ListEditor
              key={l.id}
              list={l}
              liveSlots={liveSlotsFor(l.id, bindings)}
              onChange={next => updateLocal(i, next)}
              onSave={() => saveList(i)}
              onRemove={() => removeList(i)}
              onImport={(text, diff, replace) => importInto(i, text, diff, replace)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function liveSlotsFor(id: number, bindings: SlotBindings | null): string[] {
  if (!bindings) return [];
  const out: string[] = [];
  if (bindings.charades === id) out.push('Charades');
  if (bindings.charadesAprilFoolsDay === id) out.push('April Fools');
  if (bindings.icebreakers === id) out.push('Icebreakers');
  return out;
}

function ListEditor({
  list,
  liveSlots,
  onChange,
  onSave,
  onRemove,
  onImport,
}: {
  list: WordList;
  liveSlots: string[];
  onChange: (next: WordList) => void;
  onSave: () => void;
  onRemove: () => void;
  onImport: (text: string, defaultDifficulty: number, replace: boolean) => void;
}) {
  const [open, setOpen] = useState(false);
  const [pasteText, setPasteText] = useState('');
  const [pasteDifficulty, setPasteDifficulty] = useState(0);
  const [pasteReplace, setPasteReplace] = useState(false);

  const counts = useMemo(() => {
    const by = new Map<number, number>();
    for (const w of list.words) by.set(w.difficulty, (by.get(w.difficulty) ?? 0) + 1);
    return by;
  }, [list.words]);

  const setWord = (idx: number, patch: Partial<CharadesWord>) => {
    const words = list.words.slice();
    words[idx] = { ...words[idx], ...patch };
    onChange({ ...list, words });
  };
  const removeWord = (idx: number) => {
    onChange({ ...list, words: list.words.filter((_, i) => i !== idx) });
  };
  const addWord = () => {
    onChange({ ...list, words: [...list.words, { text: '', difficulty: 0 }] });
  };

  const runImport = () => {
    if (!pasteText.trim()) return;
    onImport(pasteText, pasteDifficulty, pasteReplace);
    setPasteText('');
  };

  return (
    <div className="card !p-4">
      <div className="flex items-center gap-3">
        <input
          value={list.name}
          onChange={e => onChange({ ...list, name: e.target.value })}
          className="input text-sm font-semibold flex-1 min-w-0"
          maxLength={128}
        />
        {list.isBuiltIn && <span className="badge-admin shrink-0">built-in</span>}
        {liveSlots.map(s => <span key={s} className="badge-online shrink-0">live: {s}</span>)}
        <span className="text-[11px] text-ink-500 shrink-0">{list.words.length} cards</span>
        <button onClick={() => setOpen(o => !o)} className="btn-secondary text-xs shrink-0">
          {open ? 'Collapse' : 'Edit'}
        </button>
      </div>

      <div className="mt-2 flex flex-wrap gap-1.5 text-[11px] text-ink-400">
        {DIFFICULTIES.map(d => counts.get(d.value)
          ? <span key={d.value} className="badge-neutral">{d.label}: {counts.get(d.value)}</span>
          : null)}
      </div>

      {open && (
        <div className="mt-4 space-y-4">
          <div className="space-y-2">
            {list.words.map((w, i) => (
              <div key={i} className="flex items-center gap-2">
                <input
                  value={w.text}
                  onChange={e => setWord(i, { text: e.target.value })}
                  placeholder="Card phrase"
                  className="input text-xs flex-1"
                  maxLength={256}
                />
                <select
                  value={w.difficulty}
                  onChange={e => setWord(i, { difficulty: parseInt(e.target.value) })}
                  className="input text-xs w-32 shrink-0"
                >
                  {DIFFICULTIES.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                </select>
                <button onClick={() => removeWord(i)} className="btn-ghost text-xs text-danger shrink-0" title="Remove card">
                  <Trash />
                </button>
              </div>
            ))}
            <button onClick={addWord} className="btn-secondary text-xs"><Plus /> Add card</button>
          </div>

          <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-3">
            <h4 className="text-xs font-semibold text-ink-200">Import from paste</h4>
            <p className="mt-0.5 text-[11px] text-ink-500">
              One phrase per line. Add <span className="font-mono">| easy</span>,{' '}
              <span className="font-mono">| hard</span>, <span className="font-mono">| veryhard</span> or{' '}
              <span className="font-mono">| icebreaker</span> after a line to set its difficulty.
            </p>
            <textarea
              value={pasteText}
              onChange={e => setPasteText(e.target.value)}
              placeholder={'Dancing\nRiding a bike | hard\nThe concept of time | veryhard'}
              className="input text-xs w-full mt-2 font-mono"
              rows={5}
            />
            <div className="mt-2 flex flex-wrap items-center gap-3">
              <label className="flex items-center gap-1.5 text-xs text-ink-300">
                Default difficulty
                <select
                  value={pasteDifficulty}
                  onChange={e => setPasteDifficulty(parseInt(e.target.value))}
                  className="input text-xs w-28"
                >
                  {DIFFICULTIES.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                </select>
              </label>
              <label className="flex items-center gap-1.5 text-xs text-ink-300 cursor-pointer">
                <input type="checkbox" checked={pasteReplace} onChange={e => setPasteReplace(e.target.checked)} />
                Replace existing cards
              </label>
              <div className="flex-1" />
              <button onClick={runImport} disabled={!pasteText.trim()} className="btn-primary text-xs">
                {pasteReplace ? 'Replace from paste' : 'Import cards'}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="mt-4 flex items-center gap-3">
        <span className="text-[11px] text-ink-600 flex-1">
          Last saved {new Date(list.updatedAt).toLocaleString()}
        </span>
        <button onClick={onSave} className="btn-primary text-xs">Save</button>
        <button onClick={onRemove} className="btn-ghost text-xs text-danger" title="Delete list">
          <Trash />
        </button>
      </div>
    </div>
  );
}
