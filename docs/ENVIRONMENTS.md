# Ortamlar ve Yapılandırma Yönetimi (`docs/ENVIRONMENTS.md`)

## 1. Ortam Topolojisi

`restaurant-order`, birbirinden yalıtılmış altı çalışma ortamı tanımlar:

```text
Yerel → Test (CI) → Geliştirme → Üretim Öncesi (Staging)
                                      ↓
                          Üretim (Blue / Green)
Yerel: geliştirici bilgisayarı; test: otomatik kontroller;
geliştirme: ortak entegrasyon; staging: üretim benzeri sentetik ortam;
üretim: canlı restoran trafiği.
```

| Çevre | Amaç | Veritabanı | Dış Entegrasyonlar | Erişim Kısıtlamaları |
| :--- | :--- | :--- | :--- | :--- |
| **Yerel** | Geliştirici özelliği oluşturma | Yerel Docker / SQLite / Postgres | Sahte ağ geçitleri ve yazıcılar | Yalnızca geliştirici |
| **Test** | Otomatik CI birimi ve entegrasyon testleri | Geçici test veritabanı (çalıştırma başına sıfırlama) | Sahte ağ geçitleri ve simüle edilmiş soket | Yalnızca CI koşucusu |
| **Geliştirme** | Dahili ekip doğrulaması ve önizlemeleri | Özel Dev Postgres örneği | Korumalı alan ödeme ve test yazıcıları | Dahili geliştirme ekibi |
| **Üretim Öncesi (Staging)** | Üretim öncesi testler, UAT, yük testleri | Yansıtılmış aşamalandırma DB'si (sentetik veriler)| Canlı korumalı alan ödeme ağ geçitleri | Ekip ve beta test kullanıcıları |
| **Üretim Blue** | Aktif/bekleme canlı yuvası (bağlantı noktası 5001) | Multi-AZ Yüksek Kullanılabilirlik Postgres'i | Canlı ödeme ağ geçitleri ve donanımı | Genel / Rol Kapılı |
| **Üretim Green**| Aktif/bekleme canlı yuvası (bağlantı noktası 5002) | Multi-AZ Yüksek Kullanılabilirlik Postgres'i | Canlı ödeme ağ geçitleri ve donanımı | Genel / Rol Kapılı |

---

## 2. Yapılandırma ve Gizli Değerler Yönetişimi (12-Faktör Uygulaması)

