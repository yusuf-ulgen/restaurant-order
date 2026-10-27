# Faz 10 Uygulama Takip Belgesi (`docs/PHASE-10-TRACKER.md`)

Bu belge, `restaurant-order` projesinde **Faz 10: Garson ve Operasyon Uygulaması (WAIT-01–WAIT-07)** aşamasının tüm alt parçalarını, mimari sınırlarını, API eşleşmelerini ve uygulama durumunu takip eder.

> **Kapsam Sınırları ve Mimari İlkeler:**
> - **Yeniden Kullanım:** Mevcut `FloorService`, `StaffOrderService`, PIN ve terminal altyapısı, `ServiceRequest` domain modeli ve `OperationalOutbox` doğrudan yeniden kullanılır.
> - **Otoriter Okuma Modelleri:** Kat planı, masa oturum detayı, sipariş özeti, aktif servis çağrıları ve hazır kalemler için sunucu tarafında yetkili ve N+1 sorgu üretmeyen sayfalı modeller sağlanır.
> - **Masa Durumu Türetimi:** Masa durumu (`Available`, `Occupied`/`Seated`, `OrderPending`, `ReadyToServe`, `BillRequested`, `Inactive`) kalıcı bir veritabanı enum'ı veya finansal ödeme durumu uydurmadan; `RestaurantTable.IsActive`, `DiningSession.Status`, `Order`/`OrderItem` durumları ve `ServiceRequest` verilerinden dinamik türetilir.
> - **Servis Talepleri Yönetimi:** Personel servis çağrısı kabul (`Acknowledge`) ve tamamlama (`Complete`) uçları `service.request.manage` yetkisiyle korunur. Concurrency token ile atomik çalışır; müşteri iptaliyle yarışta çelişkili commit engellenir.
> - **Transactional Outbox ve Invalidation:** Servis durumu değişiklikleri (`table.service_request_status_changed`) ve gerekli salon invalidation olayları domain mutasyonuyla aynı transaction içinde outbox'a yazılır.
> - **Para Alanları Sözleşmesi:** Para alanları mevcut minor-unit sözleşmesiyle (`long SubtotalMinorUnits`, `TotalMinorUnits`) taşınır; sipariş toplamı tahsil edilmiş tutar veya kalan borç diye sunulmaz.
> - **WAIT-06 Sınırı:** Bu fazda WAIT-06 kapsamında yalnızca sipariş özeti, sipariş toplamı (minor-unit) ve hesap isteme tetikleyicisi (`POST .../request-bill`) çalışır. Fatura bölme motoru, nakit/kart tahsilatı, ödeme entegrasyonu ve iade Faz 13 ve Faz 16 kapsamındadır.
> - **Gelecek Fazlar:** Faz 11 KDS hazırlık fişleri ve istasyon yönlendirmesi, Faz 12 ağ üzerinden yazdırma, Faz 13 hesap motoru ve sonraki ödeme entegrasyonları kesinlikle kapsam dışıdır.
> - **Açık Kalan Önceki Faz Maddeleri:** Faz 9'un fiziksel FCM teslimi (sağlayıcı kimlik bilgileri ve izinli HTTPS cihaz) ile üretim SHARE kilidi ölçüm açıkları açık tutulur; tamamlandı gösterilmez.

---

## 1. WAIT-01 – WAIT-07 Ekran ve Özellik Envanteri

