# Roller ve İzinler Belirtimi (`docs/ROLES-AND-PERMISSIONS.md`)

## 1. Rol Tanımları

`restaurant-order`, sekiz rol için işletme, şube ve oturum sınırlarını uygulayan Rol Tabanlı Erişim Denetimi (RBAC) kullanır.

| Rol | Kapsam | Birincil Sorumluluk | Kimlik Doğrulama Yöntemi [Önerilen] |
| :--- | :--- | :--- | :--- |
| **1. Süper Yönetici** | Platform çapında | Sistem durumu, işletme katılımı, faturalandırma ve planlar. | E-posta + Şifre + MFA |
| **2. Restoran Admini** | İşletme (Marka) | Organizasyon ayarları, marka menüleri, şube sunumu, finansal bilgiler. | E-posta + Şifre (+ MFA) |
| **3. Şube Müdürü** | Şube | Kat düzeni, personel vardiyaları, menü stokları, günlük raporlar. | E-posta + Şifre / 6-hane PIN |
| **4. Operasyon/Kasa** | Şube | Para çekmecesi, POS ödemeler, bölünmüş faturalar, makbuzlar, manuel geçersiz kılmalar.| 4-hane PIN |
| **5. Mutfak** | Şube (İstasyon) | Yiyecek hazırlama kuyruğu, hazır işareti, ürünü tükendi işaretleme. | İstasyon Cihaz Girişi / PIN |
| **6. Bar** | Şube (İstasyon) | İçecek hazırlama kuyruğu, hazır olarak işaretleme, içeceği tükendi işaretleme. | İstasyon Cihaz Girişi / PIN |
| **7. Garson** | Şube (Kat) | Sipariş alma, masa hareketleri, servis çağrıları, ödeme talepleri. | 4-hane PIN |
| **8. Müşteri** | Masa Oturumu | QR menüsüne göz atma, sipariş verme, servis çağrısı, fatura görüntüleme. | Anonim (QR Oturum Tokenı) |

---

Kimlik doğrulama sütunu başlangıç önerisini korur; uygulanmış güvenilir terminal ve dört haneli PIN kuralları için ADR-0009 ile SECURITY.md esastır.

## 2. RBAC Yetki Matrisi

İşaretler:
- `✓`: Tam Erişim / İzinli
- `O`: Yalnızca Sahip Olunan / Atanan Kapsam (örneğin kendi masaları, kendi istasyonu)
- `✗`: Kesinlikle Yasaktır

