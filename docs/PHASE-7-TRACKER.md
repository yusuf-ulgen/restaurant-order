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
| **Faz 7.0** | Temel ve Müşteri Oturum Kurtarma | **Devam Ediyor** | `GET /api/v1/customer/session`, state makinesi, oturum kurtarma, kontratlar ve testler | `feat/phase-07-customer-experience` |
| **Faz 7.1** | Menü Gezinme ve Filtreler | **Planlandı** | Kategori gezinme, diyet/alerjen filtreleme, ürün listeleme ve arama | — |
| **Faz 7.2** | Ürün Özelleştirme | **Planlandı** | Varyant seçimi, değiştirici grupları, zorunlu/seçimli kural kontrolleri | — |
| **Faz 7.3** | Sepet ve Fiyat Önizleme | **Planlandı** | İstemci sepet state'i, yerel fiyat önizleme, vergi dökümü (yalnızca önizleme) | — |
| **Faz 7.4** | Servis Talepleri | **Planlandı** | Garson çağır, su/peçete iste, hesap talebi arayüzü | — |
| **Faz 7.5** | Sertleştirme ve Kapanış | **Planlandı** | Hata akışları, erişilebilirlik, uçtan uca doğrulamalar, güvenlik denetimi | — |

---

## 2. Ayrıntılı Aşama Teslimatları

### Faz 7.0: Temel ve Müşteri Oturum Kurtarma
- [x] **Faz 7 Başlangıcı ve Yol Haritası Hizalaması:**
  - `feat/phase-07-customer-experience` dalı `main` üzerinden açıldı.
  - `docs/PHASE-7-TRACKER.md` oluşturuldu; kapsam sınırları ve ilkeler belirlendi.
  - `docs/CURRENT-STATE.md`, `docs/ROADMAP.md` ve `docs/TESTING.md` güncellendi.
- [ ] **Müşteri Mevcut Oturum Uç Noktası (`GET /api/v1/customer/session`):**
  - Yalnızca `Customer` rolü erişebilir.
  - Tenant, şube ve oturum bilgileri gövde veya sorgudan değil, doğrulanmış JWT claim'lerinden türetilir.
  - Oturumun aktifliği PostgreSQL veritabanı (source-of-truth) üzerinden doğrulanır.
  - Kapalı veya birleştirilmiş oturumlar için generic RFC 7807 401 Unauthorized ProblemDetails döner.
  - Güvenli metadata yanıtı (`sessionId`, `tenantId`, `branchId`, `tableId`, `tableNumber`, `tableName`, `sessionStatus`, `concurrencyToken`, `guestCount`, `openedAtUtc`).
  - Yanıtta veya istemci durumunda hiçbir şekilde erişim tokenı yer almaz.
- [ ] **Müşteri Web Uygulama State Mimarisi (`apps/customer-web`):**
  - Belirgin durum yapısı: `bootstrapping`, `unauthenticated/QR required`, `active session`, `expired session`, `loading`, `recoverable error`.
  - Sayfa yenilemede otomatik oturum kurtarma (HttpOnly çerez + `credentials: 'include'`).
  - Token'ın `localStorage` veya `sessionStorage`'a yazılmaması güvencesi.
  - Oturum sona erdiğinde oturuma bağlı durumları (sepet/taslak) temizleme kancası (`useCustomerSession`).
- [ ] **Doğrulama ve Testler:**
  - Birim, entegrasyon ve bileşen testleri.
