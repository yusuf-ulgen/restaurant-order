# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-12. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `8930112` (PR #16 `feat/phase-06-tables-qr-sessions` birleşmesi).
- Faz 0-6 tamamlandı olarak kayıtlı; Faz 6 masalar, QR ve oturum yönetimi `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- Tamamlanan kapsam: [Faz 5 takibi](./PHASE-5-TRACKER.md), [Faz 6 takibi](./PHASE-6-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Aktif Görev — İlk FCM Sunucu Taşıması

- Üst görev [#11](https://github.com/yusuf-ulgen/restaurant-order/issues/11) açık: personel ve müşteri birlikte. İlk uygulama [#14](https://github.com/yusuf-ulgen/restaurant-order/issues/14).
- Dal: `feat/fcm-bildirim-altyapisi`. [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15) incelemede.
- Kullanıcının FCM kararı [ADR-0011](./adr/0011-background-push-provider.md) içinde `ACCEPTED`; mevcut SignalR + Redis kararı korunur.
- Sağlayıcıdan bağımsız taşıma sözleşmesi, FirebaseAdmin 3.7.0 FID uyarlayıcısı, ayrı ve varsayılan kapalı PUSH_PROVIDER, sınırlı genel payload ve TTL, iptal/süre/hata sınıflandırması eklendi.
- [FCM kurulumu ve sınırları](./FCM-SETUP.md), [bildirim planı](./NOTIFICATIONS-PLAN.md). Kimlik bildirimlerinin NOTIFICATION_PROVIDER/outbox/webhook sözleşmesi korunur.
- Taşıma henüz API veya otomatik işe bağlı değildir. Kalıcı kayıt/iptal, güncel alıcı yetkisi, outbox/deneme kayıtları, istemci service worker/izin deneyimi ve gerçek masa/sipariş olay bağlantıları #11 altında devam eder.
- Firebase geliştirme projesi `restaurant-order-development` oluşturuldu: [konsol](https://console.firebase.google.com/project/restaurant-order-development/overview). Ücretsiz Spark planı ve FCM HTTP v1 API'nin etkin olduğu konsolda doğrulandı.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Mevcut uyarılar: bir geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Faz 6: Masalar, QR ve Oturumlar — Kapanış Denetimi ve Güvenlik Sertleştirmesi

- Durum: **TAMAMLANDI (main'e birleştirildi, commit `8930112`)**.
- Müşteri JWT Bearer doğrulama boru hattı (`OnTokenValidated` ve `OnMessageReceived`) entegrasyon testleriyle doğrulandı.
- Müşteri oturum önbellek güvenliği: pozitif doğrulamalar her zaman PostgreSQL source-of-truth üzerinden yapılır, yalnızca negatif sonuçlar TTL ile önbelleklenir.
- QR v2 şeması ve yarış koşulları doğrulandı: HMAC-SHA256 sabit zamanlı doğrulama, dinamik token süre aşımı ve saat kayması toleransı.
- Masa transferi, oturum birleştirme ve idempotency: `DiningSessionMovement` aggregate'ine 64 karakter SHA-256 `RequestFingerprint` eklendi, deterministik row-lock altında replay ve çakışan payload'da 409 Conflict doğrulandı.
- 64 migrasyon dosyası EF Core ile uygulandı. E2E Playwright testleri disposable PostgreSQL/Redis üzerinde `3 passed, 0 skipped` olarak doğrulandı.

## Sonraki Somut Adım

FCM sunucu taşıma PR'ı (#15) main dalına birleştirildikten sonra, Faz 7 (Müşteri Deneyimi: Menü Gezinme, Sepet ve Sipariş Verme) planlama ve uygulamasına geçilmeye hazırdır.
