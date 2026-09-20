import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Sidebar } from '../Sidebar';
import { NavSectionConfig } from '../types';

const mockSections: NavSectionConfig[] = [
  {
    id: 'sec-main',
    title: 'Ana Menü',
    order: 1,
    items: [
      { id: 'item-dashboard', label: 'Kontrol Paneli', order: 1, isActive: true },
      { id: 'item-menu', label: 'Menü Yönetimi', order: 2 },
      { id: 'item-hidden', label: 'Gizli Öğe', order: 3, isVisible: false },
    ],
  },
  {
    id: 'sec-settings',
    title: 'Ayarlar',
    order: 2,
    items: [
      { id: 'item-tables', label: 'Şube & Masalar', order: 1, disabled: true },
    ],
  },
];

describe('Sidebar Component', () => {
  it('renders sections and visible items in order', () => {
    render(<Sidebar sections={mockSections} title="Admin Paneli" />);

    expect(screen.getByText('Admin Paneli')).toBeDefined();
    expect(screen.getByText('Ana Menü')).toBeDefined();
    expect(screen.getByText('Kontrol Paneli')).toBeDefined();
    expect(screen.getByText('Menü Yönetimi')).toBeDefined();
    expect(screen.getByText('Şube & Masalar')).toBeDefined();
    expect(screen.queryByText('Gizli Öğe')).toBeNull();
  });

  it('marks active item with aria-current="page" and data-active', () => {
    render(<Sidebar sections={mockSections} />);

    const dashboardBtn = screen.getByTestId('sidebar-item-item-dashboard');
    expect(dashboardBtn.getAttribute('aria-current')).toBe('page');
    expect(dashboardBtn.getAttribute('data-active')).toBe('true');

    const menuBtn = screen.getByTestId('sidebar-item-item-menu');
    expect(menuBtn.getAttribute('aria-current')).toBeNull();
    expect(menuBtn.getAttribute('data-active')).toBeNull();
  });

  it('handles collapsed state properly', () => {
    const { rerender } = render(
      <Sidebar sections={mockSections} isCollapsed={false} title="Admin" />
    );
    expect(screen.getByText('Ana Menü')).toBeDefined();

    rerender(<Sidebar sections={mockSections} isCollapsed={true} title="Admin" />);
    expect(screen.queryByText('Ana Menü')).toBeNull();
  });

  it('calls onToggleCollapse when collapse button is clicked', () => {
    const handleToggle = vi.fn();
    render(<Sidebar sections={mockSections} onToggleCollapse={handleToggle} />);

    const collapseBtn = screen.getByTestId('sidebar-collapse-btn');
    fireEvent.click(collapseBtn);
    expect(handleToggle).toHaveBeenCalledTimes(1);
  });

  it('renders empty navigation without crashing', () => {
    render(<Sidebar sections={[]} />);
    expect(screen.getByTestId('desktop-sidebar')).toBeDefined();
  });

  it('calls onItemClick when an item is clicked', () => {
    const handleItemClick = vi.fn();
    render(<Sidebar sections={mockSections} onItemClick={handleItemClick} />);

    const menuBtn = screen.getByTestId('sidebar-item-item-menu');
    fireEvent.click(menuBtn);
    expect(handleItemClick).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'item-menu', label: 'Menü Yönetimi' })
    );
  });

  describe('Navigation Semantics', () => {
    it('renders real <a> element when item has href', () => {
      const sectionsWithLinks: NavSectionConfig[] = [
        {
          id: 'links',
          items: [
            { id: 'internal', label: 'İç Sayfa', href: '/dashboard', isActive: true },
            { id: 'external', label: 'Dış Bağlantı', href: 'https://example.com', isExternal: true },
          ],
        },
      ];

      render(<Sidebar sections={sectionsWithLinks} />);

      const internalLink = screen.getByTestId('sidebar-item-internal');
      expect(internalLink.tagName).toBe('A');
      expect(internalLink.getAttribute('href')).toBe('/dashboard');
      expect(internalLink.getAttribute('aria-current')).toBe('page');

      const externalLink = screen.getByTestId('sidebar-item-external');
      expect(externalLink.tagName).toBe('A');
      expect(externalLink.getAttribute('href')).toBe('https://example.com');
      expect(externalLink.getAttribute('target')).toBe('_blank');
      expect(externalLink.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('renders <button> only when item has action callback and no href', () => {
      const handleAction = vi.fn();
      const sectionsWithActions: NavSectionConfig[] = [
        {
          id: 'actions',
          items: [
            { id: 'action-btn', label: 'Tıklanabilir Eylem', onClick: handleAction },
          ],
        },
      ];

      render(<Sidebar sections={sectionsWithActions} />);

      const actionEl = screen.getByTestId('sidebar-item-action-btn');
      expect(actionEl.tagName).toBe('BUTTON');
      fireEvent.click(actionEl);
      expect(handleAction).toHaveBeenCalledTimes(1);
    });

    it('renders disabled non-interactive element when item has neither href nor action', () => {
      const sectionsWithInert: NavSectionConfig[] = [
        {
          id: 'inert-sec',
          items: [
            { id: 'inert-item', label: 'Gelecek Özellik' },
          ],
        },
      ];

      render(<Sidebar sections={sectionsWithInert} />);

      const inertEl = screen.getByTestId('sidebar-item-inert-item');
      expect(inertEl.tagName).toBe('DIV');
      expect(inertEl.getAttribute('aria-disabled')).toBe('true');
    });

    it('prevents navigation and applies aria-disabled on disabled link', () => {
      const sectionsWithDisabledLink: NavSectionConfig[] = [
        {
          id: 'disabled-links',
          items: [
            { id: 'disabled-link', label: 'Pasif Bağlantı', href: '/admin/reports', disabled: true },
          ],
        },
      ];

      render(<Sidebar sections={sectionsWithDisabledLink} />);

      const disabledLink = screen.getByTestId('sidebar-item-disabled-link');
      expect(disabledLink.tagName).toBe('A');
      expect(disabledLink.getAttribute('aria-disabled')).toBe('true');
      expect(disabledLink.getAttribute('href')).toBeNull();
      expect(disabledLink.getAttribute('tabindex')).toBe('-1');
    });
  });
});
