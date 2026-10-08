# Negatif Akışlar ve Uç Durumlar (`docs/NEGATIVE-FLOWS.md`)

## 1. Kapsam

Bu belge ağ, donanım, ödeme, güvenlik ve eşzamanlılık hatalarında beklenen davranışı tanımlar. Sipariş/masa/ödeme akışlarının henüz uygulanmamış bölümleri hedef davranıştır; Faz 4–5 doğrulamaları ayrı izlenir.

## 2. Operasyon Hataları

### 2.1. Sipariş Gönderilirken Ürünün Tükenmesi (86 Yarışı)

Misafir son porsiyonu sepetine ekledikten sonra mutfak ürünü 86 (tükendi) olarak işaretleyebilir. Kullanılabilirlik, veritabanı işlemi içinde kayıttan hemen önce yeniden doğrulanır. İşlem `ITEM_OUT_OF_STOCK` ile reddedilir; sepette sorunlu ürün vurgulanır ve diğer ürünler korunur. Mesaj: "Bu ürün az önce tükendi. Devam etmek için lütfen sepetten kaldırın."

### 2.2. Aynı Masadan Eşzamanlı Sipariş

Masa 4'te iki misafir aynı anda sipariş gönderirse `table_session_id` üzerinde iyimser kilitleme veya oturum başına sıralama uygulanır. İki sipariş de aynı oturumda ayrı turlar olarak kabul edilir; her birinin ayrı `order_id` değeri ve artan tur numarası vardır. Ürünler birbirinin üzerine yazılmaz. Her misafir kendi onayını alır; istasyon fişleri "Masa 4 – Tur 2" gibi tur bilgisini gösterir.

### 2.3. Yazıcı Çevrimdışı veya Kâğıt Bitti

TCP bağlantısının varsayılan 3000 ms zaman aşımı ya da `OFFLINE / PAPER_EMPTY` durum baytı hatayı bildirir. Sipariş ve KDS fişleri veritabanında oluşturulur; yazıcı arızası dijital akışı engellemez. İş `printer_spooler` kuyruğunda `FAILED_RETRYING` durumuna alınır. Garson ve yönetim ekranlarında "Mutfak yazıcısı çevrimdışı; lütfen kâğıdı kontrol edin" uyarısı gösterilir. Yazıcı düzeldikten sonra yetkili garson/kasiyer açık bir yeniden yazdırma işlemi başlatabilir.

### 2.4. Kart Reddedilmesi veya Ödeme Zaman Aşımı

Sağlayıcının doğrudan yanıtı veya webhook'u `PAYMENT_DECLINED` ya da `GATEWAY_TIMEOUT` döndürebilir. Hesap `OPEN` veya `PARTIALLY_PAID` olarak kalır. Reddedilmiş işlem tahsilat sayılmaz; hata idempotency anahtarlı işlem kaydında tutulur. Mesaj: "Kart banka tarafından reddedildi. Başka bir kart veya nakit deneyin."

Zaman aşımı tahsilatın yapılmadığını kanıtlamaz. Yeniden denemeden önce sağlayıcının işlem durumu sorgulanır ve mutabakat yapılır; belirsiz sonuçta ikinci tahsilat başlatılmaz.

### 2.5. Hazırlanmakta Olan Ürünü İptal Etme

Misafir `IN_PREPARATION` durumundaki ürünün iptalini isteyebilir. Müşterinin mevcut [RBAC matrisinde](./ROLES-AND-PERMISSIONS.md) `orders.items.cancel_pre_prep` izni yoktur; hazırlık durumundan bağımsız olarak doğrudan müşteri iptali reddedilir. Hazırlık öncesi yetkili personel komutu, izin/sürüm/`SUBMITTED` durumunu atomik doğrular; [durum makinesine](./STATE-MACHINES.md) bakın.

Hazırlık başladıysa müşteriye "Mutfak hazırlığa başladı; lütfen garsonunuzla görüşün" denir ve Garson Çağır eylemi sunulur. Yalnızca `orders.items.void_in_prep` iznine sahip Restoran Yöneticisi veya Şube Müdürü, yönetici doğrulaması ve gerekçeyle iptal edebilir. Garsonun yalnızca PIN girmesi bu izni sağlamaz. Yönetici kimliği ve `WASTE_CANCEL` denetim olayı kaydedilir.

### 2.6. KDS veya Mobil Uygulamada Ağ Kesintisi