| Ekran Kodu | Ekran Adı | Durum | Mevcut / Eksik Bileşenler | API Eşleşmesi |
| :--- | :--- | :--- | :--- | :--- |
| `WAIT-01` | **Hızlı PIN Kilit Ekranı** | Parça 1 Altyapı | Mevcut: `PinPadView`, terminal doğrulama, token saklama.<br>Hazır: 4 haneli PIN ve vardiya kilidi. | `POST /api/v1/auth/terminals/staff/login-pin` |
| `WAIT-02` | **Kat Planı ve Masa Izgarası** | Parça 1 Altyapı | Mevcut: `FloorService.GetBranchFloorStatusAsync`.<br>Eklendi: Dinamik türetilmiş masa durumu (`Available`, `Seated`, `OrderPending`, `ReadyToServe`, `BillRequested`, `Inactive`), aktif çağrı ve hazır kalem sayaçları. | `GET /api/v1/floor/branches/{branchId}/status` |
| `WAIT-03` | **Masa Oturumu Detayı** | Parça 1 Altyapı | Mevcut: `GetSessionAsync`, `GetSessionOrdersAsync`.<br>Eklendi: Oturuma ait servis çağrıları ve hazır kalemler okuma modeli. | `GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}`<br>`GET /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders` |
| `WAIT-04` | **Hızlı Sipariş Girişi** | Parça 1 Altyapı | Mevcut: `StaffOrderService.CreateStaffOrderAsync`, Hızlı-86 ve varyant/modifier doğrulama. | `POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/orders` |
| `WAIT-05` | **Masa Taşıma / Birleştirme** | Parça 1 Altyapı | Mevcut: `FloorService.TransferSessionAsync`, `FloorService.MergeSessionAsync`. | `POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/transfer`<br>`POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/merge` |
| `WAIT-06` | **Mobil Fatura ve Ödeme (Sınırlı)** | Parça 1 Altyapı (Sınırlandı) | **Çalışan Kapsam:** Sipariş ara toplamı, vergi, servis ücreti, genel sipariş toplamı (minor-unit) ve hesap isteme tetikleyicisi.<br>**Kapsam Dışı:** Fatura bölme motoru, nakit/kart tahsilat, ödeme kaydı ve POS cihazı entegrasyonu (Faz 13/16). | `POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/request-bill` |
| `WAIT-07` | **Bildirim Çekmecesi & Servis Talepleri** | Parça 1 Geliştirildi | Eksikti, uygulandı: Personel servis çağrısı listeleme, filtreleme, kabul (`Acknowledge`) ve tamamlama (`Complete`). Hazır sipariş kalemleri listesi. | `GET /api/v1/floor/branches/{branchId}/service-requests`<br>`POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/acknowledge`<br>`POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/complete`<br>`GET /api/v1/floor/branches/{branchId}/orders/ready-items` |

---

## 2. Faz 10 Parça Dağılımı ve İlerleme

