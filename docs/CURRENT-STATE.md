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

## Aktif Görev — Faz 9: SignalR Canlı Olaylar ve Bildirimler (Düzeltme Parçası 3/4)

- Durum: `feat/phase-09-realtime-notifications` dalında geliştirme ve sertleştirme sürüyor (PR #24, Issue #20).
- Dört parçalı düzeltme planının 1., 2. ve 3. parçaları uygulandı ve doğrulandı. 4. parça tamamlanana kadar Faz 9 tamamlandı olarak kapatılmaz.
- Takip belgesi: [Faz 9 Takibi](./PHASE-9-TRACKER.md).
- Mimari Kararlar: [ADR-0004](./adr/0004-realtime-event-transmission-architecture.md) (ACCEPTED), [ADR-0011](./adr/0011-background-push-provider.md) (ACCEPTED).
- Parça 1 Kapsamı (Tamamlandı): PostgreSQL outbox şeması, RLS/claim gateway, lease/stale worker yarış korumaları, tekil Redis yayın modeli ve hub proxy.
- Parça 2 Kapsamı (Tamamlandı): Sürekli sunucu yetkilendirmesi, tipli session yönlendirmesi, rol bazlı grup izolasyonu, güvenli HTTP sync cursor ve AggregateVersion dedup.
- Parça 3 Kapsamı (Tamamlandı):
  - Cihaz kaydı sahipliği: aynı sahip yenilemesi, yabancı session/user reddi, masa A->B kapalı oturum güvenli rebind, fail-closed claim denetimi, PostgreSQL 23505 yarış yakalama, idempotent unregister.
  - Kalıcı teslim kuyruğu: `tenancy.notification_deliveries` outbox staging, worker lease koordinasyonu, claim fonksiyonu, `RetryAfter` ve sağlayıcı hata sınıfları (`InvalidRecipient` cihaz pasifleştirme), stabil event ID, no exactly-once kabulü.
  - Gönderim öncesi sürekli yetki denetimi: aktif müşteri dining session, personel branch/membership/rol kontrolü, aktif cihaz kontrolü, kısa olay TTL'i (5 dk).
  - Veri minimizasyonu: sıfır token/installation ID loglama, kilit ekranında opak event ID, yetkili HTTP okuma modeli, service worker allowlisted rota navigasyonu.
  - İstemci push akışı: destek kontrolü, kullanıcı tetikli izin kartı (`PushNotificationBanner`), `sw.js` arka plan dinleyicisi ve ön plan çift uyarı bastırma.

## Sıradaki Görev — Faz 9 Düzeltme Parçası 4/4 ve Faz 10

- Faz 9 Düzeltme Parçası 4/4: Çoklu instance doğrulaması, browser E2E senaryoları, canlı yayılım gecikme ölçümü ve nihai kalite kapıları.
- Faz 10: Garson ve Operasyon Uygulaması (Masa yönetimi, durum bazlı kat planı, canlı sipariş müdahalesi).

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.
- Bu makinede pnpm yardımcı yolu depo dışındaki sabit 11.10.0 kurulumuyla onarıldı. Yerel geçişler MIGRATION_DATABASE_URL ile ayrı geçiş rolünde uygulandı; runtime rolüne DDL yetkisi verilmedi.
- Firebase geliştirme projesi `restaurant-order-development` hazırdır; kullanıcı şimdilik yalnızca proje kurulumunu seçti. Web Push anahtarı, sunucu kimlik bilgisi ve gerçek cihaz testi yoktur; push kapalı kalır (gerçek cihaz teslimi doğrulanmadı).

## Sonraki Somut Adım

Faz 9'un 4. parça düzeltmelerine (çoklu API instance doğrulaması, E2E senaryoları ve gecikme metrikleri) geçmek.
