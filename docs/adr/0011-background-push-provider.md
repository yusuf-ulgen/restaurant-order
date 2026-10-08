# ADR-0011: Personel ve Müşteri İçin Arka Plan Push Sağlayıcısı

- **Durum:** `ACCEPTED`
- **Tarih:** 2026-10-08
- **Hedef kitle kararı:** Kullanıcı personel ve müşteriyi birlikte seçti.
- **Sağlayıcı kararı:** Kullanıcı 2026-10-08 tarihinde “sonra fcm kısmını yapmaya başlayalım” diyerek FCM ile devam etmeyi onayladı.
- **Görev:** [#11](https://github.com/yusuf-ulgen/restaurant-order/issues/11).
- **İlgili karar:** [ADR-0001](./0001-technology-stack.md); kabul edilmiş SignalR + Redis kararı korunur.

## Bağlam

Açık uygulamada canlı sipariş/servis akışı ile uygulama arka plandayken işletim sistemi bildirimi ayrı ihtiyaçlardır. Personel ve müşteri birlikte kapsanacak; müşterinin menü/sipariş akışı bildirim iznine veya PWA kurulumuna bağlanmayacak. Masa, müşteri QR oturumu ve sipariş olayları henüz tam uygulanmadığından bunların bağımlılığı açık tutulur.

## Değerlendirilen Seçenekler

1. **SignalR + Firebase Cloud Messaging:** Yönetilen push taşıması ve ileride yerel mobil uygulamalara açılma olanağı. Firebase projesi, sunucuda güvenli kimlik bilgisi, istemci kaydı ve SDK uyumluluğu gerektirir.
2. **SignalR + doğrudan Web Push/VAPID:** Web odaklı uygulamada Firebase SDK zorunluluğunu kaldırır; abonelik, anahtar, hata ve yeniden deneme yönetimi sunucuda sürdürülür.
3. **Yalnızca SignalR:** Açık ekran için uygundur; arka plan push ihtiyacını tek başına karşılamaz.

## Karar ve Kabul Sınırı

**Karar: Canlı ekran için SignalR + Redis, arka plan push için FCM.** Firebase Auth veya Firestore'a geçilmez; mevcut .NET/PostgreSQL mimarisi korunur. FCM yalnızca bildirim taşıma uyarlayıcısıdır. Mevcut IAM outbox/webhook sözleşmesi yerinden edilmez.

İlk uygulama [#14](https://github.com/yusuf-ulgen/restaurant-order/issues/14) kapsamında sunucu taşıması ve güvenli yapılandırmadır. Kayıt/iptal modeli, kalıcı teslim denemeleri, istemci izni ve gerçek olay bağlantıları #11 altında ayrı değişikliklerle devam eder. Uygulanmamış masa/sipariş uçları sırf bildirimi tamamlanmış göstermek için icat edilmez.

## Değişmez Kurallar

- Alıcı kapsamını sunucu doğrulanmış kimlik, işletme, şube, yetki ve aktif masa oturumundan türetir. Topic veya istemci kimliği erişim yetkisi değildir.
- Çıkış, rol/şube değişimi ve müşteri oturumu kapanması eski kaydın gönderim uygunluğunu kaldırır. Kilit ekranında kişisel bilgi, sipariş ayrıntısı, tutar veya kimlik sırrı gösterilmez.
- İzin reddi ve tarayıcı desteği yokluğu temel akışı engellemez. iOS/iPadOS ana ekran koşulu açıkça anlatılır.
- Kalıcı iş/deneme kaydı, süre sonu ve güncel yetki kontrolü gerekir. Sağlayıcı kabulü cihaz teslimi sayılmaz; tam bir kez teslim garantisi verilmez.
- Otomatik testlerde sahte taşıma kullanılır. Gerçek teslim ancak HTTPS test ortamında izinli test cihazıyla kanıtlanır; gerçek müşteriye deneme gönderilmez.

## Doğrulama ve Devir

Ayrıntılı olay/alıcı eşlemesi, aşamalar ve resmi kaynaklar [bildirim planındadır](../NOTIFICATIONS-PLAN.md). İlk uyarlayıcının kurulumu ve sınırları [FCM sunucu belgesindedir](../FCM-SETUP.md). Sağlayıcının kabul edilmesi tüm bildirim akışının tamamlandığı anlamına gelmez. Firebase projesi/servis hesabı oluşturulmadı ve gerçek cihaza bildirim gönderilmedi.
