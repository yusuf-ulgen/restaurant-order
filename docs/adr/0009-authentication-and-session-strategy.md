# ADR-0009: Kimlik Doğrulama, Oturum ve Çok İşletmeli Yetkilendirme Stratejisi (`docs/adr/0009-authentication-and-session-strategy.md`)

- **Durum:** `ACCEPTED`
- **Karar Verenler:** Mimarlık Ekibi, Yusuf Ülgen
- **Tarih:** 2026-10-01
- **Teknik Hikaye:** Aşama 3 — Kimlik doğrulama ve RBAC Temeli

---

## 1. Bağlam ve Sorun Açıklaması

`restaurant-order`, beş kullanıcı arayüzüne sahip çok işletmeli bir restoran yönetim platformudur:
1. QR Müşteri Web Uygulaması
2. Garson & Operasyon Mobil Uygulaması
3. Mutfak ve Bar KDS İstasyonlar
4. Restoran Yönetim Paneli
5. Platform Süper Yönetici Kontrol Düzlemi

Kimlik ve erişim yönetimi (IAM) şu gereksinimleri birlikte karşılamalıdır:
- Kimlik bilgisi karmaşıklığı, şifre karma ve oturum denetimi gerektiren yüksek güvenlikli yönetim erişimi (Süper Yönetici, Restoran Yöneticisi, Şube Müdürü).
- Yoğun servis vardiyaları sırasında düşük sürtünmeli giriş gerektiren restoran katında hızlı, yüksek frekanslı personel değişimi (Garsonlar, Kasiyerler, Aşçılar).
- Yalnızca belirli bir masa oturumuna bağlı geçici, anonim misafir yemek oturumları (Müşteri QR'si).
- İşletme kimlik bilgilerinin ve oturumlarının hiçbir zaman kurumsal sınırları aşamadığı çok işletmeli katı izolasyon.
- Kaynak kontrolüne sırlar veya özel anahtarlar vermeden dağıtılmış token güvenliği.

---

## 2. Karar Etkenleri

- **Sıfır Gizli Uyumluluk:** JWT imzalama anahtarları, yenileme belirteçleri ve parolalar hiçbir zaman kaynak kontrolünde saklanmamalı veya günlüklerde yazdırılmamalıdır.
- **Arıza Durumunda Çok İşletmeli Sınırlar:** İşletmeler arası belirteç değişimi veya kapsam üzerinde değişiklik yapılması imkansız olmalıdır.
- **Derinlemesine Savunma:** Belirteçler ve oturumlar doğrulanabilir işletme/şube kapsamlarını taşımalı ve veritabanı PostgreSQL Satır Düzeyi Güvenliğini zorunlu kılmalıdır (RLS) son çare sınırı olarak.
- **Yüksek Güvenlikle Hızlı Kat İşlemleri:** 4-haneli PIN'ler garsonlar için uygundur ancak halka açık internette tehlikelidir; kriptografik olarak sınırlandırılmış olmaları gerekir.
- **Derhal İptal:** Rol değişiklikleri veya güvenlik sürümündeki değişiklikler, etkin oturumları derhal geçersiz kılmalıdır.
- **Çerçeve Bağımsızlığı ve Temiz Mimari:** Alan ve Uygulama katmanları, sözleşmeleri harici kimlik sağlayıcılarıyla bağlantı kurmadan tanımlamalıdır.

---

## 3. Karar Sonucu

### 3.1. Kimliklerin Ayrılması: Personel Hesapları ve Geçici QR Oturumları
- **Personel Hesapları (Kimlik ve Üyelik):** İşletme ve şube üyeliklerine bağlı küresel hesaplara sahip kayıtlı kullanıcılar. E-posta + Şifre (yönetim) veya Personel yoluyla doğrulandı PIN (güvenilir terminallerdeki kat personeli).
- **Müşteri QR Oturumları:** Tamamen ayrı anonim asıl türü (`PrincipalType.Customer`). Yalnızca tek bir masa oturumuna (`table_session_id`) bağlıdır. Müşteri oturumları personel uç noktalarına erişemez. Tam kriptografik QR oturum belirteci üretimi Aşamaya aittir 6; Aşamada 3sadece müşteri asıl sözleşmesi kurulur.

### 3.2. Kimlik Doğrulama Yöntemleri ve Kimlik Bilgileri
1. **E-posta + Şifre:**
   - Süper Yönetici, Restoran Yöneticisi ve Şube Yöneticileri için birincil giriş.
   - Parolalar ASP.NET Core `PasswordHasher<T>` ile karma olarak saklanır (PBKDF2, HMAC-SHA512, 100.000+ yineleme). Özel şifreleme kesinlikle yasaktır.
2. **Personel 4-Rakam PIN Güvenilir Terminal Bağlamında:**
   - Dört haneli PIN tek başına internete açık bir kimlik doğrulama yöntemi olarak asla kullanılamaz; kaba kuvvet saldırılarına kolayca maruz kalır.
   - PIN ile girişe yalnızca belirli bir şubeye bağlı, kayıtlı ve kriptografik olarak doğrulanmış **Güvenilir Terminal** üzerinden izin verilir.
   - PIN karmasında salt, yavaş karma ve ortam/gizli değer yöneticisinden alınan sunucu tarafı pepper kullanılır. Pepper yoksa Staging/Production başlatması reddedilir.

### 3.3. Token ve Oturum Mimarisi
1. **Kısa Ömürlü JWT Erişim Jetonları:**
   - 10-dakika ömrü (ortam tarafından yapılandırılabilir).
   - Kesinlikle doğrulanmış iddialar içerir: `sub`, `sid`, `jti`, `principal_type`, `role`, `tenant_id`, `branch_id`, `security_version`, `auth_method`, `iss`, `aud`, `iat`, `nbf`, `exp`.
   - İmzalama anahtarları (`HMAC-SHA256` veya `RSA-SHA256`) çevre/gizli yöneticiden enjekte edilmiş; kaynak kontrolünde yok.
2. **Opak Dönen Yenileme Jetonları:**
   - Kriptografik olarak güvenli, rastgele 32 baytlık opak dizelerdir.
   - Ham yenileme belirteçleri veritabanında asla saklanmaz; yalnızca SHA-256 karmaları kalıcı olarak tutulur.
   - `HttpOnly`, `Secure`, `SameSite=Strict` çerezlerde saklanır (asla `localStorage` veya `sessionStorage`).
   - Her yenilemede belirteç değiştirilir: eski jeton iptal edilmiş olarak işaretlenir ve yenisiyle değiştirilir.
   - **Yeniden Kullanım Tespiti:** Zaten tüketilen bir yenileme jetonu sunulursa, güvenlik ihlali şüphesiyle tüm oturum ailesi anında iptal edilir.
3. **Oturum ve Güvenlik Sürümünün Geçersiz Kılması:**
   - Her kullanıcı üyeliğinin bir `security_version` değeri vardır.
   - Kullanıcının rolü, parolası veya durumu değiştiğinde `security_version` artırılır; önceki sürüme ait belirteçler tüm düğümlerde reddedilir.

### 3.4. Çok İşletmeli Rol ve Kapsam Hiyerarşileri
Sekiz rolün kapsam sınırları şöyledir:
1. `SuperAdmin`: Yalnızca platform kapsamı. `tenant_id` veya `branch_id` taşıyamaz.
2. `RestaurantAdmin`: İşletme kapsamı. bağlı `tenant_id`; `branch_id` boş/isteğe bağlı.
3. `BranchManager`: Şube kapsamı. bağlı `(tenant_id, branch_id)`.
4. `Cashier`: Şube kapsamı. bağlı `(tenant_id, branch_id)`.
5. `Kitchen`: Şube kapsamı. bağlı `(tenant_id, branch_id)`.
6. `Bar`: Şube kapsamı. bağlı `(tenant_id, branch_id)`.
7. `Waiter`: Şube kapsamı. bağlı `(tenant_id, branch_id)`.
8. `Customer`: Masa oturumu kapsamı. bağlı `(tenant_id, branch_id, table_session_id)`.

Çapraz işletme kombinasyonları (örneğin İşletme B şubesine sahip İşletme A belirteci), oluşturma ve ayrıştırma sırasında başarısızlıkla kapatılarak reddedilir.

### 3.5. Merkezi Yetenek Kaydı (Varsayılan Olarak Reddetme)
- Yetkilendirme, makine tarafından okunan izin anahtarlarıyla yapılır (`menu.catalog.manage`, `orders.staff.create`). Dağınık `if (role == "Waiter")` kontrolleri kullanılmaz.
- `PermissionRegistry`, rolleri `docs/ROLES-AND-PERMISSIONS.md` matrisine göre izinlerle eşler.
- **Sahip olunan / Atanan Kapsam (`O`):** Kendi kapsamı olarak işaretlenen izinler (örneğin Garson kartı ödemeleri, Kasiyer günlük geliri, KDS istasyon hazırlık fişleri) tam erişim vermez. `IResourceOwnershipRequirement` ile açık kaynak sahipliği değerlendirmesi gerekir.

---

## 4. Sonuçlar ve Ödünleşimler

### Olumlu Sonuçlar
- **Token Hırsızlığı Vektörlerinin Ortadan Kaldırılması:** HttpOnly çerezler betiklerin belirteci doğrudan okumasını sınırlar. Yenileme belirtecinin yalnızca karmasının tutulması, veritabanı sızıntısında ham belirtecin kullanılmasını önler.
- **Kat Personelinin Kullanılabilirliği:** Garsonlar, restoranı internet çapında kaba kuvvete maruz bırakmadan güvenilir terminalleri hızlı PIN'lerle açabilirler.
- **Deterministik Çok İşletmeli Yapı:** Platform, işletme, şube ve müşteri kapsamları arasındaki net ayrım, yanlışlıkla işletmeler arası veri erişimini önler.
- **Derhal Rol İptali:** `security_version`, rol kaldırıldıktan sonra eski JWT ile erişimin sürmesini engeller.

### Olumsuz Sonuçlar / Ödünleşimler
- **Durum Bilgili Geçersiz Kılma Kontrolü:** Doğrulanıyor `security_version` yüksek riskli işlemlerde önbelleğe alınmış oturum aramaları gerektirir.
- **Terminal Yönetim Giderleri:** Şubelerin personelin kullanabilmesi için terminalleri kaydettirmesi gerekiyor PIN ile giriş yapabilmesi için.

---

## 5. Doğrulama ve Test Planı

- **Matris Kapsama Birimi Testleri:** 100%'si 31 tümünde izinler 8 roller doğrulandı.
- **Negatif Kapsam Testleri:** İşletme talebi olan SuperAdmin, şubesiz Garson ve İşletme A/B çapraz kapsamı doğrulama hataları oluşturmalıdır.
- **Talep Ayrıştırıcı Testleri:** Hatalı biçimlendirilmiş GUID'ler, eksik talepler, yinelenen talepler ve süresi dolmuş belirteçler başarısız bir şekilde kapatılmalıdır.
- **Kaynak Sahipliği Testleri:** Kendi kapsamındaki izinler, geçerli sahiplik bağlamı olmadan reddedilmelidir.

---

## 6. Dağıtılmış Kimlik Doğrulama Durumu ve Platform Kalıcılık Eki

### 6.1. Platform Oturumları ve Süper Yönetici Kalıcılığı
- SuperAdmin ve platform düzeyindeki kimlik bilgileri, oturumlar ve yenileme belirteçleri, özel küresel IAM tablolar (`iam.platform_sessions`, `iam.platform_refresh_tokens`) PostgreSQL'de.
- Hiçbir koşulda `Guid.Empty` işletme kapsamlı tablolara yazılmaz (`iam.sessions`, `iam.refresh_tokens`). Çok işletmeli sınır kesinlikle korunur.
- Platform oturumları API örnekleri, süreç yeniden başlatmaları ve Blue/Green yuvaları arasında korunur.
- SuperAdmin sağlama, işletme personeli davet uç noktaları aracılığıyla başlatılamaz (kesinlikle reddedilir). SuperAdmin hesapları yalnızca güvenli bant dışı önyükleme komut dosyaları aracılığıyla sağlanır.

### 6.2. Atomik Dağıtılmış Token Rotasyonu (FOR UPDATE)
- Dağıtılmış yenileme belirteci rotasyonu, satır düzeyinde kilitler (`SELECT ... FOR UPDATE`).
- Birden fazla istek farklı sunucularda aynı yenileme belirteciyle yarışıyorsa API Örneklerde tam olarak bir istek satır kilidini alır ve atomik dönüşle başarılı olur; tüm eşzamanlı veya sonraki girişimler, belirtecin yeniden kullanımını tespit eder ve belirteç ailesinin anında iptalini tetikler.

### 6.3. Geçici Durum ve Redis Arıza-Kapalı Stratejisi
- Geçici, çapraz örnek durumu Redis aracılığıyla yönetilir:
  - **Giriş Hızı Sınırlayıcı:** atomik `INCR` + `EXPIRE` Lua komut dosyaları aracılığıyla uygulanır.
  - **terminali PIN Kaba Kuvvet:** Bulut sunucuları arasında paylaşılan dağıtılmış aşamalı gecikmeler (1, 2, 4 ve 8 saniye) ve kilitleme eşikleri.
  - **Terminal Kaydı:** Atomik olarak tüketilen tek kullanımlık kriptografik kayıt kodları `GETDEL`.
- **Arıza Kapatma Politikası:** Redis kullanılamaz hale gelirse hız sınırlama ve güvenlik kapıları açık erişime dönüşmez. İşlem reddedilir, `DistributedSecurityStateUnavailableException` fırlatılır ve HTTP 503 (Hizmet Kullanılamıyor) döndürülür.

### 6.4. Dağıtılmış İptal ve Çok Düzeyli Geçersiz Kılma
- Erişim belirtecinin geçerliliği şu sırada kontrol edilir: `OnTokenValidated` dağıtılmış yetkili durum kaynağına göre (`iam.validate_token_session`).
- Veritabanı sorgu hacmini en aza indirmek için pozitif doğrulama sonuçları Redis'te önbelleğe alınır. 60 saniyelik TTL.
- `LogoutAll` veya güvenlik sürümü artışında Redis etkin oturum anahtarları ve sürüm girdisi tüm API örnekleri için geçersiz kılınır; belirtecin süresinin dolması beklenmez.
