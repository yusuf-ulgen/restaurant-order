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

## 5. Masa, Kat Planı ve QR Olumsuz Durum Akışları (Faz 6)

- **Aktif Oturumu Olan Masanın Devre Dışı Bırakılması:** HTTP 409 Conflict ile engellenir. Kapatılmamış yemek oturumu bulunan masalar, aktif oturum `Closed` durumuna geçene kadar pasif hale getirilemez.
- **Yinelenen Masa Kodu / Numarası:** Şube kapsamlı benzersiz indeks `ix_restaurant_tables_tenant_branch_code` ve doğrulayıcı tarafından reddedilir (HTTP 400 / 409).
- **Sınır Dışı Yerleşim Koordinatları:** Negatif koordinatlar, 10 pikselden küçük veya 5000 pikselden büyük boyutlar, tuval sınırlarını aşan koordinatlar (`position_x + width > 10000` veya `position_y + height > 10000`), geçersiz şekiller (`shape NOT IN (1, 2, 3)`) veya geçersiz rotasyon açıları içeren yerleşim güncellemeleri HTTP 400 ValidationProblemDetails ve veritabanı kısıtları (`ck_restaurant_tables_shape`, `ck_restaurant_tables_canvas_bounds`) ile reddedilir.
- **Oturum Zaman Damgası ve Kronoloji İhlalleri:** Veritabanı check constraint'leri (`ck_dining_sessions_status_timestamps`, `ck_dining_sessions_chronology`) doğrusal olmayan yaşam döngüsü geçişlerini, eksik/erken atanmış zaman damgalarını veya kronolojik sıra ihlallerini (`opened_at > activated_at`, `activated_at > bill_requested_at` vb.) doğrudan SQL düzeyinde engeller.
- **Eski Eşzamanlılık Belirteci (ETag):** `If-Match` başlığı olmadan yapılan güncellemeler 412 Precondition Failed döner. Uyuşmayan belirteçlerle yapılan eşzamanlı güncellemeler 409 Conflict döner.
- **QR İmza Tahrifatı ve Sahtecilik:** Değiştirilmiş yük alanları, bozulmuş imzalar veya bilinmeyen anahtar kimlikleri (`kid`) içeren belirteçler sabit zamanlı karşılaştırma (`CryptographicOperations.FixedTimeEquals`) yoluyla HTTP 400 Bad Request ile reddedilir.
- **İptal Edilmiş QR Taramaları (Yenilenmiş Sürüm):** `qv < table.qr_version` içeren statik veya dinamik QR belirteçleri HTTP 400 Bad Request ile reddedilir; böylece döndürülen/yenilenen masaların eski fiziksel baskıları anında geçersiz kılınır.
- **Süresi Dolmuş Dinamik QR Belirteçleri:** Geçerlilik süresi dolmuş belirteçler HTTP 400 Bad Request döner.
- **Kapanmış Oturumda Dinamik QR Kullanımı:** Kapatılmış bir yemek oturumuna (`status == 4`) referans veren dinamik QR değişimi HTTP 409 Conflict ile reddedilir.
- **Oturum Kapanmasında Belirteç İptali:** Personel bir yemek oturumunu kapattığında, `InvalidateSessionCacheAsync` Redis önbellek kaydını derhal temizler. Müşteri JWT çereziyle yapılan sonraki istekler doğrulamayı geçemez (HTTP 401 Unauthorized).
- **Zayıf veya Eksik İmzalama Anahtarı:** Staging ve Production ortamlarında `QrSecurity:Keys` tanımlanmamışsa veya 256 bitten (32 bayt) kısaysa uygulama başlangıcı açık bir özel durumla (fail-fast) durdurulur.
- **Hız Sınırlama (Rate Limiting) Fail-Closed:** Dağıtık kayan pencere (sliding-window) hız sınırlaması aşırı tarama girişimlerini reddeder (HTTP 429). Redis'e ulaşılamazsa yukarı akış hizmetlerini korumak için güvenli tarafta kalarak istekleri reddeder (fail-closed).
- **Masa Taşıma ve Oturum Birleştirme Olumsuz Akışları (Faz 6.4):**
  - Aynı masaya transfer girişimi: HTTP 400 Bad Request.
  - Dolu veya pasif hedef masaya transfer: HTTP 400 Bad Request (dolu masalar için merge uç noktası kullanılmalıdır).
  - Hedef masa kapasitesini aşan misafir sayısı: HTTP 400 Bad Request.
  - `Closed` veya `BillRequested` oturumu transfer etme veya birleştirme girişimi: HTTP 400 Bad Request.
  - Şubeler veya işletmeler arası transfer/birleştirme denemeleri: Bilgi sızdırmadan HTTP 404 Not Found / HTTP 403 Forbidden.
  - Müşteri rolünün transfer/birleştirme uçlarını çağırması: HTTP 403 Forbidden.
  - Eski ETag / eşzamanlılık belirteci: HTTP 409 Conflict veya 412 Precondition Failed.
  - Karşılıklı transfer / merge yarışında deadlock: Deterministik artan UUID sıralı `FOR UPDATE` satır kilitleme ile deadlock önlenir; kaybeden yarışçı hedef masa doluluğu nedeniyle temiz hata alır.
  - Değişmez hareket tablosunda güncelleme veya silme: Veritabanı tetikleyicisi `trg_dining_session_movements_immutable` ile engellenir.
  - Birleştirilen kaynak oturumun müşteri belirteci: Kaynak oturum önbelleği anında geçersiz kılınır; eski müşteri tokenı ile istekler reddedilir.

