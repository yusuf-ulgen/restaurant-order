import React, { useState } from 'react';
import { Card, FormField, Input, Button } from '@restaurant-order/ui';
import { useTerminal } from './TerminalContext';

export const TerminalActivationView: React.FC = () => {
  const { activateTerminal, isLoading, error } = useTerminal();
  const [enrollmentCode, setEnrollmentCode] = useState('');
  const [terminalName, setTerminalName] = useState('');

  const handleActivate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!enrollmentCode || !terminalName) return;

    await activateTerminal(enrollmentCode.trim().toUpperCase(), terminalName.trim());
  };

  return (
    <div
      style={{
        display: 'flex',
        minHeight: '100vh',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1.5rem',
        backgroundColor: 'var(--color-bg, #f8fafc)',
      }}
    >
      <Card padding="lg" style={{ width: '100%', maxWidth: '440px' }}>
        <div style={{ textAlign: 'center', marginBottom: '1.5rem' }}>
          <h1 style={{ fontSize: '1.5rem', fontWeight: 700, margin: '0 0 0.5rem 0' }}>
            Cihaz Aktivasyonu
          </h1>
          <p style={{ color: 'var(--color-text-muted, #64748b)', fontSize: '0.875rem', margin: 0 }}>
            Bu cihaz henüz bir şube terminali olarak kaydedilmemiş. Yönetici panelinden oluşturulan tek kullanımlık aktivasyon kodunu girin.
          </p>
        </div>

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

        <form onSubmit={handleActivate} style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
          <FormField id="terminal-activation-code" label="Aktivasyon Kodu" required>
            <Input
              type="text"
              value={enrollmentCode}
              onChange={(e) => setEnrollmentCode(e.target.value.toUpperCase())}
              placeholder="ABCD-1234"
              required
              autoFocus
              maxLength={12}
              style={{ textTransform: 'uppercase', letterSpacing: '0.1em', fontWeight: 600 }}
            />
          </FormField>

          <FormField id="terminal-name" label="Terminal Adı (İstasyon)" required>
            <Input
              type="text"
              value={terminalName}
              onChange={(e) => setTerminalName(e.target.value)}
              placeholder="Örn: Garson İstasyonu 1, Bar POS"
              required
            />
          </FormField>

          <Button
            type="submit"
            variant="primary"
            size="lg"
            disabled={isLoading || !enrollmentCode || !terminalName}
            style={{ marginTop: '0.5rem', width: '100%' }}
          >
            {isLoading ? 'Aktivasyon Yapılıyor...' : 'Terminali Aktifleştir'}
          </Button>
        </form>
      </Card>
    </div>
  );
};
