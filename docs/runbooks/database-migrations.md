# Veritabanı Geçişleri ve Sıfır Kesinti Süreli Şema Gelişimi (`docs/runbooks/database-migrations.md`)

## 1. Amaç ve Kapsam

Bu belge, `restaurant-order` PostgreSQL 16 veritabanı şemasının geliştirilmesi için operasyon adımlarını, güvenlik kurallarını ve geri alma akışlarını tanımlar. Blue/Green dağıtım modeli kapsamında hem CI/CD otomatik doğrulamasını hem de üretim manuel yürütmesini yönetir.

---

## 2. Temel Değişmezler ve Güvenlik Kuralları

1. **Süreç Dışı Yürütme:**
   - Staging ve Production ortamlarında web uygulaması (`apps/api`) başlangıcında otomatik geçiş çalıştırmak kesinlikle yasaktır.
   - Geçişler, idempotent kullanılarak ayrı, izlenen bir kesme öncesi operasyonel adım olarak yürütülür SQL komut dosyaları veya CLI veritabanı geçişi koşucuları.
2. **Genişlet–Taşı–Daralt Uyumluluğu:**
   - Tüm şema değişiklikleri, paylaşılan veritabanında hem aktif (Mavi) hem de aday (Yeşil) slotların eşzamanlı olarak yürütülmesini desteklemelidir.
   - Aşama 1 (Genişlet): Yeni null yapılabilir sütunlar, tablolar veya mevcut davranışı bozmayan kısıtlamalar ekleyin.
   - Aşama 2 (Taşıma): Eski uygulama sürümünü bozmadan verileri çift yazma veya doldurma.
   - Aşama 3 (Daralt): Eski sütunları yalnızca eski yuva tamamen boşaltılıp kullanımdan kaldırıldıktan sonra kullanımdan kaldırın ve güvenli bir şekilde kaldırın.
3. **Yıkıcı Değil DDL Kesim Öncesi:**
   - `DROP TABLE`, `DROP COLUMN`, `RENAME COLUMN`, `ALTER TABLE ... DROP`ve `TRUNCATE` kesimden önce kesinlikle yasaktır.
4. **İdempotans Garantisi:**
   - Tüm geçiş komut dosyalarının kontrol edilmesi gerekir `__EFMigrationsHistory` ve varoluş muhafızları (`CREATE SCHEMA IF NOT EXISTS`, `IF NOT EXISTS`) tekrar yürütmenin durumu bozmamasını veya iptal etmemesini sağlamak için.
5. **Doğrulanmış Yedekleme Önkoşulu:**
   - Üretimde geçişleri gerçekleştirmeden önce yedekleme/anlık görüntü hazırlığının doğrulanması gerekir (`BACKUP_VERIFIED=true`).

---

## 3. Adım Adım Yürütme İş Akışı

### 3.1. Dağıtım Öncesi Doğrulama (Prova Çalıştırma)

Tüm geçiş dosyalarının ve komut dosyalarının zararsız ve geriye dönük uyumlu olduğunu doğrulayın:

```bash
# Veritabanı geçişi güvenlik denetimi
node scripts/blue-green/migration-check.mjs
```

### 3.2. Komut Dosyası Oluşturma

İnceleme ve denetim için tekrar çalıştırılması güvenli SQL betiği oluşturun:

```bash
dotnet ef migrations script \
  --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
  --startup-project apps/api/RestaurantOrder.Api.csproj \
  --idempotent \
  --output deploy/migrations/001_initial_tenancy_schema.sql
```

### 3.3. Aşama / Üretim Dağıtım Sırası

Staging ve Production dağıtımlarında aşağıdaki altı adım izlenir:

1. **Adım 1: Ayrıcalıklı Rol ve Bootstrap Hazırlığı (DBA / Operatör)**
   Yürüt `deploy/bootstrap/001_create_runtime_login_role.sql` ayrıcalıklı bir kullanıcı olarak (`postgres` / DBA) güvenli ortam gizli enjeksiyonu ile. Komut dosyası tam olarak şunları sağlar:
   - `restaurant_app_runtime` grup rolü (`NOLOGIN`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOBYPASSRLS`).
   - `restaurant_app_user` oturum açma rolü (`LOGIN`, `PASSWORD`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOBYPASSRLS`).
   - `restaurant_app_user` rolünü `restaurant_app_runtime` grubuna üye yapar.

   Betik, kimlik bilgilerini güvenli bir şekilde alır `psql \getenv app_runtime_password APP_RUNTIME_PASSWORD` ve `format(%L)` düz metin sırlarının süreç listelerinde veya günlüklerinde görünmesini önlemek için:
   ```bash
   export APP_RUNTIME_PASSWORD="<STRONG_CRYPTOGRAPHIC_PASSWORD>"
   psql -v ON_ERROR_STOP=1 "$DBA_CONNECTION_URL" -f deploy/bootstrap/001_create_runtime_login_role.sql
   unset APP_RUNTIME_PASSWORD
   ```

