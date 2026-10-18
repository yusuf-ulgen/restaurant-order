# Sistem Mimarisi ve Alan Sınırları (`docs/ARCHITECTURE.md`)

## 1. Üst Düzey Mimari

`restaurant-order`, restoran salonu, mutfak ve yönetim arasında modülerlik, işletme yalıtımı ve düşük gecikmeli eşitleme sağlamak için alan odaklı tasarım (DDD) ilkelerini kullanır.

```text
İstemciler: QR müşteri | Garson/operasyon | Mutfak/bar KDS | Restoran yönetimi | Süper yönetim
                                      ↓
API ağ geçidi / BFF [Proposed]: kimlik doğrulama, işletme bağlamı, hız sınırı, giriş doğrulama
WebSocket / SSE canlı olay katmanı [Proposed / ADR Required]
                                      ↓
Modüler monolit: IAM/RBAC | Menü/katalog | Masa/salon | Sipariş/hazırlık fişi | Hesap/ödeme | Yazdırma
                                      ↓
PostgreSQL 16 + RLS [ACCEPTED] | Şube ağı ESC/POS yazıcı köprüsü [Proposed]
```

Şema başlangıç taslağıdır; aşağıdaki karar tablosunda belirtilen kabul edilmiş ADR'ler teknoloji seçimi için esastır. SignalR seçilmiştir; ayrıntılı hub tasarımı daha sonraki fazdadır.

## 2. Temel Mimari İlkeler

### 2.1. Alan Sınırlarına Göre Modülerlik

Modüller açık hizmet arayüzleri veya alan olayları üzerinden haberleşir:

1. **Kimlik ve erişim (IAM):** Kimlik doğrulama, rol atama ve şube bağlamı.
2. **Restoran yapılandırması:** Marka görünümü, gezinme düzeni, şube tema geçersiz kılmaları, finans/vergi/servis ücreti ayarları, çalışma saatleri, yemek alanları, hazırlık istasyonları ve özellik bayrakları.
3. **Katalog ve menü:** Menü hiyerarşisi, varyant fiyatlandırması, ek seçenek kuralları, 86/stokta yok yönetimi.
4. **Masa ve salon:** Alanlar, masalar, QR oturumu, masa birleştirme ve taşıma.
5. **Sipariş ve hazırlık fişi:** Sipariş yaşam döngüsü, fiş bölme ve KDS durum geçişleri.
6. **Hesap ve ödeme:** Hesap oluşturma, bölünmüş ödeme, bahşiş ve platform komisyon defteri.
7. **Donanım ve yazdırma:** ESC/POS biçimi, ağ soketi gönderimi, yazdırma kuyruğu ve yeniden deneme.
8. **Çok işletmeli yönetim:** İşletme kaydı, abonelikler ve platform ayarları.

### 2.2. Canlı Olay Eşitlemesi

- Müşteri/garson siparişi mutfak ekranına bir saniyeden kısa sürede iletilmelidir.
- Başlangıç taslağındaki WebSockets/SSE ve bellek içi/Redis pub/sub seçenekleri `[Proposed / ADR Required]` idi. Taşıma teknolojisi ADR-0001 ile SignalR olarak kabul edilmiştir; ayrıntılı dağıtım ADR-0004 kapsamındadır.
- Veri sızıntısını önlemek için akışlar `tenant_id` ve `branch_id` ile kesin ayrılır.

### 2.3. Kalıcılık ve İşletme Yalıtımı

- Finans, hesap ve durum makinelerinde ilişkisel bütünlük zorunludur.
- Paylaşılan veritabanında RLS, her sorguya `tenant_id` filtresi uygular `[ACCEPTED / ADR-0002]`.
- İşletme başına şema seçeneği ilk ürün için reddedilmiştir `[Rejected for MVP / ADR-0002]`.

### 2.4. Donanım ve Yazdırma

