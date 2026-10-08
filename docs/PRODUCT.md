# Ürün Spesifikasyonu ve Vizyonu (`docs/PRODUCT.md`)

## 1. Yönetici Özeti

`restaurant-order` entegre, çok işletmeli bir dijital yemek ve operasyonel platformdur. Parçalanmış mirasın yerini alıyor POS terminaller, kağıt hazırlık fişleri ve statik PDF misafirleri, garsonları, mutfak personelini ve restoran işletmecilerini birbirine bağlayan senkronize, gerçek zamanlı bir ekosisteme sahip menüler.

---

## 2. Sorun Açıklaması ve Değer Önerisi

### 2.1. Çözülen Sorunlar
- **Masa Devir Süresi:** Menü, garson veya hesap bekleme nedeniyle yemek dışında geçen 15–20 dakikalık süreyi azaltmak hedeflenir.
- **Sipariş Transkripsiyon Hataları:** Sözlü siparişler veya elle yazılan kağıt hazırlık fişleri, değiştiricilerin yanlış yerleştirilmesine, gıda israfına ve müşteri memnuniyetsizliğine yol açar.
- **Mutfak ve Servis İletişiminin Kopması:** Kat personelinin mutfak hazırlığı sürecini görememesi, mutfak geçişine tekrar tekrar gitmesine neden oluyor.
- **Çoklu Şube Yönetim Giderleri:** Çok lokasyonlu markalar, merkezi menü sunumları, farklı fiyatlandırma ve parçalı raporlamayla mücadele ediyor.

### 2.2. Temel Değer Önerisi
- **Misafirler için:** QR aracılığıyla anında menü erişimi, gerçek zamanlı ürün kullanılabilirliği, görsel değiştirici seçimi ve şeffaf fatura takibi.
- **Garsonlar için:** Elde taşınır mobil sipariş, hızlı masa taşıma, hızlı değiştirici seçimi ve otomatik hizmet uyarıları.
- **Mutfak ve Bar için:** Dijital KDS akıllı istasyon yönlendirme (yiyecekten mutfağa, içeceklerden bara), hazırlama zamanlayıcısı renk kodlaması ve tek dokunuşla öğe 86'yı işaretleme (stokta yok olarak işaretleme) ile.
- **Restoran İşletmecileri için:** Merkezi menü ve şube yönetimi, canlı kat durumu ve ayrıntılı gelir analitiği.
- **Platform Yöneticileri için:** Tam işletme yaşam döngüsü kontrolü, organizasyon hiyerarşisi yönetimi ve platform faturalandırması.

---

## 3. 5 Ürün Arayüzleri

```text
Platform Süper Yönetici Paneli
  İşletme açılışı, abonelik/faturalandırma, sistem sağlığı
        ↓
Restoran Yönetim Paneli
  Menü, şube düzeni, personel yetkileri, raporlar, yazıcılar
        ↓
QR Müşteri Web     Garson Mobil       Mutfak / Bar KDS
Menü, sepet,       Masa, sipariş,     İstasyon kuyrukları,
çağrı, hesap       taşıma, ödeme      hazırlık zamanlayıcıları
```

### 3.1. Arayüz 1: QR Müşteri Web Uygulaması
- **Form Faktörü:** Mobil öncelikli, son derece duyarlı web uygulaması. Uygulama mağazası kurulumu gerekmez.
- **Aktivasyon:** `tenant_id`, `branch_id` ve `table_id` içeren dinamik veya statik masa QR kodu taranır. Bu kimlikler tek başına yetki vermez; oturum yetkilendirmesi ayrıca doğrulanır.
- **Temel Yetenekler:**
  - Yüksek çözünürlüklü görüntüler, alerjen etiketleri ve diyet rozetleri içeren dinamik dijital menü.
  - Etkileşimli değiştirici yapılandırması (örneğin et pişmesi, ekstra soslar, uzaklaştırmalar).
  - Canlı toplam ve vergi dökümü ile sepet yönetimi.
  - Şubenin sipariş akışına masaya bağlı sipariş gönderimi.
  - Hizmet talepleri ("Garson Çağır", "Islak Mendil İste", "Fatura İste").
  - Canlı sipariş takibi (Gönderildi -> Hazırlanıyor -> Sunuldu).

