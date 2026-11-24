import { Audit } from './Audit';
import { PlayerLogs } from './PlayerLogs';
import { TestCases } from './TestCases';
import { PageHeader } from '../components/PageHeader';
import { useId } from 'react';
import { TabPanel, Tabs, useTabParam } from '../components/Tabs';

// Internal operations views in one place: the admin audit trail, the
// per-player HTTP request ring buffer, and QA test cases with the GitHub
// issues filed against them. Old /audit and /logs routes redirect here.
export function Activity() {
  const [tab, setTab] = useTabParam<'audit' | 'logs' | 'testcases'>(
    'audit', ['audit', 'logs', 'testcases']);
  const idBase = useId();
  return (
    <div>
      <PageHeader title={({ audit: 'Audit log', logs: 'Player requests', testcases: 'Test cases' })[tab]} />
      <Tabs
        tabs={[
          { key: 'audit', label: 'Audit log' },
          { key: 'logs', label: 'Player requests' },
          { key: 'testcases', label: 'Test cases' },
        ]}
        active={tab}
        onChange={setTab}
        idBase={idBase}
        className="md:hidden"
      />
      <TabPanel idBase={idBase} active={tab}>
        {tab === 'audit' && <Audit embedded />}
        {tab === 'logs' && <PlayerLogs embedded />}
        {tab === 'testcases' && <TestCases />}
      </TabPanel>
    </div>
  );
}
