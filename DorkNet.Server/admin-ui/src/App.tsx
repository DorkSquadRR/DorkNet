import { Routes, Route, Navigate } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth } from './components/RequireAuth';
import { ToastProvider } from './components/Toast';
import { Login } from './pages/Login';
import { Dashboard } from './pages/Dashboard';
import { Players } from './pages/Players';
import { Bans } from './pages/Bans';
import { Reports } from './pages/Reports';
import { Audit } from './pages/Audit';
import { PlayerLogs } from './pages/PlayerLogs';
import { Rooms } from './pages/Rooms';
import { Room } from './pages/Room';
import { ImportRoom } from './pages/ImportRoom';
import { LegacyImportRoom } from './pages/LegacyImportRoom';
import { Store } from './pages/Store';
import { Broadcast } from './pages/Broadcast';
import { CommunityBoard } from './pages/CommunityBoard';
import { LoadingTips } from './pages/LoadingTips';
import { Gift } from './pages/Gift';
import { Passwords } from './pages/Passwords';
import { Grants } from './pages/Grants';
import { Settings } from './pages/Settings';
import { SignupCodes } from './pages/SignupCodes';

export default function App() {
  return (
    <ToastProvider>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route element={<RequireAuth><Layout /></RequireAuth>}>
          <Route index             element={<Dashboard />} />
          <Route path="players"    element={<Players />} />
          <Route path="bans"       element={<Bans />} />
          <Route path="reports"    element={<Reports />} />
          <Route path="audit"      element={<Audit />} />
          <Route path="logs"       element={<PlayerLogs />} />
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
          <Route path="store"      element={<Store />} />
          <Route path="community"  element={<CommunityBoard />} />
          <Route path="loading-tips" element={<LoadingTips />} />
          <Route path="broadcast"  element={<Broadcast />} />
          <Route path="gift"       element={<Gift />} />
          <Route path="passwords"  element={<Passwords />} />
          <Route path="grants"     element={<Grants />} />
          <Route path="settings"     element={<Settings />} />
          <Route path="signup-codes" element={<SignupCodes />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </ToastProvider>
  );
}
