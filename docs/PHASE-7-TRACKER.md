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
