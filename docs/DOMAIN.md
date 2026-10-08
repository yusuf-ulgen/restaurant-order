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

### 3.3. Masa ve Salon

- **Table:** Şube içinde benzersiz kimliği ve numarası olan masa/oturma noktası; kalıcı QR koduyla ilişkilidir.
- **TableSession:** Misafirlerin oturması veya ilk siparişle başlayan yemek oturumu; ödeme tamamlanınca kapanır. Ayrıntılı yaşam döngüsü Faz 6 öncesi kesinleştirilecektir.

### 3.4. Menü ve Katalog

- **Menu:** Şube veya zaman aralığına göre sunulabilen yiyecek/içecek koleksiyonu; kahvaltı veya tüm gün menüsü gibi.
- **MenuCategory:** Başlangıç, ana yemek, kokteyl, tatlı gibi gruplar.
- **MenuItem:** Cheeseburger veya espresso gibi ürün.
- **ItemVariant:** Farklı fiyatlı boyut/porsiyon; tek 150 g, çift 300 g, küçük/büyük gibi.
- **ModifierGroup:** Etin pişme derecesi, garnitür veya ek malzeme seçenekleri. `min_selections` zorunlu en az seçimi, `max_selections` izinli en fazla seçimi belirtir; pişme derecesinde en az bir seçim gibi.
- **ModifierItem:** İyi pişmiş, trüflü patates veya +1.50 ek kaşar gibi tek seçenek.

### 3.5. Sipariş ve Hazırlık Fişi

- **Order:** `branch_id`, `table_session_id`, `order_source` ile bağlı sipariş; kaynak müşteri QR, garson veya POS olabilir.
- **OrderItem:** Siparişteki `MenuItem`/`ItemVariant` örneği; seçilen ekler ve müşteri notlarını içerir.
- **StationTicket (KitchenTicket / BarTicket):** Siparişin belirli mutfak/bar istasyonuna yönlendirilen alt kümesi.
- **TicketItem:** Hazırlık fişinin tek kalemi; sırada, hazırlanıyor, hazır, servis edildi veya iptal durumunu taşır.

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
