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

## Faz 6: Masalar, QR ve Oturumlar — Kapanış Denetimi ve Güvenlik Sertleştirmesi

- Dal: `feat/phase-06-tables-qr-sessions` (taban: `467737b`, commit: `99b5ffa` ve güncel düzeltmeler).
- Durum: **TAMAMLANDI (CI Doğrulandı)**.
- Müşteri JWT Bearer doğrulama boru hattı (`OnTokenValidated` ve `OnMessageReceived`) gerçek authentication middleware ve korunan endpoint üzerinden entegrasyon testleriyle doğrulandı:
  - GUID formatında olmayan veya eksik `sub` claim'leri katı biçimde 401 ile reddedilir (yalnızca "parse edilebildiyse karşılaştır" mantığı engellendi).
  - `sub` ile `table_session_id` uyuşmazlığı kesin olarak 401 ile reddedilir.
  - Gelecek tarihli `iat` (>5 saniye saat kayması) ve 4096 bayttan uzun token'lar reddedilir.
  - Farklı tenant/branch veya kapalı/birleştirilmiş oturum token'ları reddedilir.
- QR v2 şeması ve yarış koşulları doğrulandı:
  - v2 kanonik veri yükü: `v=2&kid={kid}&mode={mode}&tid={tid}&bid={bid}&tcode={tcode}&qv={qv}&iat={iat}`, HMAC-SHA256 sabit zamanlı doğrulama.
  - Paralel statik QR takasında tekil oturum oluşturulması / tekilleştirme.
  - QR döndürme (rotation) sonrası eski QR sürümünün geçersiz kılınması (409 Conflict).
  - Pasif masa takasının engellenmesi (400 Bad Request).
  - Çapraz tenant/branch veri sızıntısının engellenmesi.
  - Generic RFC 7807 hata yanıtlarının iç DB/istisna ayrıntılarını sızdırmaması.
- Masa transferi ve oturum birleştirme:
  - Zorunlu `IdempotencyKey` doğrulaması, normalize istek fingerprint'i ve replay mantığı.
  - Deterministik artan GUID satır kilitleme (`SELECT ... FOR UPDATE`), `If-Match` / concurrency token kontrolleri.
  - `DiningSessionMovement` değişmez denetim kaydı ve kaynak oturum önbellek geçersiz kılma.
- Veritabanı ve migrasyon sözleşmesi:
  - Eklemeli migrasyonlar: `20261005185044`, `20261005203226`, `20261005205206`, `20261011120000`, `20261011140000`, `20261011160000`, `20261011180000`, `20261011210000`.
  - `public_code` için tam 32 karakter küçük harf onaltılık (hex) sözleşmesi (`ck_restaurant_tables_public_code_hex32`).
  - `ck_dining_sessions_status_timestamps`, `ck_dining_sessions_chronology`, `ck_restaurant_tables_shape`, `ck_restaurant_tables_canvas_bounds` kısıtları.
  - 62 migrasyon dosyası `scripts/migration-ops.mjs validate` ile doğrulandı (tahribatsız).
- Test ve kapı sonuçları (GitHub Actions CI Run [38166211733](https://github.com/yusuf-ulgen/restaurant-order/actions/runs/38166211733)):
  - Frontend birim testleri: 359/359 test başarılı (ui: 159, admin: 151, customer: 34, operations: 15; 0 konsol uyarısı).
  - Backend birim testleri: 1.422/1.422 test başarılı (0 hata, 0 atlanan, Release modunda 0 uyarı, 0 hata).
  - Backend entegrasyon testleri: 308/308 test başarılı (Testcontainers PostgreSQL & Redis).
  - Mimari testleri: 10/10 test başarılı.
  - Coverage: Satır %88.42, Dal %81.35, Metot %91.89 (tüm eşikler >= %80).
  - E2E: 2 passed, 1 skipped (canlı veritabanı olmaması durumu güvenli biçimde atlandı).
  - Kapı kontrolleri: 114 test başarılı, 896 dosya satır sınırında (< 600 satır), 61 Markdown belgesi kırık link içermeden geçerli, 0 secret bulgusu.

## Sonraki Somut Adım

Faz 6 tüm güvenlik, mimari, test ve CI gereksinimleriyle tamamlanmış ve CI üzerinde %100 yeşil olarak doğrulanmıştır. Değişiklikler commit edilip pushlandıktan sonra Faz 7 (Müşteri Deneyimi: Menü Gezinme, Sepet ve Sipariş Verme) planlama ve uygulamasına geçilmeye hazırdır. Ana dala doğrudan birleştirme veya force-push yapılmaz; PR incelemesi ve kabulü beklenir.
