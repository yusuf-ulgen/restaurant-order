import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RealtimeStatusBadge } from '../RealtimeStatusBadge.js';
import type { RealtimeConnectionStatus } from '../types.js';

describe('RealtimeStatusBadge', () => {
  const statuses: { status: RealtimeConnectionStatus; expectedText: string }[] = [
    { status: 'connected', expectedText: 'Canlı' },
    { status: 'connecting', expectedText: 'Bağlanıyor...' },
    { status: 'reconnecting', expectedText: 'Yeniden bağlanıyor...' },
    { status: 'disconnected', expectedText: 'Çevrimdışı' },
    { status: 'session_closed', expectedText: 'Oturum Sona Erdi' },
  ];

  statuses.forEach(({ status, expectedText }) => {
    it(`renders correct label and accessible role for status: ${status}`, () => {
      render(<RealtimeStatusBadge status={status} />);
      const badge = screen.getByRole('status');
      expect(badge).toBeDefined();
      expect(badge.textContent).toContain(expectedText);
    });
  });
});