## 6. Müşteri Deneyimi ve Hizmet Talebi Olumsuz Durum Akışları (Faz 7)

- **Kapalı / İptal Edilmiş Oturumda Menü ve Sepet Erişimi:** Yemek oturumu personel tarafından kapatıldığında veya süresi dolduğunda, müşterinin sonraki API istekleri HTTP 401 Unauthorized veya session inactive hatası alır. İstemci ekranı menü ve sepet erişimini keserek oturumun kapandığını bildiren karşılama ekranına döner.
- **Şube ve İşletme İzolasyonu İhlali:** Müşterinin başka bir şube veya işletmeye ait URL parametreleriyle menüye veya hizmet talebine erişmeye çalışması durumunda, session token ile URL parametreleri uyuşmazlığı tespit edilir ve bilgi sızdırmadan HTTP 403 Forbidden / HTTP 404 Not Found verilir.
- **Sepetteki Ürünün Mutfak Tarafından 86 (Tükendi) Yapılması:** Müşteri ürünü sepetine ekledikten sonra mutfak ilgili ürünü veya varyantı stokta yok (86) olarak işaretlerse, sepet görünümünde kırmızı uyarı banner'ı belirir, tükenen kalem belirginleştirilir ve sipariş butonu devre dışı bırakılır.
- **Zorunlu Seçenek / Varyant Seçilmeden Ürün Ekleme:** Varyantı veya zorunlu modifier grubu (`min_selections >= 1`) bulunan ürünlerde seçim tamamlanmadan sepete ekleme butonu istemci tarafında devre dışı bırakılır; klavye/dokunma ile zorlama durumunda erişilebilir hata mesajı verilir.
- **Bozuk veya Manipüle Edilmiş `sessionStorage` Draft:** `sessionStorage` içindeki sepet taslağı bozuk JSON içerdiğinde, şema sürümü uyuşmadığında veya oturum ID'si uyuşmadığında uygulama çökmez; bozuk taslağı sessizce ve güvenle temizleyerek temiz boş sepetle devam eder.
- **CSRF Korumasız Hizmet Talebi:** Hizmet talebi (garson çağırma vb.) POST isteklerinde çift gönderim (double-submit) `X-CSRF-Token` başlığı eksik veya çerezdeki değerle uyuşmuyorsa, istek arka uçta HTTP 403 Forbidden ile fail-closed reddedilir.
- **Eksik `Idempotency-Key` Başlığı:** Hizmet talebi veya durum değişikliği uç noktalarında `Idempotency-Key` başlığı bulunmadığında istek HTTP 400 Bad Request ile reddedilir.
- **Çift Tıklama / Ağ Tekrarı:** Kullanıcı garson çağırma butonuna arka arkaya çift tıkladığında veya ağ gecikmesinde aynı `Idempotency-Key` ile mükerrer istek gönderildiğinde, backend idempotent olarak aynı talebi döndürür ve veritabanında tek bir kayıt oluşur.
- **API / Ağ Kesintisinde Sepet Korunması ve Sahte Başarı Gösterilmemesi:** Ağ koptuğunda veya arka uç 503 döndürdüğünde sepet taslağı `sessionStorage` içinde korunur. Kullanıcıya açık RFC 7807/ağ hatası bildirilir; sahte sipariş veya servis başarı mesajı verilmez.

