import { useEffect, useMemo, useState } from 'react';
import { api, del, get, post, put } from '../lib/api';
import { absoluteTime, relativeTime } from '../lib/format';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, RefreshCw, Trash } from '../components/Icons';

interface TestPass {
  id: number;
  name: string;
  description: string;
  startDate: string;
  endDate: string | null;
  wasManuallyClosed: boolean;
  tags: string[];
  numTestCases: number;
  numClaimedTestCases: number;
  numPassedTestCases: number;
  numFailedTestCases: number;
}

interface TestCase {
  id: string;
  key: string;
  title: string;
  description: string;
  roomName: string;
  status: number;
  minNumAssignedPlayers: number;
  assignedPlayerIds: number[];
  assignedPlayerNames: string[];
  tags: string[];
  jiraUrl: string;
  jiraBugUrl: string;
  testPassId: number | null;
  createdAt?: string;
  updatedAt?: string;
}

interface TestCaseListResponse {
  total: number;
  items: TestCase[];
}

const statuses = [
  { value: 0, label: 'Not tested', badge: 'badge-neutral' },
  { value: 1, label: 'Claimed', badge: 'badge-admin' },
  { value: 2, label: 'Failed', badge: 'badge-banned' },
  { value: 3, label: 'Passed', badge: 'badge-online' },
];

const emptyPass = (): TestPass => ({
  id: 0,
  name: '',
  description: '',
  startDate: new Date().toISOString(),
  endDate: null,
  wasManuallyClosed: false,
  tags: [],
  numTestCases: 0,
  numClaimedTestCases: 0,
  numPassedTestCases: 0,
  numFailedTestCases: 0,
});

const emptyCase = (testPassId: number | null): TestCase => ({
  id: '',
  key: '',
  title: '',
  description: '',
  roomName: '',
  status: 0,
  minNumAssignedPlayers: 1,
  assignedPlayerIds: [],
  assignedPlayerNames: [],
  tags: [],
  jiraUrl: '',
  jiraBugUrl: '',
  testPassId,
});

