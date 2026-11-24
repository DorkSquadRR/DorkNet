import { Settings } from './Settings';
import { SignupCodes } from './SignupCodes';
import { PageHeader } from '../components/PageHeader';
import { useId } from 'react';
import { TabPanel, Tabs, useTabParam } from '../components/Tabs';

// Server-wide configuration: runtime toggles plus the single-use signup
// invite codes (an access-control setting in its own right). Old
// /signup-codes route redirects here with ?tab=signup.
export function SettingsHome() {
  const [tab, setTab] = useTabParam<'server' | 'signup'>('server', ['server', 'signup']);
  const idBase = useId();
  return (
    <div>
      <PageHeader title={({ server: 'Settings', signup: 'Signup codes' })[tab]} />
      <Tabs
        tabs={[
          { key: 'server', label: 'Server' },
          { key: 'signup', label: 'Signup codes' },
        ]}
        active={tab}
        onChange={setTab}
        idBase={idBase}
        className="md:hidden"
      />
      <TabPanel idBase={idBase} active={tab}>
        {tab === 'server' && <Settings embedded />}
        {tab === 'signup' && <SignupCodes embedded />}
      </TabPanel>
    </div>
  );
}
