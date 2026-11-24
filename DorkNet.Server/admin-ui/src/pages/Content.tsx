import { CommunityBoard } from './CommunityBoard';
import { LoadingTips } from './LoadingTips';
import { Charades } from './Charades';
import { PageHeader } from '../components/PageHeader';
import { useId } from 'react';
import { TabPanel, Tabs, useTabParam } from '../components/Tabs';

// The player-facing content surfaces the watch pulls at runtime — the
// community board and loading-screen tips shown on dorm load, plus the 3D
// Charades word lists fetched at card-box spawn — share one page. Old
// /community and /loading-tips routes redirect here.
export function Content() {
  const [tab, setTab] = useTabParam<'community' | 'tips' | 'charades'>(
    'community', ['community', 'tips', 'charades']);
  const idBase = useId();
  return (
    <div>
      <PageHeader title={({ community: 'Community board', tips: 'Loading tips', charades: 'Charades words' })[tab]} />
      <Tabs
        tabs={[
          { key: 'community', label: 'Community board' },
          { key: 'tips', label: 'Loading tips' },
          { key: 'charades', label: 'Charades words' },
        ]}
        active={tab}
        onChange={setTab}
        idBase={idBase}
        className="md:hidden"
      />
      <TabPanel idBase={idBase} active={tab}>
        {tab === 'community' && <CommunityBoard embedded />}
        {tab === 'tips' && <LoadingTips embedded />}
        {tab === 'charades' && <Charades embedded />}
      </TabPanel>
    </div>
  );
}
