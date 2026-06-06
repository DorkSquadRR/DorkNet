import { Routes, Route, Navigate } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth } from './components/RequireAuth';
import { ToastProvider } from './components/Toast';
import { Login } from './pages/Login';
import { Dashboard } from './pages/Dashboard';
import { Moderation } from './pages/Moderation';
import { Activity } from './pages/Activity';
import { Rooms } from './pages/Rooms';
import { Room } from './pages/Room';
import { ImportRoom } from './pages/ImportRoom';
import { LegacyImportRoom } from './pages/LegacyImportRoom';
import { Broadcast } from './pages/Broadcast';
import { Content } from './pages/Content';
import { Storage } from './pages/Storage';
import { SettingsHome } from './pages/SettingsHome';

export default function App() {
  return (
    <ToastProvider>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route element={<RequireAuth><Layout /></RequireAuth>}>
          <Route index             element={<Dashboard />} />

          {/* Players hub — directory + bans + reports as tabs. The old
              standalone routes redirect in with the matching sub-tab
              preselected. Per-player ban / grant / gift / password
              actions all live in the row's detail modal. */}
          <Route path="players"    element={<Moderation />} />
          <Route path="bans"       element={<Navigate to="/players?tab=bans" replace />} />
          <Route path="reports"    element={<Navigate to="/players?tab=reports" replace />} />
          <Route path="gift"       element={<Navigate to="/players" replace />} />
          <Route path="passwords"  element={<Navigate to="/players" replace />} />
          <Route path="grants"     element={<Navigate to="/players" replace />} />

          {/* Activity hub — audit trail + per-player request logs. */}
          <Route path="activity"   element={<Activity />} />
          <Route path="audit"      element={<Navigate to="/activity?tab=audit" replace />} />
          <Route path="logs"       element={<Navigate to="/activity?tab=logs" replace />} />

          <Route path="rooms"      element={<Rooms />} />
          <Route path="rooms/:id"  element={<Room />} />
          {/* Back-compat: the merge left RR-Originals / Instances /
              Leaderboards / old per-room pages folded into /rooms and
              /rooms/:id (leaderboards tab inside the per-room view).
              Anyone with a stale bookmark hitting those old paths
              lands back on the unified rooms list. */}
          <Route path="rr-originals" element={<Navigate to="/rooms" replace />} />
          <Route path="instances"    element={<Navigate to="/rooms" replace />} />
          <Route path="leaderboards" element={<Navigate to="/rooms" replace />} />
          <Route path="import-room"        element={<ImportRoom />} />
          <Route path="import-room-legacy" element={<LegacyImportRoom />} />

          {/* Content hub — community board + loading-screen tips. */}
          <Route path="content"      element={<Content />} />
          <Route path="community"    element={<Navigate to="/content?tab=community" replace />} />
          <Route path="loading-tips" element={<Navigate to="/content?tab=tips" replace />} />

          <Route path="broadcast"  element={<Broadcast />} />
          <Route path="storage"    element={<Storage />} />

          {/* Settings hub — server toggles + signup codes. */}
          <Route path="settings"     element={<SettingsHome />} />
          <Route path="signup-codes" element={<Navigate to="/settings?tab=signup" replace />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </ToastProvider>
  );
}
