# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-08. Devam ederken Git/GitHub durumunu yenileyin; tarihsel faz takipleri mevcut kanıtlarını korur.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main: `c2c61a3`, bildirim planı PR #13 birleşmesi. Faz 0-5 tamamlandı olarak kayıtlı; gerçek masa/QR oturumu ve sipariş alanları henüz tamamlanmadı. [Yol haritası](./ROADMAP.md), [Faz 5 takibi](./PHASE-5-TRACKER.md), [inceleme listesi](./REVIEW-BACKLOG.md).
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi. Beş regresyon testi ve 50.000 toplam/kişi birleşimi doğrulandı.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen 57 Markdown belgesi Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı. Main'e birleştirildi; çalışma zamanı kodu değişmedi.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi. GitHub CI `37763855432` başarılı. Bu PR yalnızca plan içerir.
- Kullanıcı bu üç PR'ın birleştirilmesini ve ardından FCM ile devam edilmesini onayladı. Birleşme yayın anlamına gelmez; üretime yayın yapılmadı.

## Aktif Görev — İlk FCM Sunucu Taşıması

- Üst görev [#11](https://github.com/yusuf-ulgen/restaurant-order/issues/11) açık: personel ve müşteri birlikte. İlk uygulama [#14](https://github.com/yusuf-ulgen/restaurant-order/issues/14).
- Dal: `feat/fcm-bildirim-altyapisi`, taban main `c2c61a3`. Yerel uygulama ve doğrulama tamamlandı, dal push edildi; [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15) incelemeye açıldı. CI sonucunu güncel PR başlığı üzerinden doğrulayın. Birleştirilmedi/yayınlanmadı.
- Kullanıcının FCM kararı [ADR-0011](./adr/0011-background-push-provider.md) içinde `ACCEPTED`; mevcut SignalR + Redis kararı korunur.
- Sağlayıcıdan bağımsız taşıma sözleşmesi, FirebaseAdmin 3.7.0 FID uyarlayıcısı, ayrı ve varsayılan kapalı PUSH_PROVIDER, sınırlı genel payload ve TTL, iptal/süre/hata sınıflandırması eklendi.
- [FCM kurulumu ve sınırları](./FCM-SETUP.md), [bildirim planı](./NOTIFICATIONS-PLAN.md). Kimlik bildirimlerinin NOTIFICATION_PROVIDER/outbox/webhook sözleşmesi korunur.
- Taşıma henüz API veya otomatik işe bağlı değildir. Kalıcı kayıt/iptal, güncel alıcı yetkisi, outbox/deneme kayıtları, istemci service worker/izin deneyimi ve gerçek masa/sipariş olay bağlantıları #11 altında devam eder.
- Firebase projesi, servis hesabı veya gerçek cihaza gönderim oluşturulmadı. Gerçek HTTPS Android/iPhone testi yapılmadı.

## Doğrulama

- Önceki Türkçe belge görevinde 2026-10-08 `pnpm verify`, çıkış 0: 1.228 backend birim + 10 mimari + 295 frontend + 249 entegrasyon + 115 kontrol + 2 HTTP sağlık E2E = 1.899 başarılı test. Backend Release 0 uyarı/0 hata.
- Bu FCM görevinde 2026-10-08 tarihli son pnpm verify çıkış 0: 1.283 backend birim + 10 mimari + 295 frontend + 249 entegrasyon + 115 kontrol + 2 HTTP sağlık E2E = 1.954 başarılı test. Lint, tür ve üretim derlemeleri başarılı; backend Release 0 uyarı/0 hata. Son hedefli Notifications filtresi 56/56 başarılı; bu dal 55 yeni test ekler.
- SDK sözleşme testleri gerçek SDK ve sahte HTTP ile çalışır; gerçek cihaz teslim kanıtı değildir. Mevcut E2E yalnızca iki HTTP sağlık sorgusudur; R08 açıktır.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0, Node, PostgreSQL 16 ve Redis 7; entegrasyon için Docker gerekir.
- Makineye özel .local yardımcıları ve depo dışı sır deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler. Yerel sırlar ve test günlükleri commit'e eklenmez.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekler: yalnızca test sürecinde özel JWT_SECRET kaldırılır, yerel altyapı ayarları korunur. DOTNET_PROCESSOR_COUNT=2 ile Docker yoklama eşzamanlılığı sınırlanır.
- Release derlemesinden önce göreve ait API/worker durdurulabilir; aksi durumda açık apphost dosyası kilitlenebilir.
- Önceden mevcut uyarılar: bir geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

#14 uygulama PR'ını incele ve CI sonucunu GitHub üzerinden doğrula. Ardından #11 kapsamında kayıt sahipliği/iptal ve kalıcı teslim tasarımını uygula. Alıcı yetkisi ve oturum yaşam döngüsü tamamlanmadan taşıma için genel API açma. [Katkı akışını](./CONTRIBUTING-WORKFLOW.md) izle; yeni uygulama PR'ını açık kullanıcı yetkisi olmadan birleştirme veya yayınlama.
