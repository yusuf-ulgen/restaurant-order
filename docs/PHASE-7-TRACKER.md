# Faz 7 Uygulama Takip Belgesi (`docs/PHASE-7-TRACKER.md`)

Bu belge, `restaurant-order` projesinde **Faz 7: Müşteri Deneyimi (Menü Gezinme, Ürün Özelleştirme, Sepet ve Servis Talepleri)** aşamasının tüm alt fazlarını ve uygulama durumunu takip eder.

> **Kapsam Sınırları ve Mimari İlkeler:**
> - **Gerçek sipariş ve kalıcılık sınırı:** Faz 7 gerçek sipariş aggregate'i (`Order`) veya sipariş kalıcılığı oluşturmaz.
> - **Yetkili fiyatlandırma ve sipariş gönderimi:** Gerçek sipariş gönderimi ve otoriter fiyat hesaplama Faz 8'e (Sipariş Çekirdeği) aittir.
> - **Canlı takip ve KDS:** SignalR canlı sipariş takibi Faz 9'a, Mutfak/Bar KDS Faz 11'e aittir.
> - **Hesap ve ödeme:** Adisyon/hesap motoru Faz 13'e, ödeme ağ geçidi Faz 16'ya aittir.
> - **Sahte üretim davranışı yok:** Faz 7'de sahte başarılı sipariş, yanıltıcı mock API veya doğrulanmamış üretim taklitleri yer alamaz.

---

## 1. Alt Aşama Durum Özeti

| Alt Aşama | Başlık | Durum | Temel Çıktı | Commit / PR |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 7.0** | Temel ve Müşteri Oturum Kurtarma | **Tamamlandı** | `GET /api/v1/customer/session`, state makinesi, oturum kurtarma, kontratlar ve testler | `feat/phase-07-customer-experience` |
| **Faz 7.1** | Menü Gezinme ve Filtreler | **Tamamlandı** | CUST-02 Dijital Menü, kategori çubuğu, diyet/alerjen filtreleme, arama normalizasyonu, sözleşmeler ve drift testleri | `feat/phase-07-customer-experience` |
| **Faz 7.2** | Ürün Özelleştirme | **Tamamlandı** | CUST-03 Varyant seçimi, değiştirici kuralları, saf fiyat önizlemesi, müşteri notu, DraftCartLine | `feat/phase-07-customer-experience` |
| **Faz 7.3** | Sepet ve Fiyat Önizleme | **Tamamlandı** | CUST-04 Sepet store/reducer, sessionStorage draft persistence, menü tazelik/fiyat doğrulayıcı, CartSheet | `feat/phase-07-customer-experience` |
| **Faz 7.4** | Servis Talepleri | **Tamamlandı** | CUST-06 Servis talebi aggregate'i, RLS ve partial unique index, CSRF/Idempotency, rate limit, ServiceRequestSheet ve bill request akışı | `feat/phase-07-customer-experience` |
| **Faz 7.5** | Sertleştirme ve Kapanış | **Planlandı** | Hata akışları, erişilebilirlik, uçtan uca doğrulamalar, güvenlik denetimi | — |

---

## 2. Ayrıntılı Aşama Teslimatları

### Faz 7.0: Temel ve Müşteri Oturum Kurtarma
- [x] **Faz 7 Başlangıcı ve Yol Haritası Hizalaması:**
  - `feat/phase-07-customer-experience` dalı `main` üzerinden açıldı.
  - `docs/PHASE-7-TRACKER.md` oluşturuldu; kapsam sınırları ve ilkeler belirlendi.
  - `docs/CURRENT-STATE.md`, `docs/ROADMAP.md` ve `docs/TESTING.md` güncellendi.
- [x] **Müşteri Mevcut Oturum Uç Noktası (`GET /api/v1/customer/session`):**
  - Yalnızca `Customer` rolü erişebilir.
  - Tenant, şube ve oturum bilgileri gövde veya sorgudan değil, doğrulanmış JWT claim'lerinden türetilir.
  - Oturumun aktifliği PostgreSQL veritabanı (source-of-truth) üzerinden doğrulanır.
  - Kapalı veya birleştirilmiş oturumlar için generic RFC 7807 401 Unauthorized ProblemDetails döner.
  - Güvenli metadata yanıtı (`sessionId`, `tenantId`, `branchId`, `tableId`, `tableNumber`, `tableName`, `sessionStatus`, `concurrencyToken`, `guestCount`, `openedAtUtc`).
  - Yanıtta veya istemci durumunda hiçbir şekilde erişim tokenı yer almaz.
