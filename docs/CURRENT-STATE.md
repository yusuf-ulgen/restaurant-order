# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-25. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `92ebcca` (PR #23 `fix/order-review-regressions` birleşmesi ve Faz 8 inceleme düzeltmeleri).
- Faz 0-8 tamamlandı olarak kayıtlı; Faz 8 sipariş çekirdeği, otoriter fiyatlandırma motoru, müşteri sipariş arayüzü, personel sipariş altyapısı, yaşam döngüsü ve takip eden finansal/oturum yarış inceleme düzeltmeleri `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15): FCM sunucu bildirim altyapısı ve sağlayıcıdan bağımsız taşıma sözleşmesi main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- [PR #17](https://github.com/yusuf-ulgen/restaurant-order/pull/17): Faz 7 müşteri deneyimi, sepet taslağı, servis talepleri ve güvenlik sertleştirmeleri main'e birleştirildi.
- [Issue #18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), [PR #19](https://github.com/yusuf-ulgen/restaurant-order/pull/19): Faz 8 sipariş çekirdeği, yetkili fiyatlandırma ve yaşam döngüsü altyapısı main'e birleştirildi.
- [Issue #22](https://github.com/yusuf-ulgen/restaurant-order/issues/22), [PR #23](https://github.com/yusuf-ulgen/restaurant-order/pull/23): Faz 8 sipariş inceleme regresyonları (oturum kapanışı/hesap isteme yarışı, kısmi hazırlık durumu, yanıt kaybı sonrası yenilemede stabil sipariş anahtarı) main'e birleştirildi.
- Tamamlanan kapsam: [Faz 6 takibi](./PHASE-6-TRACKER.md), [Faz 7 takibi](./PHASE-7-TRACKER.md), [Faz 8 takibi](./PHASE-8-TRACKER.md), [Faz 8 incelemesi](./PHASE-8-REVIEW.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Aktif Görev — Faz 9: SignalR Canlı Olaylar ve Bildirimler (Düzeltme Parçası 1/4)

- Durum: `feat/phase-09-realtime-notifications` dalında geliştirme ve sertleştirme sürüyor (PR #24, Issue #20).
- Dört parçalı düzeltme planının 1. parçası uygulanıyor: PostgreSQL transactional outbox, worker RLS/leadership claim güvenliği ve SignalR taşıma altyapısı düzeltmeleri. Diğer parçalar tamamlanana kadar Faz 9 tamamlandı olarak kapatılmaz.
- Takip belgesi: [Faz 9 Takibi](./PHASE-9-TRACKER.md).
- Mimari Karar: [ADR-0004](./adr/0004-realtime-event-transmission-architecture.md) (ACCEPTED).
- Parça 1 Kapsamı:
  - Gerçek PostgreSQL `tenancy.operational_outbox` şeması ile `OperationalOutboxDispatcher` kolon sözleşmesinin uyumlu hale getirilmesi.
  - Ayrıcalıksız `restaurant_app_runtime` rolü ve FORCE ROW LEVEL SECURITY altında tenant bazlı/güvenli claim gateway'in işletilmesi.
  - Eşzamanlı claim sahipliği, lease yenileme ve stale worker ezme korumaları.
  - Çoklu API instance'ında çift backplane yayınını engelleyen tekil yayın modeli.
  - Hub development proxy ve production ingress (negotiate, WebSocket upgrade, fallback) yapılandırması.

## Sıradaki Görev — Faz 9 Düzeltme Parçaları 2-4 ve Faz 10

- Faz 9 kalan düzeltme parçalarının tamamlanması.
- Faz 10: Garson ve Operasyon Uygulaması (Masa yönetimi, durum bazlı kat planı, canlı sipariş müdahalesi).

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.
- Bu makinede pnpm yardımcı yolu depo dışındaki sabit 11.10.0 kurulumuyla onarıldı. Yerel geçişler MIGRATION_DATABASE_URL ile ayrı geçiş rolünde uygulandı; runtime rolüne DDL yetkisi verilmedi.
- Firebase geliştirme projesi `restaurant-order-development` hazırdır; kullanıcı şimdilik yalnızca proje kurulumunu seçti. Web Push anahtarı, sunucu kimlik bilgisi ve gerçek cihaz testi yoktur; push kapalı kalır.

## Sonraki Somut Adım

Faz 9'un 1. parça altyapı düzeltmelerinin (PostgreSQL outbox kolon/mapping, worker RLS/claim gateway, lease/stale worker yarış korumaları, tekil Redis yayın modeli ve hub proxy) testlerini tamamlayıp commit etmek.
