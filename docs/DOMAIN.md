# Alan Modeli ve Sınırları (`docs/DOMAIN.md`)

## 1. Genel Bakış

`restaurant-order`, restoran işletmesi ve servis süreçlerini kapsar. Arayüzlerde, API sözleşmelerinde ve veritabanında aynı alan terimleri kullanılır.

## 2. Alan Sınırları

```text
İşletme / kuruluş → Marka → Şube
                         ├─ Menü/katalog: Menu, Category, MenuItem, Variant, ModifierGroup
                         ├─ Masa/salon: DiningArea, Table, QRCode, TableSession
                         └─ Sipariş/KDS: Order, OrderItem, StationTicket, TicketItem
                                             ↓
                              Hesap/ödeme: Bill, Payment, Split, Tip, Refund, Commission
```

## 3. Temel Varlıklar ve İlişkiler

### 3.1. İşletme Hiyerarşisi

- **Tenant / Organization (İşletme / Kuruluş):** Üst düzey ticari tüzel yapı; örneğin sentetik Gourmet Group Inc. Abonelik, faturalandırma sözleşmesi ve platform ayarlarını taşır.
- **Brand (Marka):** İşletme altındaki farklı restoran konsepti; örneğin Gourmet Burger veya Gourmet Pizza. Menü marka kapsamına alınabilir.
- **Branch (Şube):** Markanın fiziksel restoranı; örneğin Kadıköy şubesi. Masaları, yazıcıları, personeli ve yerel stok durumunu içerir.

### 3.2. Restoran Yapılandırması

- **BrandAppearance:** Markanın ana/ikincil/vurgu renkleri, kenarlık yarıçapı, logo URL'si ve etkin gezinme öğeleri (`NavigationConfig`).
- **BranchThemeOverride:** Marka varsayılanlarını değiştiren isteğe bağlı şube teması.
- **BranchSettings:** Saat dilimi, para birimi, varsayılan/desteklenen diller, verginin fiyata dâhil olması, vergi ve servis ücreti baz puanları, sipariş kabulü ve iletişim alanları.
- **BranchOperatingHours:** Gece yarısını aşan aralıkları destekleyen haftalık açılış ve sipariş kabul saatleri.
- **DiningArea:** Şube içindeki Indoor, Terrace, Garden, BarArea veya Other alanı; kod, sıralama ve etkinlik durumu taşır.
- **PreparationStation:** Kitchen, Bar veya Other hazırlık istasyonu; şube içinde benzersiz kod, görünen ad ve etkinlik durumu.
- **BranchFeatureFlags:** Yetkilendirme önceliği korunarak değerlendirilen şube özellik bayrakları.

### 3.3. Masa ve Salon Yönetimi

- **RestaurantTable:** Şubeye ve `DiningArea` alanına bağlı masa. `table_number`, şube bazında benzersiz `code`, oturma kapasitesi `capacity`, şekil `shape` (`Round`, `Square`, `Rectangle`), etkinlik durumu `status`, `qr_version`, QR çözünürlüğü için 128-bit `public_code` ve iyimser eşzamanlılık tokenı (`row_version`) içerir.
- **TableLayout:** Masanın 2D kat planı tuvali üzerindeki koordinatları (`x`, `y`, `width`, `height`, `rotation`). Tuval sınırlarına ve boyut kurallarına göre doğrulanır.
- **DiningSession:** Masada oturan misafir grubunu temsil eden yemek oturumu. `Open -> Active -> BillRequested -> Closed` durum makinesini izler. Masada aynı anda yalnızca tek bir açık/kapanmamış (`status <> 4`) oturum bulunabilir (`ix_dining_sessions_tenant_branch_table_active`).
- **DiningSessionMovement:** Oturumun başka bir masaya taşınmasını (`Transfer`) veya iki oturumun birleştirilmesini (`Merge`) kaydeden değişmez hareket ve denetim tablosudur. Veritabanı tetikleyicisiyle güncelleme ve silmeye kapatılmıştır; işletme ve şube izolasyonunu RLS ile korur. Masalar arası transferde hedef masa boş ve aktif olmalıdır; kapasite aşımı yapılamaz. Birleştirmede kaynak oturum `Closed` ve `MergedIntoSessionId` durumuna geçirilerek terminalize edilir, misafir sayısı hedef oturuma aktarılır ve kaynak oturumun müşteri belirteçleri anında geçersiz kılınır.