- [x] **Müşteri Web Uygulama State Mimarisi (`apps/customer-web`):**
  - Belirgin durum yapısı: `bootstrapping`, `unauthenticated/QR required`, `active session`, `expired session`, `loading`, `recoverable error`.
  - Sayfa yenilemede otomatik oturum kurtarma (HttpOnly çerez + `credentials: 'include'`).
  - Token'ın `localStorage` veya `sessionStorage`'a yazılmaması güvencesi.
  - Oturum sona erdiğinde oturuma bağlı durumları (sepet/taslak) temizleme kancası (`useSessionStateCleanup`).
- [x] **Doğrulama ve Testler:**
  - Birim, entegrasyon ve bileşen testleri tamamlandı (320 entegrasyon testi, 41 frontend testi).

### Faz 7.1: Menü Gezinme ve Filtreler (CUST-02)
- [x] **Runtime Menü Sözleşmeleri ve Drift Testleri:**
  - `packages/contracts` içine backend `RuntimeMenuReadModel` ile birebir uyumlu TypeScript arayüzleri eklendi (`RuntimeMenu`, `RuntimeMenuCategory`, `RuntimeMenuItem`, `RuntimeItemVariant`, `RuntimeModifierGroup`, `RuntimeModifierOption`).
  - Backend (`RuntimeMenuContractDriftTests.cs`) ve frontend (`runtimeMenuContract.test.ts`) sözleşme drift testleri yazıldı ve doğrulandı.
  - `runtimeMenuApi.ts` istemcisi `credentials: 'include'`, zaman aşımı/iptal (`AbortSignal`), RFC 7807 problem detayları, 401 session-expired, 403 scope ihlali, 429 rate limit (retryAfter) ve bozuk yanıt korumasıyla oluşturuldu.
- [x] **CUST-02 Dijital Menü Arayüzü ve Bileşenleri:**
  - Mobil öncelikli, 44px dokunma hedeflerine uygun `DigitalMenuView`.
  - Şube ve masa bilgisi gösterimi, masa overview ve dijital menü arasında gezinme.
  - Yapışkan/yatay kaydırılabilir kategori çubuğu (`MenuCategoryBar`).
  - Türkçe karakter normalizasyonlu (`ı`/`i`, `ş`/`s`, `ç`/`c`, `ğ`/`g`, `ö`/`o`, `ü`/`u`) büyük arama alanı (`MenuSearchAndFilterBar`).
  - Diyet tercihleri (Vegan, Vejetaryen, Glutensiz vb.) ve kaçınılacak alerjenler bottom sheet'i (`MenuFilterBottomSheet`).
  - Alerjen içeren ürünlerde belirgin uyarı (`⚠️ [Alerjen] içerir`), doğrulanmamış sağlık iddialarından kaçınma ve yasal bilgilendirme notu.
  - Ürün kartı (`MenuItemCard`): Güvenli görsel veya fallback, isim, kısa açıklama, para birimi formatlama, başlangıç fiyatı, acı seviyesi, 86 stokta yok ve tahmini kullanılabilirlik zamanı.
  - Ürün detay önizleme sayfası (`MenuItemDetailSheet`).
  - Mobil alt gezinme çubuğu (`customer-bottom-nav`) ile Menü, Garson Çağır ve Masa Bilgisi kısayolları.
- [x] **Güvenlik ve Veri Sanitizasyonu:**
  - `imageUrl` için yalnızca `https://` ve güvenli göreli (`/images/...`) yollar kabul edilir; `javascript:`, `data:`, `//protocol-relative` kesinlikle engellenir.
  - Açıklamalarda `dangerouslySetInnerHTML` kullanılmaz; tüm metinler standart React text rendering ile basılır.
  - Şube erişim güvenliği backend tarafında doğrulanmış claim'lerle garanti altındadır.
- [x] **Kapsamlı Testler:**
  - 74 frontend birim testi (arama, kategori filtreleri, alerjen uyarıları, stok tükenme durumları, boş menü, API 401/403/429/500/503 hataları, XSS koruması, klavye erişilebilirliği).

### Faz 7.2: Ürün Özelleştirme (CUST-03)
- [x] **Ortak Sözleşmeler (`packages/contracts`):**
  - `DraftCartLine` modeli (`menuItemId`, `variantId`, `selectedModifierOptionIds`, `customerNote`, `quantity`, `unitPriceMinorUnits`, `lineTotalMinorUnits`, `currency`, `configurationSignature`).
  - `CUSTOMER_NOTE_MAX_LENGTH = 200` sabiti.
