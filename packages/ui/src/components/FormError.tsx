import React from 'react';

export interface FormErrorProps extends React.HTMLAttributes<HTMLParagraphElement> {
  id?: string;
  children: React.ReactNode;
}

export const FormError: React.FC<FormErrorProps> = ({
  id,
  children,
  style,
  ...props
}) => {
  if (!children) return null;

  return (
    <p
      id={id}
      role="alert"
      style={{
        fontSize: 'var(--ro-font-size-sm)',
        color: 'var(--ro-color-error-text)',
        fontWeight: 'var(--ro-font-weight-medium)',
        margin: 0,
        marginTop: 'var(--ro-space-1)',
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--ro-space-1)',
        ...style,
      }}
      {...props}
    >
      {children}
    </p>
  );
};
