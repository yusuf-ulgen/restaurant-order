# Faz 8 Uygulama Takip Belgesi (`docs/PHASE-8-TRACKER.md`)

Bu belge, `restaurant-order` projesinde **Faz 8: Sipariş Çekirdeği (Order Aggregate, Yetkili Fiyatlandırma ve Sipariş Gönderimi)** aşamasının tüm alt aşamalarını, mimari kararlarını ve uygulama durumunu takip eder.

> **Kapsam Sınırları ve Mimari İlkeler:**
> - **Sipariş Durum Sınırları:** `Order` ve `OrderItem` varlıkları yalnızca `Draft`, `Submitted`, `InPreparation`, `Ready`, `Served` ve `Cancelled` durumlarını izler.
> - **KDS ve İstasyon Fişi Ayrımı:** Hazırlık fişinin istasyon tarafından kabul edilmesi (`Acknowledged`), gelecekteki `StationTicket` (KDS) alanına aittir; `Order` durumlarında yer almaz.
> - **Adisyon ve Ödeme Ayrımı:** `Paid` bir sipariş durumu değildir; ilerideki `Bill` (Adisyon/Hesap) durumudur. `Closed`, `DiningSession` ve `Bill` yaşam döngüsüne aittir.
> - **Gelecek Faz Sınırları:** Faz 8'de KDS hazırlık ekranları ve yönlendirme (Faz 11), SignalR canlı bildirimleri (Faz 9), fiş yazdırma (Faz 12), adisyon/hesap motoru (Faz 13) ve ödeme/bahşiş/komisyon (Faz 16) yapılmayacaktır.
> - **Sahte Üretim Davranışı Yok:** Sipariş gönderildiğinde veritabanında gerçek, denetlenebilir `Order`, `OrderItem` ve `OrderItemModifier` kayıtları oluşacaktır. Sahte başarılı sipariş veya mock üretim API'si yer alamaz.

---

## 1. Alt Aşama Durum Özeti

| Alt Aşama | Başlık | Durum | Temel Çıktı | Commit / PR |
| :--- | :--- | :--- | :--- | :--- |
| **Faz 8.0** | Hazırlık ve Kapsam | **Tamamlandı** | Yol haritası, durum makineleri ve alan modeli hizalaması, takip belgesi, GitHub Issue #18 ve dal hazırlığı | `feat/phase-08-order-core` |
| **Faz 8.1** | Order Domain Modeli ve Persistence | **Tamamlandı** | `Order`, `OrderItem`, `OrderItemModifier`, `OrderAuditEntry` aggregate, EF Core yapılandırması, RLS, immutable trigger ve testler | `feat/phase-08-order-core` |
| **Faz 8.2** | Yetkili Fiyatlandırma ve Katalog Doğrulama | **Tamamlandı** | Sunucu otoriter `OrderPricingEngine`, minor-unit deterministik finans motoru, aktif katalog doğrulama, Hızlı-86 ve istemci fiyat değişikliği atomik koruması | `feat/phase-08-order-core` |
| **Faz 8.3** | Müşteri Sipariş API'si ve Eşzamanlılık | **Tamamlandı** | `POST /api/v1/customer/orders`, `GET /api/v1/customer/orders`, `GET /api/v1/customer/orders/{orderId}`, `Idempotency-Key` replay & conflict, PostgreSQL advisory lock eşzamanlılık koruması, oturum durumu denetimi, RFC 7807 hata yanıtları, audit not redaction | `feat/phase-08-order-core` |
| **Faz 8.4** | Müşteri Sipariş Gönderme Arayüzü | **Başlanmadı** | `CartSheet` onaylama akışı, sipariş gönderimi, yüklenme/hata durumları, sepet temizliği ve sipariş özeti görünümü | `feat/phase-08-order-core` |
| **Faz 8.5** | Personel Sipariş API'si ve Yaşam Döngüsü Altyapısı | **Başlanmadı** | Personel sipariş oluşturma, masa siparişleri sorgulama ve sipariş durumu geçiş altyapısı | `feat/phase-08-order-core` |
| **Faz 8.6** | Negatif Akışlar, E2E, Güvenlik ve Kapanış | **Başlanmadı** | 15 adımlı E2E yolculuğunun gerçek sipariş gönderimiyle birleşimi, negatif güvenlik testleri, kapı kontrolleri ve kapanış | `feat/phase-08-order-core` |