- Mutfak hazırlık fişleri ve müşteri hesapları fiziksel termal yazıcılara gönderilir.
- Protokol: Ethernet/Wi-Fi üzerinden TCP/IP ile ESC/POS.
- Yerel hafif yazdırma aracısı veya backend kuyruğu; soket bağlantısı, zaman aşımı ve kâğıt bitmesini yönetir `[Proposed / ADR Required]`.

## 3. Teknoloji Kararları

| Bileşen | Teknoloji | Durum | ADR |
| :--- | :--- | :--- | :--- |
| Monorepo ve paket yönetimi | `pnpm` çalışma alanları, tek kök kilit dosyası | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Backend mimarisi | Modüler monolit, temiz mimari | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Backend çalışma zamanı | .NET 10 ASP.NET Core | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Frontend | React 19, Vite, sıkı TypeScript, PWA önceliği | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Canlı iletişim | ASP.NET Core SignalR | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Önbellek ve canlı iletişim aracısı | Redis 7 | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Veritabanı | PostgreSQL 16, çok işletmeli RLS | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Arka plan işleme | API'den ayrı worker (`apps/worker`) | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Konteyner ve yerel geliştirme | Docker, Docker Compose | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| Kalıcılık kitaplığı | EF Core 10, Npgsql | `ACCEPTED` | [ADR-0002](./adr/0002-persistence-selection.md) |
| Termal yazıcı köprüsü | Yerel Node/Go soket hizmeti veya doğrudan IP | `[Proposed / ADR Required]` | `ADR-0005`, bekliyor |
| Ödeme ağ geçidi | Çok sağlayıcılı uyarlayıcı (Stripe, Iyzico vb.) | `[Proposed / ADR Required]` | `ADR-0006`, bekliyor |

## 4. Mimari Değişmez Kurallar

1. **İşletmeler arasında ortak değiştirilebilir durum yok:** Sorgular, önbellek anahtarları ve olay içerikleri açık işletme bağlamı taşır.
2. **Belirli durum geçişleri:** Sipariş, fiş ve ödemeler yalnızca doğrulanmış [durum makineleri](./STATE-MACHINES.md) üzerinden değişir.
3. **Finansal tekrar güvenliği:** Ödeme, hesap kapatma ve iadelerde çift tahsilatı önleyen idempotency anahtarları zorunludur.
4. **Donanım yalıtımı:** Yazıcı arızası KDS ilerlemesini veya sipariş oluşturmayı engelleyemez.

## Faz 5 Katalog Sınırı

Katalog uçları `/api/v1/catalog/branches/{branchId}` altındadır. Uygulama hizmetleri işletme, şube, rol, yaşam döngüsü ve eşzamanlılığı denetler; PostgreSQL bileşik yabancı anahtarlar ve zorunlu RLS kalıcılık yalıtımını sağlar. Değişiklikler aynı iş biriminde denetim olayları üretir. ETag eşzamanlılık tokenını taşır: eksik önkoşul `412`, eski sürümle yazma `409` döndürür. Çalışma zamanı menü görünümü yalnızca etkin menü, kategori, ürün, varyant ve ek seçenekleri içerir; yönetim/denetim alanlarını içermez. Bulunabilirlik olayları başarılı kayıttan sonra gönderilir; SignalR teslimi ve sipariş kesinleştirme yarışları sonraki fazlardadır.

## Faz 6 Masa, Kat Planı ve QR Sınırı

