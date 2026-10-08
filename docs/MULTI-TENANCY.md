# Çok İşletmeli Mimari ve Yalıtım (`docs/MULTI-TENANCY.md`)

## 1. Genel Bakış ve Hiyerarşi

Tek platform, bağımsız restoran işletmelerine veri yalıtımı, gizlilik ve ayrı operasyon ayarlarıyla hizmet verir.

```text
Tenant / Organization: Faturalandırılan kuruluş, abonelik planı, kullanıcılar
  ├─ Brand A: Katalog ve marka görünümü
  │    ├─ Branch A1: Masalar, personel, yazıcılar, KDS
  │    └─ Branch A2: Masalar, personel, yazıcılar, KDS
  └─ Brand B: Katalog ve marka görünümü
       └─ Branch B1
```

## 2. Yalıtım Seçenekleri

| Model | Açıklama | Artı | Eksi | Durum |
| :--- | :--- | :--- | :--- | :--- |
| Paylaşılan veritabanı + RLS | Tablolarda `tenant_id`; filtreyi PostgreSQL uygular. | Düşük maliyet, basit geçiş, platform raporlaması. | Veri sızıntısına karşı ayrıntılı politika testleri gerekir. | `[Implemented / Active]`, ADR-0002 |
| İşletme başına şema | Ortak PostgreSQL içinde ayrı şema. | Mantıksal sınır, işletme yedeği kolaylığı. | Yüzlerce şemada geçiş ve bağlantı havuzu karmaşıklığı. | `[Alternative / Rejected for MVP]` |
| İşletme başına veritabanı | Fiziksel olarak ayrı veritabanı. | En güçlü ayrım ve bağımsız geri yükleme. | Yüksek maliyet, global analiz ve geçiş zorluğu. | `[Alternative / Rejected for MVP]` |

## 3. İşletme Bağlamının Aktarımı

1. `TenantContextMiddleware`, `X-Correlation-Id` alır veya RFC 4122 GUID üretir. `ITenantContextResolver` ile `ITenantContext` çözülür; korunan uçlarda `[RequireTenant]` denetlenir. Hata RFC 7807 ProblemDetails olur; istek sonunda AsyncLocal bağlamı kesin temizlenir.
2. Kimlik katmanı JWT/QR tokenından doğrulanmış `tenant_id`, `brand_id`, `branch_id` çözer. Başlangıç şemasında bu katman Faz 3 planıydı; güncel uygulama için ADR-0009 esastır.
3. `TenantContext`, scoped DI ve AsyncLocal içinde tutulur; `RequireTenantId()` eksik/geçersiz bağlamı reddeder.
4. Veritabanında `SELECT set_config('app.current_tenant_id', @tenantId, true);` uygulanır. RLS, `USING (tenant_id = tenancy.get_current_tenant_id())` kullanır. İşlem sonundaki commit/rollback bağlamı otomatik sıfırlar.

## 4. Veri Sızıntısını Önleyen Kontroller

1. **Bileşik yabancı anahtarlar:** Alt varlıklar indeksli `tenant_id` ve `brand_id`/`branch_id` taşır.
   - `branches(tenant_id, brand_id)` → `brands(tenant_id, id)`, `DeleteBehavior.Restrict` ile işletmeler arası atamayı engeller.
   - `brand_appearances(tenant_id, brand_id)` → `brands(tenant_id, id)`.
   - `branch_theme_overrides`, `branch_settings`, `branch_operating_hours`, `dining_areas`, `preparation_stations`, `branch_feature_flags`: `(tenant_id, branch_id)` → `branches(tenant_id, id)`; ilişkiye göre Restrict/Cascade.
   - Alan ve istasyon kodu şubede benzersizdir: `(tenant_id, branch_id, code)`.
2. **Zorunlu RLS:** `tenancy.tenants`, `brands`, `branches`, `brand_appearances`, `branch_theme_overrides`, `branch_settings`, `branch_operating_hours`, `dining_areas`, `preparation_stations`, `branch_feature_flags` tablolarında RLS etkin ve `FORCE ROW LEVEL SECURITY` uygulanmıştır. `tenancy.get_current_tenant_id()` eksik/boş/geçersiz ayarda `NULL` döndürür: okumalar sıfır satırdır; ekleme/değiştirme reddedilir.
3. **Ayrı veritabanı rolleri:** Sahip/geçiş rolü DDL ve politikaları yönetir. `restaurant_app_user`, `tenancy.*` için SELECT/INSERT/UPDATE/DELETE kullanır; `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` taşır, `tenancy` üzerinde CREATE yoktur. Şemayı veya RLS'yi değiştiremez.
4. **Ek uygulama filtresi:** EF Core global filtreleri ikinci savunmadır. `IgnoreQueryFilters()` veya ham SQL, veritabanı RLS'sini aşamaz.
5. **Bağlantı havuzu:** `set_config('app.current_tenant_id', ..., is_local => true)` işlem sonunda sıfırlanır. `ClearTenantSessionAsync` bağlantı havuza dönmeden temizler.
6. **Worker bağlamı, uygulandı:** `ITenantWorkerJobRunner`, doğrulanmış `ITenantJobEnvelope` ile açık bağlamda çalışır; her durumda temizler.
7. **Önbellek, uygulandı:** `TenantCacheKeyFactory`, ayraç enjeksiyonu korumasıyla `cache:{tenant_id}:{branch_id}:{resource}:{id}` üretir.
8. **Canlı kanal, Faz 9 önerisi:** `channel:tenant_{tenant_id}:branch_{branch_id}:kds_kitchen`.

