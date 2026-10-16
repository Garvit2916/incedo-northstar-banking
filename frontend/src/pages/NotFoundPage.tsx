import { Link } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <div className="page centered">
      <h1>404 — Not found</h1>
      <p className="muted">The page you are looking for does not exist.</p>
      <Link className="btn btn-primary" to="/">
        Back to dashboard
      </Link>
    </div>
  );
}