### 3.4. Menü ve Katalog

- **Menu:** Şube veya zaman aralığına göre sunulabilen yiyecek/içecek koleksiyonu; kahvaltı veya tüm gün menüsü gibi.
- **MenuCategory:** Başlangıç, ana yemek, kokteyl, tatlı gibi gruplar.
- **MenuItem:** Cheeseburger veya espresso gibi ürün.
- **ItemVariant:** Farklı fiyatlı boyut/porsiyon; tek 150 g, çift 300 g, küçük/büyük gibi.
- **ModifierGroup:** Etin pişme derecesi, garnitür veya ek malzeme seçenekleri. `min_selections` zorunlu en az seçimi, `max_selections` izinli en fazla seçimi belirtir; pişme derecesinde en az bir seçim gibi.
- **ModifierItem:** İyi pişmiş, trüflü patates veya +1.50 ek kaşar gibi tek seçenek.

### 3.5. Sipariş ve Hazırlık Fişi

- **Order:** `tenant_id`, `branch_id`, `dining_session_id`, `table_id`, `round_number` ve `order_source` (`Customer`, `Staff`) ile bağlı sipariş aggregate'idir. Sunucu otoriter finansal snapshot'ları (`SubtotalMinorUnits`, `TaxMinorUnits`, `ServiceChargeMinorUnits`, `TotalMinorUnits`), para birimi, `IdempotencyKey`, `RequestFingerprint` ve `ConcurrencyToken` taşır. Genel durumu (`OrderStatus`: `Draft`, `Submitted`, `InPreparation`, `Ready`, `Served`, `Cancelled`), kalemlerinin durumlarından deterministik türetilir (`Order.DeriveStatus`).
- **OrderItem:** Siparişteki satır kalemi. `menu_item_id`, `item_variant_id`, ürün/varyant ad snapshot'ları, miktar, birim fiyat, vergi oranı baz puanı (`TaxRateBasisPoints`), hazırlık istasyonu (`PreparationStationId`), müşteri notu sanitizasyonu ve satır durumu (`OrderItemStatus`) taşır.
- **OrderItemModifier:** Kaleme seçilen ek malzeme snapshot'ı. `modifier_group_id`, `modifier_option_id`, grup adı, seçenek adı ve `price_delta_minor_units` değerlerini değişmez olarak saklar.
- **OrderAuditEntry:** Sipariş oluşturma, durum geçişi veya kalem iptali/zayiatı anında üretilen değişmez (immutable) denetim kaydıdır. PostgreSQL trigger'ı ile UPDATE/DELETE engellenmiştir. Güvenlik ve veri koruması gereği serbest metin müşteri notları audit detaylarına kopyalanmaz; maskeli metadata saklanır.
- **StationTicket (KitchenTicket / BarTicket):** Gelecekteki Faz 11 KDS aşamasına ait olup siparişin belirli mutfak/bar istasyonuna yönlendirilen alt kümesini temsil eder.
- **TicketItem:** İstasyon fişinin tek kalemi; istasyon ekranında hazırlık durumunu izler.

### 3.6. Hesap, Ödeme ve Bahşiş

- **Bill:** `TableSession` hesabı; iptal edilmemiş kalemler, vergiler, indirimler ve servis ücretlerini toplar.
- **Payment:** Hesaba karşı tam veya kısmi ödeme. `payment_method`: nakit, harici POS kartı veya dijital ağ geçidi `[Proposed / ADR Required]`.
- **Tip:** Belirli garsona veya ortak personel havuzuna ayrılan isteğe bağlı bahşiş.
- **PlatformCommission:** İşletmenin planına göre platformun tuttuğu işlem ücreti.
- **Refund:** Kesinleşmiş ödemenin tümünün/kısmının iadesi; yönetici onayı ve denetim izi gerektirir.

