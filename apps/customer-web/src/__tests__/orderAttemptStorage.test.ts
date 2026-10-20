import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearOrderAttempt, getOrCreateOrderAttempt, ORDER_ATTEMPT_STORAGE_KEY } from '../cart/orderAttemptStorage';

describe('Pending order attempts', () => {
  beforeEach(() => window.sessionStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('restores the persisted key for the same session and payload', () => {
    const first = getOrCreateOrderAttempt('session-a', 'synthetic-payload');
    expect(first).toMatch(/^ord-key-[a-f0-9]{32}$/);
    expect(getOrCreateOrderAttempt('session-a', 'synthetic-payload')).toBe(first);
  });

  it('rotates the key when the payload changes', () => {
    const first = getOrCreateOrderAttempt('session-a', 'original');
    expect(getOrCreateOrderAttempt('session-a', 'changed')).not.toBe(first);
  });

  it('isolates attempts between dining sessions', () => {
    const first = getOrCreateOrderAttempt('session-a', 'same');
    expect(getOrCreateOrderAttempt('session-b', 'same')).not.toBe(first);
  });

  it('allows an intentional identical new order after clearing the cart', () => {
    const first = getOrCreateOrderAttempt('session-a', 'same');
    clearOrderAttempt();
    expect(window.sessionStorage.getItem(ORDER_ATTEMPT_STORAGE_KEY)).toBeNull();
    expect(getOrCreateOrderAttempt('session-a', 'same')).not.toBe(first);
  });

  it.each(['{broken', 'null', 'false', '4', '{"sessionId":"session-a"}',
    '{"sessionId":"session-a","payloadSignature":"same","idempotencyKey":"invalid"}'])(
    'rejects malformed stored attempts: %s', raw => {
    window.sessionStorage.setItem(ORDER_ATTEMPT_STORAGE_KEY, raw);
    expect(getOrCreateOrderAttempt('session-a', 'same')).toMatch(/^ord-key-[a-f0-9]{32}$/);
  });

  it('rejects requests without a session or signature', () => {
    expect(() => getOrCreateOrderAttempt(null, 'payload')).toThrow();
    expect(() => getOrCreateOrderAttempt('session', '')).toThrow();
    expect(window.sessionStorage.getItem(ORDER_ATTEMPT_STORAGE_KEY)).toBeNull();
  });

  it('fails before sending if the attempt cannot be persisted', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });
    expect(() => getOrCreateOrderAttempt('session-a', 'same')).toThrow('quota');
  });

  it('fails before sending if existing attempts cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('unavailable'); });
    expect(() => getOrCreateOrderAttempt('session-a', 'same')).toThrow('unavailable');
  });

  it('does not turn a successful order into an error if local cleanup fails', () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('unavailable'); });
    expect(() => clearOrderAttempt()).not.toThrow();
  });
});
