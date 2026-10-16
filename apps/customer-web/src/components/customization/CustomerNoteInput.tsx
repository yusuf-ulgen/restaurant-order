import React from 'react';
import { CUSTOMER_NOTE_MAX_LENGTH } from '@restaurant-order/contracts';
import { stripControlCharacters } from '../../utils/customizationUtils';

export interface CustomerNoteInputProps {
  note: string;
  onChangeNote: (note: string) => void;
}

export const CustomerNoteInput: React.FC<CustomerNoteInputProps> = ({
  note,
  onChangeNote,
}) => {
  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const raw = e.target.value;
    const sanitized = stripControlCharacters(raw);
    if (sanitized.length <= CUSTOMER_NOTE_MAX_LENGTH) {
      onChangeNote(sanitized);
    }
  };

  const charCount = note.length;

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-1)',
      }}
      data-testid="customer-note-section"
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <label
          htmlFor="customer-item-note"
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
          }}
        >
          Sipariş Notu (İsteğe bağlı)
        </label>
        <span
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color:
              charCount >= CUSTOMER_NOTE_MAX_LENGTH
                ? 'var(--ro-color-danger, #ef4444)'
                : 'var(--ro-color-text-muted)',
            fontWeight: 'var(--ro-font-weight-medium)',
          }}
          data-testid="customer-note-counter"
        >
          {charCount} / {CUSTOMER_NOTE_MAX_LENGTH}
        </span>
      </div>

      <textarea
        id="customer-item-note"
        name="customerNote"
        rows={2}
        value={note}
        onChange={handleChange}
        maxLength={CUSTOMER_NOTE_MAX_LENGTH}
        placeholder="Örn: Sosu ayrı olsun, acısız hazırlansın..."
        data-testid="customer-note-input"
        style={{
          width: '100%',
          boxSizing: 'border-box',
          padding: 'var(--ro-space-2) var(--ro-space-3)',
          borderRadius: 'var(--ro-radius-md)',
          border: '1px solid var(--ro-color-border)',
          backgroundColor: 'var(--ro-color-surface)',
          color: 'var(--ro-color-text-primary)',
          fontSize: 'var(--ro-font-size-sm)',
          fontFamily: 'inherit',
          resize: 'vertical',
          minHeight: '60px',
          outline: 'none',
        }}
      />
    </div>
  );
};
