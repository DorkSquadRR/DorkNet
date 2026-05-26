import { Routes, Route, Navigate } from 'react-router-dom';
import { Header } from './components/Header';
import { Home } from './pages/Home';
import { Feed } from './pages/Feed';
import { PhotoDetail } from './pages/PhotoDetail';
import { Players } from './pages/Players';
import { PlayerProfile } from './pages/PlayerProfile';
import { Rooms } from './pages/Rooms';
import { NotFound } from './pages/NotFound';

export function App() {
  return (
    <div className="min-h-screen flex flex-col">
      <Header />
      <main className="flex-1 mx-auto w-full max-w-6xl px-4 sm:px-6 py-6">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/feed" element={<Feed />} />
          <Route path="/photo/:id" element={<PhotoDetail />} />
          <Route path="/players" element={<Players />} />
          <Route path="/players/:id" element={<PlayerProfile />} />
          <Route path="/rooms" element={<Rooms />} />
          {/* Legacy paths from the vanilla-JS feed.localhost site */}
          <Route path="/p/:id" element={<RedirectPhoto />} />
          <Route path="/u/:id" element={<RedirectPlayer />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
      <footer className="border-t border-ink-800 text-xs text-ink-400">
        <div className="mx-auto max-w-6xl px-4 sm:px-6 py-4 flex flex-wrap items-center justify-between gap-2">
          <span>DorkNet — a private Rec Room server.</span>
          <span>
            <a className="hover:text-ink-200" href="https://github.com/Alexa-RR/Dorknet">GitHub</a>
            <span className="mx-2">·</span>
            <a className="hover:text-ink-200" href="/admin">Admin</a>
          </span>
        </div>
      </footer>
    </div>
  );
}

// Small redirect shims so old #/p/123 / #/u/45 links keep working.
import { useParams } from 'react-router-dom';
function RedirectPhoto()  { const { id } = useParams(); return <Navigate to={`/photo/${id}`}  replace />; }
function RedirectPlayer() { const { id } = useParams(); return <Navigate to={`/players/${id}`} replace />; }