- **Ortam Yoluyla Yapılandırma:** Ortama özgü tüm davranışların (veritabanı URL'leri, günlük düzeyleri, ödeme anahtarları) ortam değişkenleri aracılığıyla eklenmesi gerekir.
- **Sıfır Sabit Kodlu URL'ler/Kimlik Bilgileri:** Sabit kodlanmış uç noktalar, IP adresleri veya sırlar kesinlikle yasaktır.
- **Başlangıçta Doğrulama:** Staging ve Production ortamlarında `ConfigurationValidator` (API) ve `WorkerConfigurationValidator` (worker) gerekli değişkenleri denetler. Herhangi bir kritik konfigürasyon eksik veya hatalı biçimlendirilmişse, süreç bilgilendirici bir ölümcül günlükle (gizli değerler sızdırılmadan) derhal sonlandırılır.
- **Ön Uç Güvenlik Sınırı:** Yalnızca ön eki olan ortam değişkenleri `VITE_` istemci tarafı koduna paketlenir. Sunucu sırları, veritabanı kimlik bilgileri ve özel imzalama anahtarları hiçbir zaman ön uç uygulamalara açıklanmamalıdır (`apps/*-web`).

### 2.1. Ortam Değişkeni Sözleşme Referansı

| Değişken Adı | Amaç | Gerekli | Gizli | Örnek / Varsayılan |
| :--- | :--- | :--- | :--- | :--- |
| `NODE_ENV` | Çalışma zamanı ortamı modu | Hepsi | Hayır | `development` / `production` |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core barındırma modu | Hepsi | Hayır | `Development` / `Staging` / `Production` |
| `DEPLOYMENT_COLOR` | Konteyner yuvası rengi (`blue` / `green`)| Staging, Production | Hayır | `blue` / `green` |
| `ACTIVE_DEPLOYMENT_SLOT`| Trafik/iş için yetkilendirilmiş aktif slot | Staging, Production | Hayır | `blue` / `green` |
| `DATABASE_URL` | PostgreSQL bağlantı dizesi | Hepsi | Evet (Üretim) | `Host=localhost;Port=5432;...` |
| `REDIS_URL` | Redis ana bilgisayarı:bağlantı noktası bağlantı dizesi | Hepsi | Evet (Üretim) | `localhost:6379` |
| `JWT_SECRET` | JWT imzalama için simetrik anahtar | Staging, Production | Evet | `[Secured in Secret Manager]` |
| `API_PORT` | Arka uç HTTP API dinleme portu | İsteğe bağlı | Hayır | `5000` |
| `API_PORT_BLUE` | Slot Blue için giriş bağlantı noktası API | İsteğe bağlı | Hayır | `5001` |
| `API_PORT_GREEN` | Slot Green için giriş bağlantı noktası API | İsteğe bağlı | Hayır | `5002` |
| `VITE_API_URL` | halka açık API web istemcileri için uç nokta | Tüm Web Uygulamaları | Hayır | `http://localhost:5000` |
| `IMAGE_DIGEST` | Değişmez konteyner imajının SHA256 özeti | Staging, Production | Hayır | `sha256:...` |
| `LOG_LEVEL` | Uygulama günlüğü ayrıntı düzeyi | İsteğe bağlı | Hayır | `Information` |
| `PIN_PEPPER_SECRET` | Personelin dört haneli PIN karması için gizli pepper değeri | Staging, Production | Evet | `[Secured in Secret Manager]` |
| `CORS_ALLOWED_ORIGINS` | Açıkça izin verilen kaynaklar (joker karakter/yerel yok)| Staging, Production | Hayır | `https://admin.restaurantorder.app,...` |
| `NOTIFICATION_PROVIDER` | Bildirim arka ucu (`TransactionalOutbox`) | Staging, Production | Hayır | `TransactionalOutbox` |
| `PUSH_PROVIDER` | IAM'den ayrı operasyon push taşıması | İsteğe bağlı | Hayır | `Disabled` / `Fcm` |
| `FCM_PROJECT_ID` | Hedef Firebase projesi | PUSH_PROVIDER=Fcm ise | Hayır | Ortama özgü proje kimliği |
| `GOOGLE_APPLICATION_CREDENTIALS` | Yerelde gerekiyorsa güvenilir, depo dışı ADC dosyasının yolu | Yönetilen ADC yoksa | Dosya içeriği gizlidir | Git'e veya imaja eklenmez |
| `NOTIFICATION_ENCRYPTION_KEY` | 256-bit AES-GCM Giden kutusu verisi şifreleme anahtarı | Staging, Production | Evet | `[Secured in Secret Manager]` |
| `WEBHOOK_NOTIFICATION_URL` | Giden HTTPS bildirim teslimi için uç nokta | Staging, Production | Hayır | `https://notifications.internal/webhook` |
| `WEBHOOK_NOTIFICATION_SECRET` | HMAC-SHA256 imza sırrı (min 32 karakterler) | Staging, Production | Evet | `[Secured in Secret Manager]` |
| `FORWARDED_HEADERS_ENABLED` | Ters proxy iletilen başlıkları etkinleştir | İsteğe bağlı | Hayır | `false` / `true` |
| `FORWARDED_HEADERS_KNOWN_PROXIES` | Güvenilir ters proxy IP'leri (virgülle ayrılmış)| Staging, Production (etkinse) | Hayır | `192.0.2.1` |
| `FORWARDED_HEADERS_KNOWN_NETWORKS` | Güvenilir CIDR ağlar (virgülle ayrılmış)| Staging, Production (etkinse) | Hayır | `198.51.100.0/24` |
| `FORWARDED_HEADERS_FORWARD_LIMIT` | Maksimum iletilen proxy sınırı | İsteğe bağlı | Hayır | `2` |
| `Tenancy:AllowDevHeaderOverride` | X-Tenant-Id üstbilgilerini etkinleştirme | Yalnızca geliştirici | Hayır | `false` |
| `BACKUP_VERIFIED` | Geçişler için doğrulanmış veritabanı yedekleme önkoşulu | Staging, Production | Hayır | `false` |

FCM taşıması varsayılan kapalıdır; etkinse sağlayıcı/proje ayarı servis kaydında, ADC kimliği ilk çözümlemede yüklenir. API sağlık yanıtı Firebase erişim kanıtı değildir. Güvenli kurulum, ortam ayrımı ve kalan cihaz/olay bağımlılıkları için [FCM-SETUP.md](./FCM-SETUP.md) belgesine bakın.

---

## 3. Konteyner Sertleştirme ve Ağ İzolasyonu

Konteynerli iş yüklerinin tümü katı operasyonel güvenlik yönergelerine uyar:

1. **Root Dışı Yürütme:**
   - API ve worker konteynerleri ayrıcalıklı olmayan bir kullanıcı altında çalışır (`appuser`, UID `10001`).
   - Web ön uç kapsayıcıları ayrıcalıksız koşullar altında çalışır `nginx` (UID `101`).
2. **Minimum Yazılabilir Dosya Sistemi:**
   - Kapsayıcı kök dosya sistemleri salt okunur olarak bağlanır (`read_only: true`).
   - Geçici dosya işlemleri bellek destekli tmpf'lerle sınırlıdır (`/tmp`).
3. **Ağ İzolasyonu:**
   - Hizmetler dahili bir köprü ağı üzerinden iletişim kurar (`app_internal`).
   - Staging ve Production ortamlarında PostgreSQL ve Redis konteynerleri dışarıya açık sunucu portu yayımlamaz.
4. **Prodüksiyon Görüntüsü Saflığı:**
   - Çok aşamalı Docker yapıları, nihai üretim görüntülerinde sıfır geliştirme derleyicisi, SDK veya geliştirme bağımlılığının kalmasını sağlar.

---

## 4. Katı Ortamlar Arası İzolasyon Kuralları

1. **Üretim Verisinin Korunması:** Tam anonimleştirme yapılmadan üretim veritabanı dökümleri `local`, `test` veya `development` ortamlarına kesinlikle yüklenmez.
2. **Ağ İzolasyonu:** Daha düşük ortamlar, üretim veritabanlarına veya canlı ödeme işlemcisi uç noktalarına ağ isteklerini başlatamaz.
3. **Özel Şifreleme Anahtarları:** Her ortamın farklı şifreleme anahtarları ve sertifikaları kullanması gerekir.
4. **Geçiş ve Örnek Veri Ayrımı:** Staging ve Production ortamlarında API başlangıcında otomatik veritabanı geçişi çalıştırılmaz. Geçişler, trafik aktarımından önce ayrı bir adımda, tekrar çalıştırılması güvenli betiklerle uygulanır. Sentetik veri oluşturma (`DevDataSeeder`) yalnızca `Development` ortamında çalışır; diğer ortamlarda istisna fırlatarak işlemi reddeder.
