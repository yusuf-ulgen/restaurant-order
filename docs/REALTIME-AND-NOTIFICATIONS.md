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

Personel ve müşteriyi kapsayan arka plan push planı [NOTIFICATIONS-PLAN.md](./NOTIFICATIONS-PLAN.md), kabul edilmiş FCM kararı [ADR-0011](./adr/0011-background-push-provider.md) altındadır. İlk sunucu taşıması [FCM-SETUP.md](./FCM-SETUP.md) içinde açıklanır; gerçek olay bağlantıları ve istemciler henüz tamamlanmadı.
