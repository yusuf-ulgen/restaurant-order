# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-17. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `cfb27e6` (PR #19 `feat(orders): Faz 8 Siparis Cekirdegi, Yetkili Fiyatlandirma ve Yasam Dongusu` birleşmesi).
- Faz 0-8 tamamlandı olarak kayıtlı; Faz 8 sipariş çekirdeği, otoriter fiyatlandırma motoru, müşteri sipariş arayüzü, personel sipariş altyapısı ve yaşam döngüsü `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15): FCM sunucu bildirim altyapısı ve sağlayıcıdan bağımsız taşıma sözleşmesi main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- [Issue #18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), [PR #17](https://github.com/yusuf-ulgen/restaurant-order/pull/17): Faz 7 müşteri deneyimi, sepet taslağı, servis talepleri ve güvenlik sertleştirmeleri main'e birleştirildi.
- [Issue #18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), [PR #19](https://github.com/yusuf-ulgen/restaurant-order/pull/19): Faz 8 sipariş çekirdeği, yetkili fiyatlandırma ve yaşam döngüsü altyapısı main'e birleştirildi.
- Tamamlanan kapsam: [Faz 6 takibi](./PHASE-6-TRACKER.md), [Faz 7 takibi](./PHASE-7-TRACKER.md), [Faz 8 takibi](./PHASE-8-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Tamamlanan Faz — Faz 8: Sipariş Çekirdeği

- Durum: `main` dalına birleştirildi (`cfb27e6`), PR #19 kapandı.
- GitHub Issue: [#18](https://github.com/yusuf-ulgen/restaurant-order/issues/18) (Kapatıldı).
- Takip belgesi: [Faz 8 Takibi](./PHASE-8-TRACKER.md).
- Özet:
  - Domain aggregate yapısı (`Order`, `OrderItem`, `OrderItemModifier`, `OrderAuditEntry`), PostgreSQL şeması ve RLS.
  - Sunucu tarafı otoriter `OrderPricingEngine`, minor-unit deterministik finans motoru, aktif katalog doğrulaması.
  - Müşteri sipariş onay ve gönderim deneyimi (`OrderConfirmationSheet`, `OrderSuccessSheet`, `OrderHistorySheet`), stabil idempotency key ve sepet politikası yeniden onay akışı.
  - Personel sipariş altyapısı (`StaffOrderEndpoints`), RBAC yetkilendirmesi, supervisor void ve PIN doğrulama akışı.
  - 15 adımlı tam E2E ve negatif testler, CI Quality & Verification Pipeline yeşil.

## Aktif / Sıradaki Görev — Faz 9: SignalR Canlı Olaylar ve Bildirimler

- İlgili bağlayıcı belge: [docs/REALTIME-AND-NOTIFICATIONS.md](./REALTIME-AND-NOTIFICATIONS.md).
- Kapsam: Masa oturumu, sipariş durumu güncellemeleri, garson çağrısı ve mutfak/bar KDS bildirimleri için SignalR hub altyapısı, Redis backplane ve FCM entegrasyonu.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

Faz 9 için issue açılarak `feat/phase-09-realtime-notifications` dalının oluşturulması, [docs/REALTIME-AND-NOTIFICATIONS.md](./REALTIME-AND-NOTIFICATIONS.md) alan belgesine göre SignalR hub ve taşıma sözleşmelerinin başlatılması.