| Parça | Tanım | Durum | Temel Çıktılar |
| :--- | :--- | :--- | :--- |
| **Parça 1/5** | **Backend Altyapısı, Servis Çağrıları, Okuma Modelleri, Eşzamanlılık ve Outbox** | **Tamamlandı** | `service.request.manage` RBAC yetkilendirmesi, personel servis çağrısı uçları (listele/kabul/tamamla), hazır kalem uçları, dinamik masa durumu türetimi, ETag/concurrency koruması, Outbox invalidation, çok kiracılı/şube RLS ve eşzamanlılık testleri. |
| **Parça 2/5** | **WAIT-01, WAIT-02, WAIT-03 ve WAIT-05 Operasyon Arayüzleri** | **Tamamlandı** | `apps/operations-web` gerçek API'ye bağlandı: Terminal/PIN kilitleme ve kullanıcı değiştirme, doğrulanmış terminal kapsamıyla şube kilidi, alan filtreli ve aranabilir masa listesi/ızgarası + 2D etkileşimli kat planı (WAIT-02), oturma süresi, servis çağrıları kabul/tamamlama ve sipariş turları/toplamı dökümlü detay çekmecesi (WAIT-03), kaynak/hedef seçimli, açık onaylı ve concurrency korumalı transfer/birleştirme modalı (WAIT-05), 320px mobil uyum ve Playwright E2E browser testleri. |
| **Parça 3/5** | **Hızlı Sipariş Girişi (WAIT-04) ve Sipariş Kalemi İptalleri** | **Tamamlandı** | Kompakt garson menü seçimi, arama, varyant ve modifier seçici (`StaffOrderItemCustomizer`), sepet özeti (`StaffOrderCartSummary`), otoriter Half-Up minor-unit fiyatlandırma, tenant/branch/session/staff scoped taslak ve Idempotency-Key yönetimi (`staffOrderDraftStorage`), 409 (86 stok tükenmesi, fiyat değişimi, oturum kapandı) hata izolasyonu; Pre-prep gerekçeli iptal ve In-prep/Ready/Served için Supervisor Void (PIN memory-only, storage/log scrubbed) modalı (`CancelItemModal`). |
| **Parça 4/5** | **Bildirim Çekmecesi ve Operasyon Ekranlarının Güvenilir Realtime Koordinasyonu (WAIT-07)** | **Tamamlandı** | Mevcut event-log placeholder'ı gerçek operasyon verisine bağlandı (`useOperationsRealtimeCoordinator`, `OperationsCallsDrawer`). Otoriter bekleyen çağrı ve hazır ürün sayaçları; debounce/coalescing invalidation; event deduplication ve catch-up chime susturma; concurrency token korumalı kabul/tamamlama; bildirim kapatma ile servis çağrısı durumunun ayrıştırılması; hazır kalem uyarısından masaya odaklanma (`focusedTableId`); 409 yarış durumunda otomatik snapshot yenileme. |
| **Parça 5/5** | **Kapsam Doğrulaması, Güvenlik/Negatif Akışlar ve Gerçek Browser Kabul Testleri (WAIT-01–WAIT-07)** | **Tamamlandı** | 13 kabul kriterinin tamamı doğrulandı: Güvenilir terminal ve PIN kilitlenme, dinamik masa durumu/filtreleme, otoriter sipariş girişi, yanıt kaybı/idempotency, Quick-86/fiyat değişimi, yönetici supervisor void, servis çağrısı döngüsü, transfer/birleştirme atomik kilit, WAIT-06 hesap isteme sınırı (sahte tahsilat yok), 2 tenant/2 branch IDOR/RBAC izolasyonu, offline-reconnect, continuous auth iptali, mobil 320px responsive ve sıfır taşma. |

---

## 3. Parça 1/5 Doğrulama ve Güvenlik Denetimleri

- [x] **RBAC ve Yetki Matrisi:** `Permissions.ServiceRequestManage` (`service.request.manage`) eklendi; RestaurantAdmin, BranchManager, Cashier ve Waiter için `Full`, diğer roller için `Denied` doğrulandı.
- [x] **Personel Servis Çağrısı API'si:**
  - `GET /api/v1/floor/branches/{branchId}/service-requests`: Sayfalı (limit/offset), durum/masa/oturum filtreli, deterministik sıralama.
  - `POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/acknowledge`: If-Match ETag veya body ConcurrencyToken doğrulamalı, atomik durum geçişi (`Open -> Acknowledged`).
  - `POST /api/v1/floor/branches/{branchId}/service-requests/{requestId}/complete`: ConcurrencyToken doğrulamalı, atomik durum geçişi (`Open/Acknowledged -> Completed`).
- [x] **Hazır Kalemler Okuma Modeli:** `GET /api/v1/floor/branches/{branchId}/orders/ready-items` ile şubedeki aktif masalara ait hazır ürünlerin N+1 olmadan listelenmesi.
- [x] **Dinamik Masa Durumu:** `FloorService.GetBranchFloorStatusAsync` içinde ek kalıcı enum veya finansal ödeme kolonu eklenmeksizin `RestaurantTable`, `DiningSession`, `Order` ve `ServiceRequest` verilerinden türetim.
- [x] **Eşzamanlılık ve Yarış Koruması:** Müşteri iptali ile personel kabulü/tamamlaması yarışında EF Core concurrency token ve veritabanı kısıtlarıyla çelişkili commit önlendi.
- [x] **Transactional Outbox:** `table.service_request_status_changed` olayları domain mutasyonu ile aynı veritabanı transaction'ında outbox'a kaydedildi.
- [x] **Veri İzolasyonu:** İki farklı tenant ve iki farklı branch arasında NOSUPERUSER NOBYPASSRLS ile çapraz erişim test edildi ve engellendi.

