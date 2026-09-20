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

  it('renders real <a> element for items with href in MobileNavigation', () => {
    render(
      <MobileNavigation
        items={[
          { id: 'menu', label: 'Menü', href: '/menu', isActive: true },
          { id: 'ext', label: 'Yardım', href: 'https://help.example.com', isExternal: true },
          { id: 'dis', label: 'Geçersiz', href: '/closed', disabled: true },
          { id: 'inert', label: 'Gelecek' },
        ]}
      />
    );

    const menuLink = screen.getByTestId('mobile-nav-item-menu');
    expect(menuLink.tagName).toBe('A');
    expect(menuLink.getAttribute('href')).toBe('/menu');
    expect(menuLink.getAttribute('aria-current')).toBe('page');

    const extLink = screen.getByTestId('mobile-nav-item-ext');
    expect(extLink.tagName).toBe('A');
    expect(extLink.getAttribute('target')).toBe('_blank');
    expect(extLink.getAttribute('rel')).toBe('noopener noreferrer');

    const disabledLink = screen.getByTestId('mobile-nav-item-dis');
    expect(disabledLink.tagName).toBe('A');
    expect(disabledLink.getAttribute('aria-disabled')).toBe('true');
    expect(disabledLink.getAttribute('href')).toBeNull();

    const inertEl = screen.getByTestId('mobile-nav-item-inert');
    expect(inertEl.tagName).toBe('DIV');
    expect(inertEl.getAttribute('aria-disabled')).toBe('true');
  });
});
