import { Component, type ReactNode } from 'react';

export interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  errorMessage: string;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, errorMessage: '' };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, errorMessage: error.message };
  }

  componentDidCatch(error: Error) {
    // Sanitized logging: do not log credentials or sensitive data
    if (process.env.NODE_ENV !== 'test') {
      console.error('ErrorBoundary caught an error:', error.message);
    }
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return (
        <div
          role="alert"
          aria-live="assertive"
          style={{
            padding: '24px',
            textAlign: 'center',
            backgroundColor: 'var(--ro-color-surface, #ffffff)',
            borderRadius: 'var(--ro-radius-md, 8px)',
            border: '1px solid var(--ro-color-border, #e5e7eb)',
          }}
        >
          <h2 style={{ fontSize: '1.125rem', color: 'var(--ro-color-danger, #ef4444)', marginBottom: '8px' }}>
            Beklenmeyen Bir Hata Oluştu
          </h2>
          <p style={{ color: 'var(--ro-color-text-muted, #6b7280)', fontSize: '0.875rem' }}>
            İşlem sırasında bir hata ile karşılaşıldı. Lütfen sayfayı yenileyin veya tekrar deneyin.
          </p>
        </div>
      );
    }

    return this.props.children;
  }
}