---

## 4. Parça 2/5 Doğrulama ve Operasyon Arayüzü Denetimleri

- [x] **Güvenilir Terminal ve PIN Kapsamı (WAIT-01):** Terminal kaydı (`ro_terminal_context`) ve personel oturumu (`ro_staff_principal`) korunur; şube ID doğrudan doğrulanmış terminal kapsamından (`terminal.branchId`) beslenir. Arayüzde yetkisiz şube seçimi engellendi.
- [x] **Kat Planı ve Masa Izgarası (WAIT-02):**
  - Alan bazlı filtre butonları (Bahçe, Teras vb.) ve anlık Türkçe uyumlu masa arama kutusu.
  - Masa durumları gerçek API verisinden türetildi (`Available`, `Seated`, `OrderPending`, `ReadyToServe`, `BillRequested`, `Inactive`).
  - Hem renk hem metin ve ikon desteği (`🟢 Boş`, `👤 Oturuldu`, `⏳ Sipariş Hazırlanıyor`, `🍽️ Servise Hazır`, `🧾 Hesap İstendi`, `⛔ Kullanım Dışı`).
  - Açık servis çağrısı (`🔔`) ve hazır ürün (`🍽️`) sayaç rozetleri.
  - Izgara (Grid) ve 2D koordinatlı etkileşimli Kroki/Canvas modları arasında geçiş.
- [x] **Masa Oturumu Detayı (WAIT-03):**
  - Açık oturum, kişi sayısı, oturma süresi (dakika) gösterimi.
  - Açık servis çağrıları: tek dokunuşla `Kabul Et` (`Acknowledge`) ve `Tamamla` (`Complete`) aksiyonları; atomik ETag / concurrency token korumalı.
  - Sipariş turları (`roundNumber`), kalem listesi ve gerçek sipariş toplamı önizlemesi (ara toplam, vergi, servis ücreti, genel toplam).
  - Yetkili oturum açma, oturum aktifleştirme ve masadan hesap isteme (`request-bill`) tetikleyicisi.
- [x] **Masa Taşıma ve Birleştirme (WAIT-05):**
  - Kaynak ve hedef masa seçimi; taşıma için sadece müsait boş masalar, birleştirme için aktif oturumlu masalar filtrelenir.
  - Açık onay kutusu (Geri alınamaz işlem onayı).
  - `If-Match` ConcurrencyToken doğrulaması; 409 Conflict durumunda sessiz üzerine yazma engellenerek kullanıcıya net hata ikazı.
- [x] **Mobil Uyumluluk ve Erişilebilirlik:**
  - 320 px ekranda sıfır yatay taşma; min 44 px dokunma alanları.
  - Yükleniyor (`Spinner`), boş liste (`EmptyState`), hata ve tekrar dene (`ErrorState`), bayat veri ikazı (`stale-data`).
  - Çevrimdışı / ağ kopması anında masalar sahte olarak boşaltılmaz veya çağrı sayısı sıfırlanmaz.
- [x] **WAIT-06 Sınırı:** Borcun ödendiği simüle edilmedi; tahsilat veya kapatma yapılmaksızın yalnızca sipariş toplamı önizlendi.
- [x] **Test Kapsamı:** `operations-web` 26 birim/bileşen testi, `tests/e2e/tests/waiter-operations.spec.ts` Playwright testleri ve tüm workspace typecheck / lint testleri başarıyla doğrulandı.

---

## 5. Parça 3/5 Doğrulama, WAIT-04 ve Sipariş Müdahaleleri Denetimleri

