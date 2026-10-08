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
