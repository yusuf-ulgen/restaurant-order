# FCM Sunucu Taşıması — İlk Uygulama

Karar: [ADR-0011](./adr/0011-background-push-provider.md). Üst görev [#11](https://github.com/yusuf-ulgen/restaurant-order/issues/11), ilk uygulama [#14](https://github.com/yusuf-ulgen/restaurant-order/issues/14).

## Tamamlanan Sınır

`IPushNotificationTransport`, uygulama katmanında sağlayıcıdan bağımsızdır. `FirebasePushNotificationTransport`, infrastructure içinde sabitlenmiş `FirebaseAdmin` 3.7.0 ile tek Firebase Installation ID (FID) hedefine gönderir. Personel ve müşteri için aynı taşıma kullanılabilir; alıcı seçimi bu katmanın işi değildir.

Kayıt/iptal API'si, veritabanı değişikliği, kalıcı outbox, service worker, tarayıcı izni ve masa/sipariş olay bağlantısı bu ilk parçaya dahil değildir. Herkese açık gönderim ucu ve arka planda otomatik gönderim işi eklenmedi. Mevcut IAM davet/parola sıfırlama bildirimleri ayrı kalır.

## Yapılandırma

| Değişken | Anlamı |
| :--- | :--- |
| `PUSH_PROVIDER` | Varsayılan `Disabled`; etkin sağlayıcı `Fcm`. Bilinmeyen değer başlangıçta reddedilir. Kapalı taşıma başarı uydurmaz, `Disabled` döndürür. |
| `FCM_PROJECT_ID` | Fcm etkinse zorunlu hedef proje kimliği: 6–30 karakter, küçük ASCII harfle başlar, küçük harf/rakam/tire içerir, tireyle bitmez. Yol/URL kabul edilmez; değeri Firebase konsolundan alın. |
| `GOOGLE_APPLICATION_CREDENTIALS` | Yerelde gerekiyorsa depo dışındaki ADC kimlik bilgisi dosyasının mutlak yolu. Dosyanın içeriği veya özel anahtar Git'e, imaja ve günlüklere eklenmez. |

Her ortam için ayrı Firebase projesi ve yetkilendirilmiş kimlik kullanın. Firebase Cloud Messaging API'yi hedef projede etkinleştirin. Sunucu kimliğine yalnızca gereken FCM gönderim yetkisini verin; genel Owner/Editor yetkisi gerekmez. Üretimde yönetilen iş yükü kimliği / Application Default Credentials (ADC) tercih edilir. Harici kimlik bilgisi yapılandırması güvenilir dağıtım kaynağından sağlanır, istemciden alınmaz.

Örnek (gerçek proje kimliğini ortamınızda girin):

```powershell
$env:PUSH_PROVIDER = 'Fcm'
$env:FCM_PROJECT_ID = '<firebase-project-id>'
# ADC'yi güvenli ortamınızda sağlayın; kimlik dosyasını depoya kopyalamayın.
```

`NOTIFICATION_PROVIDER` değerini Fcm yapmayın: o ayar IAM webhook/outbox içindir. Mevcut `AddInfrastructure` API bileşiminde taşıma kaydedilir; worker henüz bu taşımanın tüketicisi değildir. Sağlayıcı/proje ayarları kayıt sırasında doğrulanır; ADC yalnızca taşıma ilk çözüldüğünde yüklenir. Bu nedenle başlangıç veya health yanıtı Firebase bağlantı/kimlik yetkisi kanıtı değildir. Özelliği geri almak için `PUSH_PROVIDER=Disabled` kullanıp uygulamayı yeniden başlatın.

## Gönderim ve Hata Sözleşmesi

- Çağıran, **göndermeden hemen önce** alıcının güncel işletme, şube, üyelik/yetki veya aktif müşteri masa oturumunu doğrulamak zorundadır. FID, topic veya opak olay kimliği erişim yetkisi sağlamaz. Bu denetim ve kayıt yaşam döngüsü tamamlanana kadar taşıma bir API/iş akışına bağlanmaz.
- Payload yalnızca opak `event_id`, `expires_at` ve sabit Türkçe genel bildirim içerir. Kişisel bilgi, ürün/masa ayrıntısı, hesap tutarı, kimlik tokenı veya serbest tıklama URL'si alınmaz.
- Kalan olay süresi aşağı yuvarlanır ve en fazla 60 saniyeyle sınırlandırılır; bir saniyeden az süre kaldıysa sağlayıcı çağrılmaz. Web TTL, Android TTL ve APNs süre sonu birlikte ayarlanır. İstemci daha sonra `expires_at` kontrolü de yapmalıdır; TTL, önceden görüntülenmiş bildirimi geri çekmez.
- SDK çağrısı en fazla 10 saniye veya kalan olay süresiyle sınırlıdır. Çağıranın iptali aynen yayılır. SDK kendi geçici HTTP tekrarlarını yapabilir; bu katmanda ayrıca gönderim döngüsü yoktur.
- `ProviderAccepted` cihazın gördüğünü göstermez. Zaman aşımı/geçici hata sonrasında sonuç belirsiz olabilir; tekrar gönderim çift bildirim doğurabilir. Kalıcı, idempotent iş/deneme kaydı ayrı uygulanacaktır.

| Sonuç | Sonraki tüketicinin davranışı |
| :--- | :--- |
| `Expired` | Yeniden gönderme. |
| `InvalidRecipient` | Yalnızca `UNREGISTERED`; ilgili kaydı geçersiz kıl. Henüz kalıcı kayıt deposu yoktur. |
| `ConfigurationFailure` | Yanlış proje/sender veya yetkilendirmeyi düzelt; cihaz kaydını topluca silme. |
| `PermanentFailure` | Geçersiz mesajı incele; `INVALID_ARGUMENT` tek başına cihaz kaydını silme gerekçesi değildir. |
| `RetryableFailure` | Kalıcı kuyruk eklendiğinde süre sonuna, deneme sınırına ve üstel gecikmeye göre değerlendir. `RetryAfter` varsa alt sınırdır; kota hatasında en az 60 saniye. Gecikme olayın ömrünü aşıyorsa tekrar etme. |

Taşıma FID veya sağlayıcı hata gövdesini günlüğe yazmaz ve sonuçta döndürmez. Kayıt kimliği doğrulama hatası da ham değeri tekrarlamaz. Bu, çağıranın nesne özelliklerini açıkça günlüğe yazmasına izin vermez.

## Otomatik ve Gerçek Cihaz Doğrulaması

```powershell
dotnet test tests/unit/RestaurantOrder.UnitTests.csproj -c Release --filter FullyQualifiedName~Notifications
pnpm verify
```

Testler gerçek FirebaseAdmin SDK'sını sahte HTTP taşımasıyla çalıştırır; gerçek kimlik bilgisi, Firebase projesi veya cihaz kullanılmaz. FID hedefi, içerik, tüm platform süreleri, hata sınıfları, Retry-After, süre sonu, iptal ve yapılandırma doğrulanır. Bunlar gerçek cihaz teslim testi değildir.

Sonraki adım: personel ve müşteri için sunucunun türettiği sahipliğe bağlı cihaz kaydı/iptali ve kalıcı gönderim tasarımı. Ardından istemci izin/service worker akışı ve gerçek alan olayları. Firebase web SDK sürümü sabitlenmeden eski `getToken` örnekleri kopyalanmaz; seçilen sunucu SDK'sı `Fid` alanını destekler ve HTTP sözleşme testi bunu doğrular. HTTPS test ortamında izinli Android ve iPhone cihazlarında gerçek teslim ayrıca kaydedilir; iOS/iPadOS ana ekran koşulu ve izin reddi senaryosu denenir.

## Resmi Kaynaklar

2026-10-08 tarihinde kontrol edildi:

- [Firebase Admin .NET 3.7.0](https://www.nuget.org/packages/FirebaseAdmin/3.7.0)
- [Admin SDK ile gönderim](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk)
- [Admin SDK kurulumu ve ADC](https://firebase.google.com/docs/admin/setup)
- [FCM hata kodları ve yeniden deneme](https://firebase.google.com/docs/cloud-messaging/error-codes)
- [FCM mesaj ömrü](https://firebase.google.com/docs/cloud-messaging/customize-messages/setting-message-lifespan)
- [Web istemcisi kaydı](https://firebase.google.com/docs/cloud-messaging/web/get-started)
- [Google Cloud proje kimliği kuralları](https://docs.cloud.google.com/resource-manager/docs/creating-managing-projects)