- [x] **Masa Oturumunda Menü ve Hızlı Sipariş (WAIT-04):**
  - Aktif masa oturumundan (`SessionDetailDrawer`) tek dokunuşla "➕ Yeni Sipariş Gir" çekmecesi (`StaffOrderDrawer`).
  - `GET /api/v1/catalog/branches/{branchId}/runtime-menu` üzerinden güncel menü, kategori sekmeleri ve Türkçe uyumlu anlık ürün araması (`StaffOrderItemsGrid`).
  - Hızlı-86 engellemesi: Stokta olmayan ürünler (`isAvailable === false`) anında soluk ve devre dışı.
- [x] **Varyant, Değiştirici ve Not Seçimi (`StaffOrderItemCustomizer`):**
  - Tekli porsiyon/varyant seçimi (varsayılan varyant otomatik).
  - Çoklu veya tekli modifier grupları; min/max kuralları doğrulaması (`validateCustomization`).
  - Adet seçimi (1–99) ve 200 karakter sınırlı, kontrol karakterlerinden arındırılmış müşteri/garson notu (`normalizeCustomerNote`).
- [x] **Ortak Mantık ve Otoriter Fiyatlandırma:**
  - `@restaurant-order/contracts` paketinde `calculateItemPrice`, `calculateLineItemFinancials`, `calculateOrderTotals` ve `computeOrderPayloadSignature` kullanıldı; müşteri ve personel için iki farklı kopya iş mantığı icat edilmedi.
  - Minor-unit tamsayı aritmetiği ve sunucu `pricingPolicyFingerprint` otoriter olarak hesaplandı.
- [x] **Taslak ve İdempotency Yönetimi (`staffOrderDraftStorage`):**
  - Scoped anahtar: `ro_staff_order_draft_${tenantId}_${branchId}_${sessionId}_${staffUserId}`; personel veya masa değişiminde başka taslaklar ekranda görünmez.
  - Çift tıklama, timeout ve yanıt kaybında aynı `Idempotency-Key` korunur; sepet veya fiyatlandırma değiştiğinde anahtar anında yenilenir.
  - Offline taslak yerel depoda korunur; ancak bağlantı tekrar geldiğinde ASLA otomatik sipariş gönderilmez (açık kullanıcı onayı şarttır).
- [x] **409 ve Negatif Akış İzolasyonu:**
  - `ORDER_PRICE_CHANGED`: Menü güncellenir, taslak korunur ve güncel tutarla kullanıcıdan yeni onay istenir.
  - `ITEM_OUT_OF_STOCK`: Stok tükenmesi hatası gösterilir, taslak korunur.
  - `SESSION_NOT_ACTIVE` / `BILL_REQUESTED`: Oturum kapalı uyarısı verilir.
- [x] **Sipariş Kalemi İptal Akışları (`CancelItemModal`):**
  - Hazırlık öncesi (`Submitted`): Sadece gerekçe (`reason`) ile iptal edilir (`orders.items.cancel_pre_prep`).
  - Hazırlık/servis aşaması (`InPreparation`, `Ready`, `Served`): Yönetici onayı (`SupervisorUserId` ve 4 haneli `SupervisorPin`) zorunludur.
  - Güvenlik: Garson PIN'i yönetici yetkisi yerine kullanılamaz; PIN kesinlikle kalıcı depoya (storage) veya loglara yazılmaz, form state'inde tutulup istek sonrasında anında bellekten temizlenir.
  - Concurrency: Siparişin güncel `concurrencyToken` değeri istekte gönderilir; eski veriyle gelen iptal yarışında 409 hatasıyla kullanıcıya güncel durum yansıtılır.
- [x] **Doğrulama Sonuçları:**
  - `apps/operations-web`: 31/31 birim testi eksiksiz geçti (`staff-order-drawer.test.tsx` ve `staff-order-cancel.test.tsx` dahil).
  - Frontend birim testleri: 435/435 test passed (0 fail).
  - .NET birim testleri: 1860/1860 test passed (0 fail).
  - .NET mimari testleri: 10/10 test passed (0 fail).
  - Dosya boyutu limitleri: 1207 dosya kontrol edildi, tüm elle yazılan dosyalar 450 satır sınırının altında (0 uyarı).
  - Typecheck: 7 paket hatasız passed.
  - Linter: 0 hata, 0 uyarı.

