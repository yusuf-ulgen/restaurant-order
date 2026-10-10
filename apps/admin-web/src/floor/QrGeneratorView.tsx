import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Button,
  Spinner,
  ConfirmationDialog,
} from '@restaurant-order/ui';
import type {
  RestaurantTableDto,
  TableFloorStatusDto,
  TableQrCodeDto,
  SessionDynamicQrDto,
} from '@restaurant-order/contracts';
import { floorApi } from './floorApi';

export interface QrGeneratorViewProps {
  branchId: string;
  branchName: string;
  tables: RestaurantTableDto[];
  statuses: TableFloorStatusDto[];
  onTableRotated: (updatedTable: RestaurantTableDto) => void;
}

export const QrGeneratorView: React.FC<QrGeneratorViewProps> = ({
  branchId,
  branchName,
  tables,
  statuses,
  onTableRotated,
}) => {
  const [selectedTableId, setSelectedTableId] = useState<string>(tables[0]?.id || '');
  const [qrType, setQrType] = useState<'static' | 'dynamic'>('static');
  const [staticQr, setStaticQr] = useState<TableQrCodeDto | null>(null);
  const [dynamicQr, setDynamicQr] = useState<SessionDynamicQrDto | null>(null);
  const [isLoadingQr, setIsLoadingQr] = useState(false);
  const [qrError, setQrError] = useState<string | null>(null);

  // Rotate/Revoke Confirmation Dialog
  const [isRotateConfirmOpen, setIsRotateConfirmOpen] = useState(false);
  const [isRotating, setIsRotating] = useState(false);

  // Batch Print Preview Mode
  const [isPrintMode, setIsPrintMode] = useState(false);
  const [batchQrs, setBatchQrs] = useState<Record<string, string>>({});
  const [isLoadingBatch, setIsLoadingBatch] = useState(false);

  const selectedTable = tables.find((t) => t.id === selectedTableId) || null;
  const statusEntry = statuses.find((s) => s.table?.id === selectedTableId);
  const activeSession = statusEntry?.activeSession;
  const hasActiveSession = Boolean(activeSession && activeSession.status !== 'Closed');

  // Load single QR
  const loadQr = useCallback(async () => {
    if (!branchId || !selectedTableId) return;
    setIsLoadingQr(true);
    setQrError(null);

    try {
      if (qrType === 'static') {
        const data = await floorApi.getTableStaticQr(branchId, selectedTableId);
        setStaticQr(data);
      } else if (hasActiveSession && activeSession) {
        const data = await floorApi.getSessionDynamicQr(branchId, activeSession.id);
        setDynamicQr(data);
      } else {
        setDynamicQr(null);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'QR kod yüklenemedi.';
      setQrError(msg);
    } finally {
      setIsLoadingQr(false);
    }
  }, [branchId, selectedTableId, qrType, hasActiveSession, activeSession]);

  useEffect(() => {
    if (selectedTableId) {
      loadQr();
    }
  }, [selectedTableId, qrType, loadQr]);

  // Handle Single SVG Download
  const handleDownloadSvg = () => {
    const svgContent = qrType === 'static' ? staticQr?.svg : dynamicQr?.svg;
    if (!svgContent || !selectedTable) return;

    const blob = new Blob([svgContent], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `QR-${branchName}-${selectedTable.tableNumber}-${qrType}.svg`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  // Handle Rotate / Revoke QR
  const handleConfirmRotate = async () => {
    if (!selectedTable) return;
    setIsRotating(true);
    try {
      const updated = await floorApi.rotateTableQr(
        branchId,
        selectedTable.id,
        selectedTable.concurrencyToken
      );
      onTableRotated(updated);
      setIsRotateConfirmOpen(false);
      await loadQr();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'QR kodu yenilenemedi.';
      setQrError(msg);
    } finally {
      setIsRotating(false);
    }
  };

  // Prepare Print Mode (Fetch All Static QR SVGs for all tables)
  const handleEnterPrintMode = async () => {
    setIsPrintMode(true);
    setIsLoadingBatch(true);
    const qrs: Record<string, string> = {};

    try {
      await Promise.all(
        tables.map(async (tbl) => {
          try {
            const data = await floorApi.getTableStaticQr(branchId, tbl.id);
            qrs[tbl.id] = data.svg;
          } catch {
            // Skip failed table QR in batch
          }
        })
      );
      setBatchQrs(qrs);
    } finally {
      setIsLoadingBatch(false);
    }
  };

  const handlePrint = () => {
    window.print();
  };

  return (
    <div className="floor-container" data-testid="qr-generator-view">
      {/* Top Action Bar */}
      <div className="floor-header-controls no-print">
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
          <label htmlFor="qr-table-select" style={{ fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
            Masa Seçin:
          </label>
          <select
            id="qr-table-select"
            className="floor-input-control"
            style={{ width: 'auto', minWidth: '180px' }}
            value={selectedTableId}
            onChange={(e) => setSelectedTableId(e.target.value)}
          >
            {tables.map((t) => (
              <option key={t.id} value={t.id}>
                {t.tableNumber} - {t.name} (v{t.qrVersion})
              </option>
            ))}
          </select>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
          <Button
            variant="outline"
            size="sm"
            onClick={handleEnterPrintMode}
            data-testid="batch-print-btn"
          >
            Toplu Yazdırma Görünümü
          </Button>
        </div>
      </div>

      {/* Main Single Preview View */}
      {!isPrintMode ? (
        <Card padding="md">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
            {/* QR Type Selection Tabs */}
            <div className="floor-tabs">
              <button
                type="button"
                className={`floor-tab-btn ${qrType === 'static' ? 'active' : ''}`}
                onClick={() => setQrType('static')}
              >
                Statik Masa QR (Kalıcı Baskı)
              </button>
              <button
                type="button"
                className={`floor-tab-btn ${qrType === 'dynamic' ? 'active' : ''}`}
                onClick={() => setQrType('dynamic')}
              >
                Dinamik Oturum QR (Tek Seferlik)
              </button>
            </div>

            {/* Content Display */}
            {isLoadingQr ? (
              <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-8)' }}>
                <Spinner size="md" label="QR kod üretiliyor..." />
              </div>
            ) : qrError ? (
              <div style={{ padding: 'var(--ro-space-4)', textAlign: 'center', color: 'var(--ro-color-danger)' }}>
                <p>{qrError}</p>
                <Button variant="outline" size="sm" onClick={loadQr}>
                  Tekrar Dene
                </Button>
              </div>
            ) : qrType === 'dynamic' && !hasActiveSession ? (
              <div
                style={{
                  padding: 'var(--ro-space-6)',
                  textAlign: 'center',
                  backgroundColor: '#f9f9f9',
                  borderRadius: 'var(--ro-radius-md)',
                  border: '1px dashed var(--ro-color-border)',
                }}
              >
                <p style={{ margin: 0, fontWeight: 600, color: 'var(--ro-color-text-secondary)' }}>
                  Bu masada açık veya aktif bir oturum bulunmuyor.
                </p>
                <p style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)', marginTop: '4px' }}>
                  Dinamik QR yalnızca misafir oturumu açılmış masalarda üretilebilir. Masaya yerleşim ekranından oturum açabilirsiniz.
                </p>
              </div>
            ) : (
              <div className="qr-preview-card">
                <div
                  className="qr-svg-wrapper"
                  dangerouslySetInnerHTML={{
                    __html: qrType === 'static' ? staticQr?.svg || '' : dynamicQr?.svg || '',
                  }}
                  data-testid="qr-svg-preview"
                />

                <div className="qr-label-block">
                  <span className="qr-label-branch">{branchName}</span>
                  <span className="qr-label-table">
                    Masa {selectedTable?.tableNumber} - {selectedTable?.name}
                  </span>
                  <span style={{ fontSize: '11px', color: '#757575' }}>
                    {qrType === 'static'
                      ? `Sürüm: v${selectedTable?.qrVersion} (İmzalı Güvenli Token)`
                      : `Dinamik Oturum: ${activeSession?.status || 'Aktif'}`}
                  </span>
                </div>

                <div style={{ display: 'flex', gap: 'var(--ro-space-2)', marginTop: 'var(--ro-space-3)' }}>
                  <Button
                    variant="primary"
                    size="sm"
                    onClick={handleDownloadSvg}
                    data-testid="download-svg-btn"
                  >
                    SVG İndir
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handlePrint}
                    data-testid="print-qr-btn"
                  >
                    Yazdır / PDF Olarak Kaydet
                  </Button>
                  {qrType === 'static' && (
                    <Button
                      variant="danger"
                      size="sm"
                      onClick={() => setIsRotateConfirmOpen(true)}
                      data-testid="rotate-qr-btn"
                    >
                      QR Sürümünü İptal Et & Yenile
                    </Button>
                  )}
                </div>
              </div>
            )}
          </div>
        </Card>
      ) : (
        /* Batch Print Preview Area */
        <Card padding="md">
          <div className="no-print" style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 'var(--ro-space-4)' }}>
            <div>
              <h3 style={{ margin: 0, fontSize: 'var(--ro-font-size-md)' }}>Tüm Masalar İçin Baskı Önizlemesi</h3>
              <p style={{ margin: 0, fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                Tarayıcı yazdırma penceresinde "Hedef: PDF Olarak Kaydet" seçeneğini kullanabilirsiniz.
              </p>
            </div>
            <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
              <Button variant="outline" size="sm" onClick={() => setIsPrintMode(false)}>
                Önizlemeyi Kapat
              </Button>
              <Button variant="primary" size="sm" onClick={handlePrint} data-testid="batch-print-action-btn">
                Yazdır / PDF Olarak Kaydet
              </Button>
            </div>
          </div>

          {isLoadingBatch ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-8)' }}>
              <Spinner size="md" label="Masa QR kodları hazırlanıyor..." />
            </div>
          ) : (
            <div className="qr-printable-area qr-print-grid">
              {tables.map((tbl) => (
                <div key={tbl.id} className="qr-print-card">
                  <div
                    style={{ width: '160px', height: '160px' }}
                    dangerouslySetInnerHTML={{ __html: batchQrs[tbl.id] || '' }}
                  />
                  <div className="qr-label-block">
                    <span className="qr-label-branch">{branchName}</span>
                    <span className="qr-label-table">Masa {tbl.tableNumber}</span>
                    <span style={{ fontSize: '11px', color: '#616161' }}>{tbl.name} • Kapasite: {tbl.capacity}p</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}

      {/* Rotate / Revoke QR Confirmation Dialog */}
      <ConfirmationDialog
        isOpen={isRotateConfirmOpen}
        title="QR Kodunu İptal Et ve Yenile"
        message={`Masa ${selectedTable?.tableNumber} için mevcut QR kodunu geçersiz kılmak üzeresiniz. Bu işlem sonrasında masadaki eski basılı QR kodu ile sipariş verilemez. Yeni bir QR kodu basmanız gerekecektir. Onaylıyor musunuz?`}
        confirmLabel={isRotating ? 'Yenileniyor...' : 'Evet, İptal Et & Yenile'}
        cancelLabel="Vazgeç"
        variant="danger"
        onConfirm={handleConfirmRotate}
        onCancel={() => setIsRotateConfirmOpen(false)}
      />
    </div>
  );
};
