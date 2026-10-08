# Beş Arayüz İçin Ekran Envanteri (`docs/SCREEN-INVENTORY.md`)

## 1. Arayüz 1: QR Müşteri Web Uygulaması (Mobil Öncelikli Misafir Uygulaması)

| Ekran Kimliği | Ekran Adı | Erişim Rolü | Birincil Eylemler | Görüntülenen Temel Veriler |
| :--- | :--- | :--- | :--- | :--- |
| `CUST-01` | **Masa Karşılama Ekranı** | Müşteri | QR'yi tarayın, şube ve masa bilgilerini görüntüleyin, menüye girin. | Şube adı, masa numarası, karşılama notu, aktif dil seçici. |
| `CUST-02` | **Dijital Menü ve Kategoriler** | Müşteri | Kategorilere göz atın, öğeleri arayın, diyete/alerjenlere göre filtreleyin. | Kategori çubuğu, ürün kartları (fotoğraf, başlık, fiyat, 86 Rozet). |
| `CUST-03` | **Ürün Özelleştirme Penceresi** | Müşteri | Çeşitleri seçin, gerekli/isteğe bağlı değiştiricileri seçin, not ekleyin. | Baz fiyat, değiştirici gruplar, dinamik toplam fiyat, "Sepete Ekle" CTA. |
| `CUST-04` | **Sepet ve Sipariş İncelemesi** | Müşteri | Miktarları ayarlayın, değiştiricileri gözden geçirin, siparişi mutfağa gönderin. | Satır öğeleri, seçilen değiştiriciler, alt toplam, vergi tahmini, gönder düğmesi. |
| `CUST-05` | **Canlı Sipariş Takibi** | Müşteri | Hazırlık aşamalarını takip edin, oturumdaki geçmiş siparişleri inceleyin. | Durum zaman çizelgesi (Alındı -> Hazırlanıyor -> Sunuldu), tahmini hazırlık süresi. |
| `CUST-06` | **Hizmet Talebi Modu** | Müşteri | Garson isteyin, ıslak mendil isteyin veya fatura isteyin. | İşlem düğmeleri ("Garson Çağır", "Fatura İste"), onay isteği. |

---

## 2. Arayüz 2: Garson & Operasyon Mobil Uygulaması (El Tipi Web Uygulaması)

| Ekran Kimliği | Ekran Adı | Erişim Rolü | Birincil Eylemler | Görüntülenen Temel Veriler |
| :--- | :--- | :--- | :--- | :--- |
| `WAIT-01` | **Hızlı PIN Kilit ekranı** | Garson, Kasa, Müdür | Güvenilir terminalde dört haneli PIN ile giriş yapın, kullanıcıyı değiştirin, şube/vardiya seçin. | Sayısal tuş takımı, personel adı, şube seçici. |
| `WAIT-02` | **Kat Planı ve Masa Izgarası** | Garson, Kasa, Müdür | Masa seçin, alana göre filtreleyin (Teras, Salon), masa durumunu görüntüleyin. | Renk kodlu masalar (Boş, Oturmalı, Sipariş Bekleniyor, Fatura İsteniyor). |
| `WAIT-03` | **Masa Oturumu Detayı** | Garson, Kasa, Müdür | Verilen siparişleri görüntüleyin, ürün ekleyin, fatura isteyin, masayı taşıyın. | Masa numarası, oturma süresi, ayrıntılı sipariş listesi, toplam bakiye. |
| `WAIT-04` | **Hızlı Sipariş Girişi** | Garson, Kasa | Hızlı kategoriye dokunma, değiştirici seçimi, özel not, siparişi gönderme. | Kompakt menü ızgarası, hızlı değiştirme açılır penceresi, sipariş hazırlama çekmecesi. |
| `WAIT-05` | **Masa Taşıma / Birleştirme** | Garson, Müdür | Siparişi başka bir masaya aktarın, iki masayı birleştirin. | Kaynak masa, hedef masa seçici, birleştirme onayı. |
| `WAIT-06` | **Mobil Fatura ve Ödeme**| Garson, Kasa | Ödeme yöntemini seçin, faturayı bölün, nakit/kart ödemesini kaydedin. | Ödenmesi gereken bakiye, tutara veya öğeye göre bölme, bahşiş girişi, makbuz yazdırma CTA. |
| `WAIT-07` | **Bildirim Çekmecesi** | Garson | Müşteri çağrılarını kabul edin, hazır yemek bildirimlerini görüntüleyin. | Servis çağrısı uyarıları (Masa 12: Garson Çağrısı), KDS hazır uyarılar. |