### 3.2. Arayüz 2: Garson & Operasyon Mobil Uygulaması
- **Form Faktörü:** Akıllı telefonlar ve elde taşınır cihazlar için tasarlanmış, dokunmatik optimizasyonlu mobil web uygulaması POS devices.
- **Temel Yetenekler:**
  - Masa durumlarını (Boş, Oturmalı, Sipariş Beklemede, Sunuldu, Fatura İstendi) içeren etkileşimli kat planı.
  - Gelen misafirler veya yardımlı sipariş için hızlı sipariş girişi ve değiştirici özelleştirmesi.
  - Masa yönetimi: birleştirme, ayırma ve masalar arasında sipariş taşıma.
  - Gerçek zamanlı garson bildirimleri (misafir servis çağrıları, mutfakta hazır yiyecekler).
  - POS ödeme tahsilatı entegrasyonu (nakit, harici kart terminali veya dijital bölme).

### 3.3. Arayüz 3: Mutfak & Bar KDS (Mutfak Ekran Sistemi)
- **Form Faktörü:** Zorlu mutfak ortamları için optimize edilmiş yatay tablet ve ticari dokunmatik ekran.
- **Temel Yetenekler:**
  - İstasyona özel hazırlık fişi filtreleme: Mutfak istasyonu mutfak hazırlık fişlerini görüntüler; Bar istasyonu içecek hazırlık fişlerini sergiliyor.
  - Görsel zaman takibi: Renk kodlu kartlar (Yeşil: <10 m, Sarı: 10–20m, Kırmızı: >20m gecikmiş).
  - Hazırlık Fişi ilerlemesi: "Hazırlık Halinde İşaretle" -> "Hazır Olarak İşaretle" -> "Hazırlık Fişi Geri Çağır".
  - Tek dokunuşla öğe 86'lama (tüm müşteri menülerinde ve garson uygulamalarında stokta yok durumunu anında işaretleyin).
  - Yeni gelen siparişler ve acil garson çağrıları için sesli uyarılar.

### 3.4. Arayüz 4: Restoran Yönetim Paneli
- **Form Faktörü:** Restoran Yöneticileri ve Şube Müdürleri için masaüstü ve tablet web kontrol paneli.
- **Temel Yetenekler:**
  - Menü mühendisliği: kategoriler, öğeler, değişken fiyatlandırma, değiştirici gruplar ve karma yemekler.
  - Şube düzeni editörü: yemek alanları (İç Mekan, Teras, Bahçe), masa numaralandırma ve QR kod oluşturma/dışa aktarma.
  - Personel yönetimi: kullanıcı davetleri, rol ataması (RBAC) ve PIN-hızlı mobil giriş için kod yönetimi.
  - Donanım yapılandırması: ağ termal yazıcı kurulumu (ESC/POS), istasyon yönlendirme kuralları.
  - Operasyon raporlaması: satış özetleri, yoğun saat analizi, ürün popülerliği ve personel performansı.

### 3.5. Arayüz 5: Platform Süper Yönetici Paneli
- **Form Faktörü:** Platform operatörleri için masaüstü web uygulaması.
- **Temel Yetenekler:**
  - İşletme yaşam döngüsü: organizasyon oluşturma, askıya alma, özel alan bağlama.
  - Abonelik ve faturalandırma: katman yönetimi, platform komisyon oranı yapılandırması, fatura oluşturma.
  - Sistem çapında denetim günlüğü ve operasyonel durum izleme.

---

## 4. Mimari ve Teknoloji Durumu

Kabul edilmiş teknoloji ve kalıcılık kararları [ADR dizininde](./adr/README.md) bağlayıcıdır. Aşağıdaki erken ürün taslağı seçenekleri bu kararları geçersiz kılmaz; karara bağlanmamış konular `[Proposed / ADR Required]` olarak kalır:

- **Ön Uç:** React 19, Vite, TypeScript ve PWA yaklaşımı — ADR-0001.
- **Arka Uç:** .NET 10 / ASP.NET Core modüler monolit — ADR-0001.
- **Canlı Olay Taşıma:** SignalR ve Redis backplane — ADR-0001. Arka plan push sağlayıcısı ayrı karardır.
- **Veritabanı:** PostgreSQL RLS ve EF Core — ADR-0001/0002.
- **Çevrimdışı / Donanım Proxy'si:** Yerel yazıcı köprüsü hizmeti ESC/POS ağ yazdırma `[Proposed / ADR Required]`
