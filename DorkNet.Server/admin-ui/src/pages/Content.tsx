import { CommunityBoard } from './CommunityBoard';
import { LoadingTips } from './LoadingTips';
import { PageHeader } from '../components/PageHeader';
import { Tabs, useTabParam } from '../components/Tabs';

// The two player-facing content surfaces the watch pulls on dorm load —
// the community board and the loading-screen tips — share one page.
// Old /community and /loading-tips routes redirect here.
export function Content() {
  const [tab, setTab] = useTabParam<'community' | 'tips'>('community', ['community', 'tips']);
  return (
    <div>
      <PageHeader title="Content" blurb="Player-facing content shown on dorm load: the community board and loading-screen tips." />
      <Tabs
        tabs={[
          { key: 'community', label: 'Community board' },
          { key: 'tips', label: 'Loading tips' },
        ]}
        active={tab}
        onChange={setTab}
      />
      {tab === 'community' && <CommunityBoard embedded />}
      {tab === 'tips' && <LoadingTips embedded />}
    </div>
  );
}
