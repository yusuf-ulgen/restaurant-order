import React from 'react';
import { Button } from './Button';

export interface ErrorStateProps extends React.HTMLAttributes<HTMLDivElement> {
  title: string;
  message: string;
  onRetry?: () => void;
  retryLabel?: string;
}

export const ErrorState: React.FC<ErrorStateProps> = ({
  title,
  message,
  onRetry,
  retryLabel = 'Tekrar Dene',
  style,
  ...props
}) => {
  return (
    <div
      role="alert"
      aria-live="assertive"
      style={{
        padding: 'var(--ro-space-6)',
        textAlign: 'center',
        backgroundColor: 'var(--ro-color-error-subtle)',
        border: '1px solid var(--ro-color-error-border)',
        borderRadius: 'var(--ro-radius-md)',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        ...style,
      }}
      {...props}
    >
      <h3
        style={{
          fontSize: 'var(--ro-font-size-md)',
          fontWeight: 'var(--ro-font-weight-semibold)',
          color: 'var(--ro-color-error-text)',
          margin: 0,
          marginBottom: 'var(--ro-space-2)',
        }}
      >
        {title}
      </h3>
      <p
        style={{
          fontSize: 'var(--ro-font-size-sm)',
          color: 'var(--ro-color-text-secondary)',
          lineHeight: 'var(--ro-line-height-normal)',
          margin: 0,
          maxWidth: '420px',
        }}
      >
        {message}
      </p>
      {onRetry && (
        <div style={{ marginTop: 'var(--ro-space-4)' }}>
          <Button variant="danger" size="sm" onClick={onRetry}>
            {retryLabel}
          </Button>
        </div>
      )}
    </div>
  );
};
