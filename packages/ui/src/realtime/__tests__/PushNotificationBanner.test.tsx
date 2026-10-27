import { fireEvent, render, screen, waitFor, cleanup } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PushNotificationBanner } from '../PushNotificationBanner.js';
const permissions = vi.hoisted(() => ({ current: 'granted', request: vi.fn() }));
vi.mock('../pushNotifications.js', () => ({ isPushSupported: () => true,
  getPushPermission: () => permissions.current, isPushDismissed: () => false,
  dismissPushPrompt: vi.fn(), requestPushPermission: permissions.request }));
afterEach(cleanup);

describe('push registration feedback', () => {
  it('registers on reopening with permission already granted', async () => {
    permissions.current = 'granted';
    const register = vi.fn().mockResolvedValue(true);
    render(<PushNotificationBanner onPermissionGranted={register} />);
    await waitFor(() => expect(register).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('push-notification-banner')).toBeNull();
  });

  it('keeps a retry prompt visible when registration fails', async () => {
    permissions.current = 'granted';
    const register = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    render(<PushNotificationBanner onPermissionGranted={register} />);
    await screen.findByRole('alert');
    fireEvent.click(screen.getByTestId('enable-push-button'));
    await waitFor(() => expect(screen.queryByTestId('push-notification-banner')).toBeNull());
    expect(register).toHaveBeenCalledTimes(2);
  });

  it('does not register when the user denies permission', async () => {
    permissions.current = 'default'; permissions.request.mockResolvedValue('denied');
    const register = vi.fn();
    render(<PushNotificationBanner onPermissionGranted={register} />);
    fireEvent.click(await screen.findByTestId('enable-push-button'));
    await waitFor(() => expect(screen.queryByTestId('push-notification-banner')).toBeNull());
    expect(register).not.toHaveBeenCalled();
  });
});
