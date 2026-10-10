# Güvenlik ve Uyumluluk Politikası (`docs/SECURITY.md`)

## 1. Depoda Gizli Bilgi Bulunamaz

- Veritabanı parolası, JWT imza anahtarı, ödeme API anahtarı, webhook sırrı, şifreleme anahtarı ve üçüncü taraf kimlik bilgileri **asla Git'e eklenemez**.
- Kimlik bilgileri, Authorization başlıkları, kart verileri ve kişisel müşteri verileri stdout/günlük toplama sistemine yazılmadan temizlenir.
- Commit öncesi ve CI güvenlik taramaları olası gizli bilgileri denetler; şüpheli içerik reddedilir.

## 2. PCI-DSS ve Ödeme Sınırı

Uyumluluk kapsamını sınırlamak için:

1. Platform ham kart numarasını (PAN), son kullanma tarihini veya CVV/CVC'yi **asla almaz, işlemez veya saklamaz**.
2. Dijital ödemeler, sertifikalı PCI-DSS Level 1 sağlayıcının istemci tokenlaştırması, iframe veya barındırılan/yönlendirilen alanlarını kullanır (Stripe, Iyzico gibi).
3. Harici fiziksel POS kart yetkilendirmesini kendi yapar; sistem yalnızca işlem onay kodu ve tutarı kaydeder.

## 3. OWASP Top 10 Önlemleri

| Kategori | Önlem |
| :--- | :--- |
| A01 Erişim kontrolü | Merkezi RBAC; her sorguda `tenant_id` ve PostgreSQL RLS; IDOR önleme. |
| A02 Kriptografi | İstemci/sunucu ve hizmetler arası TLS 1.3; saklanan hassas veride AES-256. |
| A03 Enjeksiyon | Parametreli SQL ve tür güvenli sorgu oluşturucular; SQL için ham metin birleştirme yok. |
| A04 Güvensiz tasarım | QR siparişi ve personel PIN denemelerinde kaba kuvvete karşı hız sınırı. |
| A05 Yanlış yapılandırma | HSTS, CSP, X-Content-Type-Options, X-Frame-Options; sınırsız CORS yasak. |
| A06 Zayıf bileşen | Otomatik bağımlılık denetimi (`npm audit`, Dependabot). |
| A07 Kimlik doğrulama | Kısa ömürlü JWT, güvenli HttpOnly çerez ve yönetici parola karmaşıklığı. |
| A08 Veri/yazılım bütünlüğü | İmzalı yayınlar ve doğrulanmış ödeme webhook imzaları. |
| A09 Günlük eksikliği | Fiyat, iptal, iade ve rol değişikliğinde değiştirilemez denetim izi. |
| A10 SSRF | Dış webhook/yazıcı soketlerini doğrulanmış IP aralıklarıyla sınırla. |

## 4. Değiştirilemez Denetim İzi

Rol/yetki atama, fiyat müdahalesi, hesap indirimi/iadesi, hazırlıktaki ürün iptali ve işletme ayarı değişiklikleri denetim kaydı üretir. Restoran Admini dâhil hiçbir uygulama rolü bunları değiştiremez veya silemez.

## 5. Faz 3 Kimlik ve Yetkilendirme Uygulaması

### 5.1. Parola Güvenliği

- ASP.NET Core `IPasswordHasher<T>`; PBKDF2/HMAC-SHA512, yapılandırılabilir yineleme sayısı, varsayılan 100.000.
- Doğrulama ve hash yükseltmesinde `CryptographicOperations.FixedTimeEquals` ile zamanlama güvenliği.
- Başarısız giriş sayacı, artan bekleme ve beş denemeden sonra hesap kilidi.

### 5.2. Güvenilir Terminalde Hızlı PIN

- Dört haneli PIN, tek başına internet kimlik bilgisi değildir; yalnızca kayıtlı `TrustedTerminal` içinde kullanılabilir.
- `PIN_PEPPER_SECRET` ile HMAC-SHA256, ardından yavaş PBKDF2 uygulanır. Pepper olmadan veritabanı dökümü üzerinden dört haneli alanın denenmesi engellenir.
- Terminal bazlı sınır: üç hatadan sonra 1 saniye, dört hatadan sonra 2 saniye gecikme; beş ardışık hatada 15 dakika kilit.
- Terminal sırrı yalnızca `HttpOnly`, `SameSite=Lax` çerezinde taşınır: `restaurant_terminal_cred`, Base64 `terminalId:secret`. API yanıtına yazılmaz; gövde/başlıktan kabul edilmez.
- Terminal çerezi ortam kimlik bilgisidir; PIN girişi ve terminal kapatma dâhil tüm değişikliklerde CSRF gerekir.

