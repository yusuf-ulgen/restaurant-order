import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PageHeader } from '../PageHeader';
import { PageContainer } from '../PageContainer';
import { ContentSection } from '../ContentSection';
import { MobileNavigation } from '../MobileNavigation';

describe('PageHeader Component', () => {
  it('renders title, subtitle, breadcrumbs, and actions', () => {
    render(
      <PageHeader
        title="Siparişler"
        subtitle="Masa bazlı aktif siparişler"
        breadcrumbs={<span>Ana Sayfa &gt; Siparişler</span>}
        actions={<button>Yeni Sipariş</button>}
      />
    );

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Siparişler');
    expect(screen.getByText('Masa bazlı aktif siparişler')).toBeDefined();
    expect(screen.getByText('Ana Sayfa > Siparişler')).toBeDefined();
    expect(screen.getByText('Yeni Sipariş')).toBeDefined();
  });

  it('supports custom heading level', () => {
    render(<PageHeader title="İkinci Seviye" headingLevel={2} />);
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('İkinci Seviye');
  });
});

describe('PageContainer Component', () => {
  it('renders children within bounded container and prevents horizontal overflow', () => {
    const { container } = render(
      <PageContainer maxWidth="md" padding="md">
        <p>Container Content</p>
      </PageContainer>
    );

    const el = container.querySelector('[data-testid="page-container"]') as HTMLElement;
    expect(el).toBeDefined();
    expect(el.style.maxWidth).toBe('768px');
    expect(el.style.overflowX).toBe('hidden');
    expect(screen.getByText('Container Content')).toBeDefined();
  });
});

describe('ContentSection Component', () => {
  it('renders section with accessible title and card variant', () => {
    render(
      <ContentSection
        title="Özet Metrikler"
        description="Günün sipariş performansı"
        variant="card"
        actions={<button>Filtrele</button>}
      >
        <p>Card content</p>
      </ContentSection>
    );

    const section = screen.getByTestId('content-section');
    expect(section).toBeDefined();

    const title = screen.getByText('Özet Metrikler');
    expect(section.getAttribute('aria-labelledby')).toBe(title.id);
    expect(screen.getByText('Günün sipariş performansı')).toBeDefined();
    expect(screen.getByText('Filtrele')).toBeDefined();
    expect(screen.getByText('Card content')).toBeDefined();
  });
});

describe('MobileNavigation Component', () => {
  it('renders navigation items with active state and triggers click', () => {
    const handleClick = vi.fn();
    render(
      <MobileNavigation
        items={[
          { id: 'home', label: 'Ana Sayfa', isActive: true },
          { id: 'orders', label: 'Siparişler', onClick: handleClick },
        ]}
      />
    );

    const homeBtn = screen.getByTestId('mobile-nav-item-home');
    expect(homeBtn.getAttribute('aria-current')).toBe('page');

    const ordersBtn = screen.getByTestId('mobile-nav-item-orders');
    fireEvent.click(ordersBtn);
    expect(handleClick).toHaveBeenCalledTimes(1);
  });
});
