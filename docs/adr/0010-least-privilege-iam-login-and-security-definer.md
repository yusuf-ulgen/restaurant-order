# ADR-0010: En Az Ayrıcalık IAM Giriş Arama ve SECURITY DEFINER Tehdit Modeli

- **Durum:** `ACCEPTED`
- **Tarih:** 2026-10-01
- **Karar Verenler:** Mimari Ekibi, Güvenlik Ekibi, Arka Uç Liderleri
- **Danışıldı:** [SECURITY.md](../SECURITY.md), [MULTI-TENANCY.md](../MULTI-TENANCY.md), [0009-authentication-and-session-strategy.md](./0009-authentication-and-session-strategy.md)
- **Yerini alır:** N/A

---

## 1. Bağlam ve Sorun Bildirimi

Çok işletmeli bir platformda kullanıcı hesapları (`iam.users`) üyelikler aracılığıyla birden fazla işletmeye ait olabilecek küresel kimlikleri temsil eder (`iam.memberships`).
Kimlik bilgileri ile oturum açma sırasında uygulamanın, herhangi bir işletme bağlamı oluşturulmadan önce normalleştirilmiş e-posta yoluyla bir kullanıcıyı bulması gerekir.

Ayrıcalıksız çalışma zamanı veritabanı rolüne (`restaurant_app_runtime`) `iam.users` üzerinde genel `SELECT` yetkisi verilirse:
1. Uygulamadaki herhangi bir SQL enjeksiyonu açığı, tüm işletmelerdeki kullanıcıların kimlik bilgilerinin ve kişisel verilerinin dışarı sızmasına yol açabilir.
2. Güvenliği ihlal edilmiş bağlantı havuzları, işletmeler arası hesap numaralandırma sorgularını yürütebilir.
3. En az ayrıcalık ve sıfır güven veri ayrımı ilkesini ihlal ediyor.

Genel kullanıcı kataloğunu keyfi SELECT sorgularına açmadan giriş doğrulamasını sağlayan, en az ayrıcalıklı mekanizma gerekir.

---

## 2. Tehdit Modeli ve Azaltmalar

### Tehdit 1: Küresel Kimlik Tablosunun Süzülmesi (SQL Enjeksiyon veya Güvenliği Tehlikeye Atılmış Çalışma Zamanı Bağlantısı)
- **Risk:** Saldırgan şu sorguyu çalıştırır: `SELECT * FROM iam.users` ve şifre karmalarını, tuz meta verilerini ve e-postaları çalar.
- **Azaltma:**
  - `REVOKE ALL ON iam.users FROM restaurant_app_runtime;`
  - Çalışma zamanı rolüne yalnızca gerekli işlemler için sütun düzeyinde erişim verilir (örneğin `UPDATE failed_login_attempts, lockout_end_utc, concurrency_token`).
  - `restaurant_app_runtime` rolüne `iam.users` üzerinde tablo genelinde `SELECT` yetkisi verilmez.

### Tehdit 2: Arama Yolu Ele Geçirme SECURITY DEFINER
- **Risk:** PostgreSQL'de bir `SECURITY DEFINER` işlev, sahibinin ayrıcalıklarıyla yürütülür. Eğer `search_path` sabitlenmemişse, saldırgan genel veya geçici şemalarda işlev yürütmeyi ele geçiren kötü amaçlı nesneler oluşturabilir.
- **Azaltma:**
  - Oturum açma arama işlevi `iam.lookup_user_for_login(p_normalized_email text)` açıkça sabitlenmiştir `SET search_path = iam, pg_temp`.
  - Dinamik SQL (`EXECUTE ...`) fonksiyon içinde kesinlikle yasaktır.
  - İşlevin sahibi şema geçişi/DBA rolüdür; `restaurant_app_runtime` sahibi olamaz.

### Tehdit 3: Aşırı Veriye Maruz Kalma ve PII Sızıntı
- **Risk:** Arama işlevi gereksiz sütunları (isimler, telefon numaraları, denetim meta verileri) döndürür.
- **Azaltma:**
  - İşlev kesinlikle sabit, minimum bir projeksiyon döndürür: `(user_id, normalized_email, password_hash, status, security_version, lockout_end_utc)`.
  - Oturum açma için gerekli projeksiyon dışındaki kişisel veriler döndürülmez; e-posta ve parola karması yalnızca sunucudaki doğrulama akışında kullanılır.

### Tehdit 4: Kullanıcı Numaralandırma ve İşletmeler Arası Hesap Keşfi
- **Risk:** Kötü niyetli aktör, platform hesaplarını keşfetmek için e-postaları sıralıyor.
- **Azaltma:**
  - Uygulama kimlik doğrulama işleyicisi, kullanıcı mevcut olsa da, kilitli olsa da veya geçersiz olsa da sabit zamanlı genel hata yanıtlarını ("Geçersiz e-posta veya parola") döndürür.
  - Parola doğrulaması PBKDF2/Argon2 gibi yavaş karma yöntemiyle yapılır. Kullanıcı yoksa zamanlama farkını azaltmak için sahte karma doğrulaması uygulanır.
  - Bir kullanıcı global olarak mevcut olsa bile işletme verilerine erişim PostgreSQL kapsamında doğrulanmış aktif bir üyelik gerektirir RLS `USING (tenant_id = tenancy.get_current_tenant_id())`.

---

## 3. Karar

1. **Özel Veritabanı İşlevi:**
   `iam.lookup_user_for_login(p_normalized_email text)`, `SECURITY DEFINER` olarak uygulanır; `search_path = iam, pg_temp` sabitlenir ve statik projeksiyon kullanılır.
2. **Erişim Kontrolü:**
   - `GRANT EXECUTE ON FUNCTION iam.lookup_user_for_login(text) TO restaurant_app_runtime;`
   - `REVOKE ALL ON iam.users FROM restaurant_app_runtime;`
   - `GRANT INSERT ON iam.users TO restaurant_app_runtime;` (kullanıcı kaydı/davetiye oluşturmak için)
   - `GRANT UPDATE (password_hash, status, security_version, failed_login_attempts, lockout_end_utc, updated_at_utc, concurrency_token) ON iam.users TO restaurant_app_runtime;`
3. **Uygulama Ağ Geçidi:**
   Uygulama/altyapı katmanında `IIamUserLookupGateway` soyutlaması oluşturulur. `iam.users` üzerinde doğrudan EF Core LINQ sorgusu yerine özel arama işlevi çağrılır.

---

## 4. Sonuçlar

### Olumlu
- Doğrudan `SELECT * FROM iam.users` uygulama çalışma zamanı bağlantıları için veritabanı motoru düzeyinde fiziksel olarak engellenir.
- Genel kimlik doğrulama araması ile işletmeye özel işlemler arasında temiz ayrım.
- Sabitlendi `search_path` ayrıcalık yükseltme saldırı vektörlerini ortadan kaldırır.

### Negatif
- Şema geçişlerinde veritabanı işlevinin sürdürülmesini gerektirir.
- `restaurant_app_runtime`, `iam.users` üzerinde doğrudan EF Core LINQ e-posta araması yapamaz; tüm aramalar `IIamUserLookupGateway` üzerinden geçer.
