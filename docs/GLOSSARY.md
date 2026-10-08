# Terminoloji Sözlüğü (`docs/GLOSSARY.md`)

Bu belge, kod, veritabanı şemaları, kullanıcı arayüzü etiketleri ve belgelerde kullanılan standartlaştırılmış iki dilli (Türkçe ve İngilizce) sözcükleri oluşturur. `restaurant-order`.

---

## 1. Alan ve Organizasyonel Kavramlar

| İngilizce Terim | Türkçe Karşılığı | Tanım ve Bağlam |
| :--- | :--- | :--- |
| **Tenant / Organization** | Organizasyon / İşletme | Platforma abone olan üst düzey tüzel kişilik. |
| **Brand** | marka | Bir kuruluşa ait farklı bir mutfak konsepti veya ticari adı. |
| **Branch** | Şube | Bir marka altında faaliyet gösteren fiziksel bir restoran lokasyonu. |
| **Dining Area** | Salon / Alan / Bölüm | Bir şube içindeki fiziksel bölge (örneğin Salon, Teras, Bahçe, Bar). |
| **Table** | Masa | Şube başına benzersiz bir tanımlayıcıya sahip, belirlenmiş bir fiziksel yemek masası. |
| **Table Session** | Masa Oturumu | Bir masanın ilk oturma/siparişten fatura ödemesine kadar olan zamansal yemek yaşam döngüsü. |
| **Table QR Code** | Masa QR Kodu | Bir şubedeki belirli bir masaya bağlı benzersiz bir taranabilir matris barkodu. |

---

## 2. Katalog ve Menü Konseptleri

| İngilizce Terim | Türkçe Karşılığı | Tanım ve Bağlam |
| :--- | :--- | :--- |
| **Menu** | Menü | Bir şube veya hizmet dönemine ilişkin yiyecek ve içecek tekliflerinin kataloğu. |
| **Category** | Kategori | Bir grup ilgili menü öğesi (örneğin Başlangıçlar, Ana Yemekler, İçecekler). |
| **Menu Item** | Menü Ürünü / Yemek | Menüde ayrı ayrı satılabilen bir ürün. |
| **Variant** | Varyant / Porsiyon | Bir menü öğesinin kendi fiyatıyla birlikte belirli bir kısmı veya boyutu (örneğin 200g vs 300g). |
| **Modifier Group** | Seçenek Grubu / Opsiyon | Bir özelleştirme koleksiyonu (örneğin Pişme Derecesi, Yan Ürün Seçimi). |
| **Modifier Item** | Seçenek / Ekstra | Bir grup içinde bireysel kişiselleştirme seçeneği (örneğin Az Pişmiş, Ekstra Peynir). |
| **86ed / Out of Stock** | Tükendi / Stokta Yok | Tüm müşteri ve personel ekranlarında geçici olarak kullanılamaz olarak işaretlenen bir öğe. |

---

## 3. Sipariş ve Mutfak Operasyonları

| İngilizce Terim | Türkçe Karşılığı | Tanım ve Bağlam |
| :--- | :--- | :--- |
| **Order** | Sipariş | Bir müşterinin veya garsonun yiyecek/içecek ürünlerine ilişkin talebi. |
| **Order Item** | Sipariş Kalemi | Değiştiriciler ve notlar da dahil olmak üzere bir sipariş içindeki tek bir öğe satırı. |
| **KDS** | Mutfak Ekranı (KDS) | Mutfak Ekran Sistemi; Hazırlık personeline hazırlık fişleri gösteren dijital ekran. |
| **Station Ticket** | İstasyon Fişi | Belirli bir hazırlık istasyonuna (Mutfak veya Bar) yönlendirilen bir alt hazırlık fişi. |
| **Kitchen Ticket** | Mutfak Fişi | Sıcak/soğuk yiyecek içeren bir hazırlık fişi mutfak hattına yönlendirilir. |
| **Bar Ticket** | Bar Fişi | Bar hazırlama tezgahına yönlendirilen, içecekleri içeren bir hazırlık fişi. |
| **Station Routing** | İstasyon Yönlendirme | Sipariş öğelerini otomatik olarak belirlenen yerlere gönderen mantık KDS ve yazıcılar. |
| **Prep Time / Timer** | Hazırlık Süresi | Hazırlık Fişin hazırlık istasyonu tarafından alınmasından bu yana geçen süre. |
| **Recall Ticket** | Fişi Geri Çağır | Yanlışlıkla tamamlandı olarak işaretlenen bir hazırlık fişin yeniden açılması KDS. |

