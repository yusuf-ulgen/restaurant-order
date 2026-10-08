# Push Bildirimleri — Uygulama Planı

- Karar: `ACCEPTED` — kullanıcı 2026-10-08 tarihinde FCM ile devam etmeyi onayladı; uygulama aşamalı sürüyor.
- Görev: [#11](https://github.com/yusuf-ulgen/restaurant-order/issues/11).
- İlk taşıma görevi: [#14](https://github.com/yusuf-ulgen/restaurant-order/issues/14), dal `feat/fcm-bildirim-altyapisi`; taban main `c2c61a3`.
- Önceki fiyatlandırma, Türkçe belge ve bildirim planı PR'ları #9, #12, #13 main'e birleştirildi.
- Karar kaydı: [ADR-0011](./adr/0011-background-push-provider.md), `ACCEPTED`.
- Hedef kitle: Kullanıcı 2026-10-08 tarihinde personel ve müşteriyi birlikte seçti.
- Mevcut bağlayıcı karar: ADR-0001, uygulama açıkken canlı iletişim için SignalR + Redis kullanır. Bu karar push sağlayıcısından bağımsızdır.

## Mevcut Kod ve Sınırlar

| Mevcut parça | Anlamı |
| :--- | :--- |
| `IIdentityNotificationSender` ve kimlik outbox/webhook taşıması | Davet ve kimlik akışıdır; sipariş push kanalı sayılmaz. Doğrulama/davet sırları push payload'ına aktarılmaz. |
| `CatalogAvailabilityOutboxMessage` | Katalog kullanılabilirlik olayının kalıcı temeli vardır; gerçek cihaz bildirimi kanıtı değildir. |
| Müşteri/operasyon `manifest.json` | PWA başlangıç dosyaları vardır; push aboneliği, service worker ve iOS cihaz doğrulaması henüz yoktur. |
| SignalR + Redis, ADR-0001 | Kabul edilmiş teknoloji kararıdır; mevcut depoda restoran olaylarına bağlı tam hub/istemci akışı henüz uygulanmış değildir. |

Mevcut `NOTIFICATION_PROVIDER=TransactionalOutbox` kimlik bildirim sözleşmesidir. Bu değer değiştirilmez; operasyon push için ayrı `PUSH_PROVIDER=Disabled|Fcm` kullanılır. [İlk taşıma uyarlayıcısı](./FCM-SETUP.md) kayıt, yetkilendirme veya outbox yerine geçmez.

## Kabul Edilen Yaklaşım

Canlı ekran akışını SignalR ile, izin verilmiş arka plan bildirimlerini Firebase Cloud Messaging ile sağlayın. Bildirim, sipariş/ödeme verisinin doğruluğunun veya teslim edildiğinin kanıtı değildir; sunucu kayıtları tek doğruluk kaynağıdır. FCM, Firebase Auth veya Firestore'a geçiş gerektirmez; mevcut .NET kimlik ve PostgreSQL alan modeli korunur.

## Seçenekler

| Seçenek | Kazanç | Sorumluluk / sınır |
| :--- | :--- | :--- |
| SignalR + FCM | Yönetilen arka plan push gönderimi; ileride yerel mobil istemcilere açılabilen sağlayıcı | Firebase projesi, servis hesabı, kayıt yaşam döngüsü, SDK/sürüm uyumu ve sağlayıcı bağımlılığı |
| SignalR + Web Push/VAPID | Web öncelikli yapıda Firebase SDK zorunluluğu yok | Sunucuda abonelik uçları/anahtarları, gönderim hataları, süresi dolan abonelikler ve yeniden deneme yönetimi |
| Yalnızca SignalR | Açık uygulamada canlı durum için mevcut karara uygun | İşletim sistemi arka plan bildirimi ihtiyacını tek başına karşılamaz |

FCM de web üzerinde tarayıcının Push API desteğine ve izinlere bağlıdır. iOS/iPadOS'ta web push, desteklenen sürümde ana ekrana eklenmiş web uygulaması gerektirir. QR müşterisini bildirim iznine veya uygulama kurulumuna zorlamayın; destek yoksa canlı sayfa ve yeniden sorgulama akışı çalışmalıdır.

## İlk Kapsam

- Personel: servis çağrısı, hazır yemek/içecek uyarısı; mutfak/bar ekranında yeni fiş için uygulama içi uyarı.
- Müşteri: kendi aktif oturumundaki sipariş durumu ve gerekli servis güncellemeleri; pazarlama bildirimi kapsam dışı.
- Uygulama açıkken aynı olay için uygulama içi uyarı ile işletim sistemi bildirimini çoğaltmayın.
- İzin isteğini kullanıcı eylemiyle ve amacı açıklanarak gösterin; reddedilirse tekrar tekrar istemeyin.

## Güvenlik ve Yaşam Döngüsü

1. Kayıt sahibini sunucunun kimlik doğrulaması belirler. İstemciden gelen tenant/şube/kullanıcı/oturum değerleri yetki kanıtı olamaz.
2. Personel kayıtları mevcut üyelik, şube ve yetkiyle; müşteri kayıtları aktif table_session_id ile sınırlanır. FCM topic adı yetkilendirme sınırı değildir.
3. Çıkış, yetki/şube değişikliği, müşteri oturumu kapanması ve izin iptali gönderim uygunluğunu kaldırır. Müşteri oturumu kapanınca eski cihaz yeni masanın tarafı olamaz.
4. Kilit ekranına müşteri adı, ürün ayrıntısı, hesap tutarı veya token koymayın. Asgari başlık ve opak olay kimliği kullanın; ayrıntıyı uygulama yeniden yetkilendirerek çeksin.
5. Süresi geçmiş/geçersiz kayıtları sağlayıcının hata sınıflandırmasına göre temizleyin. Kayıt kimliklerini ve servis hesabını günlüğe veya Git'e yazmayın.
6. Gönderimden önce güncel yetkiyi ve olayın geçerliliğini kontrol edin. Kısa TTL kullanın; kapanmış oturuma gecikmiş uyarı göndermeyin. Sağlayıcı kabulü cihaz teslimi anlamına gelmez.
7. Kalıcı outbox ve teslim denemesi kayıtları kullanın; tenant + olay + alıcı + kanal anahtarıyla tekrarları azaltın. Tekrar denemede belirsiz sonuç ihtimalini kabul edin; dış etki için tam bir kez teslim garantisi vermeyin.
8. Service worker ve tıklama yönlendirmeleri yalnızca aynı kökene, bilinen rotalara gider. Payload içindeki URL'yi serbestçe açmayın.

## Bağımlılıklar ve Uygulama Sırası

Sipariş, masa oturumu ve müşteri QR kimliği henüz tam uygulanmadı. Bildirim altyapısı gerçek olmayan sipariş/masa uçları icat ederek tamamlanmış gösterilmemelidir.

1. Sağlayıcı seçimi tamamlandı: FCM. Olay/alıcı eşlemesi aşağıdaki taslaktır; gerçek bağlantılar ilgili alan fazlarında doğrulanır.
2. Alan/uygulama sınırında kanal ve alıcı politikası, kayıt yaşam döngüsü ve kalıcı gönderim tasarımını belirle.
3. İlk sunucu uyarlayıcısı ve güvenli yapılandırma #14 kapsamındadır; testte gerçek SDK ve sahte HTTP taşıması kullanılır. Cihaz kaydı ve kalıcı gönderim henüz yoktur.
4. Personel ve müşteri arayüzlerinde destek/izin durumu, service worker ve uygulama içi bildirimleri ekle.
5. Gerçek masa/sipariş olayları ilgili fazlarda hazır olduğunda bağla; tenant/şube/oturum yalıtım testleriyle doğrula.
6. HTTPS test ortamında Android ve iPhone gerçek cihaz denemelerini kaydet. Bunlar yapılmadan gerçek push teslimini doğrulanmış sayma.

## İlk Olay ve Alıcı Eşlemesi

Bunlar [mevcut olay taslağına](./REALTIME-AND-NOTIFICATIONS.md) dayanır; yeni uygulanmış uç nokta veya izin değildir.

| Olay | Hedef ve kanal |
| :--- | :--- |
| `table.service_requested` | Yetkili salon personeli: uygulama içi ve izin varsa push. |
| `kds.ticket_bumped` | İlgili şube/masa için yetkili garson: hazır ürün uyarısı. |
| `order.item_status_changed` | Yalnızca ilgili aktif müşteri masa oturumu: canlı durum ve izin varsa push. |
| `order.created` | İlgili mutfak/bar istasyonu: açık KDS üzerinde canlı fiş ve ses. |
| `table.session_closed` | Müşteri kaydının gönderim uygunluğunu kaldır; eski oturuma yeni veri gönderme. |

## Doğrulama Ölçütleri

Yanlış tenant/şube/oturum reddi; izin reddi; desteklenmeyen tarayıcı; çıkış ve oturum kapanışında iptal; yinelenen olay; geçersiz kayıt; geçici sağlayıcı hatası; süresi geçmiş uyarı; yeniden bağlanmada güncel durumu alma; güvenli bildirim tıklaması; açık uygulamada çift uyarının önlenmesi.

## Kaynaklar

2026-10-08 tarihinde resmi belgeler kontrol edildi:

- [FCM web başlangıç](https://firebase.google.com/docs/cloud-messaging/web/get-started)
- [FCM web mesaj alma](https://firebase.google.com/docs/cloud-messaging/web/receive-messages)
- [FCM kayıt yaşam döngüsü](https://firebase.google.com/docs/cloud-messaging/manage-tokens)
- [FCM mesaj süresi](https://firebase.google.com/docs/cloud-messaging/customize-messages/setting-message-lifespan)
- [WebKit iOS/iPadOS Web Push](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/)
- [SignalR Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0)

Güncel FCM web belgesi kayıt yönetiminde Firebase Installation ID akışını öneriyor ve eski registration-token API'lerini kullanımdan kaldırılmış gösteriyor. Uygulamada seçilen SDK sürümüyle bu akışın uyumu ayrıca doğrulanmalı; eski getToken örnekleri sürüm kontrolü yapılmadan kopyalanmamalıdır.
