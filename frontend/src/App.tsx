import { Navigate, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth, RequireRole } from './auth/Guards';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { ProductsPage } from './pages/ProductsPage';
import { ApplicationsPage } from './pages/ApplicationsPage';
import { ApplicationDetailPage } from './pages/ApplicationDetailPage';
import { ProfilePage } from './pages/ProfilePage';
import { NotificationsPage } from './pages/NotificationsPage';
import { ReviewQueuePage } from './pages/ReviewQueuePage';
import { ReviewDetailPage } from './pages/ReviewDetailPage';
import { TicketsPage } from './pages/TicketsPage';
import { TicketDetailPage } from './pages/TicketDetailPage';
import { AdminUsersPage } from './pages/AdminUsersPage';
import { AdminProductsPage } from './pages/AdminProductsPage';
import { AdminAuditPage } from './pages/AdminAuditPage';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { NotFoundPage } from './pages/NotFoundPage';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to="/dashboard" replace />} />
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
          path="/tickets"
          element={
            <RequireRole roles={['SupportAgent', 'Administrator']}>
              <TicketsPage />
            </RequireRole>
          }
        />
        <Route
          path="/tickets/:id"
          element={
            <RequireRole roles={['SupportAgent', 'Administrator']}>
              <TicketDetailPage />
            </RequireRole>
          }
        />
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
