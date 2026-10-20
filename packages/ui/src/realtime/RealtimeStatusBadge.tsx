import React from 'react';
import type { RealtimeConnectionStatus } from './types.js';

export interface RealtimeStatusBadgeProps {
  status: RealtimeConnectionStatus;
  className?: string;
}

const statusConfig: Record<
  RealtimeConnectionStatus,
  { label: string; dotColor: string; bg: string; text: string }
> = {
  connected: {
    label: 'Canlı',
    dotColor: '#10B981', // green-500
    bg: 'rgba(16, 185, 129, 0.1)',
    text: '#059669',
  },
  connecting: {
    label: 'Bağlanıyor...',
    dotColor: '#F59E0B', // amber-500
    bg: 'rgba(245, 158, 11, 0.1)',
    text: '#D97706',
  },
  reconnecting: {
    label: 'Yeniden bağlanıyor...',
    dotColor: '#F59E0B',
    bg: 'rgba(245, 158, 11, 0.1)',
    text: '#D97706',
  },
  disconnected: {
    label: 'Çevrimdışı',
    dotColor: '#9CA3AF', // gray-400
    bg: 'rgba(156, 163, 175, 0.1)',
    text: '#6B7280',
  },
  session_closed: {
    label: 'Oturum Sona Erdi',
    dotColor: '#EF4444', // red-500
    bg: 'rgba(239, 68, 68, 0.1)',
    text: '#DC2626',
  },
};

export const RealtimeStatusBadge: React.FC<RealtimeStatusBadgeProps> = ({ status, className }) => {
  const config = statusConfig[status] ?? statusConfig.disconnected;

  return (
    <div
      role="status"
      aria-live="polite"
      className={className}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '6px',
        padding: '3px 8px',
        borderRadius: '9999px',
        backgroundColor: config.bg,
        color: config.text,
        fontSize: '12px',
        fontWeight: 500,
        fontFamily: 'inherit',
        lineHeight: 1,
      }}
    >
      <span
        style={{
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          backgroundColor: config.dotColor,
          display: 'inline-block',
        }}
      />
      <span>{config.label}</span>
    </div>
  );
};