---

## 6. Parça 4/5 Doğrulama ve WAIT-07 Realtime Koordinasyon Denetimleri

- [x] **Gerçek Operasyonel Veri ve Otoriter Sayaç:**
  - `apps/operations-web/src/floor/useOperationsRealtimeCoordinator.ts` oluşturuldu; bellekteki `liveNotifications` placeholder'ı kaldırıldı.
  - Bekleyen çağrı sayısı rastgele event sayısından değil, otoriter `fetchActiveServiceRequests(branchId)` kaynağından türetildi.
  - Hazır ürün sayısı `fetchBranchReadyItems(branchId)` kaynağından beslendi.
  - Sayfa yenilendiğinde (reload) aktif çağrılar ve hazır ürünler kaybolmaz; mount anında sunucudan otoriter snapshot çekilir.
- [x] **WAIT-07 Bildirim ve Çağrı Çekmecesi (`OperationsCallsDrawer`):**
  - Üç sekmeli mobil uyumlu yapı: "🔔 Aktif Çağrılar", "🍽️ Hazır Ürünler" ve "📜 Olay Akışı".
  - Çağrı kartlarında: Masa no, talep türü (Garson, Su, Peçete, Yardım), müşteri notu, bekleme süresi ("2 dk önce") ve durum rozeti.
  - "Kabul Et" (`Open -> Acknowledged`) ve "Tamamla" (`Acknowledged -> Completed`) aksiyonları; güncel `concurrencyToken` zorunlu kılındı.
  - Ayrıştırma kuralı: Olay akışındaki bir bildirimi kapatmak veya temizlemek servis talebini kabul etmez ya da tamamlamaz.
  - Hazır ürün kartında: Masa no, tur no, ürün adı ve adedi; "Masaya Git" tıklandığında çekmece kapanır ve kat planında ilgili masanın detay çekmecesine (`focusedTableId`) odaklanılır.
- [x] **Realtime Eşitleme, Debouncing ve Coalescing:**
  - Phase 9 `useRealtimeEvents` hook'u doğrudan kullanıldı; ikinci bir SignalR/push bağlantısı oluşturulmadı.
  - Servis çağrısı ve hazır ürün olayları snapshot invalidation olarak kullanıldı; 60ms debounce ile coalescing uygulandı.
  - `processedEventIdsRef` ile event deduplication; aynı olay tekrar geldiğinde sayaç, kart veya ses çoğalmaz.
  - `meta?.isCatchUp` kontrolüyle catch-up sırasında geçmiş olaylar için ses (`playChime`) çalınması engellendi.
  - `onResyncRequired` tam resync tetikler: hem kat planı, hem aktif çağrılar, hem de hazır ürünler yeniden çekilir.
  - Sürekli yetkilendirme (continuous auth): Personel PIN kilitlendiğinde veya logout olduğunda tüm bağlantı, timer ve talepler temizlenir; özel veri korunur.
- [x] **Eşzamanlılık ve Yarış Testleri:**
  - `StaffServiceRequestServiceUnitTests`: İki garsonun aynı çağrıyı kabul yarışında birincisinin başarılı olduğu, ikincisinin `ConcurrencyConflictException` (409) aldığı test edildi.
  - Tekrarlanan tamamlama (repeated complete) ve müşteri iptali yarışında concurrency conflict doğrulandı.
  - Şube/kiracı izolasyonu ve yetkisiz roller (`Kitchen`, `Bar`, `Customer`, `SuperAdmin`) için 403 Forbidden erişim engeli doğrulandı.
