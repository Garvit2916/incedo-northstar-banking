import { Link } from 'react-router-dom';
import { Icon } from '../components/icons';

export function NotFoundPage() {
  return (
    <div className="error-page">
      <div className="error-card">
        <span className="error-code">404</span>
        <span className="error-icon" aria-hidden="true">
          <Icon name="search" size={26} />
        </span>
        <h1>Page not found</h1>
        <p>The page you are looking for does not exist or may have moved.</p>
        <Link className="btn btn-primary" to="/dashboard">
          Back to dashboard
        </Link>
      </div>
    </div>
  );
}
