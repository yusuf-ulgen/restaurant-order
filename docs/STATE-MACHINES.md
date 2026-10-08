# Durum Makineleri ve Varlık Yaşam Döngüleri (`docs/STATE-MACHINES.md`)

## 1. Genel Bakış

Geçersiz durumları önlemek için tüm değişiklikler doğrulanmış geçişleri kullanır: boş masaya ödeme, iptal ürünü servis etme veya yetkisiz hazırlık iptali gibi durumlar engellenir.

Bu masa/sipariş/hesap akışları planlanan fazları anlatır; uygulanmış uçlar değildir. Yetkilerin kaynağı [rol matrisi](./ROLES-AND-PERMISSIONS.md) olup örnekler ek yetki vermez. Masa çizimi eski bir salon görünümü taslağıdır; karışık hazırlık, yeni sipariş turları ve kısmi ödemeleri tek kalıcı durumla temsil etmeye yeterli değildir. Doluluk, oturum, hazırlık ve ödeme ayrımı [R02](./REVIEW-BACKLOG.md) kapsamında Faz 6 öncesi ADR ile çözülmelidir. Sipariş yaşam döngüsü ve kısmi iadeler R11'de izlenir.

## 2. Masa Durum Makinesi

```
   [ AVAILABLE ]  <----------------------------------------------------+
         |                                                             |
         | (Misafir gelir / QR taranır / garson oturtur)                 |
         v                                                             |
    [ SEATED ]                                                         |
         |                                                             |
         | (Sipariş gönderilir)                                           |
         v                                                             |
[ ORDER_PENDING ]                                                      |
         |                                                             |
         | (KDS hazırlığı başlatır)                                           |
         v                                                             |
  [ PREPARING ]                                                        |
         |                                                             |
         | (Tüm ürünler servis edilir)                                 |
         v                                                             |
   [ SERVED ]  <------------------+                                    |
         |                        | (Yeni sipariş turu)          |
         | (Müşteri hesabı ister)  |                                    |
         v                        |                                    |
[ BILL_REQUESTED ] ---------------+                                    |
         |                                                             |
         | (Ödemeler tamamlanır)                                      |
         v                                                             |
     [ PAID ]                                                          |
         |                                                             |
         | (Masa temizlenir)                                 |
         +-------------------------------------------------------------+
```

| Mevcut Durum | Olay / Eylem | Sonraki Durum | Yetkili Roller | Değişmez Kurallar ve Koruma Koşulları |
| :--- | :--- | :--- | :--- | :--- |
| `AVAILABLE` | `SEAT_GUESTS` | `SEATED` | Garson, Kasa, Müdür, Müşteri | Masada başka etkin oturum bulunamaz. |
| `SEATED` | `SUBMIT_ORDER` | `ORDER_PENDING` | Müşteri, Garson, Kasa | Sepet ve ürünler doğrulanır; fiyatlar dondurulur. Masa akışında en az bir geçerli kalem gerekir. |
| `ORDER_PENDING`| `START_PREPARATION`| `PREPARING` | Mutfak, Bar, Sistem | En az bir istasyon fişi hazırlığa alınır. |
| `PREPARING` | `MARK_ALL_SERVED` | `SERVED` | Garson | İptal edilmemiş tüm kalemler masaya teslim edilir; mutfak/bar yalnızca READY işaretler, servis teslimini yapmaz. |
| `SERVED` | `REQUEST_BILL` | `BILL_REQUESTED` | Müşteri, Garson | billing.bill.request yetkisi ve geçerli oturum kapsamı gerekir. |
| `SERVED` | `SUBMIT_NEW_ORDER`| `ORDER_PENDING` | Müşteri, Garson | Yeni kalemler gönderim/hazırlık korumalarından geçer; onay adımını atlayamaz. |
| `BILL_REQUESTED`| `SETTLE_PAYMENT`| `PAID` | Ödeme yöntemi için izin verilen roller | Ödeme yönteminin billing.payment.cash veya billing.payment.pos_card yetkisi gerekir. Garsonun POS yetkisi kendi kapsamındadır; nakit yetkisi değildir. Kalan borç sıfır olmalıdır. |
| `PAID` | `CLEAR_TABLE` | `AVAILABLE` | Garson, Kasa, Müdür | Oturum kapanmış ve masa yeni misafir için hazırlanmış olmalıdır. |

---

## 3. Sipariş ve Sipariş Kalemi Durum Makinesi