- [x] **Saf Fiyat Hesaplama ve Doğrulama (`customizationUtils.ts`):**
  - Integer minor units bazlı saf hesaplama (`unitPrice = variant.absolutePrice || basePrice + sum(deltas)`, `lineTotal = unitPrice * quantity`).
  - Float ve güvensiz matematiksel işlemler kesin olarak engellendi; sayısal aşım ve negatif değer koruması sağlandı.
  - Deterministik konfigürasyon imzası (`generateConfigurationSignature`) üretimi.
  - Müşteri notu normalizasyonu (trim, kontrol karakterlerini temizleme, salt boşluğu null yapma, asla loglamama ve raw HTML basmama).
- [x] **CUST-03 Ürün Özelleştirme Bileşenleri:**
  - `ItemCustomizationSheet`: Mobilde bottom sheet, geniş ekranda modal/dialog şeklinde açılan, focus trap ve escape/backdrop destekli kapsayıcı.
  - `VariantSelector`: Erişilebilir radio grubu, stokta olmayan varyantın seçilememesi, geçerli default varyantın otomatik seçilmesi.
  - `ModifierGroupSection`: Tekli (radio) / çoklu (checkbox) seçim, min/max sınırları, sınır aşımında sessiz silme yerine belirgin hata uyarısı, fiyat deltası gösterimi.
  - `QuantitySelector`: 44px minimum dokunma hedefli artırma/azaltma butonları (1-99 sınırları).
  - `CustomerNoteInput`: Canlı karakter sayaçlı ve sanitizasyonlu metin alanı.
  - `PricePreviewBar`: Dinamik birim/toplam fiyat gösterimi, Faz 7 önizleme uyarısı ve "Sepete Ekle" CTA butonu.
- [x] **Doğrulama ve Testler:**
  - `customizationUtils.test.ts` (21 test) ve `ItemCustomizationSheet.test.tsx` (13 test) ile tüm senaryolar (varyantsız ürün, stokta olmayan default, required modifier, min/max, overflow, imza, state reset, klavye) doğrulandı.

### Faz 7.3: Sepet ve Fiyat Önizleme (CUST-04)
- [x] **Sepet Durum Yönetimi (`apps/customer-web/src/cart/cartReducer.ts`):**
  - İmmutable cart reducer: ürün ekleme, aynı konfigürasyon imzasında miktar birleştirme (1-99 sınırları), farklı özelleştirmeleri ayrı satırda tutma, miktar güncelleme, satır silme ve tüm sepeti temizleme.
  - Ara toplam (`subtotalMinorUnits`) ve toplam ürün adedi hesaplaması, integer taşma koruması.
- [x] **Güvenli Draft Persistence (`apps/customer-web/src/cart/cartStorage.ts`):**
  - Hiçbir auth token, QR token veya hassas veri storage'a yazılmaz.
  - Yalnızca `sessionStorage` içinde güvenli taslak tutulur.
  - Aktif `sessionId` bağlamı: oturum değiştiğinde, kapandığında veya bozulduğunda taslak otomatik temizlenir.
  - Şema sürüm doğrulaması (`CART_STORAGE_SCHEMA_VERSION = 1`) ve hataya karşı korumalı bellek içi (memory-only) çalışma.
- [x] **Menü Güncelliği ve Fiyat Değişimi Doğrulaması (`cartValidator.ts`):**
  - Sepet açıldığında canlı runtime menu ile karşılaştırma: kaldırılan ürün, 86 olan ürün/varyant, kaldırılan değiştirici ve değişen birim fiyat tespiti.
  - Faz 8'e kadar fiyatları sessizce değiştirmeme; müşteriye açık uyarı gösterme.
- [x] **CUST-04 Sepet Arayüzü (`CartSheet.tsx`, `CartItemRow.tsx`):**
  - Mobil öncelikli bottom sheet, sticky ara toplam ve ürün özetleri.
  - Adet değiştirme stepper'ı, onaylı silme ve boş sepet görünümü.
  - Faz 8 Sınırı: "Siparişi Gönder" butonu devredışı bırakılmış ve açık ürün diliyle bilgilendirme eklenmiştir; sahte sipariş ID'si veya uydurma mock order API'si kesinlikle kullanılmamıştır.
- [x] **Kapsamlı Testler:**
  - `cartReducerAndStorage.test.ts` (15 test) ve `CartSheetAndValidator.test.tsx` (9 test) ile tüm senaryolar doğrulandı.

