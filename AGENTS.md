# AGENTS.md — Ana AI Ajanı ve Katkıcı Kuralları

> **Bağlayıcı kaynak:** Bu belge, `restaurant-order` üzerinde çalışan tüm AI kodlama ajanları (Claude, Gemini, GPT, Muse, GitHub Copilot vb.) ve insan katkıcılar için **tek bağlayıcı kural kaynağıdır**.
> Araca özgü dosyalar (`.AGENT.md`, `CLAUDE.md`, `GEMINI.md`, `.claude/README.md` vb.) yalnızca bu belgeye yönlendiren uyarlayıcılardır.

## 1. Projenin Amacı ve Kapsamı

`restaurant-order`, birden fazla işletmeye hizmet veren, restoran siparişlerini ve işletme süreçlerini uçtan uca yöneten bir sistemdir. Beş temel kullanıcı arayüzü vardır:

1. **QR Müşteri Web Uygulaması:** Menü inceleme, sipariş verme, servis çağırma ve hesap görüntüleme için mobil öncelikli duyarlı arayüz.
2. **Garson ve Operasyon Uygulaması:** Masa yönetimi, sipariş oluşturma/değiştirme ve yetkiye göre ödeme işlemleri için mobil arayüz.
3. **Mutfak / Bar KDS:** Mutfak ve barın hazırlık kuyruklarını gerçek zamanlı gösteren ekranlar.
4. **Restoran Yönetim Paneli:** Şube ve restoran yöneticilerinin menü, personel, masa, rapor ve yazıcı ayarlarını yönettiği panel.
5. **Platform Süper Yönetim Paneli:** İşletme, faturalandırma ve işletme yaşam döngüsünün platform düzeyinde yönetimi.

## 2. Zorunlu Temel Kurallar

### 2.1. Dosya Boyutu Sınırları

- **450 satır:** Uyarı eşiğidir. Elle yazılan kaynak, test veya dokümantasyon bu sınıra yaklaşınca sorumluluklara göre bölme planı yapılır.
- **600 satır:** Kesin üst sınırdır; elle yazılan hiçbir dosya bunu aşamaz.
- **Yalnızca izin listesiyle istisna:** Üretilmiş dosyalar, kilit dosyaları (`package-lock.json` vb.), üçüncü taraf kodu ve anlık görüntüler ancak proje izin listesinde açıkça belirtilirse sınırı aşabilir.
- **Bölme yöntemi:** Özellik, alan modeli, kullanım senaryosu, uyarlayıcı veya bileşen sorumluluğuna göre bölün; rastgele parçalamayın.

### 2.2. Test ve Doğrulama Politikası

- Her davranış, başarılı/başarısız akış, yetki kontrolü, hata dalı ve durum geçişinin otomatik testi bulunmalıdır.
- **Kritik yollar:** Sipariş, ödeme, bahşiş dağıtımı, iade, işletme yalıtımı, ağ üzerinden yazdırma ve eşzamanlı işlemler.
- **Doğrulanmamış başarı yok:** Gerçekte çalıştırıp doğrulamadan hiçbir işlem, test veya derleme için `PASS`, `SUCCESS`, `VERIFIED` ya da Türkçe karşılığıyla başarı bildirmeyin. Varsayım kanıt değildir.
- Davranış değiştiğinde ilgili test, API sözleşmesi ve dokümantasyon aynı değişiklik kapsamında güncellenmelidir.

### 2.3. Güvenlik ve Veri Koruması

- **Depoda gizli bilgi bulunamaz:** API anahtarları, veritabanı parolaları, JWT sırları, özel anahtarlar, ödeme kimlik bilgileri ve erişim tokenları Git'e veya günlük akışına yazılamaz.
- **Gerçek müşteri verisi bulunamaz:** Gerçek ad, telefon, kart bilgisi ve diğer kişisel verileri eklemeyin veya günlüğe yazmayın. Testlerde sentetik veri kullanın.
- OWASP Top 10 ilkelerini tüm katmanlarda uygulayın: giriş doğrulama, SQL enjeksiyonu ve XSS önleme, güvenli başlıklar vb.
- İşletme verisi yalıtımı her veri erişim noktasında zorunludur.

### 2.4. Ortam Ayrımı

- `local`, `test`, `development`, `staging`, `production` ortamları kesin biçimde ayrılır.
- Yapılandırma ortam değişkenleriyle yönetilir; ortamlar arası bağımlılıklar koda sabitlenemez.
- Üretim, kesintisiz yayın ve hızlı geri dönüş için Blue/Green yuvalarıyla tasarlanmıştır. Bkz. [Blue/Green işletim kılavuzu](docs/BLUE-GREEN-RUNBOOK.md).

### 2.5. Kapsam ve Karar Yönetimi

- **Erken kesinleştirme yok:** Açık teknoloji/mimari kararlarını `[TBD]`, `[Öneri / Proposed]` veya `[ADR Gerekli / Requires ADR]` olarak işaretleyin. Onaysız öneriyi kesin karar saymayın.
- **Yetkisiz kapsam değişikliği yok:** Açık kullanıcı onayı olmadan kapsamı, çerçeveleri veya kabul edilmiş mimari anlaşmaları değiştirmeyin.
- **Kullanıcı çalışmasını koruyun:** İlgisiz dosyalara ve kaydedilmemiş kullanıcı değişikliklerine dokunmayın.

### 2.6. Türkçe Dokümantasyon ve İletişim

