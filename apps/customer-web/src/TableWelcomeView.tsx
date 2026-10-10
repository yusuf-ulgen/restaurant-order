import React, { useState, useEffect, useCallback } from 'react';
import { Card, Button, Spinner, Badge } from '@restaurant-order/ui';
import type { QrResolveResponse, QrExchangeResponse } from '@restaurant-order/contracts';
import {
  customerQrApi,
  CustomerQrApiError,
  getCustomerSession,
  setCustomerSession,
  clearCustomerSession,
} from './customerQrApi';

export interface TableWelcomeViewProps {
  initialToken?: string;
  qrToken?: string;
  onSessionReady?: (session: QrExchangeResponse) => void;
}

type Language = 'tr' | 'en';

const TRANSLATIONS = {
  tr: {
    loading: 'Masa ve oturum bilgileri doğrulanıyor...',
    welcomeHeader: 'Masaya Hoş Geldiniz',
    continueToTable: 'Masaya Devam Et',
    exchanging: 'Oturum Başlatılıyor...',
    sessionActive: 'Masa Oturumu Açıldı',
    sessionNotice:
      'Masa oturumunuz başarıyla aktif edildi. Menü tarama, sipariş verme ve servis çağırma özellikleri sonraki aşamalarda etkinleşecektir.',
    tableLabel: 'Masa',
    branchLabel: 'Şube',
    retry: 'Yeniden Dene',
    errors: {
      invalid: 'Geçersiz veya bozuk QR kodu. Lütfen masadaki QR kodunu tekrar okutun.',
      revoked: 'Bu QR kodunun sürümü yenilenmiş ve geçerliliğini yitirmiştir. Lütfen güncel masa QR kodunu tarayın.',
      session_closed: 'Bu masaya ait oturum sonlandırılmıştır.',
      inactive_table: 'Bu masa şu anda hizmete kapalıdır. Lütfen restoran personeline başvurun.',
      rate_limited: 'Çok fazla deneme yapıldı. Lütfen biraz bekleyin.',
      unavailable: 'Sistem geçici olarak hizmet veremiyor. Lütfen kısa süre sonra tekrar deneyin.',
      generic: 'Bağlantı sırasında bir hata oluştu. Lütfen tekrar deneyin.',
    },
  },
  en: {
    loading: 'Validating table and session credentials...',
    welcomeHeader: 'Welcome to Your Table',
    continueToTable: 'Continue to Table',
    exchanging: 'Starting Session...',
    sessionActive: 'Table Session Started',
    sessionNotice:
      'Your table session is active. Menu browsing, ordering, and service calling features will be enabled in upcoming phases.',
    tableLabel: 'Table',
    branchLabel: 'Branch',
    retry: 'Try Again',
    errors: {
      invalid: 'Invalid or corrupted QR code. Please scan the table QR code again.',
      revoked: 'This QR code has been rotated and is no longer valid. Please scan the updated table QR code.',
      session_closed: 'The dining session for this table has ended.',
      inactive_table: 'This table is currently inactive. Please speak to the restaurant staff.',
      rate_limited: 'Too many requests. Please wait a moment.',
      unavailable: 'The service is temporarily unavailable. Please try again shortly.',
      generic: 'An unexpected connection error occurred. Please try again.',
    },
  },
};

