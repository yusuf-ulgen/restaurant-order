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

Canlı iletişim teknolojisi ADR-0001 ile **SignalR + Redis** olarak kabul edilmiştir; bu tablo önceki seçenek değerlendirmesini korur. Ayrıntılı hub/grup tasarımı ADR-0004 kapsamındadır. Telefonun arka planındaki push bildirimi ayrı kanaldır; sağlayıcı kararı [issue #11](https://github.com/yusuf-ulgen/restaurant-order/issues/11) kapsamında beklenmektedir.

## 2. Olay Türleri ve Şema

Olaylar standart JSON zarfı kullanır:

```json
{
  "event_id": "evt_01HXYZ...",
  "event_type": "order.created",
  "timestamp": "2026-09-19T14:55:00Z",
  "tenant_id": "org_abc123",
  "branch_id": "brn_xyz789",
  "data": {}
}
```

### 2.1. Temel Olaylar

| Olay | Üreten | Tüketen | İçerik |
| :--- | :--- | :--- | :--- |
| `order.created` | Müşteri QR, garson | KDS, kasa, yönetim | `order_id`, `table_id`, ürünler, ek seçenekler, sipariş turu |
| `order.item_status_changed` | KDS, garson | Müşteri QR, garson | `order_id`, `item_id`, `new_status` (`IN_PREP`, `READY`) |
| `table.service_requested` | Müşteri QR | Garson | `table_id`, `request_type` (`WAITER`, `WIPES`, `BILL`) |
| `kds.ticket_bumped` | KDS | Garson | `ticket_id`, `table_id`, istasyon (`KITCHEN`/`BAR`) |
| `menu.item_86ed` | KDS, yönetim | Müşteri QR, garson | `item_id`, `is_available: false` |
| `table.session_closed` | Kasa, garson | Müşteri QR, yönetim | `table_id`, `session_id`, `settled_at` |

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

Personel ve müşteriyi kapsayan arka plan push planı [NOTIFICATIONS-PLAN.md](./NOTIFICATIONS-PLAN.md), sağlayıcı önerisi [ADR-0011](./adr/0011-background-push-provider.md) altında `PROPOSED` durumundadır.
