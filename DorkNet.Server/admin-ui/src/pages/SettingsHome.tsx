import { Settings } from './Settings';
import { IdentityServer } from './IdentityServer';
import { SignupCodes } from './SignupCodes';
import { PageHeader } from '../components/PageHeader';
import { Tabs, useTabParam } from '../components/Tabs';

// Server-wide configuration: runtime toggles plus the single-use signup
// invite codes (an access-control setting in its own right). Old
// /signup-codes route redirects here with ?tab=signup.
export function SettingsHome() {
  const [tab, setTab] = useTabParam<'server' | 'identity' | 'signup'>('server', ['server', 'identity', 'signup']);
  return (
    <div>
      <PageHeader title="Settings" blurb="Runtime server toggles, authentication, and single-use signup invite codes." />
      <Tabs
        tabs={[
          { key: 'server', label: 'Server' },
          { key: 'identity', label: 'IdentityServer' },
          { key: 'signup', label: 'Signup codes' },
        ]}
        active={tab}
        onChange={setTab}
      />
      {tab === 'server' && <Settings embedded />}
      {tab === 'identity' && <IdentityServer />}
      {tab === 'signup' && <SignupCodes embedded />}
    </div>
  );
}
