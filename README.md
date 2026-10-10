# Restoran Sipariş ve Operasyon Yönetimi (`restaurant-order`)

QR ile masa siparişi, mutfak hazırlığı, servis ve restoran yönetimini bir araya getiren çok işletmeli platform.

## 1. Genel Bakış

Tek restoranlardan çok şubeli zincirlere kadar müşteri, garson, mutfak ve yönetim arasındaki iletişimi ortak bir sistemde toplamak hedeflenir. Siparişlerin mutfak/bar istasyonlarına yönlendirilmesi, ESC/POS yazdırma, hesap takibi ve işletmeler arasında veri yalıtımı ürün kapsamındadır.

**Mevcut durum:** Faz 0–5 tamamlandı; masa/QR oturumları ve uçtan uca restoran akışı sonraki aşamalardadır. Aşağıdaki ürün kapsamı, tüm özelliklerin uygulanmış olduğu anlamına gelmez. Güncel görev ve doğrulama kaydı [CURRENT-STATE.md](docs/CURRENT-STATE.md) dosyasındadır.

## 2. Ürün Arayüzleri

1. **QR Müşteri Web Uygulaması:** Uygulama mağazası kurulumu olmadan menü, sipariş, servis çağrısı ve hesap görüntüleme.
2. **Garson ve Operasyon Mobil Uygulaması:** Dokunmatik kullanım, masa yönetimi, sipariş alma ve masa taşıma.
3. **Mutfak / Bar KDS:** İstasyon filtreli hazırlık kuyruğu, durum renkleri ve sesli uyarılar.
4. **Restoran Yönetim Paneli:** Menü, stok durumu, personel izinleri, masalar ve şube raporları.
5. **Platform Süper Yönetici Paneli:** İşletme açılışı, abonelikler ve sistem sağlığı.

## 3. Kullanıcı Rolleri (RBAC)

Sekiz rol tanımlıdır: Süper Yönetici, Restoran Yöneticisi, Şube Müdürü, Operasyon/Kasa, Mutfak, Bar, Garson ve Müşteri. Platform, işletme, şube ve masa oturumu kapsamları birbirinden ayrılır. Bir ürün ekranındaki eylem listesi yetki vermez; bağlayıcı izinler [rol ve izin matrisindedir](docs/ROLES-AND-PERMISSIONS.md).

## 4. Dokümantasyon Haritası

Tüm belgelerin açıklamalı dizini [docs/README.md](docs/README.md) dosyasındadır.

| Konu | Belgeler |
| :--- | :--- |
| Ürün ve alan | [Ürün](docs/PRODUCT.md), [Alan modeli](docs/DOMAIN.md), [Sözlük](docs/GLOSSARY.md), [Ekranlar](docs/SCREEN-INVENTORY.md) |
| Mimari | [Mimari](docs/ARCHITECTURE.md), [Depo yapısı](docs/REPOSITORY-STRUCTURE.md), [Karar kayıtları](docs/adr/README.md) |
| İş kuralları | [Durum makineleri](docs/STATE-MACHINES.md), [Negatif akışlar](docs/NEGATIVE-FLOWS.md), [Ödeme ve bahşiş](docs/PAYMENTS-TIPS-COMMISSIONS.md) |
| Entegrasyonlar | [Yazdırma](docs/ORDER-ROUTING-AND-PRINTING.md), [Canlı olaylar ve bildirimler](docs/REALTIME-AND-NOTIFICATIONS.md) |
| Güvenlik ve kalite | [Çok işletmeli yapı](docs/MULTI-TENANCY.md), [Güvenlik](docs/SECURITY.md), [Test](docs/TESTING.md) |
| Operasyon | [Ortamlar](docs/ENVIRONMENTS.md), [Teslimat](docs/DELIVERY.md), [Blue/Green](docs/BLUE-GREEN-RUNBOOK.md), [Olay müdahalesi](docs/INCIDENT-RESPONSE.md), [Operasyon rehberleri](docs/runbooks/README.md) |
| Planlama | [Yol haritası](docs/ROADMAP.md), [İnceleme iş listesi](docs/REVIEW-BACKLOG.md), [Şablonlar](docs/templates/TASK-TEMPLATE.md) |

## 5. Kalite Kontrolleri ve Doğrulama Komutları

```bash
# Dosya boyutu, belge bağlantıları, gizli değer taraması ve kontrol testleri
pnpm verify:gates
# Çalışma alanında TypeScript tür denetimi
pnpm typecheck
# Ön uç ve ortak paketlerde kod kuralları
pnpm lint
# Arka uç ve ön uç testleri
pnpm test
# Ön uç uygulama ve paketlerini derleme
pnpm build
# Tam doğrulama akışı
pnpm verify
# Yerel Docker Compose yapılandırmasını doğrulama
docker compose -f deploy/docker-compose.yml config
# Veritabanı geçişi denetimi ve tekrar çalıştırılması güvenli SQL üretimi
pnpm migration:validate
pnpm migration:script
```

## 6. Katkı ve Yapay Zekâ Kuralları

İşe başlamadan [AGENTS.md](AGENTS.md), [katkı iş akışı](docs/CONTRIBUTING-WORKFLOW.md) ve [güncel durum](docs/CURRENT-STATE.md) okunmalıdır. Issue açılır, görev dalında çalışılır, doğrulama yapılır ve açıklamalı PR ile incelemeye sunulur; `main` dalına doğrudan commit yapılmaz.

Belgeler, görev/devir kayıtları, issue ve PR açıklamaları Türkçe yazılır. Kod tanımlayıcıları, dosya yolları, komutlar ve makine tarafından okunan değerler korunur. İnsan tarafından yazılan dosyalarda 450 satır uyarı, 600 satır üst sınırdır. Çalıştırılmayan test başarılı gösterilemez. Gizli değerler ve gerçek müşteri verileri depoya veya günlüklere yazılamaz. Onaylanmamış mimari kararlar `[Proposed / ADR Required]` olarak kalır.