export const TableWelcomeView: React.FC<TableWelcomeViewProps> = ({
  initialToken,
  qrToken,
  onSessionReady,
}) => {
  const [lang, setLang] = useState<Language>('tr');
  const t = TRANSLATIONS[lang];

  // Extract raw token from props or window location (/q/:token or ?token= or #/q/:token)
  const extractToken = useCallback((): string => {
    const rawProp = qrToken || initialToken;
    if (rawProp) return rawProp;
    if (typeof window === 'undefined') return '';

    // Check query param ?token=...
    const urlParams = new URLSearchParams(window.location.search);
    const queryToken = urlParams.get('token');
    if (queryToken) return queryToken;

    // Check path /q/:token
    const pathMatch = window.location.pathname.match(/\/q\/([^/?#]+)/);
    if (pathMatch?.[1]) return decodeURIComponent(pathMatch[1]);

    // Check hash #/q/:token
    const hashMatch = window.location.hash.match(/#\/q\/([^/?#]+)/);
    if (hashMatch?.[1]) return decodeURIComponent(hashMatch[1]);

    return '';
  }, [initialToken, qrToken]);

  const [token, setToken] = useState<string>(extractToken);
  const [resolveData, setResolveData] = useState<QrResolveResponse | null>(null);
  const [activeSession, setActiveSession] = useState<QrExchangeResponse | null>(getCustomerSession);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isExchanging, setIsExchanging] = useState<boolean>(false);
  const [errorType, setErrorType] = useState<string | null>(null);
  const [customErrorMessage, setCustomErrorMessage] = useState<string | null>(null);

  // Strip token from browser URL address bar after exchange
  const sanitizeUrl = useCallback(() => {
    if (typeof window === 'undefined') return;
    try {
      const cleanPath = window.location.pathname.replace(/\/q\/[^/?#]+/, '');
      const searchParams = new URLSearchParams(window.location.search);
      searchParams.delete('token');
      const qs = searchParams.toString();
      const newUrl = `${cleanPath || '/'}${qs ? `?${qs}` : ''}`;
      window.history.replaceState(null, '', newUrl);
    } catch {
      // Ignore if history API restricted
    }
  }, []);

  // Resolve QR token
  const handleResolve = useCallback(async (tokenToResolve: string) => {
    if (!tokenToResolve) {
      setIsLoading(false);
      setErrorType('invalid');
      setCustomErrorMessage(null);
      return;
    }

    setIsLoading(true);
    setErrorType(null);
    setCustomErrorMessage(null);

    try {
      const data = await customerQrApi.resolveQr(tokenToResolve);
      setResolveData(data);
    } catch (err: unknown) {
      if (err instanceof CustomerQrApiError) {
        setErrorType(err.errorType);
        setCustomErrorMessage(err.message);
      } else {
        setErrorType('generic');
        setCustomErrorMessage(null);
      }
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    const raw = extractToken();
    setToken(raw);
    if (!activeSession) {
      handleResolve(raw);
    } else {
      setIsLoading(false);
    }
  }, [extractToken, activeSession, handleResolve]);

  // Exchange QR token for session
  const handleExchange = async () => {
    if (!token) return;
    setIsExchanging(true);
    setErrorType(null);
    setCustomErrorMessage(null);

    try {
      const session = await customerQrApi.exchangeQr(token);
      setCustomerSession(session);
      setActiveSession(session);
      // Clean raw QR token from URL immediately
      sanitizeUrl();
      onSessionReady?.(session);
    } catch (err: unknown) {
      if (err instanceof CustomerQrApiError) {
        setErrorType(err.errorType);
        setCustomErrorMessage(err.message);
      } else {
        setErrorType('generic');
        setCustomErrorMessage(null);
      }
    } finally {
      setIsExchanging(false);
    }
  };

  const handleLeaveSession = () => {
    clearCustomerSession();
    setActiveSession(null);
    setResolveData(null);
    const raw = extractToken();
    handleResolve(raw);
  };

  const displayedErrorMessage = customErrorMessage || (errorType ? (t.errors[errorType as keyof typeof t.errors] || t.errors.generic) : null);

  return (
    <div style={{ maxWidth: '480px', margin: '0 auto', padding: 'var(--ro-space-4)' }} data-testid="table-welcome-container">
      {/* Language Switcher Bar */}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 'var(--ro-space-3)' }}>
        <div style={{ display: 'flex', gap: '4px' }}>
          <Button
            variant={lang === 'tr' ? 'primary' : 'outline'}
            size="sm"
            onClick={() => setLang('tr')}
            data-testid="lang-tr-btn"
          >
            TR
          </Button>
          <Button
            variant={lang === 'en' ? 'primary' : 'outline'}
            size="sm"
            onClick={() => setLang('en')}
            data-testid="lang-en-btn"
          >
            EN
          </Button>
        </div>
      </div>

      {/* Loading View */}
      {isLoading ? (
        <Card padding="md">
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', padding: 'var(--ro-space-8)', gap: 'var(--ro-space-3)' }}>
            <Spinner size="lg" label={t.loading} />
            <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-muted)' }}>
              {t.loading}
            </p>
          </div>
        </Card>
      ) : errorType ? (
        /* Error Views */
        <Card padding="md">
          <div style={{ textAlign: 'center', padding: 'var(--ro-space-4)' }} data-testid="qr-error-view">
            <h2 style={{ fontSize: 'var(--ro-font-size-lg)', fontWeight: 700, margin: '0 0 var(--ro-space-2) 0' }}>
              {errorType === 'invalid'
                ? (lang === 'tr' ? 'Geçersiz QR Kod' : 'Invalid QR Code')
                : errorType === 'revoked'
                ? (lang === 'tr' ? 'İptal Edilmiş QR' : 'Revoked QR Code')
                : errorType === 'inactive_table'
                ? (lang === 'tr' ? 'Masa Kapalı' : 'Inactive Table')
                : (lang === 'tr' ? 'Bir Sorun Oluştu' : 'An Error Occurred')}
            </h2>
            <p style={{ color: 'var(--ro-color-danger)', fontSize: 'var(--ro-font-size-sm)', marginBottom: 'var(--ro-space-4)' }}>
              {displayedErrorMessage}
            </p>
            <Button
              variant="outline"
              size="md"
              onClick={() => handleResolve(token)}
              data-testid="qr-retry-btn"
            >
              {t.retry}
            </Button>
          </div>
        </Card>
      ) : activeSession ? (
        /* Successful Session Welcome State */
        <Card padding="md">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }} data-testid="session-welcome-view">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Badge variant="success">{t.sessionActive}</Badge>
              <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                {activeSession.sessionStatus}
              </span>
            </div>

            <div>
              <h2 style={{ margin: '0 0 4px 0', fontSize: 'var(--ro-font-size-xl)', fontWeight: 700 }}>
                {t.tableLabel} {activeSession.tableNumber}
              </h2>
              <p style={{ margin: 0, color: 'var(--ro-color-text-secondary)', fontSize: 'var(--ro-font-size-md)' }}>
                {activeSession.tableName}
              </p>
            </div>

            <div
              style={{
                backgroundColor: '#f5f5f5',
                padding: 'var(--ro-space-3)',
                borderRadius: 'var(--ro-radius-md)',
                border: '1px solid var(--ro-color-border)',
              }}
            >
              <p style={{ margin: 0, fontSize: 'var(--ro-font-size-xs)', color: '#424242' }}>
                {t.sessionNotice}
              </p>
            </div>

            <div style={{ display: 'flex', justifyContent: 'center' }}>
              <Button
                variant="ghost"
                size="sm"
                onClick={handleLeaveSession}
                data-testid="leave-session-btn"
              >
                Oturumu Değiştir / Çıkış
              </Button>
            </div>
          </div>
        </Card>
      ) : resolveData ? (
        /* Token Resolved -> Table Welcome CTA State */
        <Card padding="md">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }} data-testid="table-resolve-view">
            <div>
              <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                {resolveData.branchName}
              </span>
              <h2 style={{ margin: '4px 0 0 0', fontSize: 'var(--ro-font-size-xl)', fontWeight: 700 }}>
                {t.tableLabel} {resolveData.tableNumber}
              </h2>
              <p style={{ margin: '2px 0 0 0', color: 'var(--ro-color-text-secondary)', fontSize: 'var(--ro-font-size-md)' }}>
                {resolveData.tableName}
              </p>
            </div>

            <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-muted)' }}>
              {lang === 'tr'
                ? 'Bu masada sipariş deneyiminizi başlatmak için aşağıdaki butona dokunun.'
                : 'Tap the button below to begin your dining experience at this table.'}
            </p>

            <Button
              variant="primary"
              size="lg"
              onClick={handleExchange}
              disabled={isExchanging}
              data-testid="continue-to-table-btn"
              style={{ minHeight: '48px', width: '100%' }}
            >
              {isExchanging ? t.exchanging : t.continueToTable}
            </Button>
          </div>
        </Card>
      ) : null}
    </div>
  );
};
