import { Navigate, Route, Routes, useParams } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth, RequireRole } from './auth/Guards';
import { LandingPage } from './pages/LandingPage';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { ProductsPage } from './pages/ProductsPage';
import { ApplicationsPage } from './pages/ApplicationsPage';
import { ApplicationDetailPage } from './pages/ApplicationDetailPage';
import { CustomerTicketsPage } from './pages/CustomerTicketsPage';
import { ProfilePage } from './pages/ProfilePage';
import { NotificationsPage } from './pages/NotificationsPage';
import { ReviewQueuePage } from './pages/ReviewQueuePage';
import { ReviewDetailPage } from './pages/ReviewDetailPage';
import { SupportWorkspacePage } from './pages/SupportWorkspacePage';
import { KnowledgePage } from './pages/KnowledgePage';
import { AdminUsersPage } from './pages/AdminUsersPage';
import { AdminProductsPage } from './pages/AdminProductsPage';
import { AdminAuditPage } from './pages/AdminAuditPage';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { NotFoundPage } from './pages/NotFoundPage';

export function App() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/login" element={<LoginPage />} />

      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/forbidden" element={<ForbiddenPage />} />

        <Route
          path="/products"
          element={
            <RequireRole roles={['Customer']}>
              <ProductsPage />
            </RequireRole>
          }
        />
        <Route
          path="/applications"
          element={
            <RequireRole roles={['Customer']}>
              <ApplicationsPage />
            </RequireRole>
          }
        />
        <Route
          path="/applications/:id"
          element={
            <RequireRole roles={['Customer']}>
              <ApplicationDetailPage />
            </RequireRole>
          }
        />
        <Route
          path="/my-tickets"
          element={
            <RequireRole roles={['Customer']}>
              <CustomerTicketsPage />
            </RequireRole>
          }
        />
        <Route
          path="/profile"
          element={
            <RequireRole roles={['Customer']}>
              <ProfilePage />
            </RequireRole>
          }
        />
        <Route
          path="/reviews"
          element={
            <RequireRole roles={['Reviewer', 'Administrator']}>
              <ReviewQueuePage />
            </RequireRole>
          }
        />
        <Route
          path="/reviews/:id"
          element={
            <RequireRole roles={['Reviewer', 'Administrator']}>
              <ReviewDetailPage />
            </RequireRole>
          }
        />
        <Route
          path="/support"
          element={
            <RequireRole roles={['SupportAgent', 'Administrator']}>
              <SupportWorkspacePage />
            </RequireRole>
          }
        />
        <Route
          path="/support/:id"
          element={
            <RequireRole roles={['SupportAgent', 'Administrator']}>
              <SupportWorkspacePage />
            </RequireRole>
          }
        />
        <Route
          path="/knowledge"
          element={
            <RequireRole roles={['SupportAgent', 'Administrator']}>
              <KnowledgePage />
            </RequireRole>
          }
        />
        <Route path="/tickets" element={<Navigate to="/support" replace />} />
        <Route path="/tickets/:id" element={<TicketRedirect />} />
        <Route
          path="/admin/users"
          element={
            <RequireRole roles={['Administrator']}>
              <AdminUsersPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/products"
          element={
            <RequireRole roles={['Administrator']}>
              <AdminProductsPage />
            </RequireRole>
          }
        />
        <Route
          path="/admin/audit"
          element={
            <RequireRole roles={['Administrator']}>
              <AdminAuditPage />
            </RequireRole>
          }
        />
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

function TicketRedirect() {
  const { id } = useParams();
  return <Navigate to={id ? `/support/${id}` : '/support'} replace />;
}
