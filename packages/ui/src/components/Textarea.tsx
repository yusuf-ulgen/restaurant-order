import React, { forwardRef } from 'react';

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  isInvalid?: boolean;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  {
    isInvalid = false,
    disabled = false,
    style,
    rows = 3,
    'aria-invalid': ariaInvalid,
    ...props
  },
  ref
) {
  const baseStyles: React.CSSProperties = {
    display: 'block',
    width: '100%',
    padding: 'var(--ro-space-2) var(--ro-space-3)',
    fontSize: 'var(--ro-font-size-base)',
    fontFamily: 'var(--ro-font-sans)',
    color: 'var(--ro-color-text-primary)',
    backgroundColor: disabled ? 'var(--ro-color-bg-subtle)' : 'var(--ro-color-surface)',
    border: `1px solid ${isInvalid ? 'var(--ro-color-error)' : 'var(--ro-color-border)'}`,
    borderRadius: 'var(--ro-radius-md)',
    boxSizing: 'border-box',
    transition: 'border-color var(--ro-transition-fast)',
    cursor: disabled ? 'not-allowed' : 'text',
    opacity: disabled ? 0.7 : 1,
    resize: 'vertical',
  };

  return (
    <textarea
      ref={ref}
      rows={rows}
      disabled={disabled}
      aria-invalid={ariaInvalid ?? (isInvalid ? 'true' : undefined)}
      style={{
        ...baseStyles,
        ...style,
      }}
      {...props}
    />
  );
});
