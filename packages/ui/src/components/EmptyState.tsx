import React, { type ReactNode } from 'react';

export interface EmptyStateProps {
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  icon?: ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  title,
  description,
  actionLabel,
  onAction,
  icon,
}) => {
  return (
    <div
      role="status"
      aria-label={title}
      style={{
        padding: '32px 16px',
        textAlign: 'center',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
      }}
    >
      {icon && <div style={{ marginBottom: '16px' }}>{icon}</div>}
      <h3
        style={{
          fontSize: '1.125rem',
          fontWeight: 600,
          color: 'var(--ro-color-text, #111827)',
          marginBottom: '8px',
        }}
      >
        {title}
      </h3>
      <p
        style={{
          color: 'var(--ro-color-text-muted, #6b7280)',
          fontSize: '0.875rem',
          maxWidth: '400px',
          lineHeight: '1.5',
          margin: 0,
        }}
      >
        {description}
      </p>
      {actionLabel && onAction && (
        <button
          type="button"
          onClick={onAction}
          style={{
            marginTop: '16px',
            padding: '8px 16px',
            backgroundColor: 'var(--ro-color-primary, #2563eb)',
            color: '#ffffff',
            border: 'none',
            borderRadius: 'var(--ro-radius-md, 8px)',
            cursor: 'pointer',
            fontSize: '0.875rem',
            fontWeight: 500,
          }}
        >
          {actionLabel}
        </button>
      )}
    </div>
  );
};