Canlı bağlantının heartbeat/ping hatası kesintiyi tespit eder. İstemci "Çevrimdışı — Yeniden bağlanıyor…" göstergesini açar ve yerel sipariş taslağını saklar. Yeniden bağlanınca durumu eşitler, oturumu doğrular ve taslağın gönderilmesini kullanıcıya önerir. Kuyruk yenilendiğinde KDS sesli uyarısı verilir; eski oturumla otomatik sipariş gönderilmez.

### 2.7. Yetkisiz Kaynağa Erişim

Müşteri URL veya istek gövdesini değiştirerek başka masanın hesabına ya da `/admin` alanına erişmeye çalışabilir. Doğrulanmış `table_session_id` ve `role` talepleri kontrol edilir; istek hemen `403 Forbidden` ile reddedilir. IP, zaman ve hedef kaynak güvenlik denetimine kaydedilir. Müşteri kendi masa karşılama ekranına yönlendirilir.

## 3. Restoran Yapılandırması Hataları (Faz 4)

| Senaryo | Beklenen Davranış |
| :--- | :--- |
| İşletme A, B'nin ayar/tema/alan/istasyonuna erişir | PostgreSQL RLS ve EF Core filtreleri yalıtımı uygular; eksik veya yabancı kaynak `404 Not Found` olarak görünür. |
| Şube 1 müdürü aynı işletmedeki Şube 2'yi değiştirir | Atanan şube ile hedef kaynak karşılaştırılır; `403 Forbidden`. |
| Yönetici olmayan rol yapılandırma yazar | `branch.configuration.manage` veya `tenant.branding.manage` gereklidir; yetkisiz istek `403 Forbidden`. |
| İşletme kimliği yok veya alan adı çözümlenemiyor | İşlem hattı erişimi reddeder; RFC 7807 ile `400 Bad Request` / `401 Unauthorized`. Veritabanı bağlamı `NULL` kalır ve sıfır satır döner. |
| İki yönetici eski ETag ile günceller | Sürüm uyuşmazlığı `409 Conflict` döndürür; sessiz üzerine yazma yapılmaz. |
| Geçersiz renk/CSS/URL/vergi/saat girişi | FluentValidation/model doğrulaması kayıt öncesi `400 Bad Request` döndürür. Vergi 10.000 baz puanı (%100) aşamaz; negatif hizmet ücreti ve geçersiz saat reddedilir. |
| Kapalı veya askıdaki şube değiştirilir | Şube durumu kontrol edilir; operasyonel değişiklik `400 Bad Request` / `409 Conflict` ile reddedilir. |
| Özellik bayrağı açık, kullanıcı yetkisiz | Bayrak yetki vermez; RBAC önce uygulanır ve `403 Forbidden` döner. |
| Veritabanı işlemi yarıda hata verir | `BeginTransactionAsync` kapsamındaki işlem tümüyle geri alınır; kısmi durum kalmaz. |
| Yönetici kabuğunda tema/ayar API'si 500 veya zaman aşımı verir | Güvenli varsayılan tasarım değişkenleri ve gezinme kullanılır; boş ekran/çökme önlenir. |
| Kullanıcı İşletme A'dan B'ye geçer | Yeni tema uygulanmadan `:root` üzerindeki eski dinamik CSS değişkenleri temizlenir. |

## 4. Katalog Hataları (Faz 5)

- Nesne kimlikleri hizmet sorgularında ve RLS/bileşik kısıtlarda işletme/şube kapsamıyla doğrulanır; yabancı kapsam reddedilir veya kaynak yokmuş gibi görünür.
- BranchManager'ın şubeler arası yazması ve Mutfak/Bar istasyon uyumsuzlukları reddedilir.
- Negatif/taşan fiyat, yinelenen slug/kod, geçersiz seçenek sınırı, desteklenmeyen etiket ve diyet/alerjen çelişkisi doğrulamadan geçmez.
- Eksik eşzamanlılık belirteci 412; eski belirteç, yinelenen kullanılabilirlik kaydı ve eşzamanlı sıralama çakışması 409 döndürür.
- Arşivlenmiş menü değiştirilemez. Pasif menü/kategori/ürün/varyant/grup/seçenek çalışma zamanı menüsüne alınmaz.
- Katalog değişiklikleri ve denetim kayıtları `SaveChanges` ile tek EF iş biriminde yazılır. Başarısız kayıtta kullanılabilirlik olayı gönderilmez. Beklenmeyen API hatası ayrıntı sızdırmadan korelasyon kimlikli RFC 7807 500 döndürür.
- Yönetici ekranında ağ hatası yeniden denemeyi, 409 yeniden yüklemeyi/yeniden denemeyi, 412 belirtecin yenilenmesini açıklayan mesajı sunar.
