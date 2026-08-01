import { Audit } from './Audit';
import { PlayerLogs } from './PlayerLogs';
import { TestCases } from './TestCases';
import { PageHeader } from '../components/PageHeader';
import { Tabs, useTabParam } from '../components/Tabs';

// Internal operations views in one place: the admin audit trail, the
// per-player HTTP request ring buffer, and QA test cases with the GitHub
// issues filed against them. Old /audit and /logs routes redirect here.
export function Activity() {
  const [tab, setTab] = useTabParam<'audit' | 'logs' | 'testcases'>(
    'audit', ['audit', 'logs', 'testcases']);
  return (
    <div>
      <PageHeader title="Activity" blurb="Admin audit trail, per-player HTTP request logs, and QA test cases." />
      <Tabs
        tabs={[
          { key: 'audit', label: 'Audit log' },
          { key: 'logs', label: 'Player requests' },
          { key: 'testcases', label: 'Test cases' },
        ]}
        active={tab}
        onChange={setTab}
      />
      {tab === 'audit' && <Audit embedded />}
      {tab === 'logs' && <PlayerLogs embedded />}
      {tab === 'testcases' && <TestCases />}
    </div>
  );
}