export function TestManagement() {
  const [passes, setPasses] = useState<TestPass[]>([]);
  const [cases, setCases] = useState<TestCase[]>([]);
  const [selectedPassId, setSelectedPassId] = useState<number | 'all' | null>(null);
  const [statusFilter, setStatusFilter] = useState<number | 'all'>('all');
  const [query, setQuery] = useState('');
  const [selectedCase, setSelectedCase] = useState<TestCase | null>(null);
  const [editingPass, setEditingPass] = useState<TestPass | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const toast = useToast();

  const selectedPass = useMemo(
    () => typeof selectedPassId === 'number' ? passes.find(p => p.id === selectedPassId) ?? null : null,
    [passes, selectedPassId],
  );

  const loadPasses = async () => {
    const rows = await get<TestPass[]>('/testpasses');
    setPasses(rows);
    setSelectedPassId(current => current === null ? (rows[0]?.id ?? 'all') : current);
  };

  const loadCases = async () => {
    const params = new URLSearchParams({ take: '500' });
    if (typeof selectedPassId === 'number') params.set('testPassId', String(selectedPassId));
    if (statusFilter !== 'all') params.set('status', String(statusFilter));
    if (query.trim()) params.set('query', query.trim());
    const res = await get<TestCaseListResponse>(`/testcases?${params.toString()}`);
    setCases(res.items);
  };

  const reload = async () => {
    setLoading(true);
    setErr(null);
    try {
      await loadPasses();
      await loadCases();
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    loadCases().catch(e => setErr((e as Error).message));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedPassId, statusFilter]);

  const search = async () => {
    try { await loadCases(); }
    catch (e) { toast.push((e as Error).message, 'error'); }
  };

  const savePass = async (pass: TestPass) => {
    try {
      const payload = passToPayload(pass);
      const saved = pass.id ? await put<TestPass>(`/testpasses/${pass.id}`, payload) : await post<TestPass>('/testpasses', payload);
      setEditingPass(saved);
      await loadPasses();
      toast.push('Test pass saved', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const deletePass = async (pass: TestPass) => {
    if (!confirm(`Delete "${pass.name || `pass ${pass.id}`}"?`)) return;
    const removeCases = pass.numTestCases > 0 && confirm('Also delete the cases in this pass? Cancel keeps the cases as standalone.');
    try {
      await api(`/testpasses/${pass.id}?deleteCases=${removeCases ? 'true' : 'false'}`, { method: 'DELETE' });
      setEditingPass(null);
      setSelectedPassId('all');
      await reload();
      toast.push(removeCases ? 'Pass and cases deleted' : 'Pass deleted, cases detached', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const saveCase = async (testCase: TestCase) => {
    try {
      const payload = caseToPayload(testCase);
      const saved = testCase.createdAt
        ? await put<TestCase>(`/testcases/${encodeURIComponent(testCase.id)}`, payload)
        : await post<TestCase>('/testcases', payload);
      setSelectedCase(saved);
      await loadPasses();
      await loadCases();
      toast.push('Test case saved', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const deleteCase = async (testCase: TestCase) => {
    if (!confirm(`Delete test case "${testCase.id || testCase.title}"?`)) return;
    try {
      await del(`/testcases/${encodeURIComponent(testCase.id)}`);
      setSelectedCase(null);
      await loadPasses();
      await loadCases();
      toast.push('Test case deleted', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div className="space-y-5">
      <PageHeader
        title="Test management"
        blurb="Create test passes, attach cases, track claims, and keep pass/fail state in the API the watch already consumes."
        actions={(
          <div className="flex gap-2">
            <button onClick={() => setEditingPass(emptyPass())} className="btn-secondary text-xs"><Plus /> Pass</button>
            <button onClick={() => setSelectedCase(emptyCase(typeof selectedPassId === 'number' ? selectedPassId : null))} className="btn-primary text-xs"><Plus /> Case</button>
            <button onClick={reload} className="btn-secondary text-xs" disabled={loading}><RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh</button>
          </div>
        )}
      />

      {err && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>}

      <section className="grid grid-cols-1 2xl:grid-cols-[360px_minmax(0,1fr)] gap-4">
        <div className="space-y-3">
          <button
            className={`w-full text-left card !rounded-lg p-3 ${selectedPassId === 'all' ? 'ring-1 ring-brand-500/50' : ''}`}
            onClick={() => setSelectedPassId('all')}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-semibold text-ink-50">All test cases</span>
              <span className="badge-neutral">{passes.reduce((n, p) => n + p.numTestCases, 0)}</span>
            </div>
            <p className="mt-1 text-xs text-ink-400">Includes cases not assigned to a pass.</p>
          </button>

          {passes.map(pass => (
            <button
              key={pass.id}
              className={`w-full text-left card !rounded-lg p-3 hover:bg-ink-800/60 ${selectedPassId === pass.id ? 'ring-1 ring-brand-500/50' : ''}`}
              onClick={() => setSelectedPassId(pass.id)}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <div className="text-sm font-semibold text-ink-50 truncate">{pass.name || `Pass ${pass.id}`}</div>
                  <div className="text-[11px] text-ink-500">#{pass.id} · {relativeTime(pass.startDate)}</div>
                </div>
                {pass.wasManuallyClosed || pass.endDate ? <span className="badge-neutral">Closed</span> : <span className="badge-online">Open</span>}
              </div>
              <Progress pass={pass} />
              <div className="mt-2 flex justify-between text-[11px] text-ink-400">
                <span>{pass.numPassedTestCases} passed</span>
                <span>{pass.numFailedTestCases} failed</span>
                <span>{pass.numClaimedTestCases} claimed</span>
              </div>
            </button>
          ))}

          {passes.length === 0 && !loading && (
            <div className="card !rounded-lg p-4 text-xs text-ink-400">No test passes yet.</div>
          )}
        </div>

        <div className="card overflow-hidden">
          <div className="border-b border-ink-800 px-4 py-3 flex flex-wrap items-center gap-2">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold text-ink-50 truncate">{selectedPass?.name ?? 'Test cases'}</h2>
              <p className="text-xs text-ink-400">{cases.length} shown</p>
            </div>
            {selectedPass && <button onClick={() => setEditingPass(selectedPass)} className="btn-secondary text-xs ml-auto">Edit pass</button>}
            <select value={statusFilter} onChange={e => setStatusFilter(e.target.value === 'all' ? 'all' : Number(e.target.value))} className="input !w-36 text-xs">
              <option value="all">All statuses</option>
              {statuses.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
            <input value={query} onChange={e => setQuery(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') search(); }} placeholder="Search cases" className="input !w-52 text-xs" />
            <button onClick={search} className="btn-secondary text-xs">Search</button>
          </div>

          <div className="table-scroll">
            <table className="w-full min-w-[960px] text-sm">
              <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/60 border-b border-ink-800">
                <tr>
                  <th className="text-left font-medium px-4 py-2.5">Case</th>
                  <th className="text-left font-medium px-4 py-2.5">Room</th>
                  <th className="text-left font-medium px-4 py-2.5">Status</th>
                  <th className="text-left font-medium px-4 py-2.5">Assigned</th>
                  <th className="text-left font-medium px-4 py-2.5">Tags</th>
                  <th className="text-right font-medium px-4 py-2.5">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-800">
                {cases.map(testCase => (
                  <tr key={testCase.id} className="table-row-hover">
                    <td className="px-4 py-2.5">
                      <div className="font-medium text-ink-50">{testCase.title || '(untitled)'}</div>
                      <div className="text-xs text-ink-400">{testCase.id}{testCase.key && testCase.key !== testCase.id ? ` · ${testCase.key}` : ''}</div>
                    </td>
                    <td className="px-4 py-2.5 text-ink-300">{testCase.roomName || '-'}</td>
                    <td className="px-4 py-2.5"><StatusBadge value={testCase.status} /></td>
                    <td className="px-4 py-2.5 text-xs text-ink-300">{testCase.assignedPlayerNames.join(', ') || testCase.assignedPlayerIds.join(', ') || '-'}</td>
                    <td className="px-4 py-2.5 text-xs text-ink-400">{testCase.tags.join(', ') || '-'}</td>
                    <td className="px-4 py-2.5 text-right">
                      <button onClick={() => setSelectedCase(testCase)} className="btn-ghost text-xs">Edit</button>
                    </td>
                  </tr>
                ))}
                {cases.length === 0 && (
                  <tr><td colSpan={6} className="py-10 text-center text-xs text-ink-400">No matching test cases.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {editingPass && (
        <PassEditor
          pass={editingPass}
          onChange={setEditingPass}
          onCancel={() => setEditingPass(null)}
          onSave={savePass}
          onDelete={editingPass.id ? deletePass : undefined}
        />
      )}

      {selectedCase && (
        <CaseEditor
          testCase={selectedCase}
          passes={passes}
          onChange={setSelectedCase}
          onCancel={() => setSelectedCase(null)}
          onSave={saveCase}
          onDelete={selectedCase.createdAt ? deleteCase : undefined}
        />
      )}
    </div>
  );
}

function Progress({ pass }: { pass: TestPass }) {
  const total = Math.max(pass.numTestCases, 1);
  const passed = (pass.numPassedTestCases / total) * 100;
  const failed = (pass.numFailedTestCases / total) * 100;
  return (
    <div className="mt-3 h-2 overflow-hidden rounded bg-ink-800">
      <div className="h-full bg-success float-left" style={{ width: `${passed}%` }} />
      <div className="h-full bg-danger float-left" style={{ width: `${failed}%` }} />
    </div>
  );
}

function StatusBadge({ value }: { value: number }) {
  const status = statuses.find(s => s.value === value) ?? statuses[0];
  return <span className={status.badge}>{status.label}</span>;
}

function PassEditor({ pass, onChange, onCancel, onSave, onDelete }: {
  pass: TestPass;
  onChange: (pass: TestPass) => void;
  onCancel: () => void;
  onSave: (pass: TestPass) => void;
  onDelete?: (pass: TestPass) => void;
}) {
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink-950/70 px-4">
      <div className="card !rounded-lg w-full max-w-2xl p-4 space-y-3">
        <div>
          <h2 className="text-sm font-semibold text-ink-50">{pass.id ? 'Edit test pass' : 'New test pass'}</h2>
          {pass.id !== 0 && <p className="text-xs text-ink-400">#{pass.id} · started {absoluteTime(pass.startDate)}</p>}
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-[160px_minmax(0,1fr)] gap-2">
          <label className="text-xs text-ink-300">Id<input type="number" value={pass.id || ''} disabled={pass.id !== 0} onChange={e => onChange({ ...pass, id: Number(e.target.value) || 0 })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Name<input value={pass.name} onChange={e => onChange({ ...pass, name: e.target.value })} className="input mt-1 text-xs" /></label>
        </div>
        <label className="text-xs text-ink-300">Description<textarea value={pass.description} onChange={e => onChange({ ...pass, description: e.target.value })} className="input mt-1 text-xs" rows={3} /></label>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
          <label className="text-xs text-ink-300">Start<input type="datetime-local" value={toLocalInput(pass.startDate)} onChange={e => onChange({ ...pass, startDate: fromLocalInput(e.target.value) })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">End<input type="datetime-local" value={pass.endDate ? toLocalInput(pass.endDate) : ''} onChange={e => onChange({ ...pass, endDate: e.target.value ? fromLocalInput(e.target.value) : null })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Tags<input value={pass.tags.join(', ')} onChange={e => onChange({ ...pass, tags: splitCsv(e.target.value) })} className="input mt-1 text-xs" /></label>
        </div>
        <label className="flex items-center gap-2 text-xs text-ink-300"><input type="checkbox" checked={pass.wasManuallyClosed} onChange={e => onChange({ ...pass, wasManuallyClosed: e.target.checked })} /> Manually closed</label>
        <div className="flex justify-end gap-2">
          {onDelete && <button onClick={() => onDelete(pass)} className="btn-ghost text-xs text-danger mr-auto"><Trash /> Delete</button>}
          <button onClick={onCancel} className="btn-ghost text-xs">Cancel</button>
          <button onClick={() => onSave(pass)} className="btn-primary text-xs">Save</button>
        </div>
      </div>
    </div>
  );
}

function CaseEditor({ testCase, passes, onChange, onCancel, onSave, onDelete }: {
  testCase: TestCase;
  passes: TestPass[];
  onChange: (testCase: TestCase) => void;
  onCancel: () => void;
  onSave: (testCase: TestCase) => void;
  onDelete?: (testCase: TestCase) => void;
}) {
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink-950/70 px-4">
      <div className="card !rounded-lg w-full max-w-3xl p-4 space-y-3 max-h-[92vh] overflow-y-auto">
        <div>
          <h2 className="text-sm font-semibold text-ink-50">{testCase.createdAt ? 'Edit test case' : 'New test case'}</h2>
          {testCase.updatedAt && <p className="text-xs text-ink-400">Updated {relativeTime(testCase.updatedAt)}</p>}
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
          <label className="text-xs text-ink-300">Id<input value={testCase.id} onChange={e => onChange({ ...testCase, id: e.target.value })} placeholder="RR-1234" className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Key<input value={testCase.key} onChange={e => onChange({ ...testCase, key: e.target.value })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Pass<select value={testCase.testPassId ?? ''} onChange={e => onChange({ ...testCase, testPassId: e.target.value ? Number(e.target.value) : null })} className="input mt-1 text-xs"><option value="">Standalone</option>{passes.map(pass => <option key={pass.id} value={pass.id}>{pass.name || `Pass ${pass.id}`}</option>)}</select></label>
        </div>
        <label className="text-xs text-ink-300">Title<input value={testCase.title} onChange={e => onChange({ ...testCase, title: e.target.value })} className="input mt-1 text-xs" /></label>
        <label className="text-xs text-ink-300">Description<textarea value={testCase.description} onChange={e => onChange({ ...testCase, description: e.target.value })} className="input mt-1 text-xs" rows={4} /></label>
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-2">
          <label className="text-xs text-ink-300">Room<input value={testCase.roomName} onChange={e => onChange({ ...testCase, roomName: e.target.value })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Status<select value={testCase.status} onChange={e => onChange({ ...testCase, status: Number(e.target.value) })} className="input mt-1 text-xs">{statuses.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}</select></label>
          <label className="text-xs text-ink-300">Min players<input type="number" value={testCase.minNumAssignedPlayers} onChange={e => onChange({ ...testCase, minNumAssignedPlayers: Number(e.target.value) || 1 })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Tags<input value={testCase.tags.join(', ')} onChange={e => onChange({ ...testCase, tags: splitCsv(e.target.value) })} className="input mt-1 text-xs" /></label>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          <label className="text-xs text-ink-300">Assigned player ids<input value={testCase.assignedPlayerIds.join(', ')} onChange={e => onChange({ ...testCase, assignedPlayerIds: splitNumbers(e.target.value) })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Assigned names<input value={testCase.assignedPlayerNames.join(', ')} onChange={e => onChange({ ...testCase, assignedPlayerNames: splitCsv(e.target.value) })} className="input mt-1 text-xs" /></label>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          <label className="text-xs text-ink-300">Jira URL<input value={testCase.jiraUrl} onChange={e => onChange({ ...testCase, jiraUrl: e.target.value })} className="input mt-1 text-xs" /></label>
          <label className="text-xs text-ink-300">Jira bug URL<input value={testCase.jiraBugUrl} onChange={e => onChange({ ...testCase, jiraBugUrl: e.target.value })} className="input mt-1 text-xs" /></label>
        </div>
        <div className="flex justify-end gap-2">
          {onDelete && <button onClick={() => onDelete(testCase)} className="btn-ghost text-xs text-danger mr-auto"><Trash /> Delete</button>}
          <button onClick={onCancel} className="btn-ghost text-xs">Cancel</button>
          <button onClick={() => onSave(testCase)} className="btn-primary text-xs">Save</button>
        </div>
      </div>
    </div>
  );
}

function splitCsv(value: string): string[] {
  return value.split(',').map(x => x.trim()).filter(Boolean);
}

function splitNumbers(value: string): number[] {
  return splitCsv(value).map(Number).filter(n => Number.isFinite(n));
}

function passToPayload(pass: TestPass) {
  return {
    id: pass.id,
    name: pass.name,
    description: pass.description,
    startDate: pass.startDate,
    endDate: pass.endDate,
    wasManuallyClosed: pass.wasManuallyClosed,
    tags: pass.tags,
  };
}

function caseToPayload(testCase: TestCase) {
  return {
    id: testCase.id,
    key: testCase.key,
    title: testCase.title,
    description: testCase.description,
    roomName: testCase.roomName,
    status: testCase.status,
    minNumAssignedPlayers: testCase.minNumAssignedPlayers,
    assignedPlayerIds: testCase.assignedPlayerIds,
    assignedPlayerNames: testCase.assignedPlayerNames,
    tags: testCase.tags,
    jiraUrl: testCase.jiraUrl,
    jiraBugUrl: testCase.jiraBugUrl,
    testPassId: testCase.testPassId,
  };
}

function toLocalInput(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function fromLocalInput(value: string): string {
  return new Date(value).toISOString();
}