### 3.7. Kimlik ve Erişim

- **User:** Kimliği doğrulanmış personel veya platform görevlisi.
- **Role:** [Rol belgesindeki](./ROLES-AND-PERMISSIONS.md) sekiz standart rol.
- **UserBranchAssignment:** Personeli şubeye bağlar; isteğe bağlı hızlı PIN erişimiyle ilişkilidir.
- **AuditLog:** İptal, indirim, iade ve fiyat müdahalesi gibi kritik eylemlerin değiştirilemez kaydı.

## 4. İlişki Çoklukları

| İlişki | Çokluk | Açıklama |
| :--- | :--- | :--- |
| Organization → Brand | 1 : N | Bir işletmenin birden fazla markası olabilir. |
| Brand → Branch | 1 : N | Bir markanın birden fazla fiziksel şubesi olabilir. |
| Branch → DiningArea | 1 : N | Şube birden fazla alan içerir. |
| DiningArea → Table | 1 : N | Alan birden fazla masa içerir. |
| Table → TableSession | 1 : N, aynı anda 1 etkin | Geçmiş oturumlar saklanır; yalnızca biri etkin olabilir. |
| TableSession → Order | 1 : N | Bir oturumda birden fazla sipariş turu olabilir. |
| Order → OrderItem | 1 : N | Sipariş birden fazla kalem içerir. |
| OrderItem → Modifier | N : M | Kalem birden fazla ek seçeneği taşıyabilir. |
| Order → StationTicket | 1 : N | Sipariş mutfak/bar fişlerine ayrılır. |
| TableSession → Bill | 1 : 1, etkin | Oturum tek hesapta toplanır. |
| Bill → Payment | 1 : N | Hesap birden fazla kısmi ödemeyle kapatılabilir. |

## Faz 5 Katalog Bütünlüğü

Menüler işletme ve şubeye aittir; `Draft -> Active -> Archived` geçişinde Archived son durumdur. Kategori, ürün, varyant, ek seçenek grubu ve seçenekler yumuşak yaşam döngüsü kullanır. Fiyat ve ek ücretler sınırlı, negatif olmayan tam sayı alt para birimleridir; varyant fiyatı mutlak tutardır. `0 <= min <= max <= etkin seçenek sayısı`; etkin varsayılan seçenek sayısı max'ı aşamaz. Diyet/alerjen birleşimleri kapalı desteklenen etiket kümesine göre doğrulanır. Bulunabilirlik, ürün yaşam döngüsünden ayrı şube ayarıdır; ürün düzeyindeki 86 işlemi, çalışma zamanı görünümünde etkin varyantları da kullanılamaz yapar.

## Faz 6 Masa, Kat Planı ve QR Bütünlüğü

Masalar, bileşik yabancı anahtar (composite foreign key) bütünlüğü ile işletme (tenant), şube ve servis alanına (dining area) aittir. Şube bazında masa kodu ve numarası benzersizlik kuralına tabidir (`ix_restaurant_tables_tenant_branch_code`). Masa yerleşim koordinatları kat planı tuvali sınır kısıtlamalarına uymalıdır (`x, y >= 0`, `10 <= width <= 5000`, `10 <= height <= 5000`, `x + width <= 10000`, `y + height <= 10000`, `shape IN (1, 2, 3)` ve `rotation [0, 360)` aralığında; veritabanı kısıtları: `ck_restaurant_tables_shape`, `ck_restaurant_tables_canvas_bounds`). Masa yemek oturumlarına veya geçmiş denetim kayıtlarına bağlıysa fiziksel silme yasaktır; masalar yazılımsal durum (soft-status) devre dışı bırakma kullanır. Etkin, kapatılmamış bir oturumu olan bir masanın devre dışı bırakılması kesin olarak engellenir (409 Conflict döner). QR kod üretimi sabit zamanlı doğrulamaya (constant-time verification) sahip HMAC-SHA256 imzaları kullanır. Masa rotasyonu `qr_version` değerini artırır ve `public_code` değerini yeniden oluşturarak eski fiziksel QR baskılarını anında geçersiz kılar. Masa taşıma ve oturum birleştirme mekanikleri (Faz 6.4), masalar ve oturumlar düzeyinde eksiksiz uygulanmış olup değişmez denetim geçmişi (`DiningSessionMovement`) ve deadlock koruması sağlar; sipariş düzeyindeki kalem transferleri ve hesap bölme entegrasyonu ise ilgili sipariş modelleri oluştukça sonraki aşamalarda bağlanacaktır.

