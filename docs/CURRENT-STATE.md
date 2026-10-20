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
- [Issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20): Faz 9 SignalR canlı olaylar, Redis backplane, transactional outbox, sürekli yetkilendirme, HTTP sync ve FCM push cihaz kaydı tamamlandı.
- Tamamlanan kapsam: [Faz 6 takibi](./PHASE-6-TRACKER.md), [Faz 7 takibi](./PHASE-7-TRACKER.md), [Faz 8 takibi](./PHASE-8-TRACKER.md), [Faz 9 takibi](./PHASE-9-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Tamamlanan Faz — Faz 9: Canlı Olaylar ve Bildirimler

- Durum: `feat/phase-09-realtime-notifications` dalında tamamlandı, PR açık, CI doğrulandı.
- GitHub Issue: [#20](https://github.com/yusuf-ulgen/restaurant-order/issues/20).
- Takip belgesi: [Faz 9 Takibi](./PHASE-9-TRACKER.md).
- Mimari Karar: [ADR-0004](./adr/0004-realtime-event-transmission-architecture.md) (ACCEPTED).
- Özet:
  - ASP.NET Core SignalR hub (`/hubs/events`) ve Redis backplane (`restaurant-order:events:broadcast`).
  - Çift yönlü drift testleri bulunan tipli sürüm zarfı (`EventEnvelope<T>`).
  - Transactional Outbox (`tenancy.operational_outbox`), `FOR UPDATE SKIP LOCKED` claim ve exponential backoff ile Worker dispatcher.
  - Sunucu tarafından atanan gruplar (`session`, `floor`, `station`, `catalog`), dinamik token ve oturum iptali ile sürekli yetkilendirme (continuous authorization).
  - Sequence tabanlı yetkili HTTP sync okuma modeli (`/api/v1/events/sync`).
  - FCM cihaz kaydı (`tenancy.device_registrations`), aktif oturum ve üyelik bağı, ADR-0011 push gönderimi ve oturum kapanışında otomatik geçersiz kılma.
  - Müşteri ve operasyon ekranlarında canlı güncellemeler, Web Audio kullanıcı etkileşimli sesli uyarı (chime), mute seçeneği ve offline sepet koruması.
  - Çift instance pub/sub doğrulaması, p95 < 500ms yayılım gecikmesi testi ve Playwright E2E senaryoları.

## Aktif / Sıradaki Görev — Faz 10: Garson ve Operasyon Uygulaması

- İlgili bağlayıcı belge: [docs/ROLES-AND-PERMISSIONS.md](./ROLES-AND-PERMISSIONS.md), [docs/SCREEN-INVENTORY.md](./SCREEN-INVENTORY.md).
- Takip belgesi: `docs/PHASE-10-TRACKER.md` (oluşturulacak).
- Kapsam: Masa yönetimi, durum bazlı kat planı, hızlı mobil sipariş girişi/değiştirme ve servis çağrısı müdahalesi için Arayüz 2 (Garson ve Operasyon Uygulaması).

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

Faz 10 kapsamında Garson ve Operasyon terminali arayüzünün (Arayüz 2) yetkilendirme, kat planı ve canlı sipariş yönetimi ile uçtan uca uygulanması.