- [x] **Test ve Kalite Kanıtları:**
  - `apps/operations-web`: 7/7 test dosyası, 37/37 birim testi eksiksiz geçti (`operations-calls-drawer.test.tsx` dahil).
  - Frontend birim testleri: 441/441 test passed (0 fail).
  - .NET birim testleri: 1862/1862 test passed (0 fail).
  - .NET mimari testleri: 10/10 test passed (0 fail).
  - Kalite kapıları (`verify:gates`): 116 pass, 0 fail.
  - Linter ve Typecheck: 0 hata, 0 uyarı.
  - Dosya boyutu limitleri: 1210 dosya kontrol edildi (0 uyarı).

---

## 7. Parça 5/5: Kapsam Doğrulaması ve 13 Kabul Kriteri Denetimi

- [x] **1. Güvenilir Terminal & PIN Giriş:** `PinPadView` ile 4 haneli PIN, hatalı PIN'de kilitli kalma ve anında `Kilitle` ile kilit ekranına dönüş doğrulandı.
- [x] **2. Gerçek Alan / Masa Verisi:** `GET /status` üzerinden dinamik türetilmiş 6 durum (`Available`, `Seated`, `OrderPending`, `ReadyToServe`, `BillRequested`, `Inactive`), alan filtreleri ve oturum açma/aktifleştirme (`openSession`, `activateSession`) doğrulandı.
- [x] **3. Otoriter Personel Siparişi:** `StaffOrderDrawer` ile menü, arama, varyant ve modifier seçimi; Half-Up minor-unit fiyatlandırma ve tekil kayıt oluşumu doğrulandı.
- [x] **4. İdempotency & Yanıt Kaybı:** `Idempotency-Key` ve `staffOrderDraftStorage` ile reload/çift dokunmada mükerrer sipariş oluşumu engellendi.
- [x] **5. Hızlı-86 & Fiyat Değişimi:** 409 `ITEM_OUT_OF_STOCK` ve `ORDER_PRICE_CHANGED` durumlarında taslak korunarak kullanıcı bilgilendirildi.
- [x] **6. Kalem İptali & Supervisor Void:** Pre-prep gerekçeli iptal; hazırlık/servis aşamasında Supervisor PIN doğrulamalı void (`CancelItemModal`); PIN'in log ve depolara yazılmaması sağlandı.
- [x] **7. Servis Çağrısı Döngüsü:** Müşteri servis çağrısının operasyon ekranına anlık ulaşması, güncel concurrency token ile kabul/tamamlanması ve müşteriye yansıması doğrulandı.
- [x] **8. Masa Taşıma & Birleştirme:** `TransferSession` ve `MergeSession` uçları, açık onay diyaloğu, artan GUID satır kilitleme ve eski ETag/token koruması doğrulandı.
- [x] **9. WAIT-06 Hesap İsteme Sınırı:** `request-bill` ile oturumun `BillRequested` durumuna geçmesi; sahte borç kapatma veya ödeme simülasyonu yapılmaksızın gerçek sipariş toplamının (minor-unit) önizlenmesi korundu. Fatura bölme ve tahsilat Faz 13/16'ya bırakıldı.
- [x] **10. Çok Kiracılı & Şube İzolasyonu (IDOR / RBAC):** 2 tenant ve 2 branch arasında doğrudan API isteklerinde 403 Forbidden ve NOSUPERUSER NOBYPASSRLS RLS kısıtları test edildi.
- [x] **11. Offline → Reconnect → Catch-up:** SignalR kopması sonrası HTTP sync, deduplication ve catch-up sırasında ses çalınmaması doğrulandı.
- [x] **12. Continuous Auth & Revoke:** Token expire, yetki kaybı veya oturum askıya alınmasında SignalR bağlantısının kapatılması, korunan ekranın temizlenmesi ve kilit ekranına dönüş doğrulandı.
- [x] **13. Mobil 320/375 px Erişilebilirlik:** `tests/e2e/tests/waiter-operations-acceptance.spec.ts` ile 320px ekranda sıfır yatay taşma ve min 36-44px dokunma hedefleri browser seviyesinde doğrulandı.


