# ADR-0004: Gerçek Zamanlı Olay Aktarım Mimarisi (`docs/adr/0004-realtime-event-transmission-architecture.md`)

- **Durum:** `ACCEPTED` (SignalR + Redis seçimi ADR-0001 ile kabul edilmiş; bu kayıt ayrıntılı hub, grup hiyerarşisi, outbox dispatch ve yetkilendirme mimarisini kesinleştirir)
- **Karar verenler:** Yusuf Ülgen, Sistem Mimarı, Güvenlik Sorumlusu
- **Tarih:** 2026-10-20
- **Teknik görev:** [Issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20), [Faz 9 Takibi](../PHASE-9-TRACKER.md)

---

## 1. Bağlam ve Sorun

`restaurant-order` çok işletmeli (multi-tenant) restoran yönetim sisteminde misafirler (QR müşteri web), salon personeli (garson), mutfak/bar (KDS) ve yönetim panelleri arasında yüksek erişilebilirlikli ve düşük gecikmeli (<500 ms) canlı olay yayılımı gereklidir.

Çözülmesi gereken temel mimari ve güvenlik güçlükleri şunlardır:
1. **İşletme ve Masa Yalıtımı:** Bir masadaki müşterinin başka masanın veya şubenin sipariş/servis olaylarını dinlemesi kesinlikle engellenmelidir.
2. **Sürekli Yetkilendirme (Continuous Authorization):** Yalnızca bağlantı kurulurken yapılan JWT doğrulaması yetersizdir. Müşteri masa oturumu kapandığında, personelin oturumu sonlandığında (logout/token revocation), yetkisi değiştiğinde veya işletme askıya alındığında açık WebSocket bağlantısı üzerinden veri akışı derhal kesilmelidir.
3. **Kalıcı Transactional Outbox:** Sipariş veya durum değişikliği veritabanında commit edilirken canlı olay yayınının başarısız olması (Redis kesintisi, ağ gecikmesi) durumunda olaylar kaybolmamalı; veritabanı rollback olursa hiçbir sahte olay (phantom event) yayınlanmamalıdır.
4. **Çoklu Instance ve Blue/Green Uyumu:** Birden fazla API podu/konteyneri çalıştığında bir instance üzerinden gönderilen olayın diğer instance'a bağlı istemciye iletilmesi ve Blue/Green slot geçişlerinde kesintisiz toparlanma sağlanmalıdır.
5. **Topoloji Bağımsızlığı:** Background Worker'ın outbox kayıtlarını tüketirken `RestaurantOrder.Api` projesine ters bağımlılık oluşturmaması gerekir.

---

## 2. Kararı Belirleyen Etkenler

- **Gecikme Hedefi:** Yol haritasındaki <500 ms p95 canlı olay dağıtımı.
- **Güvenlik ve Veri Minimizasyonu:** OWASP Top 10 ve çok işletmeli yalıtım. Olay gövdelerinde müşteri notu, PIN, token veya kişisel veri taşınmaz; istemci detayları yetkili HTTP read modelinden çeker.
- **At-Least-Once ve Idempotency:** Ağ kopmalarında veya yeniden denemelerde duplicate olaylar tolere edilmeli; istemci `event_id` üzerinden güvenle deduplicate edebilmelidir.
- **Çevrimdışı ve Reconnect Dayanıklılığı:** İstemcinin kopma sonrası güncel aggregate durumunu yakalaması için sequence tabanlı HTTP sync mekanizması bulunmalıdır.

---

## 3. Değerlendirilen Seçenekler

1. **Seçenek 1 (Doğrudan In-Memory SignalR + HTTP Polling):**
   - Tek pod için basit, ancak çoklu instance desteği yok; Redis backplane olmadan instance A'ya bağlı istemci instance B'deki olayı alamaz.
