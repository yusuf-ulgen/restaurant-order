import React from 'react';

export interface SkipLinkProps {
  targetId?: string;
  label?: string;
  className?: string;
  style?: React.CSSProperties;
}

export const SkipLink: React.FC<SkipLinkProps> = ({
  targetId = 'main-content',
  label = 'Ana içeriğe atla',
  className,
  style,
}) => {
  return (
    <>
      <a
        href={`#${targetId}`}
        data-testid="skip-link"
        className={`ro-skip-link ${className || ''}`}
        style={{
          position: 'fixed',
          top: '-9999px',
          left: 'var(--ro-space-4)',
          zIndex: 'var(--ro-z-tooltip, 1700)',
          padding: 'var(--ro-space-2) var(--ro-space-4)',
          backgroundColor: 'var(--ro-color-primary)',
          color: 'var(--ro-color-text-inverse, #ffffff)',
          borderRadius: 'var(--ro-radius-md)',
          fontWeight: 'var(--ro-font-weight-medium)',
          textDecoration: 'none',
          boxShadow: 'var(--ro-shadow-lg)',
          outline: '2px solid var(--ro-color-focus)',
          ...style,
        }}
      >
        {label}
      </a>
      <style>{`
        .ro-skip-link:focus {
          top: var(--ro-space-4) !important;
        }
      `}</style>
    </>
  );
};
