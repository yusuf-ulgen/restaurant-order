# Faz 8 — Takip İncelemesi ve Düzeltmeler

Tarih: 2026-10-20. Başlangıç görevi [#18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), incelenen PR [#19](https://github.com/yusuf-ulgen/restaurant-order/pull/19), başlangıç başlığı `c1b8ce0`. PR #19 inceleme sırasında başka bir işlemle birleştirildi; bulgular [#22](https://github.com/yusuf-ulgen/restaurant-order/issues/22) ve `fix/order-review-regressions` dalında, main `d4a1255` tabanından takip edilir.

## İncelenen Sınırlar

Sunucuda fiyatlandırma ve yeniden onay, müşteri/personel yetkileri, oturum yaşam döngüsüyle sipariş oluşturma yarışı, sipariş/kalem durum tutarlılığı, idempotency ve tarayıcı yenilemesinden sonra tekrar gönderim incelendi. İşletme RLS'si, merkezi RBAC ve mevcut para modeli korundu.

## Düzeltilen Bulgular

### 1. Sipariş doğrulaması ile oturum değişikliği aynı işlem sınırını paylaşmıyordu

Siparişler `order_session_*` advisory kilidi alıyordu; kapanış, hesap isteme ve taşıma işlemleri bu kilidi kullanmıyordu. Böylece doğrulama anında aktif olan oturum, sipariş commit edilmeden değişebiliyordu.

Müşteri ve personel oluşturma yollarında ortak `OrderSessionLock` eklendi. Advisory kilide ek olarak işletme/şube/oturum kapsamlı satır kilidi, durum okunmadan önce alınır ve transaction sonuna kadar tutulur. Önce kapanış başladıysa sipariş bekler ve güncel kapalı durumu görerek reddedilir; önce sipariş başladıysa oturum değişikliği siparişin commit'ini bekler.

Altı PostgreSQL regresyon testi iki yöndeki yarışları, müşteri/personel yollarını ve hesap isteme/kapanış durumlarını kapsar. Sipariş yazımı geçici, ayrıcalıksız runtime rolüyle yapılır; koordinasyon gerçek PostgreSQL kilit beklemesinden gözlenir.

### 2. Kısmi hazırlık tüm siparişi erken hazır gösterebiliyordu

Bir kalem hazırlanırken diğer kalem `Submitted` durumunda kaldığında, toplu `MarkReady` siparişi doğrudan `Ready` yapıyordu. Bu, hazırlanmamış kalemlere rağmen siparişin servis edilmiş sayılmasına da yol açabiliyordu.

Toplu hazır işaretlemesi yalnızca hazırlıktaki kalemleri değiştirir; genel durum `DeriveStatus` ile yeniden türetilir. Hazırlıkta kalem yoksa işlem token veya zaman damgasını değiştirmeden reddedilir. Üç alan testi ve durum makinesi belgesi aynı kurala bağlandı.

### 3. Yanıt kaybından sonra sayfa yenileme yeni sipariş anahtarı üretiyordu

Sepet yenilemeden sonra geri yüklenirken sipariş denemesinin anahtarı yalnızca bileşen belleğinde tutuluyordu. Sunucu kaydı tamamlayıp yanıt kaybolduğunda, yenileme sonrası aynı sepet yeni anahtarla ikinci kez gönderilebiliyordu.

Bekleyen denemenin anahtarı gönderimden önce `sessionStorage` içine kaydedilir; oturum ve kanonik payload imzası eşleşirse geri yüklenir. Payload/oturum değişikliği veya sepetin açıkça temizlenmesi yeni deneme oluşturur. Kimlik tokenı kaydedilmez; imza mevcut sepet taslağı alanlarıyla sınırlıdır. Deneme güvenle saklanamıyorsa API çağrısı yapılmaz ve sepet korunur.

Gerçek CartProvider/OrderConfirmationSheet yeniden bağlama testi, yanıt kaybı ve sayfa yenilemesini kapsar. Ayrı testler oturum/payload ayrımı, bozuk kayıt, temizleme ve saklama hatalarını doğrular.

## Doğrulama Kaydı

- Düzeltme öncesi: 2 alan regresyonu, 6 gerçek PostgreSQL yarışı ve 1 tarayıcı bileşeni yenileme regresyonu başarısız oldu; hatalar yeniden üretildi.
- Düzeltme sonrası hedefli backend: 155 sipariş birim testi ve 6 PostgreSQL yarış testi başarılı; atlama yok.
- İlk hedefli frontend çalışması: 25 test başarılı. Tam çalışmada 1.670 backend birim, 10 mimari ve 568 arayüz testi doğrulandı; buna gönderim öncesi saklama hatasını doğrulayan ek UI testi de dahildir.
- Tam `pnpm verify`, entegrasyon/E2E ve güncel başlığın CI sonucu takip PR'ının doğrulama bölümünde tutulur. Eski `SKIP_TESTCONTAINERS=true` yerel kayıtları gerçek veritabanı kanıtı sayılmaz.

## Sonraki Aşama

Kullanıcı inceleme, düzeltme ve başarılı kontrollerden sonra birleştirmeyle devam edilmesini onayladı. PR #19 birleştirildiği için düzeltmeler ayrı takip PR'ıyla main'e alınır. Faz 9 zaten [#20](https://github.com/yusuf-ulgen/restaurant-order/issues/20) altında hazırlanmıştır; eşzamanlı açılan kopya #21 kapatıldı. Firebase Web Push anahtarı veya sunucu kimlik bilgisi oluşturulmaz; önceki kullanıcı tercihi korunur.