## Faz 7 Müşteri Deneyimi ve Hizmet Talebi (ServiceRequest) Bütünlüğü

Hizmet talepleri (`ServiceRequest`), müşteri oturumundan (`DiningSession`) personele iletilen çağrıları yöneten bağımsız bir alandır. Bileşik yabancı anahtar (`tenant_id`, `branch_id`, `dining_session_id`, `table_id`) ve PostgreSQL RLS ile tam yalıtılır. Hizmet talebi türleri (`Waiter`, `Water`, `Napkin`, `Assistance`) taşır; hesap isteme (`RequestBillAsync`) ise DiningSession yaşam döngüsü durumu (`BillRequested`) üzerinden doğrudan yönetilir ve ServiceRequest içine kopyalanmaz. İstemci tarafından gönderilen `Idempotency-Key` başlığı (`tenant_id, idempotency_key` benzersiz indeksi) ile mükerrer tıklamalarda ve ağ tekrarlarında mükerrer kayıt oluşumu engellenir. Yaşam döngüsü `Open -> Cancelled` (ve operasyon fazlarında `Acknowledged -> Completed`) durumlarını izler. Müşteri sepet taslağı yalnızca istemci tarafında (`sessionStorage`) oturum ID'sine bağlı olarak tutulur; gerçek sipariş kaydı (order persistence) ve veritabanı sipariş modelleri Faz 8 kapsamındadır.

## Faz 8 Sipariş Çekirdeği (Order Core) ve Yaşam Döngüsü Bütünlüğü

Siparişler (`Order`), bir yemek oturumu (`DiningSession`) kapsamında misafir veya personel tarafından başlatılan, otoriter sunucu doğrulamasına tabi satış sözleşmeleridir. `Order` ve `OrderItem` varlıkları şu yaşam döngüsü durumlarını izler:
- `Draft`: Sepet/taslak aşaması (müşteri tarafında `sessionStorage`, personel tarafında arayüz durumu).
- `Submitted`: Sunucu tarafında yetkili fiyatlandırma motoru (`OrderPricingEngine`) ve katalog/86-stok doğrulamaları yapılarak veritabanına yazılmış kesinleşmiş sipariş.
- `InPreparation`: Mutfak veya bar istasyonunda hazırlık sürecine alınmış sipariş/kalem.
- `Ready`: Hazırlığı tamamlanmış, masaya servise hazır sipariş/kalem.
- `Served`: Masaya teslim edilmiş sipariş/kalem.
- `Cancelled`: Hazırlık öncesi veya yetkili personel/yönetici onayıyla iptal edilmiş sipariş/kalem.

**Sınırlar ve Mimari Ayrım:**
- Hazırlık fişinin kabul edilmesi (`Acknowledged`), gelecekteki `StationTicket` (KDS) alanına aittir; `Order` aggregate'inde durum değildir.
- `Paid`, bir sipariş durumu değildir; ilerideki `Bill` (Hesap/Ödeme) alanına aittir.
- `Closed`, `DiningSession` ve `Bill` yaşam döngüsüne aittir; `Order` durumlarında yer almaz.
- Faz 8'de KDS hazırlık ekranları ve yönlendirme (Faz 11), SignalR canlı bildirimleri (Faz 9), fiş yazdırma (Faz 12), adisyon/hesap motoru (Faz 13) ve ödeme/bahşiş/komisyon (Faz 16) yapılmayacaktır.
- Sipariş gönderildiğinde veritabanında gerçek, denetlenebilir `Order`, `OrderItem` ve `OrderItemModifier` kayıtları oluşur; sahte başarılı sipariş veya mock üretim API'si yer almaz.
