import React, { useRef } from 'react';
import { Modal } from './Modal';
import { Button } from './Button';

export interface ConfirmationDialogProps {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'danger' | 'primary';
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
}

export const ConfirmationDialog: React.FC<ConfirmationDialogProps> = ({
  isOpen,
  title,
  message,
  confirmLabel = 'Onayla',
  cancelLabel = 'Vazgeç',
  variant = 'danger',
  onConfirm,
  onCancel,
  isLoading = false,
}) => {
  const cancelBtnRef = useRef<HTMLButtonElement | null>(null);

  return (
    <Modal
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
      size="sm"
      initialFocusRef={cancelBtnRef}
      closeOnClickOutside={!isLoading}
      closeOnEscape={!isLoading}
      showCloseButton={!isLoading}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
        <p
          style={{
            margin: 0,
            fontSize: 'var(--ro-font-size-base)',
            color: 'var(--ro-color-text-secondary)',
            lineHeight: 'var(--ro-line-height-normal)',
          }}
        >
          {message}
        </p>

        <div
          style={{
            display: 'flex',
            justifyContent: 'flex-end',
            gap: 'var(--ro-space-2)',
            marginTop: 'var(--ro-space-2)',
          }}
        >
          <Button
            ref={cancelBtnRef}
            variant="outline"
            size="md"
            onClick={onCancel}
            disabled={isLoading}
          >
            {cancelLabel}
          </Button>
          <Button
            variant={variant === 'danger' ? 'danger' : 'primary'}
            size="md"
            onClick={onConfirm}
            loading={isLoading}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  );
};