### Faz 7.4: Servis Talepleri (CUST-06)
- [x] **ServiceRequest Alan Modeli ve Güvenlik:**
  - Yeni `ServiceRequest` aggregate root oluşturuldu (`Id`, `TenantId`, `BranchId`, `DiningSessionId`, `TableId`, `RequestType`, `Status`, `Note`, `IdempotencyKey`, `RequestFingerprint`, `CreatedAtUtc`, `AcknowledgedAtUtc`, `CompletedAtUtc`, `ConcurrencyToken`).
  - Talep türleri: `Waiter` (1), `Water` (2), `Napkin` (3), `Assistance` (4).
  - Durumlar: Faz 7 için `Open` (1) ve `Cancelled` (2). `Acknowledged` (3) ve `Completed` (4) gelecek operasyon fazları için belgelendi, sahte durum uygulanmadı.
  - Not sanitizasyonu: HTML karakterleri (`<`, `>`) ve kontrol karakterleri temizlenir, maksimum 200 karakter.
  - Deterministik SHA256 yük parmak izi (`ComputeFingerprint`) ile `Idempotency-Key` çakışma (409) tespiti.
- [x] **Veritabanı, RLS ve Bütünlük Kuralları:**
  - `tenancy.service_requests` tablosu, `restaurant_tables` ve `dining_sessions` tablolarına composite foreign key'ler.
  - PostgreSQL Row Level Security (RLS) etkinleştirildi ve `FORCE ROW LEVEL SECURITY` uygulandı; tenant izolasyon politikası ve `restaurant_app_runtime` rol izinleri eklendi.
  - Session/type bazında tek açık talep koruması: `ix_service_requests_tenant_session_type_open` partial unique index (`WHERE status = 1`).
  - Oturum dışından tenant/branch/table ID kabul edilmez; tüm veriler doğrulanmış müşteri JWT'sinden türetilir.
  - Kapalı, birleştirilmiş veya hesap istenmiş (`BillRequested`) oturumlarda yeni servis talepleri reddedilir.
  - Oturum bazlı kayan pencere ve 15 saniyelik cooldown rate limiter (`InMemoryServiceRequestRateLimiter`).
- [x] **Müşteri API Uç Noktaları:**
  - `POST /api/v1/customer/service-requests`: Zorunlu `Idempotency-Key` başlığı ve CSRF koruması. Aynı payload tekrarında mevcut kaydı idempotent döner; farklı payload'da 409 Conflict döner; açık talep varsa güvenle döner.
  - `GET /api/v1/customer/service-requests/open`: Aktif oturuma ait açık servis taleplerini listeler.
  - `DELETE /api/v1/customer/service-requests/{requestId}`: Kendi açık talebini iptal eder. Başkasına ait veya geçersiz ID için bilgi sızdırmayan 404 döner.
  - Hesap İste akışı: Ayrı aggregate oluşturulmadı; mevcut `POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/request-bill` uç noktası own-session kapsamı, concurrency token ve CSRF ile kullanıldı.
  - Güvenlik denetim olayları (`CustomerServiceRequestCreated`, `CustomerServiceRequestCancelled`).
- [x] **CUST-06 Müşteri Arayüzü (`ServiceRequestSheet`, `ServiceActionCard`):**
  - Modern bottom sheet: Garson Çağır, Su İste, Peçete İste, Yardım İste, Hesap İste.
  - Her eylemde: açıklama, onay kutusu, gönderiliyor durumu, başarı durumu, açık talep badge'i, iptal etme, retry ve rate limit countdown sayacı.
  - Çift tıklama ve ağ gecikmesi koruması (client-side in-flight guard + idempotency key).
  - Canlı bildirim sınırı: Talepler veritabanında kalıcı olarak saklanır; personele canlı SignalR/FCM dağıtımının Faz 9/10'da bağlanacağı açıkça belirtildi, sahte canlı bildirim iddia edilmedi.
- [x] **Kapsamlı Testler:**
  - Domain, rate limiter ve uygulama birim testleri (22 test).
  - Gerçek PostgreSQL ve RLS rolüyle entegrasyon testleri (başarılı request, idempotency tekrarı, 409 çakışması, session/type tek açık kayıt, iptal akışı, yabancı talep 404, hesap isteme yaşam döngüsü).
  - Frontend birim ve bileşen testleri (`customerServiceApi.test.ts`, `ServiceRequestSheet.test.tsx`, `App.test.tsx`).



