import React from 'react';
import { Modal } from '@restaurant-order/ui';
import { PinPad } from './PinPad';
import { useTerminal } from './TerminalContext';

interface PinAuthModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: () => void;
}

export const PinAuthModal: React.FC<PinAuthModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const { loginWithPin, isLoading, error } = useTerminal();

  const handlePinSubmit = async (pin: string) => {
    const success = await loginWithPin(pin);
    if (success) {
      if (onSuccess) onSuccess();
      onClose();
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Personel Hızlı Geçiş"
      description="4 haneli PIN kodunuzu girerek vardiya oturumu başlatın."
      size="sm"
    >
      <PinPad
        onSubmit={handlePinSubmit}
        isLoading={isLoading}
        errorMessage={error}
        onCancel={onClose}
      />
    </Modal>
  );
};
