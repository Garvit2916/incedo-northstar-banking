import { Link } from 'react-router-dom';

export function ForbiddenPage() {
  return (
    <div className="page centered">
      <h1>403 — Forbidden</h1>
      <p className="muted">You do not have permission to view this page.</p>
      <Link className="btn btn-primary" to="/">
        Back to dashboard
      </Link>
    </div>
  );
}
