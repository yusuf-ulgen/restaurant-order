import React, { useState } from 'react';
import { Card, Button, Input, FormField } from '@restaurant-order/ui';
import { useAuth } from './AuthContext';

export const LoginForm: React.FC = () => {
  const { login, isLoading, error } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenantSlug, setTenantSlug] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) return;
    await login(email, password, tenantSlug.trim() || undefined);
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', padding: '1rem' }}>
      <Card padding="lg" style={{ width: '100%', maxWidth: '420px' }}>
        <h2 style={{ margin: '0 0 0.5rem 0', fontSize: '1.5rem', fontWeight: 600 }}>Restoran Yönetim Girişi</h2>
        <p style={{ margin: '0 0 1.5rem 0', color: 'var(--color-text-muted, #64748b)', fontSize: '0.875rem' }}>
          Yönetici veya personel hesabınızla oturum açın.
        </p>

        {error && (
          <div
            role="alert"
            style={{
              padding: '0.75rem',
              marginBottom: '1rem',
              backgroundColor: '#fee2e2',
              color: '#991b1b',
              borderRadius: '0.375rem',
              fontSize: '0.875rem',
            }}
          >
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
          <FormField id="login-email" label="E-Posta Adresi" required>
            <Input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="ornek@restoran.com"
              required
              autoComplete="email"
            />
          </FormField>

          <FormField id="login-password" label="Şifre" required>
            <Input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              required
              autoComplete="current-password"
            />
          </FormField>

          <FormField id="login-tenant" label="Restoran Kodu / Slug (Opsiyonel)">
            <Input
              type="text"
              value={tenantSlug}
              onChange={(e) => setTenantSlug(e.target.value)}
              placeholder="kadikoy-lezzet"
              autoComplete="organization"
            />
          </FormField>

          <Button
            type="submit"
            variant="primary"
            size="lg"
            disabled={isLoading || !email || !password}
            style={{ marginTop: '0.5rem', width: '100%' }}
          >
            {isLoading ? 'Giriş yapılıyor...' : 'Giriş Yap'}
          </Button>
        </form>
      </Card>
    </div>
  );
};
