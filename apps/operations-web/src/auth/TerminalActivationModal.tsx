import React, { useState } from 'react';
import { Modal, FormField, Input, Button } from '@restaurant-order/ui';
import { useTerminal } from './TerminalContext';

interface TerminalActivationModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const TerminalActivationModal: React.FC<TerminalActivationModalProps> = ({
  isOpen,
  onClose,
}) => {
  const { activateTerminal, isLoading, error } = useTerminal();
  const [enrollmentCode, setEnrollmentCode] = useState('');
  const [terminalName, setTerminalName] = useState('');

  const handleActivate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!enrollmentCode || !terminalName) return;

    const success = await activateTerminal(enrollmentCode.trim().toUpperCase(), terminalName.trim());
    if (success) {
      onClose();
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Cihaz Aktivasyonu (Terminal Kaydı)"
      description="Yönetici panelinden oluşturulan 8 haneli tek kullanımlık aktivasyon kodunu girin."
      size="sm"
    >
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

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem', marginTop: '0.5rem' }}>
          <Button type="button" variant="outline" size="md" onClick={onClose} disabled={isLoading}>
            İptal
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="md"
            disabled={isLoading || !enrollmentCode || !terminalName}
          >
            {isLoading ? 'Aktivasyon Yapılıyor...' : 'Terminali Aktifleştir'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