2. **Adım 2: Kesim Öncesi Şema Geçişi (Geçiş Sahibi / CI)**
   Aday yuvasını ısıtmadan önce EF Core geçişlerini ayrıcalıklı geçiş sahibi rolü olarak uygulayın. Geçiş, hızlı bir önkoşul kontrolünü zorunlu kılarak `restaurant_app_runtime` var, yaratır RLS Yardımcı işlevler, politikalar ve hibeler DML/sequence şema izinleri `tenancy`:
   ```bash
   export DATABASE_URL="postgresql://${MIGRATION_USER}:${MIGRATION_PASSWORD}@${DB_HOST}:5432/restaurant_order_prod"
   export BACKUP_VERIFIED="true"

   dotnet ef database update \
     --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
     --startup-project apps/api/RestaurantOrder.Api.csproj
   ```

3. **Adım 3: Çalışma Zamanı Kimlik Bilgisi Gizli Ekleme (Aday Yuvası)**
   Çalışma zamanı bağlantı dizesini enjekte edin (kullanarak `restaurant_app_user` ve sağlanan şifreyi) aday yuvasına (`Green`) dağıtım gizli yöneticisi/kasa aracılığıyla konteyner ortamı (örneğin AWS Sırlar Yöneticisi, HashiCorp Vault, Kubernetes Sırrı).

4. **Adım 4: Çalışma Zamanı Bağlantısı & RLS Temel İşlev Doğrulaması**
   Ayrıcalıksız çalışma zamanı rolünün temiz bir şekilde bağlandığını ve Satır Düzeyinde Güvenlik arıza kapatma korumalarının uygulandığını doğrulayın:
   - Kimliği doğrulanmamış/eksik işletme bağlamı: Sorgular sıfır satır döndürür (`SELECT COUNT(*) FROM tenancy.tenants` sonucu sıfırdır).
   - Kimliği doğrulanmış işletme bağlamı: Sorgular yalnızca işletme bağlamına ait kayıtları döndürür `app.current_tenant_id`.

5. **Adım 5: Yeşil Uygulama Yuvası Başlatma**
   Aday slot kapsayıcılarını başlatın (`apps/api`), EF Core bağlantı havuzlarının ve arka plan çalışanlarının geçirilen veritabanına karşı ısınması.

6. **Adım 6: Sağlık Doğrulaması ve Blue/Green Geçiş**
   Sentetik durum kontrollerini yürütün (`/health/live`, `/health/ready`), hata oranlarını doğrulayın ve Nginx giriş yönlendiricisi (`node scripts/blue-green/cutover.mjs --execute`).

### 3.4. Idempotent Kimlik Bilgisi Rotasyonu
Çalışma zamanı kimlik bilgilerinin tehlikeye girdiğinden veya rutin rotasyona tabi olduğundan şüpheleniliyorsa:
1. Dağıtım gizli yöneticisinde/kasasında yeni yüksek entropili kimlik bilgileri oluşturun.
2. DBA ortamında yeni `APP_RUNTIME_PASSWORD` değerini ayarlayıp `deploy/bootstrap/001_create_runtime_login_role.sql` betiğini yeniden çalıştırın. Betik, `ALTER ROLE restaurant_app_user WITH PASSWORD %L` ile parolayı günceller; şema ayrıcalıkları ve grup üyelikleri korunur.
3. Dağıtım yuvalarındaki bağlantı dizelerini güncelleyin ve hareketli konteyner yeniden başlatmaları gerçekleştirin.
4. Olay penceresi sırasında yetkisiz sorgular için PostgreSQL denetim günlüklerini inceleyin.

---

## 4. Geri Alma ve Aşağı Geçiş Stratejisi

1. **Tahribatsız Güvenlik Penceresi:**
   - Genişletme aşaması geçişleri yalnızca eklemeli, mevcut davranışı bozmayan öğeler (yeni tablolar, yeni null yapılabilir sütunlar, bileşik yabancı anahtarlar) eklediğinden, önceki uygulama yuvası, yeni sürüm iptal edilse bile kesintisiz olarak çalışmaya devam eder.
2. **Durdurulan Dağıtım (Kesintiden Önce):**
   - Etkin olmayan yuvada sağlık araştırmaları veya temel işlev testleri başarısız olursa trafik etkin yuvada kalır.
   - Ek şema değişiklikleri uykuda kalır ve hemen geri alınmasına gerek yoktur.
3. **Acil Durum Geri Alma (Kesim Sonrası):**
   - Trafik, Nginx girişi yoluyla güvenli yuvaya geri kaydırılır. 60 saniye (`node scripts/blue-green/rollback.mjs --execute --confirm-rollback`).
   - Olay stabilizasyonundan sonra şemanın geri alınması kesinlikle gerekiyorsa:
     ```bash
     dotnet ef database update <PreviousMigrationName> \
       --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
       --startup-project apps/api/RestaurantOrder.Api.csproj
     ```
4. **Adli Koruma:**
   - Devam eden bir olayı araştırırken asla yıkıcı aşağı geçişler gerçekleştirmeyin.
