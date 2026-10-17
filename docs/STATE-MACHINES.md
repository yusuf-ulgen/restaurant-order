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

> **Faz 8 Sipariş Çekirdeği ve Deterministik Durum Türetme (Order.DeriveStatus):**
> - `Order/OrderItem` yalnızca `Draft`, `Submitted`, `InPreparation`, `Ready`, `Served` ve `Cancelled` durumlarını izler.
> - Sipariş (`Order`) aggregate genel durumu, kalemlerinin (`OrderItem`) durumlarından deterministik türetilir:
>   1. Tüm kalemler `Cancelled` ise -> Sipariş durumu `Cancelled`.
>   2. İptal edilmemiş tüm aktif kalemler `Served` ise -> Sipariş durumu `Served`.
>   3. Herhangi bir kalem `Ready` ise ve hiçbir kalem `InPreparation` değilse -> Sipariş durumu `Ready`.
>   4. Herhangi bir kalem `InPreparation` ise veya karışık hazırlık evresindeyse (`Submitted` ile `Ready`/`Served` bir arada) -> Sipariş durumu `InPreparation`.
>   5. Yalnızca `Submitted` durumundaki kalemler varsa -> Sipariş durumu `Submitted`.
> - İstasyon fişinin istasyon tarafından kabul edilmesi (`Acknowledged`), gelecekteki `StationTicket` (KDS) alanına aittir; `Order` aggregate'inde durum değildir.
> - `Paid`, sipariş durumu değildir; ilerideki `Bill` (Hesap/Adisyon) durumudur.
> - `Closed`, `DiningSession` ve `Bill` yaşam döngüsüne aittir; `Order` veya `OrderItem` durumları arasında yer almaz.
> - Her durum geçişinde `Order.ConcurrencyToken` yenilenir; eski ETag / `If-Match` ile yapılan istekler `409 Conflict` veya `412 Precondition Failed` ile korunur.

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

---

## 6. DiningSession Durum Makinesi (Faz 6)

```text
   [ OPEN ] (1)
       |
       | (Aktive et / Activate)
       v
   [ ACTIVE ] (2)
       |
       | (Hesap iste / RequestBill)
       v
[ BILL_REQUESTED ] (3)
       |
       | (Kapat / Close)
       v
   [ CLOSED ] (4) [Son Durum / Terminal]
```

| Mevcut Durum | Olay / Tetikleyici | Sonraki Durum | Yetkili Roller | Değişmezler ve Koruma Koşulları |
| :--- | :--- | :--- | :--- | :--- |
| `OPEN` (1) | `ACTIVATE_SESSION` | `ACTIVE` (2) | Sistem, Garson, Kasa | Yalnızca `Open` durumundaki oturumlar aktif edilebilir (`Activate()`). |
| `ACTIVE` (2) | `REQUEST_BILL` | `BILL_REQUESTED` (3) | Müşteri, Garson, Kasa | Misafir veya personel hesap talebinde bulunur (`RequestBill()`). Yalnızca `Active` durumunda çağrılabilir. |
| `BILL_REQUESTED` (3) | `CLOSE_SESSION` | `CLOSED` (4) | Garson, Kasa, Müdür | Hesap kapatma tamamlanır (`Close(reason)`). Son durumdur (terminal). |

### DiningSession Değişmezleri:
1. **Doğrusal Yaşam Döngüsü ve Zaman Damgası Kısıtları:** Oturum kesin olarak `Open (1) -> Active (2) -> BillRequested (3) -> Closed (4)` doğrusal akışını izler. Atlamalı doğrudan geçişler (`Open -> Closed`, `Active -> Closed` veya `BillRequested -> Active`) alan modelinde bulunmaz ve veritabanı check constraint'i `ck_dining_sessions_status_timestamps` ile zorunlu kılınır:
   - `Open` (1): `activated_at`, `bill_requested_at`, `closed_at` alanlarının tümü `NULL` olmalıdır.
   - `Active` (2): `activated_at` dolu olmalı; `bill_requested_at` ve `closed_at` alanları `NULL` olmalıdır.
   - `BillRequested` (3): `activated_at` ve `bill_requested_at` dolu olmalı; `closed_at` `NULL` olmalıdır.
   - `Closed` (4): Standart kapanışta (`merged_into_session_id IS NULL`) `activated_at`, `bill_requested_at` ve `closed_at` alanlarının tümü dolu olmalıdır.
2. **Kronolojik Sıralama Kısıtı:** Veritabanı check constraint'i `ck_dining_sessions_chronology` ile zaman damgalarının mutlak UTC kronolojisi korunur:
   - Standart kapanışlarda: `opened_at <= activated_at <= bill_requested_at <= closed_at`.
   - Oturum birleştirme kapanışlarında: `opened_at <= closed_at` (ve varsa `activated_at <= closed_at`).
3. **Masa Başına Tek Kapatılmamış Oturum:** PostgreSQL kısmi benzersiz indeksi `ix_dining_sessions_tenant_branch_table_active` (`WHERE status <> 4`) ile zorunlu kılınır. Eşzamanlı taramalar benzersizlik ihlalini yakalar ve mevcut oturuma bağlanır.
4. **Kapatılmış (Closed) Son Durum:** `Closed` (4) durumuna geçtikten sonra durum değişiklikleri kesin olarak reddedilir (HTTP 409 Conflict).
5. **Anında Önbellek Geçersiz Kılma:** Bir oturum kapatıldığında `InvalidateSessionCacheAsync` çağrılır ve müşteri JWT belirtecinin geçerliliği derhal sonlandırılır.
6. **Masa Devre Dışı Bırakma Koruması:** Kapatılmamış aktif oturumu bulunan bir masanın devre dışı bırakılması engellenir (HTTP 409 Conflict).
7. **Masa Taşıma ve Oturum Birleştirme Mekanikleri (Faz 6.4):** Oturum transferi boş ve aktif masalara atomik yönlendirme sağlar; oturum birleştirme iki masanın oturumunu hedefte birleştirerek kaynağı kontrollü bir domain operasyonu (`CloseAsMerged()`) ile terminalize eder (`Closed`, `merged_into_session_id IS NOT NULL`). Bu terminal geçiş genel `Close()` kuralını gevşetmez; yalnızca birleştirme akışında kontrollü olarak işletilir. Her hareket `tenancy.dining_session_movements` tablosunda değişmez biçimde saklanır. Deterministik UUID kilitleme sırasıyla deadlock riski önlenir.
