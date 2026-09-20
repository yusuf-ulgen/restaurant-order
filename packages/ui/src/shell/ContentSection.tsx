import React, { useId } from 'react';

export interface ContentSectionProps {
  title?: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  children: React.ReactNode;
  variant?: 'default' | 'card' | 'bordered';
  headingLevel?: 2 | 3 | 4;
  className?: string;
  style?: React.CSSProperties;
}

export const ContentSection: React.FC<ContentSectionProps> = ({
  title,
  description,
  actions,
  children,
  variant = 'default',
  headingLevel = 3,
  className,
  style,
}) => {
  const generatedId = useId();
  const titleId = title ? `section-title-${generatedId}` : undefined;
  const HeadingTag = `h${headingLevel}` as 'h2' | 'h3' | 'h4';

  const variantStyles: Record<typeof variant, React.CSSProperties> = {
    default: {
      backgroundColor: 'transparent',
      border: 'none',
      padding: '0',
    },
    card: {
      backgroundColor: 'var(--ro-color-surface)',
      border: '1px solid var(--ro-color-border)',
      borderRadius: 'var(--ro-radius-lg)',
      padding: 'var(--ro-space-5)',
      boxShadow: 'var(--ro-shadow-sm)',
    },
    bordered: {
      backgroundColor: 'transparent',
      border: '1px solid var(--ro-color-border)',
      borderRadius: 'var(--ro-radius-md)',
      padding: 'var(--ro-space-4)',
    },
  };

  return (
    <section
      data-testid="content-section"
      aria-labelledby={titleId}
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        marginBottom: 'var(--ro-space-6)',
        width: '100%',
        maxWidth: '100%',
        boxSizing: 'border-box',
        ...variantStyles[variant],
        ...style,
      }}
    >
      {(title || actions) && (
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            gap: 'var(--ro-space-3)',
            marginBottom: 'var(--ro-space-4)',
          }}
        >
          <div>
            {title && (
              <HeadingTag
                id={titleId}
                style={{
                  margin: 0,
                  fontSize: 'var(--ro-font-size-lg)',
                  fontWeight: 'var(--ro-font-weight-semibold)',
                  color: 'var(--ro-color-text-primary)',
                }}
              >
                {title}
              </HeadingTag>
            )}
            {description && (
              <p
                style={{
                  margin: 0,
                  marginTop: 'var(--ro-space-1)',
                  fontSize: 'var(--ro-font-size-sm)',
                  color: 'var(--ro-color-text-muted)',
                }}
              >
                {description}
              </p>
            )}
          </div>

          {actions && (
            <div
              data-testid="content-section-actions"
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--ro-space-2)',
                flexShrink: 0,
              }}
            >
              {actions}
            </div>
          )}
        </div>
      )}

      <div>{children}</div>
    </section>
  );
};
