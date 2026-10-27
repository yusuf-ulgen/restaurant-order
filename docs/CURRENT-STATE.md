# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-27. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

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

## Aktif Görev — Faz 9: SignalR Canlı Olaylar ve Bildirimler (Düzeltme Parçası 4/4 ve Kapanış — TAMAMLANDI)

- Durum: `feat/phase-09-realtime-notifications` dalında geliştirme ve sertleştirme tamamlandı (PR #24, Issue #20).
- Dört parçalı düzeltme planının tüm parçaları (1., 2., 3. ve 4. parçalar) uygulandı ve %100 doğrulandı.
- Takip belgesi: [Faz 9 Takibi](./PHASE-9-TRACKER.md).
- Mimari Kararlar: [ADR-0004](./adr/0004-realtime-event-transmission-architecture.md) (ACCEPTED), [ADR-0011](./adr/0011-background-push-provider.md) (ACCEPTED).
- Parça 1 Kapsamı (Tamamlandı): PostgreSQL outbox şeması, RLS/claim gateway, lease/stale worker yarış korumaları, tekil Redis yayın modeli ve hub proxy.
- Parça 2 Kapsamı (Tamamlandı): Sürekli sunucu yetkilendirmesi, tipli session yönlendirmesi, rol bazlı grup izolasyonu, güvenli HTTP sync cursor ve AggregateVersion dedup.
- Parça 3 Kapsamı (Tamamlandı): Cihaz kaydı sahipliği, kalıcı bildirim teslim kuyruğu, sürekli yetki denetimi, veri minimizasyonu ve istemci push yaşam döngüsü.
- Parça 4 Kapsamı (Tamamlandı):
  - İki gerçek API instance doğrulaması (`TwoApiInstanceRealtimeIntegrationTests.cs`, 5 test, 5/5 geçti): A instance'ından mutasyon, B instance'ındaki SignalR istemcisine tekil teslim, tenant/branch scope izolasyonu, deduplication, instance restart recovery ve session revocation doğrulaması.
  - Uçtan uca gecikme ölçümü: Üretim outbox/worker/Redis/API/hub yolunda p50 < 200ms ve p95 < 500ms (ölçülen: p50 ~18ms, p95 ~38ms) bütçesi doğrulandı.
  - Playwright canlı tarayıcı E2E testleri (`realtime-notifications.spec.ts`, 9 senaryo, 9/9 geçti):
    1. Müşteri siparişi oluşturur; yetkili personel canlı olayı hub üzerinden alır.
    2. Gerçek sipariş durum değişikliği müşteri ekranını günceller.
    3. Servis talebi doğru branch'e ulaşır; yanlış branch'e ulaşmaz.
    4. Quick-86 sonrası müşteri ekranında/kartında ürün 'Tükendi' görünür (katı DTO, 200 OK doğrulandı).
    5. HTTP catch-up sync uç noktası (`GET /api/v1/events/sync`) sıralı ve sayfalı olayları döner.
    6. Oturum kapanınca müşteri bağlantısı sonraki özel olayları alamaz.
    7. Başka tenant'a ait istemci olayları alamaz (cross-tenant izolasyonu).
    8. Masanın kapanması personel bağlantısını kesmez.
    9. Push izni reddedilince temel sipariş akışı kesintisiz çalışır.

## Sıradaki Görev — Faz 10: Garson ve Operasyon Uygulaması

- Faz 10: Garson ve Operasyon Uygulaması (Masa yönetimi, durum bazlı kat planı, canlı sipariş müdahalesi).

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini aşıyor; hiçbir elle yazılmış dosya 600 satır tavanını aşmaz (1140 dosya denetlendi, %100 uyumlu).
- Firebase geliştirme projesi `restaurant-order-development` hazırdır; push bildirimi simüle edilmiş/sözleşme testleriyle doğrulanmış olup gerçek fiziksel mobil cihaz teslimi staging donanımında ayrı doğrulanacaktır.

## Sonraki Somut Adım

PR #24'ün exact HEAD CI doğrulaması ve Faz 10'a geçiş hazırlığı.

## 2026-10-27 — Yerel Değişikliklerin Push Kaydı

- Kullanıcı isteği: mevcut yerel değişiklikleri `feat/phase-09-realtime-notifications` dalına commit edip push etmek; mevcut [PR #24](https://github.com/yusuf-ulgen/restaurant-order/pull/24), [Issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20). Taban commit: `efbac0f`.
- Kapsam: Faz 9 API/worker düzenlemeleri, iki API instance entegrasyon testleri, canlı bildirim E2E senaryoları, kapsam raporu yardımcısı ve ilgili belgeler.
- Bu oturumdaki doğrulama: `pnpm verify` çalıştırıldı. Dosya boyutu, belge bağlantıları ve gizli bilgi taraması geçti. Kontrol betiklerinde 114 test geçti; Docker daemon çalışmadığından `blue-green-docker.test.mjs` içindeki 1 test başarısız oldu ve komut 1 koduyla durdu.
- Zincirin sonraki lint, tür kontrolü, uygulama testleri ve derleme aşamaları bu oturumda çalışmadı. Yukarıdaki önceki doğrulama kayıtları bu revizyon için yeniden doğrulama sayılmaz.
- Sonraki adım: Docker açık ortamda tüm doğrulamayı ve push sonrası PR HEAD CI kontrollerini tamamlamak. Birleştirme veya yayın yapılmadı.
