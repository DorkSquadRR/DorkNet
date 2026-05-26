import { Link } from 'react-router-dom';

export function NotFound() {
  return (
    <div className="card !p-8 text-center">
      <h2 className="text-xl font-semibold text-ink-50">Page not found</h2>
      <p className="text-sm text-ink-400 mt-1">The page you were looking for doesn't exist on this server.</p>
      <Link to="/" className="btn-primary text-sm mt-4 inline-flex">Go home</Link>
    </div>
  );
}