Masa ve kat planı yönetim uçları `/api/v1/floor/branches/{branchId}` altındadır (`/tables`, `/areas`, `/layout`, `/sessions/{sessionId}/transfer`, `/sessions/{sourceSessionId}/merge`). Yönetimsel değişiklikler şube/işletme RBAC (`branch.floor.manage`, `floor.sessions.manage`), ETag iyimser eşzamanlılık (`If-Match`) ve denetim izini zorunlu kılar. Genel QR etkileşimleri `/api/v1/qr/resolve` ve `/api/v1/qr/exchange` uçlarını kullanır; genel tarama çözünürlüğü 128-bit `public_code` ve HMAC-SHA256 imzaları ile dahili masa kimliklerinden (ID) tamamen ayrıştırılır. Kriptografik doğrulama sabit zamanlı karşılaştırma (`FixedTimeEquals`) kullanır, çoklu anahtarlı kesintisiz rotasyonu (`kid`) destekler ve monotonik sürüm iptalini (`qv`) zorunlu kılar. Müşteri oturum takası `HttpOnly`, `Secure`, `SameSite=Lax` çerezlerinde saklanan kapsamlı belirteçler üretir; oturum kapandığında veya birleştirildiğinde Redis önbelleğini anında temizleyen `ICustomerSessionValidator` ile doğrulanır. Masa transferi ve oturum birleştirme (Faz 6.4), masalar ve oturumlar seviyesinde değişmez hareket geçmişi (`DiningSessionMovement`), deterministik artan UUID kilitleme sırasıyla deadlock önleme ve ETag doğrulaması ile tamamlanmıştır.

## Faz 7 Müşteri Deneyimi ve Hizmet Talebi Sınırı

Müşteri menü ve servis talebi uçları `/api/v1/customer` altındadır (`/menu`, `/categories`, `/items/{id}`, `/service-requests`). Müşteri istekleri HttpOnly çerezinde taşınan güvenli JWT oturumu üzerinden `tenant_id`, `branch_id`, `dining_session_id` ve `table_id` bağlamını otomatik çözer; istemciden gelen güvenilmeyen parametreler kabul edilmez. Masa oturumu doğrulaması Redis önbelleği ve PostgreSQL RLS ile çift katmanlı güvenceye alınmıştır.

## Faz 8 Sipariş Çekirdeği ve Yetkili Fiyatlandırma Sınırı

Sipariş yönetimi uçları müşteri için `/api/v1/customer/orders` ve personel için `/api/v1/staff/branches/{branchId}/orders` altında yer alır:

- **Yetkili Fiyatlandırma Motoru (`IAuthoritativePricingEngine`):** İstemciden gelen hiçbir fiyat, vergi, servis ücreti veya toplam değeri kabul edilmez. Sunucu tarafında aktif şube mali ayarları, menü kataloğu, aktif modifier grupları ve seçenekleri üzerinden tek veritabanı işleminde hesaplanır.
- **İdempotency ve Deterministik Parmak İzi:** Müşteri siparişlerinde `Idempotency-Key` başlığı zorunludur. İstek gövdesi kanonik SHA-256 parmak izi ile doğrulanır; aynı anahtar aynı yük ile tekrarlandığında kayıt yinelenmeden mevcut sipariş döner (`200 OK` / `201 Created`). Aynı anahtarla farklı payload gönderilirse `409 Conflict` üretilir.
- **Oturum ve Eşzamanlılık Güvenliği:** Siparişler doğrudan `Submitted` durumunda oluşturulur (sunucu tarafında taslak sipariş tutulmaz). Kapanmış (`Closed`) veya hesap istenmiş (`BillRequested`) masalarda yeni sipariş reddedilir (`400 Bad Request`). Masadan gelen eşzamanlı siparişler izole veritabanı işlemlerinde artan sıra numarası (`RoundNumber`) alır.
- **Personel Yaşam Döngüsü ve İptal Kuralları:** Hazırlık öncesi (`PendingPreparation`) kalemler yetkili garson veya personel tarafından iptal edilebilir (`orders.items.cancel_pre_prep`). Hazırlığa girmiş (`InPreparation`) veya hazır (`Ready`) kalemler ise yalnızca `orders.items.void_in_prep` yetkisine sahip şube yöneticileri veya kasiyerler tarafından iptal (void) edilebilir.
- **Kapsam Sınırları:** Canlı SignalR KDS yayınları Faz 9'a; KDS istasyon hazırlık fişleri Faz 11'e; adisyon/hesap kapatma motoru Faz 13'e; ödeme entegrasyonu Faz 16'ya aittir.
