import React from 'react';
import { Spinner } from '@restaurant-order/ui';
import { useAuth } from './AuthContext';
import { LoginForm } from './LoginForm';

interface ProtectedRouteProps {
  children: React.ReactNode;
  allowedRoles?: string[];
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children, allowedRoles }) => {
  const { user, isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
        <Spinner size="lg" />
      </div>
    );
  }

  if (!isAuthenticated || !user) {
    return <LoginForm />;
  }

  if (allowedRoles && allowedRoles.length > 0 && !allowedRoles.includes(user.role)) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', padding: '1rem' }}>
        <div role="alert" style={{ textAlign: 'center', maxWidth: '400px' }}>
          <h2>Yetkisiz Erişim</h2>
          <p>Bu sayfayı görüntülemek için gerekli yetkilere sahip değilsiniz.</p>
        </div>
      </div>
    );
  }

  return <>{children}</>;
};
