# Terminology Glossary (`docs/GLOSSARY.md`)

This document establishes the standardized bilingual (Turkish & English) vocabulary used across code, database schemas, UI labels, and documentation for `restaurant-order`.

---

## 1. Domain & Organizational Concepts

| English Term | Türkçe Karşılığı | Definition & Context |
| :--- | :--- | :--- |
| **Tenant / Organization** | Organizasyon / Kiracı | The top-level legal entity subscribing to the platform. |
| **Brand** | Marka | A distinct culinary concept or trade name under an organization. |
| **Branch** | Şube | A physical restaurant location operating under a brand. |
| **Dining Area** | Salon / Alan / Bölüm | A physical zone within a branch (e.g., Salon, Teras, Bahçe, Bar). |
| **Table** | Masa | A designated physical dining table with a unique identifier per branch. |
| **Table Session** | Masa Oturumu | The temporal dining lifecycle of a table from first seated/order to bill payment. |
| **Table QR Code** | Masa QR Kodu | A unique scannable matrix barcode linked to a specific table in a branch. |

---

## 2. Catalog & Menu Concepts

| English Term | Türkçe Karşılığı | Definition & Context |
| :--- | :--- | :--- |
| **Menu** | Menü | A catalog of food and beverage offerings for a branch or service period. |
| **Category** | Kategori | A group of related menu items (e.g., Başlangıçlar, Ana Yemekler, İçecekler). |
| **Menu Item** | Menü Ürünü / Yemek | An individual sellable product on the menu. |
| **Variant** | Varyant / Porsiyon | A specific portion or size of a menu item with its own price (e.g., 200g vs 300g). |
| **Modifier Group** | Seçenek Grubu / Opsiyon | A collection of customizations (e.g., Pişme Derecesi, Yan Ürün Seçimi). |
| **Modifier Item** | Seçenek / Ekstra | An individual customization choice within a group (e.g., Az Pişmiş, Ekstra Peynir). |
| **86ed / Out of Stock** | Tükendi / Stokta Yok | An item temporarily marked unavailable across all customer and staff screens. |

---

## 3. Order & Kitchen Operations

| English Term | Türkçe Karşılığı | Definition & Context |
| :--- | :--- | :--- |
| **Order** | Sipariş | A customer's or waiter's request for food/beverage items. |
| **Order Item** | Sipariş Kalemi | A single item line within an order, including its modifiers and notes. |
| **KDS** | Mutfak Ekranı (KDS) | Kitchen Display System; digital screen displaying tickets to prep staff. |
| **Station Ticket** | İstasyon Fişi | A sub-ticket routed to a specific preparation station (Kitchen or Bar). |
| **Kitchen Ticket** | Mutfak Fişi | A ticket containing hot/cold food items routed to the kitchen line. |
| **Bar Ticket** | Bar Fişi | A ticket containing beverages routed to the bar preparation counter. |
| **Station Routing** | İstasyon Yönlendirme | Logic that automatically dispatches order items to designated KDS/printers. |
| **Prep Time / Timer** | Hazırlık Süresi | The elapsed time since a ticket was received by the preparation station. |
| **Recall Ticket** | Fişi Geri Çağır | Reopening a ticket that was accidentally marked complete in KDS. |

---

## 4. Billing, Payments & Finance

| English Term | Türkçe Karşılığı | Definition & Context |
| :--- | :--- | :--- |
| **Bill / Check** | Hesap / Adisyon | The total invoice for a table session detailing items, taxes, and discounts. |
| **Split Bill** | Hesabı Bölme | Dividing a bill into multiple partial payments (by amount or by specific items). |
| **Payment** | Ödeme | A financial transaction settling part or all of an active bill. |
| **Tip / Gratuity** | Bahşiş | An optional monetary gift added by a customer for service staff. |
| **Platform Commission** | Platform Komisyonu | The platform fee deducted from processed tenant transactions. |
| **Refund** | İade | Reversing a previously settled payment back to the customer. |
| **Void / Cancellation** | İptal | Cancelling an unpaid item or order prior to settlement (requires authorization). |

---

## 5. Hardware & Realtime Terms

| English Term | Türkçe Karşılığı | Definition & Context |
| :--- | :--- | :--- |
| **ESC/POS** | ESC/POS Protokolü | The industry-standard command protocol for thermal receipt printers. |
| **Network Printer** | Ağ / Termal Yazıcı | Thermal printer connected via Ethernet/Wi-Fi to the local branch network. |
| **Chime / Buzzer** | Uyarı Zili / Bildirim Sesi | Audio notification emitted on KDS or mobile app for new orders or calls. |
| **Waiter Call** | Garson Çağırma | A customer action from the QR app requesting waitstaff assistance. |
| **Realtime Channel** | Gerçek Zamanlı Kanal | A WebSocket or SSE connection streaming live state updates. |

---

## 6. Roles & Operational Terms

| Role / Term | Türkçe Karşılığı | Scope |
| :--- | :--- | :--- |
| **Super Admin** | Süper Yönetici | Platform-wide administration and tenant management. |
| **Restaurant Admin** | Restoran Yöneticisi | Brand/organization administration and financial reporting. |
| **Branch Manager** | Şube Müdürü | Daily branch operations, shift management, and table layouts. |
| **Operations / Cashier** | Operasyon / Kasa | Cash register, bill settlement, and POS payment collection. |
| **Kitchen** | Mutfak | Food preparation line and KDS queue management. |
| **Bar** | Bar | Beverage preparation line and bar KDS queue management. |
| **Waiter** | Garson | Table service, order taking, and guest assistance. |
| **Customer** | Müşteri | Dining guest accessing QR menu and ordering. |
| **Blue/Green Deployment** | Mavi/Yeşil Dağıtım | Zero-downtime release method utilizing two identical production slots. |
| **Incident (Sev-1 to Sev-4)** | Olay / Kesinti Seviyeleri | Production incident classifications from critical outage to minor defect. |
