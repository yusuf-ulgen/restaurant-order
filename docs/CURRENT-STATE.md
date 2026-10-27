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

## Aktif Görev — Faz 9 İnceleme Düzeltmeleri

- Son incelenen/push edilmiş taban HEAD `254c817bf0e9deb9eda343d6e03c2db6e44fe767`; uzak dal düzeltmelerden önce aynı committeydi. CI kapsamı ve worker gecikmesi düzeltmeleri aynı dalda PR #24 incelemesine sunuluyor. Merge veya deploy yapılmadı; yeni commitin CI sonucu ayrıca değerlendirilir.
- Dal ve başlangıç HEAD: `feat/phase-09-realtime-notifications`, `f0caac8d613fbf40df9d71b939b153d3978456c5`; başlangıç çalışma ağacı temiz ve uzak dalla eşitti.
- [Issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20), [PR #24](https://github.com/yusuf-ulgen/restaurant-order/pull/24). İnceleme düzeltmeleri aynı dalda PR #24'e sunuluyor; birleştirme veya deploy yapılmadı. Push ve CI durumu GitHub üzerinden ayrıca doğrulanır.
- Önceki tamamlandı ve tüm testler doğrulandı kayıtları incelemede geri açıldı. Faz 10'a geçilmez.
- Kapsam: worker tenant/RLS, worker/API rol ayrımı, dayanıklı staging, lease/claim fencing, gerçek iptal yolları, HTTP cursor, FCM tarayıcı kaydı ve doğrulama kanıtları.
- Güncel teknik sonuçlar, iki CI engelinin tanısı ve komut kanıtları: [Faz 9 inceleme kaydı](./PHASE-9-REVIEW.md). Önceki CI başarıları yeni yerel çalışma ağacını doğrulamaz.
## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Dosya boyutu denetiminin güncel sonucu ve 450 satır uyarıları inceleme kaydında tutulur; eski dosya sayıları yeni çalışma ağacı için kanıt sayılmaz.
- Gerçek Firebase projesi ve kimlik bilgileri bu incelemede kullanılmadı. SDK/worker sözleşme testleri fiziksel FCM teslimini kanıtlamaz; HTTPS cihaz doğrulaması R13 kapsamında bekliyor.
- Bu oturumdaki ilk Compose E2E denemesinde mevcut yerel PostgreSQL 5432 çakışması nedeniyle `Host=localhost` migration'ı yerel `restaurant_order_dev` veritabanına `20261027130000_HardenWorkerQueuePrivileges` uyguladı. Geri alma/veri silme yapılmadı. Sonraki 21/21 browser koşusu rastgele portlu ayrı geçici veritabanında tamamlandı; ayrıntı inceleme kaydında.

## Sonraki Somut Adım

CI kapsamı ve gecikme düzeltmelerini PR #24 üzerinde incelemek; CI'ın tüm adımlarını yeni committe izlemek; gerçek FCM yapılandırması ve izinli HTTPS cihazla fiziksel teslimi ayrıca kanıtlamak. Yerel otomatik sonuçlar inceleme kaydındadır; Faz 10'a geçilmedi.

## 2026-10-27 — İnceleme Öncesi Push Kaydı

- Kullanıcı isteği: mevcut yerel değişiklikleri `feat/phase-09-realtime-notifications` dalına commit edip push etmek; mevcut [PR #24](https://github.com/yusuf-ulgen/restaurant-order/pull/24), [Issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20). Taban commit: `efbac0f`.
- Kapsam: Faz 9 API/worker düzenlemeleri, iki API instance entegrasyon testleri, canlı bildirim E2E senaryoları, kapsam raporu yardımcısı ve ilgili belgeler.
- Bu oturumdaki doğrulama: `pnpm verify` çalıştırıldı. Dosya boyutu, belge bağlantıları ve gizli bilgi taraması geçti. Kontrol betiklerinde 114 test geçti; Docker daemon çalışmadığından `blue-green-docker.test.mjs` içindeki 1 test başarısız oldu ve komut 1 koduyla durdu.
- Zincirin sonraki lint, tür kontrolü, uygulama testleri ve derleme aşamaları bu oturumda çalışmadı. Yukarıdaki önceki doğrulama kayıtları bu revizyon için yeniden doğrulama sayılmaz.
- Sonraki adım: Docker açık ortamda tüm doğrulamayı ve push sonrası PR HEAD CI kontrollerini tamamlamak. Birleştirme veya yayın yapılmadı.
