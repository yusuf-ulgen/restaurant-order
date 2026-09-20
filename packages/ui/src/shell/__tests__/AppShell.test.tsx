import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AppShell } from '../AppShell';

describe('AppShell Component', () => {
  it('renders skip link, main content, and default landmarks', () => {
    render(
      <AppShell>
        <div>Main Page Content</div>
      </AppShell>
    );

    const skipLink = screen.getByTestId('skip-link');
    expect(skipLink.getAttribute('href')).toBe('#main-content');
    expect(screen.getByRole('main')).toBeDefined();
    expect(screen.getByText('Main Page Content')).toBeDefined();
  });

  it('omits sidebar completely when variant="customer"', () => {
    render(
      <AppShell
        variant="customer"
        sidebar={{
          sections: [
            {
              id: 'sec1',
              title: 'Customer Sidebar',
              items: [{ id: '1', label: 'Item 1' }],
            },
          ],
        }}
      >
        <div>Customer View</div>
      </AppShell>
    );

    expect(screen.queryByTestId('desktop-sidebar')).toBeNull();
    expect(screen.queryByText('Customer Sidebar')).toBeNull();
    expect(screen.getByText('Customer View')).toBeDefined();
  });

  it('renders sidebar when variant="admin"', () => {
    render(
      <AppShell
        variant="admin"
        sidebar={{
          title: 'Admin Sidebar',
          sections: [
            {
              id: 'sec1',
              title: 'Yönetim',
              items: [{ id: 'dash', label: 'Dashboard' }],
            },
          ],
        }}
      >
        <div>Admin View</div>
      </AppShell>
    );

    expect(screen.getByTestId('desktop-sidebar')).toBeDefined();
    expect(screen.getByText('Admin Sidebar')).toBeDefined();
    expect(screen.getByText('Dashboard')).toBeDefined();
  });

  it('opens and closes mobile drawer via header toggle', () => {
    render(
      <AppShell
        variant="admin"
        header={{ title: 'Admin Title' }}
        sidebar={{
          title: 'Sidebar Title',
          sections: [
            {
              id: 'sec1',
              items: [{ id: '1', label: 'Mobile Nav Item' }],
            },
          ],
        }}
      >
        <div>Content</div>
      </AppShell>
    );

    const toggleBtn = screen.getByTestId('mobile-menu-toggle');
    fireEvent.click(toggleBtn);

    // Mobile Drawer dialog should open
    const drawer = screen.getByRole('dialog');
    expect(drawer).toBeDefined();
    expect(screen.getAllByText('Mobile Nav Item').length).toBeGreaterThan(0);

    // Close button inside Drawer
    const closeBtn = screen.getByRole('button', { name: 'Kapat' });
    fireEvent.click(closeBtn);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('renders mobile bottom navigation when mobileNav is provided', () => {
    const handleHomeClick = vi.fn();
    render(
      <AppShell
        variant="operations"
        mobileNav={{
          items: [
            { id: 'tables', label: 'Masalar', isActive: true },
            { id: 'orders', label: 'Siparişler', onClick: handleHomeClick },
          ],
        }}
      >
        <div>Operations View</div>
      </AppShell>
    );

    expect(screen.getByTestId('mobile-navigation')).toBeDefined();
    expect(screen.getByText('Masalar')).toBeDefined();
    const ordersBtn = screen.getByTestId('mobile-nav-item-orders');
    fireEvent.click(ordersBtn);
    expect(handleHomeClick).toHaveBeenCalledTimes(1);
  });

  it('handles missing or empty optional configuration without crashing', () => {
    render(
      <AppShell
        header={undefined}
        footer={undefined}
        sidebar={undefined}
        mobileNav={undefined}
      >
        <div>Safe Content</div>
      </AppShell>
    );

    expect(screen.getByText('Safe Content')).toBeDefined();
  });

  describe('Controlled vs Uncontrolled Sidebar State', () => {
    it('reflects external prop updates when sidebar.isCollapsed is controlled', () => {
      const handleToggle = vi.fn();
      const sections = [{ id: 's1', items: [{ id: 'i1', label: 'Item 1' }] }];

      const { rerender } = render(
        <AppShell
          variant="admin"
          sidebar={{
            sections,
            isCollapsed: false,
            onToggleCollapse: handleToggle,
          }}
        >
          <div>Controlled View</div>
        </AppShell>
      );

      const desktopSidebar = screen.getByTestId('desktop-sidebar');
      expect(desktopSidebar.getAttribute('data-collapsed')).toBeNull();

      // Parent updates isCollapsed to true
      rerender(
        <AppShell
          variant="admin"
          sidebar={{
            sections,
            isCollapsed: true,
            onToggleCollapse: handleToggle,
          }}
        >
          <div>Controlled View</div>
        </AppShell>
      );

      expect(desktopSidebar.getAttribute('data-collapsed')).toBe('true');

      // Clicking collapse button fires onToggleCollapse
      const collapseBtn = screen.getByTestId('sidebar-collapse-btn');
      fireEvent.click(collapseBtn);
      expect(handleToggle).toHaveBeenCalledTimes(1);
    });

    it('toggles state internally when sidebar.isCollapsed is uncontrolled', () => {
      const sections = [{ id: 's1', items: [{ id: 'i1', label: 'Item 1' }] }];

      render(
        <AppShell
          variant="admin"
          sidebar={{
            sections,
            // isCollapsed is undefined => uncontrolled
          }}
        >
          <div>Uncontrolled View</div>
        </AppShell>
      );

      const desktopSidebar = screen.getByTestId('desktop-sidebar');
      expect(desktopSidebar.getAttribute('data-collapsed')).toBeNull();

      const collapseBtn = screen.getByTestId('sidebar-collapse-btn');
      fireEvent.click(collapseBtn);
      expect(desktopSidebar.getAttribute('data-collapsed')).toBe('true');

      fireEvent.click(collapseBtn);
      expect(desktopSidebar.getAttribute('data-collapsed')).toBeNull();
    });
  });
});