---

## 3. Arayüz 3: Mutfak & Bar KDS (İstasyon Görüntüleme Sistemi)

| Ekran Kimliği | Ekran Adı | Erişim Rolü | Birincil Eylemler | Görüntülenen Temel Veriler |
| :--- | :--- | :--- | :--- | :--- |
| `KDS-01` | **İstasyon Hazırlık Fişi Sırası** | Mutfak, Bar, Müdür | İstasyona göre filtreleyin (Mutfak / Bar), fişi ilerletin (Hazırlanıyor -> Hazır). | Geçen zamanlayıcıları (yeşil/sarı/kırmızı) içeren sipariş kartları tablosu. |
| `KDS-02` | **Hazırlık Fişi Detay Kartı** | Mutfak, Bar | Tek tek öğeleri tamamlandı olarak işaretleyin, özel alerji notlarını görüntüleyin. | Sipariş #, Masa #, öğe adları, vurgulanan değiştiriciler, garson adı. |
| `KDS-03` | **Hazırlık Fişini Geri Çağırma Penceresi** | Mutfak, Bar, Müdür | Son tamamlanan 20 fişi inceleyin, yanlışlıkla tamamlanan fişi geri açın.| Zaman damgası ve artış geçmişiyle birlikte tamamlanan hazırlık fişlerin listesi. |
| `KDS-04` | **Hızlı 86 Stok Tükenme Modu** | Mutfak, Bar, Müdür | Öğeyi arayın, stokta yok (86) tek dokunuşla durum. | Aktif geçiş anahtarlarına sahip menü öğesi listesi. |

---

## 4. Arayüz 4: Restoran Yönetim Paneli (Masaüstü / Tablet Web)

| Ekran Kimliği | Ekran Adı | Erişim Rolü | Birincil Eylemler | Görüntülenen Temel Veriler |
| :--- | :--- | :--- | :--- | :--- |
| `ADM-01` | **Operasyon Kontrol Paneli** | Restoran Yöneticisi, Müdür | Canlı satışları, aktif masaları, açık siparişleri, istasyon hazırlık gecikmesini görüntüleyin. | KPI kartlar, canlı kat widget'ı, saatlik satış tablosu, en çok satanlar. |
| `ADM-02` | **Menü Katalog Düzenleyici** | Restoran Yöneticisi, Şube Müdürü | Menüler, kategoriler, öğeler, çeşitler, değiştirici gruplar, diyet/alerjen meta verileri ve kullanılabilirlik oluşturun/düzenleyin. | Şube kapsamlı menü seçici, kategori ve öğe listeleri, editör sayfaları, güvenli katalog önizlemesi, Hızlı-86 ve stok yenileme kontrolleri. |
| `ADM-03` | **Değiştirici Gruplar Yöneticisi** | Restoran Yöneticisi, Müdür | Değiştirici gruplar oluşturun, min/maks seçimlerini ayarlayın, öğelere atayın. | Değiştirici grupları tablosu, bağlantılı öğeler, değiştirici fiyatlandırması. |
| `ADM-04` | **Zemin ve Masa Düzeni** | Restoran Yöneticisi, Müdür | Yemek alanları oluşturun, masa ekleyin/konumlandırın, masa numaraları atayın. | Görsel tuval veya ızgara, masa kapasitesi, alan sekmeleri. |
| `ADM-05` | **QR Kod Oluşturucu** | Restoran Yöneticisi, Müdür | Masa QR kodları oluşturun, baskıya hazır olarak indirin PDF/SVG olarak topluca. | Masa listesi, QR önizlemesi, toplu indirme CTA, özel markalama seçenekleri. |
| `ADM-06` | **Personel ve Roller Dizini** | Restoran Yöneticisi, Müdür | Personeli davet edin, rolleri atayın, ayarlayın/sıfırlayın 4-haneli PIN'ler. | Kullanıcı listesi, atanan roller, aktif durum, şube ataması. |
| `ADM-07` | **Yazıcılar ve Yönlendirme** | Restoran Yöneticisi, Müdür | Ekle ESC/POS ağ yazıcıları, kategorileri istasyonlara eşleyin. | Yazıcı IP/portu, istasyon eşlemesi (Bar, Mutfak, Kasiyer), test baskısı. |
| `ADM-08` | **Finansal ve Z Raporları** | Restoran Yöneticisi, Müdür | Günlük satışları, gün sonu Z raporunu, vergi özetlerini dışa aktarın. | Günlük ciro, ödeme kırılımı (Nakit, Kart), indirim toplamları. |
| `ADM-09` | **Marka ve Şube Teması** | Restoran Yöneticisi, Müdür | Renkleri, logoyu, kenarlık yarıçapını özelleştirin, kontrol edin WCAG kontrastını. | Palet girişleri, kontrast rozeti, belirteç önizlemesi, sıfırlama düğmesi. |
| `ADM-10` | **Gezinme Yapılandırması** | Restoran Yöneticisi | Yönetici gezinme öğelerini etkinleştirin/devre dışı bırakın, etiketleri özelleştirin. | Rota kontrol listesi, ekran etiketleri, sipariş kontrolleri. |
| `ADM-11` | **Şube Ayarları ve Saatleri**| Restoran Yöneticisi, Müdür | Para birimini, saat dilimini, temel vergi/hizmet oranlarını, haftalık saatleri yapılandırın. | Fiyat girişleri, vergi dahil etme anahtarı, çalışma saatleri programı. |
| `ADM-12` | **Yemek Alanları Yönetimi** | Restoran Yöneticisi, Müdür | Yemek alanları ekleyin/düzenleyin (İç Mekan, Teras, Bahçe), aktif durumu değiştirin. | Alan kodu, alan türü rozeti, sıralama düzeni, etkin geçiş. |
| `ADM-13` | **Hazırlama İstasyonları** | Restoran Yöneticisi, Müdür | İstasyon ekleyin/düzenleyin (Mutfak, Bar, Diğer), aktif durumu değiştirin. | İstasyon kodu, görünen ad, istasyon türü rozeti, etkin geçiş. |
| `ADM-14` | **Şube Özelliği Bayrakları** | Restoran Yöneticisi, Müdür | Operasyonel özellik bayraklarını şununla değiştirin: RBAC önceliğini koruyarak. | Bayrak anahtarı, etkin geçiş, açıklamalar, denetim durumu. |