| İzin / Kaynak | Yetki Anahtarı | Süper Yönetici | Restoran Admini | Şube Müdürü | Operasyon / Kasa | Mutfak | Bar | Garson | Müşteri |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Platform Yönetimi** | | | | | | | | | |
| İşletme Oluştur / Askıya Al | `platform.tenants.manage` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Platform Ücretlerini Yapılandırma | `platform.fees.manage` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Sistem Denetim Günlüklerini Görüntüle | `platform.audit.view` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Marka ve Şube Yapılandırması** | | | | | | | | | |
| Marka Oluşturun / Düzenleyin | `tenant.brands.manage` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Şube Oluştur / Düzenle | `tenant.branches.manage` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Kurumsal Markalamayı Görüntüleyin | `tenant.branding.view` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| Markalamayı ve Temaları Yönetin | `tenant.branding.manage` | `✗` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Şube Yapılandırmasını Görüntüle | `branch.configuration.view` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| Şube Yapılandırmasını Yönet | `branch.configuration.manage` | `✗` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Yazıcıları / Ağı Yapılandırma | `branch.printers.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Yemek Alanı ve Masaları Düzenle | `branch.tables.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Menü ve Katalog** | | | | | | | | | |
| Kategoriler ve Öğeler Oluşturun / Düzenleyin | `menu.catalog.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Ürün Fiyatlarını ve Çeşitlerini Ayarlayın | `menu.pricing.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Hızlı 86 (Stokta Yok Olarak İşaretleyin) | `menu.inventory.quick86` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✗` | `✗` |
| Menüyü ve Uygunluğu Görüntüle | `menu.catalog.view` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| **Salon ve Masa İşlemleri** | | | | | | | | | |
| Masa Oturumunu Aç/Kapat | `floor.sessions.manage` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `O` |
| Masaları Birleştir / Taşı | `floor.tables.move` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Salon Durumunu Görüntüle | `floor.status.view` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| **Sipariş ve Hazırlık Fişi Yaşam Döngüsü** | | | | | | | | | |
| QR ile Sipariş Verin | `orders.qr.create` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` |
| Masa Siparişi Ver (Personel) | `orders.staff.create` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Sipariş Öğesini İptal Et (Hazırlık Öncesi) | `orders.items.cancel_pre_prep` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Hazırlanan/Hazır Sipariş Kalemini İptal Et| `orders.items.void_in_prep` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **KDS Hazırlık** | | | | | | | | | |
| Mutfak Yemek Sırasını Görüntüle | `kds.kitchen.view` | `✗` | `✓` | `✓` | `✗` | `✓` | `✗` | `✗` | `✗` |
| Bar İçki Sırasını Görüntüle | `kds.bar.view` | `✗` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` | `✗` |
| Hazırlık Fişi Durumunu Güncelle (Hazırlık/Hazır)| `kds.ticket.update` | `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| Tamamlanan Hazırlık Fişi Geri Çağırma | `kds.ticket.recall` | `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| **Faturalandırma ve Ödemeler** | | | | | | | | | |
| Masadan Hesap İste | `billing.bill.request` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` | `✓` |
| Nakit Ödeme Al | `billing.payment.cash` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` |
| Harici POS Kart Ödemesi Kaydet | `billing.payment.pos_card` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `O` | `✗` |
| Faturayı Tutar veya Öğeye Göre Böl | `billing.bill.split` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Sipariş İndirimi Uygula | `billing.discount.apply` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Geri Ödemeye İzin Ver | `billing.refund.authorize` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Personel ve Analitik** | | | | | | | | | |
| Personeli Yönetin ve Rolleri Atayın | `branch.staff.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Günlük Şube Gelirlerini Görüntüle | `reports.branch.revenue` | `✗` | `✓` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` |
| Çoklu Şube Raporlarını Görüntüle | `reports.tenant.multi_branch` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |

---

## 3. Yetkilendirme Güvenlik Kuralları

1. **Varsayılan ret:** Eşlenmemiş eylem veya eksik yetki claim'i `403 Forbidden` döndürür.
2. **İşletme sınırı:** A işletmesinin Restoran Admini hiçbir koşulda B'nin verisini okuyamaz veya değiştiremez.
3. **Müşteri oturumu:** Token tek `table_session_id` ile kriptografik bağlıdır; başka masanın verisini göremez veya siparişini veremez.
4. **Yönetici onayı:** Hazırlıktaki ürün iptali, hesap indirimi ve iade yönetici PIN doğrulaması gerektirir.
5. **Merkezi yetki kaydı:** `RestaurantOrder.Application.Auth.Permissions` içindeki anahtarlar yukarıdaki makine anahtarlarıyla birebir eşleşir.
6. **Üyelik yaşam döngüsü:** `UserMembershipStatus` (`Active`, `Suspended`, `Disabled`) işletme üyeliği kapsamındadır. A'daki askıya alma B üyeliğini etkilemez. İlgili üyeliğin askıya alınması/devre dışı bırakılması, o kapsamın oturumlarını tüm örneklerde hemen iptal eder. Şube Müdürü yalnızca atanmış şubenin personelini yönetir; başka şube `403` döndürür.
7. **Atomik token tüketimi:** Davet ve parola sıfırlama tokenları `UPDATE ... WHERE is_consumed = FALSE RETURNING ...` ile tek atomik adımda tüketilir; eşzamanlı kullanımda yalnızca biri başarılıdır. Ham tokenlar API yanıtına yazılmaz; ayrı güvenli kanaldan iletilir.
8. **Özellik bayrağı yetki değildir:** `order_acceptance`, `service_charge` gibi bayraklar işlevi değiştirir; RBAC vermez veya aşmaz. `branch.configuration.manage` olmadan bayrak değeri ne olursa olsun yapılandırma değiştirilemez.
9. **Şube kapsamı:** BranchManager'ın `O` yönetim yetkileri atanmış şubeyle sınırlıdır. Başka şubenin yapılandırması, alanları, istasyonları veya saatleri için okuma/değiştirme `403 Forbidden` döndürür.
