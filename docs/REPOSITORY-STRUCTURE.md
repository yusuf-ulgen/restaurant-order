# Depo Yapısı ve Kodlama Standartları (`docs/REPOSITORY-STRUCTURE.md`)

## 1. Planlanan Depo Düzeni (Monorepo Mimarisi)

Depo, çekirdek alan paketlerini, arka uç hizmetlerini ve ön uç uygulamalarını net bir şekilde ayıran modüler bir çalışma alanı olarak yapılandırılmıştır. Beş ürün arayüzü üç web uygulamasında gruplanır:

```
restaurant-order/
├── AGENTS.md                          # Ana katkı ve yapay zekâ kuralları
├── README.md                          # Depo özeti ve başlangıç
├── pnpm-workspace.yaml                # Monorepo çalışma alanı ayarı
├── package.json                       # Kök betikler ve geliştirme araçları
├── pnpm-lock.yaml                     # Ortak kilit dosyası
├── RestaurantOrder.sln                # .NET 10 çözümü
├── global.json                        # .NET 10 SDK sürümü
├── .editorconfig                      # Diller arası biçim kuralları
├── .gitignore                         # Git dışlama kuralları
├── .env.example                       # Yerel ortam değişkeni şablonu
├── docs/                              # Mimari, alan, ürün ve operasyon belgeleri
│   ├── adr/                           # Mimari karar kayıtları
│   ├── runbooks/                      # Operasyon ve dağıtım rehberleri
│   └── templates/                     # ADR, özellik ve olay şablonları
├── apps/                              # Web uygulamaları ve arka uç süreçleri
│   ├── customer-web/                  # Arayüz 1: QR müşteri PWA uygulaması (React 19 + Vite)
│   ├── operations-web/                # Arayüz 2: Garson ve operasyon uygulaması (React 19 + Vite)
│   ├── admin-web/                     # Arayüz 4–5: Restoran ve süper yönetim (React 19 + Vite)
│   ├── api/                           # ASP.NET Core 10 modüler monolit REST API
│   └── worker/                        # .NET 10 arka plan worker süreci
├── packages/                          # Ortak monorepo paketleri
│   ├── ui/                            # Ortak tasarım değişkenleri ve UI bileşenleri (React 19)
│   ├── contracts/                     # OpenAPI sözleşme sınırı ve ortak DTO türleri
│   └── config/                        # Ortak TypeScript, ESLint ve araç ayarları
├── tests/                             # Test ve doğrulama paketleri
│   ├── architecture/                  # .NET mimari sınır testleri
│   ├── integration/                   # ASP.NET Core entegrasyon testleri
│   └── e2e/                           # Playwright uçtan uca test paketi
├── deploy/                            # Konteyner ve yerel altyapı
│   ├── docker-compose.yml             # Yerel PostgreSQL 16 ve Redis 7
│   └── docker/                        # Çok aşamalı Dockerfile dosyaları (api, worker, web)
└── scripts/                           # Araç ve doğrulama betikleri
    ├── verify.ps1                     # Tam monorepo doğrulama akışı
    └── dev.ps1                        # Yerel geliştirme başlatıcısı
```

> **Kuruluş Durumu:** Monorepo'nun teknik temeli React ile destekleniyor 19 ön uç kabukları, ASP.NET Core 10 API başlangıç uç noktaları (`/health/live`, `/health/ready`), .NET 10 arka plan çalışan ana bilgisayarı ve paylaşılan TypeScript yapılandırması ve kullanıcı arayüzü paketleri. Bu kuruluş aşamasına ait tarihsel açıklamadır; güncel IAM, yapılandırma ve katalog kapsamı CURRENT-STATE.md içinde izlenir.

---

## 2. Dosya Boyutu ve Ayrıştırma Kuralları

Bakımı mümkün olmayan monolitik dosyaları önlemek için, insan tarafından yazılan tüm kaynak, test ve belge dosyalarında aşağıdaki sınırlar sıkı bir şekilde uygulanır:

### 2.1. Eşikler
- **450 Satır (Uyarı):** Herhangi bir dosya yaklaştığında 450 satırlar, yazarların ayrıştırmayı planlaması gerekir.
- **600 Satır (Katı Maksimum Tavan):** İnsan tarafından yazılan hiçbir dosya aşağıdaki değerleri aşamaz: 600 Her koşulda çizgiler. CI geçitleri ve otomatik ön işleme komut dosyaları, bu kuralı ihlal eden işlemeleri reddeder.

### 2.2. İzin Verilenler Listesi Politikası (İstisnalar)
 600-satır tavanı kesinlikle insan tarafından yazılan kod için geçerlidir. İzin verilen tek istisnalar şunlardır:
1. **Paket Kilit Dosyaları:** `package-lock.json`, `pnpm-lock.yaml`, `yarn.lock`.
2. **Oluşturulan Veritabanı Yapıları:** Otomatik olarak oluşturulan veritabanı şeması dökümleri veya geçiş anlık görüntüleri (makine tarafından oluşturulmuşsa).
3. **API Dokümantasyon Çıktıları:** OpenAPI/Swagger JSON/YAML oluşturulan dosyalar.
4. **Üçüncü Taraf Satıcı Paketleri:** Üçüncü taraf kitaplıklar satıcı dizinlerine eklendi.
5. **Anlık Görüntü Test Dosyaları:** Makine tarafından oluşturulan Jest/Vitest anlık görüntü dosyaları.

Şunu aşan herhangi bir dosya: 600 Açık bir izin verilenler listesinde yer almayan satırlar kritik bir kusur olarak kabul edilir.

### 2.3. Ayrıştırma Yönergeleri
Dosyalar **asla** keyfi olarak bölünemez (örneğin `file_part1.ts`, `file_part2.ts`). Bunun yerine şuna bölün:
- **Özellik / Alt Alan:** İlgili alan mantığını birlikte gruplayın (örneğin `order-lifecycle.ts`, `order-pricing.ts`).
- **Kullanım Örneği / Etkileşimci:** Uygulama kullanım durumlarını bireysel işleyicilere ayırın (örneğin `PlaceOrderHandler.ts`, `CancelOrderItemHandler.ts`).
- **Bağlantı Noktası / Adaptör:** İş mantığını harici protokollerden ayırın (örneğin `EscPosPrinterAdapter.ts`, `StripePaymentAdapter.ts`).
- **Bileşen Sorumluluğu:** Kullanıcı arayüzü ekranlarını atomik, odaklanmış bileşenlere bölün (örneğin `KdsTicketCard.tsx`, `KdsTimerBadge.tsx`).

---

## 3. Modüler Sınır Kuralları

1. **Tek Yönlü Bağımlılıklar:** Paylaşılan paketler (`packages/domain`) uygulamalara (`apps/*`) bağımlı olamaz; uygulamalardan kod içe aktaramaz.
2. **Alan Saflığı:** Alan paketi çerçeve bağımlılıkları (React, Express, NestJS vb.) ve ORM öznitelikleri içermemelidir.
3. **Arayüzler Arası İçe Aktarma Yok:** Uygulamalar (örneğin `apps/kitchen-kds`) kardeş uygulamalardan dosyaları doğrudan içe aktaramaz (örneğin `apps/waiter-mobile`). Paylaşılan kod şu adreste bulunmalıdır: `packages/*`.
4. **İşletme Bağlamı Kapsamı:** Tüm veritabanı erişim katmanı işlevlerinin zorunlu kılınması gerekir `tenant_id` ve `branch_id` arguments.