### 5.3. Token ve Yenileme Oturumu

- Doğrulanmış işletme/rol claim'leri içeren, imzalı, 15 dakikalık erişim JWT'si.
- 256 bit güvenli rastgele opak yenileme tokenı; `iam.refresh_tokens` içinde yalnızca SHA-256 hash'i saklanır.
- Yenileme tokenı yalnızca `HttpOnly`, `Secure`, `SameSite=Strict` çerezle taşınır.
- Her yenilemede token döner; tüketilmiş token yeniden kullanılırsa bütün oturum ailesi iptal olur.
- `SingleFlightRefreshQueue`, eşzamanlı 401 yanıtlarını tek yenileme isteğinde birleştirir.

### 5.4. Varsayılan Ret ve İşletme Yalıtımı

- `PermissionAuthorizationHandler`, sekiz rol ve başlangıçtaki 31 yeteneği denetler. Kayıtsız yetki/eşlenmemiş kapsam `403` ve RFC 7807 ProblemDetails döndürür.
- Tüm `iam.*` tabloları `app.current_tenant_id` üzerinden RLS uygular.

### 5.5. Dağıtılmış Güvenlik

- SuperAdmin oturumları global `iam.platform_sessions` ve `iam.platform_refresh_tokens` tablolarındadır; işletme tablolarına `Guid.Empty` eklenmez.
- Yenileme, örnekler arası yarışları önlemek için `SELECT ... FOR UPDATE` kullanır.
- Giriş sınırları, atomik Lua ve PIN gecikme/kilitleri Redis ile örnekler arasında paylaşılır. Redis yoksa koruma atlanmaz, `503 Service Unavailable` döner.
- `OnTokenValidated`, kısa Redis önbelleğiyle `iam.validate_token_session` sorgular. `LogoutAll` önbelleği hemen geçersiz kılar; token süresi beklenmez.

### 5.6. Üretim ve Taşıma Güvenliği

- CSRF: Zamanlama güvenli HMAC double-submit çerezi ve merkezi `fetchWithCsrf`; `POST`/`PUT`/`PATCH`/`DELETE` denetlenir. Çıkışta istemci çerezleri temizlenir.
- Staging/Production'da `Cors:AllowedOrigins` veya `CORS_ALLOWED_ORIGINS` zorunludur; yalnızca HTTPS. `*`, HTTP veya localhost başlangıcı durdurur.
- Geniş özel ağlar (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`) varsayılan güvenilir vekil değildir. `FORWARDED_HEADERS_ENABLED=true` ile birlikte `FORWARDED_HEADERS_KNOWN_PROXIES` / `FORWARDED_HEADERS_KNOWN_NETWORKS` üzerinden açık IP/CIDR gerekir; üretim/hazırlıkta eksikse başlangıç durur.
- Staging/Production'da kimlik ve terminal çerezlerinde `Secure` zorunludur.
- `/api/v1/test/tenant-scope` ve `/api/v1/dev/seed`, Staging/Production'da eşlenmez.

### 5.7. Üyelik, Atomik İşlem ve Bildirim Outbox'ı

- `UserMembershipStatus` (`Active`, `Suspended`, `Disabled`) global kullanıcıya değil işletme üyeliğine aittir. A'daki askıya alma B üyeliğini etkilemez. İlgili işletme/şube oturumları hemen iptal olur; diğer işletme oturumları ve global kullanıcı durumu korunur.
- Davet/sıfırlama tokenı tüketimi, parola, güvenlik sürümü ve denetim olayı tek atomik işlemdedir. Kullanıcı etkinleştirme, hash veya günlük başarısızsa token tüketimi de geri alınır; yeniden denenebilir.
- Ham davet/sıfırlama tokenı API yanıtına veya düz metin outbox'a yazılmaz. `AesGcmIdentityOutboxPayloadProtector`, AES-256-GCM ile şifreler; `iam.identity_notifications_outbox` aynı açık işlemde doldurulur.
- Staging/Production için `NOTIFICATION_PROVIDER=TransactionalOutbox` gerekir; geliştirme `TestSink`'i yasaktır.
- `IdentityNotificationOutboxDispatcher`, `FOR UPDATE SKIP LOCKED` ile kayıtları alır, çözer ve `WebhookIdentityNotificationTransport` üzerinden HTTPS gönderir. `X-Webhook-Signature` HMAC-SHA256, `X-Idempotency-Key` tekrar güvenliği içindir.
- Kimlik/personel hataları `application/problem+json` kullanır: 400, 401, 403, 404, 409, 422, 429. Yapılandırılmış hata kodu vardır; kimlik bilgisi veya stack trace sızmaz.
