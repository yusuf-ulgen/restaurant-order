# Gerçek Zamanlı Olaylar ve Bildirimler (`docs/REALTIME-AND-NOTIFICATIONS.md`)

## 1. Genel Bakış ve Taşıma

Müşteri telefonu, garson cihazı, mutfak/bar ekranı ve yönetim paneli arasında bir saniyeden kısa sürede canlı iletişim hedeflenir.

### 1.1. İlk Taşıma Karşılaştırması

| Ölçüt | WebSockets (WS) | Server-Sent Events (SSE) |
| :--- | :--- | :--- |
| İletişim | Çift yönlü | Yalnızca sunucudan istemciye |
| Protokol | `ws://` / `wss://` | HTTP/2 veya HTTP/1.1 |
| Güvenlik duvarı / vekil | Bazı vekiller engelleyebilir veya bağlantıyı düşürebilir. | Standart HTTP vekillerinden geçer. |
| Uygun kullanım | Etkileşimli KDS ve garson cihazları | QR durum akışı ve salt okunur paneller |
| İlk taslak durumu | `[Proposed / ADR Required]` | `[Proposed / ADR Required]` |

Canlı iletişim teknolojisi ADR-0001 ile **SignalR + Redis** olarak kabul edilmiştir; bu tablo önceki seçenek değerlendirmesini korur. Ayrıntılı hub/grup tasarımı [ADR-0004](./adr/0004-realtime-event-transmission-architecture.md) kapsamındadır. Telefonun arka planındaki push bildirimi ayrı kanaldır; [ADR-0011](./adr/0011-background-push-provider.md) ile FCM seçilmiştir. Uygulama [issue #20](https://github.com/yusuf-ulgen/restaurant-order/issues/20) ve [Faz 9 Takibi](./PHASE-9-TRACKER.md) kapsamında yürütülür.

## 2. Olay Türleri ve Şema

Olaylar standart, sürümlenmiş JSON zarfı (`EventEnvelope`) kullanır:

```json
{
  "event_id": "019488a0-1234-7890-abcd-ef0123456789",
  "event_type": "order.created",
  "schema_version": 1,
  "occurred_at_utc": "2026-10-20T14:55:00Z",
  "tenant_id": "org_abc123",
  "branch_id": "brn_xyz789",
  "aggregate_id": "ord_01HXYZ...",
  "sequence_number": 1042,
  "data": {}
}
```

### 2.1. Temel Olaylar

| Olay | Üreten | Tüketen | İçerik ve Amaç |
| :--- | :--- | :--- | :--- |
| `order.created` | Müşteri QR, garson | Salon, KDS, yönetim | `order_id`, `dining_session_id`, `table_id`, `round_number`, `item_count`, `currency` |
| `order.item_status_changed` | Mutfak, bar, garson | Müşteri QR, salon | `order_id`, `item_id`, `new_status` (`InPreparation`, `Ready`, `Served`, `Cancelled`) |
| `table.service_requested` | Müşteri QR | Salon (`floor`) | `service_request_id`, `table_id`, `request_type`, `requested_at_utc` |
| `table.service_request_status_changed` | Garson | Müşteri QR, salon | `service_request_id`, `table_id`, `new_status` (`Acknowledged`, `Resolved`, `Cancelled`) |
| `menu.item_availability_changed` | Yönetim, mutfak | Müşteri QR, salon, istasyon | `item_id`, `item_variant_id`, `is_available`, `reason_code` |
| `table.session_closed` | Kasa, garson | Müşteri QR, yönetim | `dining_session_id`, `table_id`, `closed_at_utc` |
| `catalog.policy_invalidated` | Yönetim | Müşteri QR | `policy_fingerprint`, `invalidated_at_utc` (fiyat/politika yeniden onay tetikleyicisi) |
| `kds.ticket_bumped` *(Faz 11)* | KDS | Salon | `ticket_id`, `station` (Faz 11 KDS fazına ayrılmıştır) |

## 3. Kanal ve Grup Ayrımı

İşletme yalıtımı ve bant genişliği için gruplar ayrılır:

- **Müşteri QR:** `tenant:{tenant_id}:branch:{branch_id}:table:{table_id}`. Yalnızca mevcut masaya ilişkin olaylar. Bu başlangıç taslağı tek başına yetki sınırı değildir; ADR-0009 gereği aktif masa oturumu yetkisi de denetlenir.
- **Mutfak:** `tenant:{tenant_id}:branch:{branch_id}:station:kitchen`. Yemek fişleri ve 86 olayları.
- **Bar:** `tenant:{tenant_id}:branch:{branch_id}:station:bar`. İçecek fişleri ve 86 olayları.
- **Salon:** `tenant:{tenant_id}:branch:{branch_id}:floor`. Servis çağrıları, hazır ürün ve masa durumu.

## 4. Ses ve Bildirim Deneyimi

- Yeni KDS fişinde, gürültülü mutfakta duyulabilen ayırt edici ses hedeflenir.
- Garson cihazında servis çağrısı için titreşim ve kalıcı uyarı gösterilir; örneğin “Masa 14 — Garson çağrısı”. Cihaz/tarayıcı desteği gözetilir.
- Bağlantı tekrarları 1, 2, 4 saniye biçiminde artar; üst sınır 15 saniyedir.
- Kopma sırasında iş akışını engellemeyen çevrimdışı göstergesi görünür.
- Yeniden bağlanınca kaçırılan olaylar HTTP eşitleme ucundan alınır.

Personel ve müşteriyi kapsayan arka plan push planı [NOTIFICATIONS-PLAN.md](./NOTIFICATIONS-PLAN.md), kabul edilmiş FCM kararı [ADR-0011](./adr/0011-background-push-provider.md) altındadır. Faz 9 kapsamında sunucu SignalR hub'ı, Redis backplane, Transactional Outbox worker dispatcher'ı, HTTP sync okuma modeli, FCM cihaz kaydı (`/api/v1/notifications/devices`) ve ön yüz sesli/canlı bildirimleri uygulanmış ve doğrulanmıştır (bkz. [Faz 9 Takibi](./PHASE-9-TRACKER.md)).

## 5. Arka Plan Push Bildirimleri ve Teslim Kuyruğu

### 5.1. Cihaz Kaydı Sahipliği ve Güvenli Yeniden Bağlama (Rebind)

- `DeviceRegistrationEndpoints` uç noktası (`POST /api/v1/notifications/devices`), kurulum kimliği (`installation_id`) bazlı cihaz kayıtlarında katı sahiplik doğrulaması yapar.
- **Aynı Sahip Yenileme:** Aynı kullanıcı veya oturuma ait mevcut kayıtlar güvenle yenilenir (`last_seen_at_utc`, push token).
- **Yabancı Sahiplik Reddi:** Başka bir aktif oturuma veya farklı kullanıcıya ait kayıtlar, yalnızca `installation_id` bilindiği gerekçesiyle güncellenemez; `409 Conflict` (`DEVICE_REGISTRATION_CONFLICT`) döner.
- **Güvenli Yeniden Bağlama (Rebind):** Paylaşılan tablet veya masa değişiminde (Masa A -> Masa B), eski oturum kapalıysa (`Closed`) cihaz kaydı yeni müşteri oturumuna güvenle yeniden bağlanır (`RebindToCustomer`). Sessiz sahiplik devri yapılmaz.
- **Eksik Yetki Talebi (Claim):** Müşteri oturumunda `dining_session_id`, personel oturumunda `sub` / `tenant_id` claim'leri zorunludur; eksiklik durumunda `401 Unauthorized` ile fail-closed davranılır.
- **Eşzamanlı Yarış Koruması:** Eşzamanlı cihaz kayıt isteklerinde PostgreSQL `23505` (unique constraint violation) hatası yakalanır; ham 500 hatası yerine mevcut kayıt tekrar sorgulanarak sahiplik kuralları doğrultusunda güvenle ele alınır.
- **İdempotent Kayıt Silme:** `DELETE /api/v1/notifications/devices` çağrısı, kayıt bulunamadığında veya zaten silinmişse idempotent olarak `204 No Content` döner.

### 5.2. Kalıcı Teslimat Kuyruğu (`tenancy.notification_deliveries`)

- Redis subscriber içinde fire-and-forget push gönderimi kaldırılmıştır. Bildirimler, outbox dispatch döngüsü sırasında `tenancy.notification_deliveries` tablosuna kalıcı olarak evrelenir (`PushNotificationDeliveryStager`).
- **Durum Makinesi:** `Pending` -> `Claimed` -> `ProviderAccepted` | `FailedRetryable` | `FailedPermanent` | `Expired`.
- **Worker Koordinasyonu ve Kiralama (Lease):** Teslimat işleri, lider worker tarafından PostgreSQL `FOR UPDATE SKIP LOCKED` mekanizmasıyla (`claim_pending_notification_deliveries` fonksiyonu veya LINQ sorgusu) güvenli kiralama süreleriyle (`claimed_until_utc`) alınır. Süresi dolan kiralamalar çökme toparlanması (crash recovery) ile yeniden kuyruğa girer.
- **Sağlayıcı Hata Sınıfları ve Geri Çekilme:** `RetryAfter` başlığı varsa tam süresine uyulur, aksi halde üstel geri çekilme (`backoff`) uygulanır. `InvalidRecipient` durumunda geçersiz cihaz kaydı doğrudan pasifleştirilir (`is_active = false`).
- **Teslimat Güvencesi:** Sağlayıcı sonucu belirsiz olduğunda ağ hatalarında tekilleştirme garantisi (exactly-once) varsayılmaz; retry sırasında `event_id` stabil kalır. `ProviderAccepted` sonucu kesin cihaz teslimi olarak değil, yalnızca sağlayıcının kuyruğa kabulü olarak işaretlenir.

### 5.3. Gönderim Öncesi Sürekli Yetki Denetimi

Her teslimat denemesinde (`PushNotificationDispatcher`):
- Müşterinin aktif masa oturumu (`dining_sessions` durumu `Active`) doğrulanır.
- Personelin güncel şube üyeliği, aktifliği ve rol yetkileri (`staff_memberships`) denetlenir.
- Cihaz kaydının aktifliği (`is_active`) kontrol edilir.
- Olay geçerliliği kısa bir TTL (5 dakika) ile sınırlandırılır; cihaz kaydının 30 günlük ömrü olay TTL'i yerine kullanılamaz. Süresi dolan olaylar doğrudan `Expired` durumuna alınır.

### 5.4. Veri Minimizasyonu ve Günlük Güvenliği

- Cihaz kurulum kimliği (`installation_id`), FCM registration token'ı ve sunucu kimlik bilgileri loglara kesinlikle yazılmaz.
- Hata loglarında `{InstallationId}` kaldırılmış, yerine anonim hash veya sayısal kayıt ID'si kullanılmıştır.
- Kilit ekranında müşteri notu, sipariş kalemi veya finansal tutar gösterilmez; bildirimler genel başlıklar ve opak `event_id` taşır.
- İstemci uygulamayı açtığında güncel ve detaylı verileri yetkili HTTP uç noktalarından çeker.
- Service worker bildirim tıklamalarında serbest dış URL'lere yönlendirme engellenmiş; yalnızca izin verilen iç rotalar (`/`, `/table`, `/orders`, `/menu`, `/operations`) açılır.

### 5.5. İstemci Push ve Service Worker Akışı

- Müşteri (`apps/customer-web`) ve operasyon (`apps/operations-web`) arayüzlerinde tarayıcı desteği (`Notification`, `serviceWorker`, `PushManager`) denetlenir.
- Kullanıcıya açık izin isteme kartı (`PushNotificationBanner`) sunulur; izin reddi veya destek yokluğu menü inceleme veya sipariş akışını engellemez (zorunlu kılınmaz).
- Service worker (`sw.js`) arka plan bildirimlerini dinler, güvenli kilit ekranı minimizasyonunu uygular ve uygulama ön planda açıkken çift uyarının bastırılmasını (`suppressDoubleAlert`) destekler.

## 6. Çoklu Instance, Canlı Yayılım ve Gecikme Metrikleri

- **Çoklu API Instance Mimarisi:** Üretim ortamında birden fazla API podu çalıştığında, mutasyon A podu üzerinden yürütüldüğünde transactional outbox ve worker aracılığıyla Redis pub/sub'a aktarılır. B podundaki `RedisEventSubscriberService` mesajı alarak SignalR gruplarına iletir ve B poduna bağlı istemciye anında teslim eder (`TwoApiInstanceRealtimeIntegrationTests`).
- **Tekil Köprü Claim Kilidi:** Instance sayısının artmasıyla SignalR backplane'inde olay çoğalmasını önlemek amacıyla Redis üzerinde atomik anahtar (`restaurant-order:realtime:bridged:{eventId}`) ile ilk claim eden instance köprü görevini üstlenir.
- **Uçtan Uca Gecikme Bütçesi:** Tanımlı üretim outbox -> worker polling (50ms) -> Redis pub/sub -> API B -> SignalR istemci yolunda <500ms p95 gecikme bütçesi doğrulanmıştır (Ölçülen: p50 ~18 ms, p95 ~38 ms).
- **Cluster Geneli Oturum/Yetki Revocation:** Bir masa oturumu kapandığında veya personel yetkisi iptal edildiğinde (`PublishSessionInvalidatedAsync`, `PublishUserRevokedAsync`), kümedeki tüm API instance'ları bilgilendirilir ve yerel tracker'lardaki bağlantılar derhal sonlandırılır.
- **FCM Doğrulama Sınırları:** FCM sağlayıcı sözleşmesi, TTL ve kalıcı teslim kuyruğu otomatik entegrasyon testleriyle doğrulanmıştır; gerçek mobil cihaz teslimi staging ortamında fiziksel donanımla ayrı olarak doğrulanır.