---

## 2. Ayrıntılı Aşama Teslimatları

### Faz 8.0: Hazırlık ve Kapsam
- [x] **Faz 8 Başlangıcı ve Yol Haritası Hizalaması:**
  - `feat/phase-08-order-core` dalı `main` (`aaa906d`) üzerinden açıldı.
  - GitHub Issue [#18](https://github.com/yusuf-ulgen/restaurant-order/issues/18) oluşturuldu.
  - `docs/PHASE-8-TRACKER.md` oluşturuldu; kapsam sınırları ve ilkeler belirlendi.
  - `docs/CURRENT-STATE.md`, `docs/ROADMAP.md`, `docs/STATE-MACHINES.md`, `docs/DOMAIN.md` ve `docs/SCREEN-INVENTORY.md` sipariş durum sınırlarıyla hizalandı.

### Faz 8.1: Order Domain Modeli ve Persistence
- [x] **Order Domain Aggregate:**
  - `Order`, `OrderItem`, `OrderItemModifier`, `OrderAuditEntry` modelleri ve strongly-typed kimlik sınıfları (`OrderId`, `OrderItemId`).
  - Değişmez (immutable) snapshot yapısı: `MenuItemId`, `ItemVariantId`, adlar, birim fiyat, modifier delta toplamı, satır toplamı ve vergi snapshot'ı.
  - Durum makinesi: `Draft` -> `Submitted` -> `InPreparation` -> `Ready` -> `Served` (ve yalnızca yetkili personel için `Cancelled`). Müşteri doğrudan sipariş iptal edemez.
  - `long` minor-unit ve `checked` taşma koruması ile negatif/taşan parasal tutarlar engellendi.
  - Müşteri notu sanitizasyonu: HTML enjeksiyonu ve kontrol karakterleri engellendi (max 500 karakter).
  - Boş sipariş gönderimi ve 1–99 harici miktarlar domain seviyesinde engellendi.
- [x] **Veritabanı ve PostgreSQL RLS:**
  - `tenancy.orders`, `tenancy.order_items`, `tenancy.order_item_modifiers`, `tenancy.order_audit_entries` tabloları.
  - Unique bileşik indeksler: `(tenant_id, dining_session_id, round_number)` ve `(tenant_id, idempotency_key)`.
  - Composite foreign key'ler ve katalog geçmişi koruması (`OnDelete(DeleteBehavior.Restrict)`).
  - PostgreSQL Row Level Security (RLS) politikaları ve `FORCE ROW LEVEL SECURITY`.
  - `order_audit_entries` üzerinde UPDATE ve DELETE işlemlerini kesin olarak engelleyen PostgreSQL trigger fonksiyonu (`prevent_order_audit_update_delete`).
  - EF Core Global Query Filter secondary defense-in-depth katmanı.
  - Blue/Green geriye dönük uyumlu `AddOrdersAndOrderPersistence` genişletici migration'ı oluşturuldu.

### Faz 8.2: Yetkili Fiyatlandırma ve Katalog Doğrulama
- [x] **OrderPricingEngine & Minor-Unit Finans Motoru:**
  - İstemciden gelen ad, birim fiyat, satır toplamı, vergi, servis ücreti ve genel toplam değerleri güvenilir kabul edilmez; sunucu tarafında aktif katalog ve şube ayarlarından otoriter olarak hesaplanır.
  - `OrderPricingCalculator`: Saf minor-unit (`long`) ve basis point (`int`, bps) tabanlı deterministik Half-Up yuvarlama (`(numerator + denominator / 2) / denominator`).
  - `PricesIncludeTax = true` durumunda vergi dahil ayrıştırma, `false` durumunda vergi ekleme formülü.
  - Servis ücreti (`ServiceChargeRateBps`) vergi sonrası ürün brüt toplamına uygulanır.
  - Tek transaction içinde tam doğrulama:
    - `DiningSession` aktifliği ve işletme/şube mülkiyeti (`SESSION_NOT_ACTIVE`).
    - `BranchSettings.IsOrderTakingEnabled` sipariş alım kapısı (`ORDER_TAKING_DISABLED`).
    - Aktif menü, aktif kategori ve aktif ürün kontrolü (`ITEM_NOT_ACTIVE`).
    - Şube Hızlı-86 (stokta yok) ürün ve varyant engeli (`ITEM_OUT_OF_STOCK`).
    - Varyantın ilgili ürüne ait olduğunu doğrulama (`INVALID_VARIANT`).
    - Ürüne atanmış modifier grupları, min/max seçim sınırları ve seçenek aktifliği kontrolü (`INVALID_MODIFIER_SELECTION`).
    - Yinelenen modifier kimliği seçimi engeli (`DUPLICATE_MODIFIER`).
    - Şube para birimi ile istemci uyuşmazlığı denetimi (`CURRENCY_MISMATCH`).
  - Fiyat değişikliği koruması (`ORDER_PRICE_CHANGED`): İstemcinin beklediği birim fiyat yetkili fiyattan farklıysa sipariş atomik olarak reddedilir; etkilenen satır, beklenen fiyat ve güncel fiyat hata nesnesinde güvenle taşınır.
  - Katalog fiyatı siparişten sonra değişse bile siparişe ait finansal snapshot'ın değişmezliği garantilendi.

### Faz 8.3: Müşteri Sipariş API'si ve Eşzamanlılık
- [x] **POST /api/v1/customer/orders:**
  - Customer rolü ve oturum claim kapsamı zorunluluğu (`tenantId`, `branchId`, `tableSessionId`). Request body içinden tenant/branch/session ID kabul edilmez.
  - Double-submit CSRF koruması (`CsrfValidationMiddleware`) ve zorunlu `Idempotency-Key` başlığı.
  - Eşzamanlı istek yarış durumlarını önlemek için PostgreSQL advisory transaction lock (`pg_advisory_xact_lock(hashtext('order_session_' || sessionId))`).
  - Round number artışı ve benzersizliği: Aynı oturumdan gelen eşzamanlı istekler sırayla `1`, `2` şeklinde benzersiz atanır; siparişler birbirini ezmez.
  - Idempotency fingerprinting: Aynı key + aynı payload replay (`200 OK` veya `201 Created`), mükerrer kayıt oluşmaz. Aynı key + farklı payload ise `409 Conflict` (`IDEMPOTENCY_CONFLICT`) döner.
  - Yalnızca `Active` durumdaki yemek oturumlarında sipariş kabulü (`Open`, `BillRequested`, `Closed`, `Merged` durumlarında ret, `SESSION_NOT_ACTIVE`).
  - Fiyat motoru entegrasyonu: Sipariş doğrudan `Submitted` durumunda kalıcılaşır; sahte `Draft` kaydı üretilmez.
  - RFC 7807 standart hata yanıtları ve correlation ID desteği (`ITEM_OUT_OF_STOCK`, `ORDER_PRICE_CHANGED`, `INVALID_MODIFIER_SELECTION`, `ORDER_TAKING_DISABLED`, `SESSION_NOT_ACTIVE`, `IDEMPOTENCY_CONFLICT`, `DISTRIBUTED_SECURITY_STATE_UNAVAILABLE`).
  - Güvenlik audit kaydı: Sipariş oluşturma olayı değişmez `OrderAuditEntry` üretir. Müşteri serbest metin notu audit günlüğünden tamamen maskelenir (`hasNotes` güvenli boolean metadata saklanır).
- [x] **GET /api/v1/customer/orders & GET /api/v1/customer/orders/{orderId}:**
  - Müşteri yalnızca kendi aktif oturumuna ait siparişleri görebilir.
  - Farklı oturuma ait sipariş istendiğinde bilgi sızdırmadan `404 Not Found` döner.
  - ETag / Concurrency Token başlığı döner.
  - Finansal snapshot ve kalem detayları (modifierlar, fiyatlar, toplamlar) eksiksiz sunulur.

### Faz 8.4: Müşteri Sipariş Gönderme Deneyimi
- [x] **Contracts ve API Client (`packages/contracts`, `apps/customer-web`):**
  - Sipariş sözleşmeleri modüler `packages/contracts/src/customer-orders.ts` içine ayrıldı ve dışa aktarıldı.
  - Backend C# (`CustomerOrderContractDriftTests.cs`) ve frontend TS (`customerOrderContract.test.ts`) çift yönlü drift testleri eklendi.
  - `apps/customer-web/src/api/customerOrderApi.ts`: `submitOrder`, `getOrders`, `getOrderById` endpoint client'ları oluşturuldu; CSRF ve `Idempotency-Key` başlıkları bağlandı.
  - Problem Details hata kodları kullanıcı dostu Türkçe hata mesajlarına dönüştürüldü (`mapOrderProblemToUserMessage`). Teknik GUID'ler arayüzden gizlendi.
- [x] **Sepet Onay ve Sipariş Akışı:**
  - `OrderConfirmationSheet.tsx`: Ürünler, varyantlar, modifier adları, müşteri notları, miktarlar, ara toplam, vergi, servis ücreti ve genel toplam dökümü.
  - `OrderSuccessSheet.tsx`: Sipariş numarası / round number (`Tur #1`) ve `Submitted` ("Mutfak Onayı Bekliyor") durum rozeti.
  - Yalnızca backend `201 Created` / `200 OK` yanıt verdikten sonra başarı ekranı gösterilir ve sepet taslağı (`sessionStorage`) temizlenir.
- [x] **Idempotency, Çift Tıklama ve Tekrar Deneme Güvenliği:**
  - Ağ tekrarlarında aynı payload için oluşturulan `Idempotency-Key` korunur (`cartSignature` tabanlı stabil key).
  - Buton in-flight durumunda devre dışı bırakılarak çift tıklama ile mükerrer sipariş oluşumu engellenir.
  - Ağ hatasında veya doğrulama başarısızlığında sepet kesinlikle korunur, temizlenmez.
  - Başarılı replay response'u (`200 OK`) gerçek başarı olarak işlenir, ikinci sipariş gösterilmez.
- [x] **Negatif Akışlar ve Erişilebilirlik:**
  - `ORDER_PRICE_CHANGED`: Güncel fiyatlar bildirilir, kullanıcının yeniden onayı istenir.
  - `ITEM_OUT_OF_STOCK`: Tükendiği bildirilen ürün işaretlenir, diğer ürünler korunur.
  - `INVALID_MODIFIER_SELECTION`: İlgili ürünün düzenlenmesi uyarısı verilir.
  - `ORDER_TAKING_DISABLED`: Sipariş gönderimi kapatılır, menü inceleme korunur.
  - `SESSION_NOT_ACTIVE`: Oturumun kapandığı bildirilir.
  - 503 / ağ kesintisinde sahte başarı gösterilmez, sepet korunur.
  - Ekran okuyucu uyumluluğu için `role="alert"` ve otomatik hata odağı (`focus()`) uygulandı.
- [x] **Sipariş Geçmişi Görünümü (`OrderHistorySheet.tsx`):**
  - Aktif oturumdaki turları listeleyen temel görünüm; manuel "🔄 Yenile" butonu eklendi.
  - Round number, oluşturulma zamanı, ürün detayları ve durum rozetleri gösterilir; Phase 9 SignalR canlı KDS bilgisi not düşüldü.
- [x] **Testler:**
  - Birim, bileşen ve API testleri (`OrderSubmission.test.tsx`, `OrderNegativeAndHistory.test.tsx`, `customerOrderApi.test.ts`, `customerOrderContract.test.ts`).
  - Playwright E2E akışı (`tests/e2e/tests/customer-journey.spec.ts` 10b) gerçek sipariş gönderimi ve doğrulamasıyla güncellendi.

### Faz 8.5: Personel Sipariş API'si ve Yaşam Döngüsü Altyapısı
- [ ] **Personel Sipariş Uç Noktaları:**
  - Garson ve kasiyer rolleri için masa oturumuna sipariş ekleme uç noktaları.
  - Masa oturumuna ait siparişleri ve durumlarını listeleme (`GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders`).
  - Sipariş ve kalem durumu geçişleri (`CancelItem`, `BumpStatus`).

### Faz 8.6: Negatif Akışlar, E2E, Güvenlik ve Kapanış
- [ ] **Test ve Doğrulama:**
  - Domain ve Pricing Engine birim testleri.
  - Testcontainers PostgreSQL ve RLS rolü ile entegrasyon testleri (fiyat uyuşmazlığı, stokta olmayan ürün, kapalı oturum, eşzamanlı istekler).
  - 15 adımlı Playwright E2E müşteri yolculuğunun gerçek sipariş gönderimi ve veritabanı kaydı doğrulamasıyla genişletilmesi.
  - Kalite kapıları (Gate 1–6) doğrulamaları ve Faz 8 kapanış raporu.