```
   [ DRAFT ] ------------------------> [ CANCELLED ]
       |                                     ^
       | (Siparişi gönder)                      |
       v                                     |
  [ SUBMITTED ] -----------------------------+ (Hazırlık öncesi iptal)
       |                                     |
       | (İstasyon teslim alır)               |
       v                                     |
[ IN_PREPARATION ] --------------------------+ (Yönetici PIN onayıyla iptal)
       |                                     ^
       | (İstasyon hazır işaretler)               |
       v                                     |
    [ READY ] <--- (Fişi geri çağır)           |
       |                                     |
       | (Masaya servis edilir)                |
       v                                     |
   [ SERVED ] -------------------------------+ (Yönetici ikramı / iptali)
```

| Mevcut Durum | Olay / Tetikleyici | Sonraki Durum | Yetkili Roller | Notlar / Koruma Koşulları |
| :--- | :--- | :--- | :--- | :--- |
| `DRAFT` | `SUBMIT_ORDER` | `SUBMITTED` | Müşteri, Garson, Kasa | Sepet ve ürünler doğrulanır; fiyatlar dondurulur. Masa akışında en az bir geçerli kalem gerekir. |
| `SUBMITTED` | `ACKNOWLEDGE_TICKET` | `IN_PREPARATION`| Mutfak, Bar | Fiş KDS üzerinde görünür; hazırlık sayacı başlar. |
| `SUBMITTED` | `CANCEL_ITEM` | `CANCELLED` | Restoran Admini, Müdür, Garson, Kasa | orders.items.cancel_pre_prep gerekir; müşteri doğrudan iptal etmez, personelle iletişime geçer. |
| `IN_PREPARATION`| `BUMP_READY` | `READY` | Mutfak, Bar | Garsona masaya servis için bildirilir. |
| `IN_PREPARATION`| `SUPERVISOR_VOID` | `CANCELLED` | Restoran Admini, Müdür | Yönetici PIN doğrulaması ve gerekçe gerekir. |
| `READY` | `RECALL_TICKET` | `IN_PREPARATION`| Mutfak, Bar | Yanlışlıkla hazır işaretlenen fiş hazırlığa geri döner. |
| `READY` | `DELIVER_TO_TABLE` | `SERVED` | Garson | Garson masaya teslimi doğrular. |
| `SERVED` | `POST_SERVICE_VOID` | `CANCELLED` | Restoran Admini, Müdür | Resmî denetim kaydı ve gerekçe gerekir. |

---

## 4. Hesap ve Ödeme Durum Makinesi

```
   [ OPEN ] (Yeni siparişler burada toplanır)
       |
       | (Kısmi ödeme alınır)
       v
[ PARTIALLY_PAID ] <-----+
       |                 | (Ek kısmi ödeme)
       |                 +--+
       | (Kalan borç ödenir)
       v
 [ FULLY_PAID ]
       |
       | (Yönetici iadeyi onaylar)
       v
  [ REFUNDED ]
```

| Mevcut Durum | Olay / Tetikleyici | Sonraki Durum | Koruma Koşulları ve Kurallar |
| :--- | :--- | :--- | :--- |
| `OPEN` | `RECEIVE_PARTIAL_PAYMENT`| `PARTIALLY_PAID` | Ödeme sıfırdan büyük ve kalan borçtan küçüktür; yöntem yetkisi ve kapsam denetlenir. |
| `OPEN` | `RECEIVE_FULL_PAYMENT` | `FULLY_PAID` | Ödeme kalan borca eşittir. |
| `PARTIALLY_PAID` | `RECEIVE_PARTIAL_PAYMENT` | `PARTIALLY_PAID` | Ödeme sıfırdan büyük ve kalan borçtan küçüktür; yöntem yetkisi ve kapsam denetlenir. |
| `PARTIALLY_PAID`| `RECEIVE_REMAINING` | `FULLY_PAID` | Kalan borç 0.00 olur. |
| `FULLY_PAID` | `AUTHORIZE_REFUND` | `REFUNDED` | Yönetici PIN doğrulaması ve denetim izi gerekir. |
| `OPEN` | `VOID_SESSION` | `VOIDED` | Yalnızca bütün siparişler iptal edilmişse ve borç 0.00 ise. |

---

## 5. Eşzamanlılık ve Değişmez Kurallar

1. `orders`, `table_sessions`, `bills` kayıtları `version` taşır; geçiş `WHERE version = :expected_version` ile sürümü denetler.
2. Çoklu kart/nakit paylaşımında her ödeme ayrı kaydedilir; kalan borç atomik olarak kontrol edilir.
3. Müşterinin doğrudan iptal yetkisi yoktur. Yetkili personelin iptali ile hazırlık yarışında yetki, sürüm ve mevcut durum atomik denetlenir; çelişen geçişlerden yalnızca biri başarılı olur. Hazırlık önce commit edilirse sıradan iptal reddedilir, yönetici iptali gerekir. Commit sırasından bağımsız koşulsuz öncelik verilemez.

Teknik varlık adları: sipariş `Order`, sipariş kalemi `OrderItem`.
