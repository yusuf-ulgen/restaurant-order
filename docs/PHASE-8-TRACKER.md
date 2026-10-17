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
| **Faz 8.1** | Order Domain Modeli ve Persistence | **Başlanmadı** | `Order`, `OrderItem`, `OrderItemModifier` aggregate, value object'ler, EF Core yapılandırması, RLS ve migrasyon | `feat/phase-08-order-core` |
| **Faz 8.2** | Yetkili Fiyatlandırma ve Katalog Doğrulama | **Başlanmadı** | Sunucu otoriter `OrderPricingEngine`, menü/ürün/varyant/modifier aktiflik kontrolleri, Hızlı-86 stok doğrulaması | `feat/phase-08-order-core` |
| **Faz 8.3** | Müşteri Sipariş API'si ve Eşzamanlılık | **Başlanmadı** | `POST /api/v1/customer/orders`, `Idempotency-Key`, PostgreSQL advisory lock eşzamanlılık koruması, oturum durumu denetimi | `feat/phase-08-order-core` |
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
- [ ] **Order Domain Aggregate:**
  - `Order`, `OrderItem`, `OrderItemModifier` varlıkları ve kimlik sınıfları (`OrderId`, `OrderItemId`).
  - İntihar/silme korumalı, değişmez (immutable) satır yapısı.
  - Durum makinesi: `Draft` -> `Submitted` -> `InPreparation` -> `Ready` -> `Served` (ve `Cancelled`).
  - Para birimi ve tam sayı alt para birimi (`minorUnits`) fiyat saklama.
- [ ] **Veritabanı ve PostgreSQL RLS:**
  - `tenancy.orders`, `tenancy.order_items`, `tenancy.order_item_modifiers` tabloları.
  - `tenant_id`, `branch_id`, `dining_session_id`, `table_id` composite foreign key'ler.
  - PostgreSQL Row Level Security (RLS) politikaları ve `FORCE ROW LEVEL SECURITY`.
  - Idempotency ve oturum bazlı partial index'ler.

### Faz 8.2: Yetkili Fiyatlandırma ve Katalog Doğrulama
- [ ] **OrderPricingEngine:**
  - İstemciden gelen taslak fiyatlarını kesinlikle kabul etmeyen, sunucu tarafında otoriter fiyat hesaplama.
  - Aktif menü, kategori, ürün, varyant ve modifier seçeneklerinin doğrulanması.
  - Min/max seçim sınırlarının zorunlu denetimi.
  - Şube Hızlı-86 (stokta yok) durumundaki ürün ve varyantların sipariş edilmesini engelleyen guardrails.
  - Şube vergi ve servis ücreti oranlarının hesaplanması.

### Faz 8.3: Müşteri Sipariş API'si ve Eşzamanlılık
- [ ] **POST /api/v1/customer/orders:**
  - Customer rolü ve oturum claim kapsamı zorunluluğu (`tenantId`, `branchId`, `tableSessionId`).
  - Double-submit CSRF koruması ve zorunlu `Idempotency-Key` başlığı.
  - Eşzamanlı istek yarış durumlarını önlemek için PostgreSQL advisory lock (`pg_advisory_xact_lock`).
  - Yalnızca `Active` durumdaki yemek oturumlarında sipariş kabulü (`Open`, `BillRequested` veya `Closed` durumlarında ret).
  - Rate limiting koruması.

### Faz 8.4: Müşteri Sipariş Gönderme Arayüzü
- [ ] **Müşteri Arayüzü (`CartSheet`, `ActiveSessionView`):**
  - "Siparişi Gönder" eylemi, sipariş onay penceresi ve özet görünümü.
  - Çift tıklama engeli (client-side in-flight guard).
  - Başarılı sipariş sonrası istemci sepet taslağının (`sessionStorage`) güvenle temizlenmesi.
  - Ağ hatası veya yetkili fiyat değişikliğinde anlaşılır hata bildirimi ve sepetin korunması.

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
