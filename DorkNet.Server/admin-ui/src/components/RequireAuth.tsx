import { useSyncExternalStore } from 'react';
import { Navigate, useLocation } from 'react-router';
import { getToken, subscribeAuth } from '../lib/auth';

// Gate every authenticated route. We re-render on any auth state
// change so a 401 from `api()` (which clears the token and pings
// listeners) bounces the user out of the dashboard mid-session.
export function RequireAuth({ children }: { children: React.ReactNode }) {
  const token = useSyncExternalStore(subscribeAuth, getToken, () => null);
  const location = useLocation();
  if (!token) return <Navigate to="/login" replace state={{ from: location }} />;
  return <>{children}</>;
}
