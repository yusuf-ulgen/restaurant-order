export const ORDER_ATTEMPT_STORAGE_KEY = 'ro_customer_pending_order';

interface PendingOrderAttempt {
  sessionId: string;
  payloadSignature: string;
  idempotencyKey: string;
}

/**
 * Yanıt kaybından sonra yenilenen sayfa da aynı sipariş denemesini kullanır.
 * İmza mevcut sepet taslağının alanlarıyla sınırlıdır; kimlik tokenı saklanmaz.
 * Kayıt güvenle saklanamıyorsa gönderim başlamadan hata verilir.
 */
export function getOrCreateOrderAttempt(sessionId: string | null, payloadSignature: string): string {
  if (!sessionId || !payloadSignature) throw new Error('Sipariş oturumu doğrulanamadı.');

  const storage = window.sessionStorage;
  const raw = storage.getItem(ORDER_ATTEMPT_STORAGE_KEY);
  let previous: unknown;
  try {
    previous = raw ? JSON.parse(raw) : null;
  } catch {
    previous = null;
  }

  if (previous && typeof previous === 'object') {
    const attempt = previous as Partial<PendingOrderAttempt>;
    if (attempt.sessionId === sessionId && attempt.payloadSignature === payloadSignature &&
        typeof attempt.idempotencyKey === 'string' && /^ord-key-[a-f0-9]{32}$/.test(attempt.idempotencyKey)) {
      return attempt.idempotencyKey;
    }
  }

  const random = crypto.getRandomValues(new Uint8Array(16));
  const idempotencyKey = `ord-key-${Array.from(random, byte => byte.toString(16).padStart(2, '0')).join('')}`;
  const attempt: PendingOrderAttempt = { sessionId, payloadSignature, idempotencyKey };
  storage.setItem(ORDER_ATTEMPT_STORAGE_KEY, JSON.stringify(attempt));
  return idempotencyKey;
}

export function clearOrderAttempt(): void {
  try {
    window.sessionStorage.removeItem(ORDER_ATTEMPT_STORAGE_KEY);
  } catch {
    // Başarılı sunucu yanıtı yerel temizlik hatası nedeniyle başarısız gösterilmez.
  }
}
