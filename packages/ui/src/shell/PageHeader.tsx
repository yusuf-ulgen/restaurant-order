import React from 'react';

export interface PageHeaderProps {
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  breadcrumbs?: React.ReactNode;
  actions?: React.ReactNode;
  headingLevel?: 1 | 2 | 3;
  className?: string;
  style?: React.CSSProperties;
}

export const PageHeader: React.FC<PageHeaderProps> = ({
  title,
  subtitle,
  breadcrumbs,
  actions,
  headingLevel = 1,
  className,
  style,
}) => {
  const HeadingTag = `h${headingLevel}` as 'h1' | 'h2' | 'h3';

  return (
    <div
      data-testid="page-header"
      className={className}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 'var(--ro-space-4)',
        marginBottom: 'var(--ro-space-6)',
        width: '100%',
        maxWidth: '100%',
        boxSizing: 'border-box',
        ...style,
      }}
    >
      <div style={{ flex: 1, minWidth: '240px' }}>
        {breadcrumbs && (
          <nav aria-label="Ekmek Kırıntısı" style={{ marginBottom: 'var(--ro-space-2)' }}>
            {breadcrumbs}
          </nav>
        )}
        <HeadingTag
          style={{
            margin: 0,
            fontSize:
              headingLevel === 1
                ? 'var(--ro-font-size-2xl)'
                : headingLevel === 2
                ? 'var(--ro-font-size-xl)'
                : 'var(--ro-font-size-lg)',
            fontWeight: 'var(--ro-font-weight-bold)',
            color: 'var(--ro-color-text-primary)',
            lineHeight: 'var(--ro-line-height-tight)',
            wordBreak: 'break-word',
          }}
        >
          {title}
        </HeadingTag>
        {subtitle && (
          <p
            style={{
              margin: 0,
              marginTop: 'var(--ro-space-1)',
              fontSize: 'var(--ro-font-size-sm)',
              color: 'var(--ro-color-text-muted)',
              lineHeight: 'var(--ro-line-height-normal)',
            }}
          >
            {subtitle}
          </p>
        )}
      </div>

      {actions && (
        <div
          data-testid="page-header-actions"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            gap: 'var(--ro-space-2)',
            flexShrink: 0,
          }}
        >
          {actions}
        </div>
      )}
    </div>
  );
};
