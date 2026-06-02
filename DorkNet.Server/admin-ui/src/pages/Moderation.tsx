import { Players } from './Players';
import { Bans } from './Bans';
import { Reports } from './Reports';
import { PageHeader } from '../components/PageHeader';
import { Tabs, useTabParam } from '../components/Tabs';

// Unified moderation hub. The account directory (with its per-player
// detail / ban / grant / gift / password modal), the active-ban list,
// and the open-report queue were three separate sidebar entries — they
// describe one workflow, so they're tabs of one page now. The old
// /bans and /reports routes redirect here with ?tab= preselected.
export function Moderation() {
  const [tab, setTab] = useTabParam<'directory' | 'bans' | 'reports'>('directory', ['directory', 'bans', 'reports']);
  return (
    <div>
      <PageHeader title="Players" blurb="Search and moderate accounts, review active bans, and triage open reports." />
      <Tabs
        tabs={[
          { key: 'directory', label: 'Directory' },
          { key: 'bans', label: 'Bans' },
          { key: 'reports', label: 'Reports' },
        ]}
        active={tab}
        onChange={setTab}
      />
      {tab === 'directory' && <Players embedded />}
      {tab === 'bans' && <Bans embedded />}
      {tab === 'reports' && <Reports embedded />}
    </div>
  );
}