- Projenin dokümantasyon dili **Türkçedir**. Tüm AI ajanları ve katkıcılar yeni/güncellenen dokümanları, ADR açıklamalarını, görev ve devir notlarını, issue/PR başlık ve açıklamalarını, commit açıklamalarını Türkçe yazmalıdır.
- Çeviri yaparken anlamı, güvenlik zorunluluklarını, karar durumunu, tarihleri ve doğrulama kanıtlarını koruyun; çeviriyi kapsam veya mimari değişikliğine dönüştürmeyin.
- Dosya yollarını, API uçlarını, sınıf/değişken adlarını, yetki anahtarlarını, enum değerlerini, ortam değişkenlerini, paket/ürün adlarını ve çalıştırılabilir komutları çevirmeyin. `feat`, `fix`, `docs`, `Refs #`, `Closes #` gibi araç sözdizimi korunur; ardından gelen açıklama Türkçedir.
- Kaynak kodu/test adlarını veya API sözleşmelerini yalnızca dil değişikliği için yeniden adlandırmayın. Kod örneklerindeki açıklama yorumları Türkçe olabilir; örneğin çalışabilirliği korunur.
- Başlık değişince ilgili bağlantı çapalarını güncelleyin. İngilizce ikinci bir belge kopyası oluşturarak iki ayrı kural kaynağı üretmeyin.

## 3. Desteklenen Roller (RBAC)

Yetkilendirmede [rol ve yetki belgesindeki](docs/ROLES-AND-PERMISSIONS.md) sekiz rol esas alınır:

1. **Süper Yönetici (Super Admin):** Tüm işletmeler, platform faturalandırması ve yapılandırması.
2. **Restoran Admini (Restaurant Admin):** İşletme/marka sahibi; markalar, şubeler, üst düzey finans ve kullanıcılar.
3. **Şube Müdürü (Branch Manager):** Fiziksel düzen, vardiyalar, menüler ve şube raporları.
4. **Operasyon/Kasa (Cashier):** Kasa, nakit çekmecesi, bölünmüş ödemeler, fişler ve yetkili sipariş müdahaleleri.
5. **Mutfak (Kitchen):** Yemek hazırlığı, KDS durumları ve stokta olmayan ürünler.
6. **Bar:** İçecek hazırlığı, bar KDS durumları ve stokta olmayan içecekler.
7. **Garson (Waiter):** Atanmış masalar, sipariş alma, servis ve hesap isteme.
8. **Müşteri (Customer):** Masa QR'ı, menü, sipariş ve hesap görüntüleme.

## 4. Dokümantasyon Dizini ve Çalışma Akışı

Başlarken veya devam ederken [katkı akışını](docs/CONTRIBUTING-WORKFLOW.md) izleyin: Git/GitHub durumunu kontrol edin, issue açın veya mevcut olanı kullanın, dalda çalışın, doğrulayın, commit ve PR oluşturun, [güncel durumu](docs/CURRENT-STATE.md) yenileyin. Ertelenen bulguları [inceleme listesinde](docs/REVIEW-BACKLOG.md) tutun. Öneriyi kabul edilmiş mimari karar saymayın.

Özellik geliştirmeden önce ilgili alan belgesini okuyun:

| Alan | Bağlayıcı belge |
| :--- | :--- |
| Genel bakış ve standartlar | [docs/README.md](docs/README.md) |
| Ürün vizyonu ve kapsam | [docs/PRODUCT.md](docs/PRODUCT.md) |
| Alan modeli ve kavramlar | [docs/DOMAIN.md](docs/DOMAIN.md) |
| Ortak terimler | [docs/GLOSSARY.md](docs/GLOSSARY.md) |
| Mimari ve alan sınırları | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Depo düzeni | [docs/REPOSITORY-STRUCTURE.md](docs/REPOSITORY-STRUCTURE.md) |
| Erişim kontrolü | [docs/ROLES-AND-PERMISSIONS.md](docs/ROLES-AND-PERMISSIONS.md) |
| Arayüz ve ekranlar | [docs/SCREEN-INVENTORY.md](docs/SCREEN-INVENTORY.md) |
| Tasarım sistemi | [docs/DESIGN-SYSTEM.md](docs/DESIGN-SYSTEM.md) |
| Durum makineleri | [docs/STATE-MACHINES.md](docs/STATE-MACHINES.md) |
| Hata akışları | [docs/NEGATIVE-FLOWS.md](docs/NEGATIVE-FLOWS.md) |
| Çok işletmeli yalıtım | [docs/MULTI-TENANCY.md](docs/MULTI-TENANCY.md) |
| Ödeme, bahşiş, komisyon | [docs/PAYMENTS-TIPS-COMMISSIONS.md](docs/PAYMENTS-TIPS-COMMISSIONS.md) |
| Sipariş yönlendirme ve yazdırma | [docs/ORDER-ROUTING-AND-PRINTING.md](docs/ORDER-ROUTING-AND-PRINTING.md) |
| Canlı olaylar ve bildirimler | [docs/REALTIME-AND-NOTIFICATIONS.md](docs/REALTIME-AND-NOTIFICATIONS.md) |
| Test standartları | [docs/TESTING.md](docs/TESTING.md) |
| Güvenlik | [docs/SECURITY.md](docs/SECURITY.md) |
| Ortamlar | [docs/ENVIRONMENTS.md](docs/ENVIRONMENTS.md) |
| Teslimat ve sürümler | [docs/DELIVERY.md](docs/DELIVERY.md) |
| Blue/Green işletimi | [docs/BLUE-GREEN-RUNBOOK.md](docs/BLUE-GREEN-RUNBOOK.md) |
| Olay müdahalesi | [docs/INCIDENT-RESPONSE.md](docs/INCIDENT-RESPONSE.md) |
| Yol haritası | [docs/ROADMAP.md](docs/ROADMAP.md) |
| Mimari karar kayıtları | [docs/adr/README.md](docs/adr/README.md) |
| İşletim kılavuzları | [docs/runbooks/README.md](docs/runbooks/README.md) |
| Şablonlar | [docs/templates/](docs/templates/) |
