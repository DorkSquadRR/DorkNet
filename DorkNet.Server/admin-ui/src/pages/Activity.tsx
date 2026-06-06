import { Audit } from './Audit';
import { PlayerLogs } from './PlayerLogs';
import { PageHeader } from '../components/PageHeader';
import { Tabs, useTabParam } from '../components/Tabs';

// Both log views in one place: the admin audit trail and the per-player
// HTTP request ring buffer. Old /audit and /logs routes redirect here.
export function Activity() {
  const [tab, setTab] = useTabParam<'audit' | 'logs'>('audit', ['audit', 'logs']);
  return (
    <div>
      <PageHeader title="Activity" blurb="Admin audit trail and per-player HTTP request logs." />
      <Tabs
        tabs={[
          { key: 'audit', label: 'Audit log' },
          { key: 'logs', label: 'Player requests' },
        ]}
        active={tab}
        onChange={setTab}
      />
      {tab === 'audit' && <Audit embedded />}
      {tab === 'logs' && <PlayerLogs embedded />}
    </div>
  );
}
