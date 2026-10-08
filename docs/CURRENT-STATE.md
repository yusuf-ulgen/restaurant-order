# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-08. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı

- Main tabanı: `467737b`, birleşmiş Faz 5 menü/katalog çalışması.
- Faz 0-5 tamamlandı olarak kayıtlı; [yol haritasında](./ROADMAP.md) sıradaki iş Faz 6 masa/oturum yönetimi.
- Tamamlanan kapsam: [Faz 5 takibi](./PHASE-5-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Önceki Düzeltme

- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), dal: `fix/pricing-and-contributor-handoff`.
- [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltildi; RBAC örnekleri ve katkı/devir akışı düzenlendi. GitHub kontrolleri başarılı; PR henüz birleştirilmedi veya yayınlanmadı.
- Eski kodda beş yeni regresyon testi başarısızdı; düzeltmeyle 36 hedefli test başarılı. Bir değişmez-kural testi 50.000 toplam/kişi birleşimini kontrol ediyor.
- 2026-10-08 tarihli önceki `pnpm verify`: 1.228 backend birim, 10 mimari, 295 frontend, 249 entegrasyon, 115 kontrol betiği ve 2 HTTP sağlık E2E testi; toplam 1.899. Lint, tür, dosya/bağlantı/gizli bilgi kontrolleri ve üretim derlemeleri başarılı; backend Release derlemesi sıfır uyarı/hata.

## Dokümantasyon Görevi — İncelemede

- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), dal: `docs/turkce-dokumantasyon`.
- Taban: `ba2ec17`, PR #9 dalı. Bağımlı PR bu dala açılır; #9 birleştikten sonra taban main'e alınmalıdır.
- Kapsam: takip edilen Markdown belgeleri, AI yönergeleri ve issue/PR şablonlarını Türkçeye çevirme; kalıcı Türkçe yazım kuralı. Teknik adlar, komutlar, karar durumları ve kanıtlar korunur.
- Durum: takip edilen 57 Markdown belgesi Türkçeleştirildi; kalıcı dil kuralı ve araç yönergeleri güncellendi. [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12) incelemeye sunuldu; birleştirilmedi. Kaynak kodu ve çalışma zamanı bağımlılıkları değişmedi.
- Bu görevde 2026-10-08 tarihinde `pnpm verify` çıkış kodu 0: 1.228 backend birim + 10 mimari + 295 frontend + 249 entegrasyon + 115 kontrol + 2 HTTP sağlık E2E = 1.899 başarılı test. Lint, tür denetimi ve üretim derlemeleri başarılı; backend 0 uyarı/0 hata. Son belge düzenlemelerinden sonra bağlantı/dosya/gizli değer kontrolleri ve `git diff --check` ayrıca çalıştırılır.
- İzin matrisinin anahtar ve izin hücreleri önceki sürümle birebir karşılaştırıldı. Eski faz sayıları tarihsel kayıt olarak korundu. SignalR kararının ve OpenAPI üretim açığının durumu mevcut kayıtlara göre netleştirildi.

## Bildirim Çalışması

- [Issue #11](https://github.com/yusuf-ulgen/restaurant-order/issues/11): push kapsamı ve sağlayıcı kararı.
- Aktif dal: `feat/push-bildirimleri`; taban `ec943fa`, PR #12 dalı. Birleştirme sırası #9 → #12 → bildirim çalışması.
- [Taslak PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13) açıldı; sağlayıcı kararı bekliyor ve issue #11 açık. [Bildirim planı](./NOTIFICATIONS-PLAN.md) ve [ADR-0011](./adr/0011-background-push-provider.md) öneri olarak eklendi. Kod/SDK/veritabanı değişikliği veya gerçek push gönderimi yok.
- Bu dalda yalnızca plan/ADR ve belge bağlantıları değişti. `pnpm verify:gates` 2026-10-08 tarihinde çıkış 0 ile başarılı: 115/115 kontrol testi, bağlantı/dosya/gizli değer kontrolleri. Aynı kaynak kodu tabanı için yukarıdaki tam 1.899 test doğrulaması geçerlidir; yeni push işlevi test edildiği iddia edilmez.
- Kullanıcı personel ve müşteriyi birlikte seçti. Sağlayıcı yanıtı bekleniyor; SignalR + FCM önerisi henüz kabul edilmiş ADR değildir.
- SignalR + Redis, ADR-0001 kapsamında mevcut canlı iletişim kararıdır. Arka plan push ayrı kanaldır. Gerçek masa/sipariş ve müşteri oturumu uygulamaları henüz tamamlanmadığı için bağımlılıklar gizlenmemelidir.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon için Docker gerekir.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Önceki hedefli derleme açık API dosyası nedeniyle engellendi; göreve ait API/worker durdurulunca çözüldü.
- Mevcut uyarılar: bir geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.
- E2E süiti yalnızca iki HTTP sağlık sorgusudur; tam tarayıcı iş akışı kapsamı R08 olarak açıktır.

## Sonraki Somut Adım

Çeviri PR incelemesini ve CI sonucunu izleyin. Bildirim sağlayıcısı için kullanıcı yanıtını alın; ADR-0011 onaylanmadan sağlayıcı entegrasyonunu kesinleştirmeyin. Sonrasında planın kayıt/iptal, taşıma ve gerçek olay entegrasyonu adımlarıyla ilerleyin. Faz 6 öncesi açık iş kurallarını çözün. [Katkı akışını](./CONTRIBUTING-WORKFLOW.md) izleyin; onaysız birleştirme/yayın yapmayın.
