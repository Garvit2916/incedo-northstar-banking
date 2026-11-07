import { Link } from 'react-router-dom';
import { Icon } from '../components/icons';

export function ForbiddenPage() {
  return (
    <div className="page">
      <div className="error-card" style={{ margin: 'auto' }}>
        <span className="error-code">403</span>
        <span className="error-icon" aria-hidden="true">
          <Icon name="lock" size={26} />
        </span>
        <h1>Access restricted</h1>
        <p>Your account does not have permission to view this page. Contact an administrator if you believe this is a mistake.</p>
        <Link className="btn btn-primary" to="/dashboard">
          Back to dashboard
        </Link>
      </div>
    </div>
  );
}