2. **Seçenek 2 (SignalR + Redis Backplane + Transactional Outbox + Redis Pub/Sub Köprüsü):**
   - ASP.NET Core SignalR, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` paketi ile arka planda Redis'e bağlanır.
   - Domain servisleri mutasyon ile birlikte `OperationalOutboxMessage` kaydını aynı PostgreSQL transaction'ında yazar.
   - Background Worker (veya outbox dispatcher) outbox kayıtlarını claim eder ve Redis Pub/Sub kanalına yayınlar.
   - API instance'ları Redis Pub/Sub kanalını dinleyerek yetkili SignalR gruplarına dağıtır.
   - İstemciden gelen keyfi `JoinGroup` çağrıları yasaktır; grup üyelikleri sunucu tarafında doğrulanmış claim'lere göre belirlenir.
3. **Seçenek 3 (Yalnızca SSE - Server-Sent Events):**
   - Tarayıcı dostu, ancak çift yönlü iletişim ve hazır ASP.NET Core Redis kümeleme ekosistemi açısından SignalR kadar olgun değildir.

---

## 4. Karar Sonucu

**Seçilen seçenek:** **Seçenek 2: SignalR + Redis Backplane + Transactional Outbox + Redis Pub/Sub Köprüsü.**

### 4.1. Grup Hiyerarşisi ve Yetkilendirme Matrisi

Grup adları sunucu tarafından deterministik olarak oluşturulur:
- **Müşteri Masası:** `tenant:{tenant_id}:branch:{branch_id}:table:{table_id}` — Yalnızca aktif masa oturumuna sahip müşteri dinleyebilir.
- **Şube Salonu:** `tenant:{tenant_id}:branch:{branch_id}:floor` — Servis talepleri, hazır ürün bildirimleri (garson ve şube müdürü).
- **İstasyonlar:** `tenant:{tenant_id}:branch:{branch_id}:station:kitchen` ve `tenant:{tenant_id}:branch:{branch_id}:station:bar` — İlgili istasyona ait fişler ve 86 olayları.
- **Katalog İptal Kanalı:** `tenant:{tenant_id}:branch:{branch_id}:catalog` — Güvenli, hassas veri içermeyen menü ve fiyat politikası invalidation bildirimleri.

### 4.2. Sürekli Yetkilendirme Modeli

- `EventsHub.OnConnectedAsync` sırasında JWT token ve (varsa) müşteri oturum çerezi doğrulanır.
- Oturum kapatıldığında, token revoke edildiğinde veya masa oturumu kapandığında, Redis Pub/Sub üzerinden bir `session.revoked` kontrol sinyali yayınlanarak ilgili bağlantı sunucu tarafından `Abort()` edilir veya ilgili gruptan çıkarılır.
- İstemciden gelen hiçbir `JoinGroup(groupName)` parametresi kabul edilmez; metotlar API seviyesinde kapalıdır.

### 4.3. Transactional Outbox ve Worker Topolojisi

- Veritabanı tablosu: `tenancy.operational_outbox` (PostgreSQL RLS korumalı).
- Outbox kaydı ve domain aggregate mutasyonu aynı transaction içinde yazılır.
- `OperationalOutboxDispatcher`:
  - Active Blue/Green slotunda ve exclusive distributed lease altında çalışır.
  - `FOR UPDATE SKIP LOCKED` (veya lease/claim mekanizması) ile birden fazla worker'ın aynı kaydı işlemesini engeller.
  - Olayı Redis kanalı `restaurant-order:events:broadcast` üzerine yayınlar.
  - Başarılı yayından sonra kaydı `Dispatched` olarak işaretler.
  - Hata durumunda üstel gecikme (exponential backoff) uygular; 5 denemeden sonra zehirli mesaj kuyruğuna (`DeadLetter`) alır.
  - API katmanındaki `RedisEventSubscriberService` Redis kanalını dinleyerek `IHubContext<EventsHub>` üzerinden uygun gruplara iletir. Böylece Worker -> API projesine ters bağımlılık oluşmaz.

### 4.4. HTTP Eşitleme (Sync) ve Reconnect

- Bağlantı koptuğunda istemci üstel gecikmeyle (1s, 2s, 4s ... max 15s) yeniden bağlanmayı dener.
- Yeniden bağlandığında kaçırılan olayları almak için `GET /api/v1/events/sync?since_sequence={seq}` ucuna sorgu atar.
- İstemci state'i snapshot + kaçırılan olaylar ile uzlaştırılır (reconciliation).
- Çevrimdışıyken sepetteki sipariş **asla otomatik gönderilmez**; kullanıcının açık onayı ve Phase 8 finansal fiyat doğrulama kapısı korunur.

### Olumlu Sonuçlar

- Kesin veri yalıtımı ve sıfır yetkisiz veri sızıntısı.
- Rollback olan işlemlerde sıfır sahte olay.
- Redis veya SignalR kesintisinde sipariş veri kaybı yaşanmaz; outbox kalıcı kalır.
- Yatayda birden fazla API instance'ı ile kesintisiz yük dağıtımı.

### Olumsuz Sonuçlar ve Ödünleşimler

- Redis ve PostgreSQL üzerindeki ek I/O yükü (outbox tablosu ve Redis Pub/Sub).
- At-least-once teslim nedeniyle istemcilerin `event_id` deduplication yapması zorunludur.

---

## 5. Doğrulama ve Test Planı

1. **Birim & Drift Testleri:** C# `EventEnvelope<T>` ile TypeScript `events.ts` arasında çift yönlü drift testleri (`EventContractDriftTests.cs`, `eventsContract.test.ts`).
2. **Transactional Outbox Entegrasyon Testleri:**
   - Transaction rollback durumunda outbox kaydı oluşmadığı doğrulanır.
   - Idempotency replay durumunda ikinci outbox kaydı oluşmadığı doğrulanır.
   - İki eşzamanlı dispatcher yarışında çifte yayın yapılmadığı doğrulanır.
3. **SignalR & Redis Yetki Testleri:**
   - Cross-tenant ve cross-table grup erişimlerinin engellendiği.
   - Bağlantı sırasında token/oturum iptalinde bağlantının sonlandırıldığı.
4. **Çoklu Instance Testi:** Instance A'da üretilen olayın Instance B'ye bağlı istemciye ulaştığı.
5. **Gecikme Ölçümü:** p50/p95 gecikmesinin <500 ms olduğu standart test yükü altında ölçülür.
6. **Playwright E2E:** 7 zorunlu gerçek kullanıcı senaryosu.

---

## 6. Bağlantılar ve Kaynaklar

### 2026-10-27 İnceleme Uygulaması

İptal sinyalleri tek başına erişim garantisi değildir: her API yalnızca yerel tracker'ındaki bağlantılara, güncel session/security-version/üyelik/tenant/branch yetkisini PostgreSQL'den doğruladıktan sonra gönderir. Periyodik sweep kaçırılan invalidation'ı toparlar; gruplardan çıkarma ve bağlantı context'ini abort etme birlikte uygulanır. Kısa ömürlü Hub nesnesi callback içinde tutulmaz.

API ve worker çalışma zamanı hesapları ayrıdır. Global claim EXECUTE yalnızca dar worker rolündedir; claim sonrası her kayıt tenant transaction'ında işlenir. Staging ile outbox completion aynı PostgreSQL transaction'ını paylaşır; Redis yayınıyla atomiklik veya tam bir kez teslim iddia edilmez. Unique event/device görevi ve sabit event ID replay'i güvenli kılar.

HTTP cursor yalnızca tamamlanan taramadan ilerler. XID/sequence sırası eşdeğer sayılmaz; kısa SHARE kilidiyle açık writer'ların commit/rollback'i beklenir. Üretim kilit maliyeti ve gerçek doğrulama sınırları [inceleme kaydındadır](../PHASE-9-REVIEW.md). Bu ek, kabul edilmiş taşıma yığınını veya Faz 9 kapsamını değiştirmez.

- [ADR-0001: Çekirdek Teknoloji Yığını](./0001-technology-stack.md)
- [ADR-0011: Personel ve Müşteri İçin Arka Plan Push Sağlayıcısı](./0011-background-push-provider.md)
- [docs/REALTIME-AND-NOTIFICATIONS.md](../REALTIME-AND-NOTIFICATIONS.md)
- [docs/NOTIFICATIONS-PLAN.md](../NOTIFICATIONS-PLAN.md)
- [Microsoft SignalR Redis Backplane Docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0)