## 5. Veritabanı Güvenlik Rolleri

### 5.1. NOLOGIN Grubu ve LOGIN Kullanıcısı

1. **`restaurant_app_runtime`:**
   - Ayrıcalıklı `deploy/bootstrap/001_create_runtime_login_role.sql` oluşturur.
   - `NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`.
   - `20260920182029_AddTenantRowLevelSecurity.cs`, `tenancy` DML ve sequence kullanımını verir; CREATE yetkisini kaldırır.
   - Geçiş, grup yoksa açıklayıcı hatayla durur. Grup doğrudan giriş yapamaz; parolası yoktur.
2. **`restaurant_app_user`:**
   - Yayın hattı, gizli bilgi yöneticisi veya DBA, aynı bootstrap betiğiyle uygulamadan önce oluşturur.
   - Parola `psql \getenv app_runtime_password APP_RUNTIME_PASSWORD` ve `format(%L)` ile güvenli alınır.
   - `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`.
   - `GRANT restaurant_app_runtime TO restaurant_app_user` üyeliği verilir.
   - Yüksek entropili rastgele parola `DATABASE_URL` üzerinden enjekte edilir; şema yetkilerini bozmadan değiştirilebilir.
   - Kimlik bilgisi Git'e, geçişe, test verisine veya konteyner imajına **asla eklenmez**.

### 5.2. Ortamlara Göre Sağlama

- Yerel geliştirme: Git'e eklenmeyen yerel ortam yapılandırması ve Docker Compose. `.env` ile kontrol betiği uyumsuzluğu R10'da izlenir.
- Entegrasyon: `TestcontainersFixture`, runtime grubu ve `test_rt_<random_suffix>` gibi geçici kullanıcıyı 256 bit rastgele parolayla oluşturur; üyelik verir ve test sonunda kaldırır.
- Staging/Production: Yönetilen bulut kimliği veya kasa tarafından üretilen bilgiler ortam sırlarıyla enjekte edilir.

### 5.3. Yayın ve Geçiş Sırası

1. DBA, bootstrap betiğini `APP_RUNTIME_PASSWORD` güvenli enjeksiyonuyla çalıştırır; runtime grubu/kullanıcısını hazırlar.
2. Sahip/geçiş rolü, uygulamadan önce `dotnet ef database update` veya `scripts/migration-ops.mjs` çalıştırır.
3. Runtime bağlantı bilgisi gizli bilgi yöneticisinden boş Green yuvasına enjekte edilir.
4. Runtime kullanıcısının bağlanabildiği, bağlamsız sıfır satır ve bağlamla yalnızca yetkili işletme satırları gördüğü doğrulanır.
5. Aday API konteyneri açılır; havuz ve arka plan hizmetleri ısıtılır.
6. `/health/live`, `/health/ready` ve hata oranı denetlenir; Nginx üzerinden trafik geçirilir.

### 5.4. Bağlantı Havuzu Varsayımları

- `set_config('app.current_tenant_id', @tenantId, true)` yalnızca açık işlemde geçerlidir (`is_local => true`). Commit, rollback ve hata ayarı temizler.
- `RestaurantOrderDbContext.ClearTenantSessionAsync()`, Npgsql havuzuna dönüşte `app.current_tenant_id` değerini açıkça boşaltır.
- Uygulama sahip olmayan rolle çalışır; `FORCE ROW LEVEL SECURITY` uygulanır; SUPERUSER/BYPASSRLS yoktur.
- Eksik, boş, bozuk veya var olmayan işletme kimliğinde `tenancy.get_current_tenant_id()` NULL döndürür; politika erişimi reddeder, okuma sıfır satırdır.

### 5.5. Kimlik Bilgisi Sızıntısı

1. Kasada yeni yüksek entropili parola oluşturun.
2. DBA bağlantısıyla `ALTER ROLE restaurant_app_user WITH PASSWORD '<NEW_STRONG_PASSWORD>';` çalıştırın.
3. Yuvaların bağlantı bilgilerini güncelleyip kesintisiz yeniden başlatın.
4. Sızıntı aralığındaki anormal sorguları denetim günlüklerinden inceleyin.

## 6. Test Zorunlulukları

- Her entegrasyon testi en az sentetik Tenant A ve Tenant B ile çalışır.
- A bağlamında B kimliklerine erişim sıfır kayıt döndürmelidir.
- Test sahip olmayan runtime rolüyle çalışır; ayrıcalıklı rolün sahte başarısı kabul edilmez.
- Eksik/boş/geçersiz UUID ve var olmayan işletme UUID'si açıkça test edilir; sıfır satır beklenir.
- İşletmeler arası görünürlük açığı **Sev-1 güvenlik olayıdır**.

## Faz 5 Katalog Tabloları

`tenancy` şemasındaki `menus`, `menu_categories`, `menu_items`, `item_variants`, `modifier_groups`, `modifier_options`, `menu_item_modifier_group_assignments`, `branch_item_availability` yalıtılır. Sahipliğin aggregate sınırını geçtiği ilişkilerde bileşik işletme/şube anahtarları kullanılır; RLS etkin ve zorunludur. BranchManager atanmış şubeyle; mutfak/bar bulunabilirlik değişiklikleri kendi hazırlık istasyonunun ürünleriyle sınırlıdır. Çalışma zamanı menüsü yanıttan önce hem işletme hem şubeye göre filtrelenir.