## 7. Sipariş Çekirdeği Olumsuz Durum Akışları (Faz 8)

- **İstemci Fiyat Manipülasyonu ve Fiyat Değişikliği Yarışı:** İstemciden gelen birim fiyat veya tutarlar asla güvenilmez. Sepette beklenen fiyat ile yetkili katalog fiyatı uyuşmadığında istek `409 Conflict` (`ORDER_PRICE_CHANGED`) ile reddedilir; güncel ve önceki fiyat bilgisi Problem Details içinde güvenle sunulur.
- **Sipariş Anında Ürünün/Varyantın 86 (Tükendi) Olması:** Sepet onaylanırken ürün veya varyant mutfak tarafından stokta yok (86) veya pasif yapılmışsa, veritabanı işlemi `409 Conflict` (`ITEM_OUT_OF_STOCK`) ile reddedilir.
- **Zorunlu Değiştirici (Modifier) Eksikliği:** Ürüne atanmış zorunlu bir değiştirici grubu (`min_selections >= 1`) için yeterli seçim yapılmadığında istek `400 Bad Request` (`INVALID_MODIFIER_SELECTION`) ile reddedilir.
- **Yinelenen Değiştirici Seçimi:** Aynı seçenek kimliğinin mükerrer gönderilmesi durumunda istek `400 Bad Request` (`DUPLICATE_MODIFIER_SELECTION`) ile reddedilir.
- **Aktif Olmayan / Kapanmış / Hesap İstenmiş Oturumda Sipariş:** Yemek oturumu `Active` durumda değilse (`Open`, `BillRequested`, `Closed`, `Merged`), sipariş isteği `409 Conflict` (`SESSION_NOT_ACTIVE`) ile engellenir.
- **Şube Sipariş Alımının Devre Dışı Olması:** Şube ayarlarında `IsOrderTakingEnabled = false` ise sipariş gönderimi `403 Forbidden` (`ORDER_TAKING_DISABLED`) ile engellenir.
- **Yinelenen Idempotency Key Çakışması:** Aynı `Idempotency-Key` farklı bir oturum veya farklı bir sipariş sepeti içeriğiyle yeniden kullanılırsa `409 Conflict` (`IDEMPOTENCY_CONFLICT`) döner.
- **Aynı Masadan Eşzamanlı İki Farklı Sipariş:** PostgreSQL advisory lock (`pg_advisory_xact_lock(hashtext('order_session_' || sessionId))`) sayesinde yarış durumları sıralanır; iki sipariş de veri kaybı olmadan sırayla `RoundNumber: 1` ve `RoundNumber: 2` olarak kaydedilir.
- **Çift Tıklama ve Ağ Tekrarı Replay:** Aynı anahtar ve aynı sepet parmak iziyle gelen mükerrer isteklerde veritabanında yeni sipariş oluşturulmaz; mevcut sipariş `200 OK` (veya `201 Created`) ile döndürülür.
- **Ağ Kesintisinde ve 503 Durumunda Sepet Korunması:** API'ye erişilemediğinde veya `503 Service Unavailable` (`DISTRIBUTED_SECURITY_STATE_UNAVAILABLE`) alındığında sepet taslağı silinmez; kullanıcıya hata açıkça gösterilir ve sahte sipariş başarısı üretilmez.
- **Personel İptali ile Hazırlık Yarışı:** Mutfak sipariş kalemini hazırlığa aldığında (`InPreparation`), sıradan personelin hazırlık öncesi iptal isteği `409 Conflict` veya `403 Forbidden` ile reddedilir. Hazırlık aşamasındaki kalemler yalnızca yönetici yetkisi veya 4-haneli yönetici PIN doğrulaması ile zayiat gerekçesi (`WASTE_CANCEL`) belirtilerek void edilebilir.
- **Zaten İptal Edilmiş Kalemin Tekrar İptali:** İptal edilmiş bir kalem tekrar iptal edilmeye çalışıldığında açık domain hatası ile `400 Bad Request` (`OrderItem is already cancelled`) döner.
- **Çapraz Oturum / Çapraz Şube İzolasyonu:** Başka bir masanın veya şubenin sipariş kimliği istendiğinde bilgi sızdırmadan `404 Not Found` döner.