---

## 5. Arayüz 5: Platform Süper Yönetici Paneli (Masaüstü Web)

| Ekran Kimliği | Ekran Adı | Erişim Rolü | Birincil Eylemler | Görüntülenen Temel Veriler |
| :--- | :--- | :--- | :--- | :--- |
| `SPAD-01`| **İşletme Dizini** | Süper Yönetici | Restoran organizasyonlarını arayın, filtreleyin, ekleyin veya askıya alın. | İşletme listesi, abonelik durumu, aktif şubeler, oluşturulma tarihi. |
| `SPAD-02`| **İşletme Katılım Formu**| Süper Yönetici | İşletme oluşturun, ilk yöneticiyi atayın, özel etki alanlarını yapılandırın. | Kuruluş ayrıntıları, marka adı, ilk şube, faturalandırma planı seçici. |
| `SPAD-03`| **Abonelik ve Faturalandırma** | Süper Yönetici | Platform katmanlarını, komisyon oranlarını yönetin, toplu hacmi görüntüleyin.| Plan fiyatlandırması, aktif işletme sayıları, aylık platform geliri. |
| `SPAD-04`| **Platform Denetim Günlükleri** | Süper Yönetici | Denetim olaylarını işletmeye, aktöre veya eylem türüne göre filtreleyin. | Zaman damgası, işletme kimliği, aktör, etkinlik eylemi, IP adresi. |
| `SPAD-05`| **Sistem Sağlığı Monitörü** | Süper Yönetici | Veritabanı gecikmesini, gerçek zamanlı soket bağlantılarını, çalışan kuyruklarını kontrol edin. | Durum göstergeleri, hata oranı grafikleri, aktif bağlantı sayaçları. |
## Aşama 5 Yönetici Kataloğu

Yönetici menüsü ekranı şube menülerini, kategorileri, öğeleri, çeşitleri, değiştirici grupları/seçenekleri, diyet/alerjen meta verilerini ve hızlı86/yeniden stokla. Mevcut yönetici kabuğunu ve tasarım sistemini, duyarlı düzenleyici sayfalarını, güvenli metin önizlemesini ve izne duyarlı kontrolleri kullanır. Yalnızca katalog yönetimidir; müşteri siparişi daha sonraki bir aşama olarak kalır.
