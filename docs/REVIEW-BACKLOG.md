# İnceleme Bulguları ve Öneriler

İnceleme: 2026-10-08. Temel: `467737b` (Faz 5 birleştirildi). İzleme: [#8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), düzeltme incelemesi: [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9).

Doğrulanmış hatalar, belge uyumsuzlukları ve ürün önerileri ayrı değerlendirilir. Öneriler onaylanmış mimari veya tamamlanmış özellik değildir. Uygulamaya başlanırken ilgili bulgu için odaklı issue açılır; test, ADR ve PR bağlantıları buraya eklenir.

| Kimlik | Öncelik / Durum | Bulgu ve Sonraki Adım |
| :--- | :--- | :--- |
| R01 | P0 / Görev dalında düzeltildi, incelemede | Eşit hesap bölme negatif borç üretebiliyordu (0.02 / 4). Artan kuruşlar kişilere birer birer dağıtıldı; kesirli kuruş girdileri reddediliyor. Toplamın korunması, negatif olmama ve adil dağılım regresyon testleri eklendi. |
| R02 | P1 / Öneri, ADR gerekli | Masa durumu doluluk, hazırlık ve ödeme kavramlarını karıştırıyor. Fiziksel masa, masa oturumu, sipariş kalemi hazırlığı ve hesap ödemesi ayrılmalı; ekran rozetleri bunlardan türetilmeli. Faz 6 öncesinde çoklu sipariş turları, kısmi servis, hazırlık sırasında hesap isteme, boş oturumu kapatma, taşıma ve birleştirme tanımlanmalı. |
| R03 | P1 / Belge düzeltildi, ürün kararı ertelendi | Durum örnekleri müşteri iptali, mutfağın servis yapması ve ödeme tahsilatında RBAC ile çelişiyordu. Mevcut izinler korundu. Müşterinin iptal talebi ayrı bir öneridir; doğrudan iptal hakkı vermez. |
| R04 | P1 / Öneri | Önce tek bir tam restoran akışı sunulmalı: oturum aç, menüye bak, sipariş ver, hazırla, servis et, nakit/harici terminal ödemesini kaydet, kapat. Faz 7 müşteri akışının Faz 8 siparişe, Faz 13 hesabın Faz 16 ödemeye bağımlılığı netleştirilmeli. Gelişmiş ödeme, yazdırma, bahşiş ve raporlama ilk pilot dışında tutulabilir. |
| R05 | P1 / Güvenlik önerisi | Statik QR imzası değiştirmeyi engeller, kopyalamayı engellemez. Menü erişimi ile güncel masa oturumu erişimi ayrılmalı; süre sonu/iptal, birden fazla misafir, kapatma/taşıma/birleştirme davranışı belirlenmeli. `table_session_id` kapsamlı müşteri belirtecinin verilme ve oturuma katılma politikası tanımlanmalı. |
| R06 | P1 / Uygulama açığı | Politika OpenAPI'den üretim gerektiriyor; TypeScript sözleşmeleri hâlen elle tutuluyor. Tekrarlanabilir üretim ve CI sapma denetimi eklenmeli. Geniş `literal union | string` tanımları ancak bilinmeyen değer politikası ve istemci testleriyle daraltılmalı. |
| R07 | P1 / Güvenilirlik önerisi | Redis `IN_PROGRESS` TTL rezervasyonu, çökme veya kira süresi dolduğunda dış etkinin en fazla bir kez oluşmasını tek başına garanti etmez. Kalıcı işletme kapsamlı iş anahtarları, istek karmaları, sahiplik/fencing, sağlayıcı idempotency desteği ve mutabakat tanımlanmalı. Belirsiz yazdırma sonucu ve açık yeniden baskı görünür olmalı. İş yürütme henüz plan aşamasındadır; bu bulgu gerçek üretim ödemelerinin yinelendiğinin kanıtı değildir. |
| R08 | P1 / Test kapsamı açığı | Mevcut E2E testleri iki HTTP sağlık kontrolüdür. Alan özellikleri geldikçe gerçek tarayıcı restoran akışı, eşzamanlı gönderim, iptal/hazırlık yarışı, sabitlenmiş sipariş fiyatı, kısmi ödeme bakiyesi ve eski oturum reddi eklenmeli. |
| R09 | P1 / Para modeli önerisi | Katalog tamsayı küçük birim, eski fiyatlandırma yardımcısı iki ondalıklı tutar kullanıyor. Mevcut düzeltmede imza korundu; sipariş/hesap entegrasyonundan önce ortak para sınırı, desteklenen para birimi hassasiyeti, dönüşüm, vergi yuvarlama ve baz puan oranları belirlenmeli. |
| R10 | P2 / Geliştirici deneyimi açığı | Yerel kurulum özel başlatma/proxy yardımcılarına ihtiyaç duydu. Ön uç ayarları `VITE_API_URL` değerini beklendiği gibi kullanmıyor; `.env` kopyalama gizli değer denetimiyle çakışıyor; entegrasyon testleri geliştirme anahtarı varsayıyor ve Docker sağlık kontrolleri yükte zaman aşımına uğrayabiliyor. Git dışında gizli değerler ve bağımsız test yapılandırmasıyla tekrarlanabilir kurulum sağlanmalı. |
| R11 | P1 / Ödeme mutabakatı önerisi | Kısmi iade olmasına rağmen tek `REFUNDED` hesap durumu kullanılıyor. Değiştirilemez ödeme/iade kayıtları, toplam iade edilebilir tutar, hesap düzeltmeleri, kalan bakiye ve kapalı oturuma etkisi tanımlanmalı. ROADMAP içindeki Kabul Edildi/Ödendi/Kapalı sipariş durumları STATE-MACHINES hazırlık durumlarıyla uzlaştırılmalı. |
| R12 | P2 / Görev dalına eklendi | Issue ile başlama ve kalıcı devir kaydı eksikti. CONTRIBUTING-WORKFLOW, CURRENT-STATE, görev şablonu ve PR devir alanları eklendi; AGENTS.md ile bağlandı. |

## Korunacak Temel

Modüler monolit, mevcut teknoloji yığını, PostgreSQL RLS ve işletme kısıtları, ayrıcalıksız çalışma zamanı, merkezi RBAC, iyimser eşzamanlılık, işlemsel outbox ve otomatik test temeli korunmalı. Dağıtım karmaşıklığını artırmadan iş sözleşmeleri ve eksiksiz restoran akışı iyileştirilmeli.

## Önerilen Sonraki Karar

Tek şubeli pilot seçilip R02, R03, R05, R09 ve R11 Faz 6 öncesinde odaklı şartname/ADR ile çözülmeli. Bu liste tamamlanmış fazların yeniden yazılması talimatı değildir.