---

## 4. Faturalandırma, Ödemeler ve Finans

| İngilizce Terim | Türkçe Karşılığı | Tanım ve Bağlam |
| :--- | :--- | :--- |
| **Bill / Check** | Hesap / Adisyon | Bir masa oturumuna ilişkin öğelerin, vergilerin ve indirimlerin ayrıntılarını içeren toplam fatura. |
| **Split Bill** | Hesabı Bölme | Bir faturayı birden fazla kısmi ödemeye bölmek (tutar veya belirli kalemlere göre). |
| **Payment** | Ödeme | Aktif bir faturanın bir kısmını veya tamamını kapatan bir finansal işlem. |
| **Tip / Gratuity** | Bahşiş | Müşteri tarafından servis personeli için eklenen isteğe bağlı bir parasal hediye. |
| **Platform Commission** | Platform Komisyonu | İşlenen işletme işlemlerinden kesilen platform ücreti. |
| **Refund** | İade | Daha önce yapılmış bir ödemenin müşteriye geri döndürülmesi. |
| **Void / Cancellation** | İptal | Ödeme yapılmadan önce ödenmemiş bir ürünü veya siparişi iptal etmek (yetki gerektirir). |

---

## 5. Donanım ve Gerçek Zamanlı Şartlar

| İngilizce Terim | Türkçe Karşılığı | Tanım ve Bağlam |
| :--- | :--- | :--- |
| **ESC/POS** | ESC/POS Protokolü | Termal makbuz yazıcıları için endüstri standardı komut protokolü. |
| **Network Printer** | Ağ / Termal Yazıcı | Yerel şube ağına Ethernet/Wi-Fi aracılığıyla bağlanan termal yazıcı. |
| **Chime / Buzzer** | Uyarı Zili / Bildirim Sesi | Yeni sipariş veya çağrı geldiğinde KDS ya da mobil uygulamada çalınan uyarı. |
| **Waiter Call** | Garson Çağırma | QR uygulamasından garson yardımı talep eden bir müşteri işlemi. |
| **Realtime Channel** | Gerçek Zamanlı Kanal | Bir WebSocket veya SSE bağlantı akışı canlı durum güncellemeleri. |

---

## 6. Roller ve Operasyonel Koşullar

| Rol / Terim | Türkçe Karşılığı | Kapsam |
| :--- | :--- | :--- |
| **Super Admin** | Süper Yönetici | Platform çapında yönetim ve işletme yönetimi. |
| **Restaurant Admin** | Restoran Yöneticisi | Marka/kuruluş yönetimi ve finansal raporlama. |
| **Branch Manager** | Şube Müdürü | Günlük şube operasyonları, vardiya yönetimi ve masa düzenleri. |
| **Operations / Cashier** | Operasyon / Kasa | Yazar kasa, fatura kesme ve POS ödeme tahsilatı. |
| **Kitchen** | mutfak | Yiyecek hazırlama hattı ve KDS kuyruk yönetimi. |
| **Bar** | Bar | İçecek hazırlama hattı ve bar KDS kuyruk yönetimi. |
| **Waiter** | Garson | Masa servisi, sipariş alma ve misafir asistanlığı. |
| **Customer** | Müşteri | Yemek konuğu QR menüsüne erişiyor ve sipariş veriyor. |
| **Blue/Green Deployment** | Blue/Green Dağıtım | İki özdeş üretim yuvasını kullanan sıfır kesinti süreli sürüm yöntemi. |
| **Incident (Sev-1 to Sev-4)** | Olay / Kesinti Seviyeleri | Kritik kesintiden küçük kusura kadar üretim olayı sınıflandırmaları. |
