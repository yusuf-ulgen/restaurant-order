import React from 'react';
import { FormError } from './FormError';

export interface FormFieldControlProps {
  id?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: 'true' | 'false' | boolean;
  'aria-required'?: 'true' | 'false' | boolean;
  required?: boolean;
}

export interface FormFieldProps {
  id: string;
  label: string;
  required?: boolean;
  description?: string;
  error?: string;
  children: React.ReactElement<FormFieldControlProps>;
  className?: string;
  style?: React.CSSProperties;
}

export const FormField: React.FC<FormFieldProps> = ({
  id,
  label,
  required = false,
  description,
  error,
  children,
  className,
  style,
}) => {
  const descriptionId = description ? `${id}-desc` : undefined;
  const errorId = error ? `${id}-error` : undefined;

  const describedBy = [errorId, descriptionId].filter(Boolean).join(' ') || undefined;

  const childWithProps = React.cloneElement(children, {
    id,
    'aria-describedby': describedBy,
    'aria-invalid': error ? 'true' : undefined,
    'aria-required': required ? 'true' : undefined,
    required: required || children.props?.required,
  });

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        marginBottom: 'var(--ro-space-4)',
        ...style,
      }}
    >
      <label
        htmlFor={id}
        style={{
          fontSize: 'var(--ro-font-size-sm)',
          fontWeight: 'var(--ro-font-weight-medium)',
          color: error ? 'var(--ro-color-error-text)' : 'var(--ro-color-text-primary)',
          marginBottom: 'var(--ro-space-1)',
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--ro-space-1)',
        }}
      >
        <span>{label}</span>
        {required && (
          <span
            aria-hidden="true"
            style={{ color: 'var(--ro-color-error)' }}
          >
            *
          </span>
        )}
      </label>

      {childWithProps}

      {description && !error && (
        <p
          id={descriptionId}
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
            margin: 0,
            marginTop: 'var(--ro-space-1)',
          }}
        >
          {description}
        </p>
      )}

      {error && <FormError id={errorId}>{error}</FormError>}
    </div>
  );
};
