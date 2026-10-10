# Aşama 5 Uygulama İzleyici (`docs/PHASE-5-TRACKER.md`)

> **Tarihsel kayıt:** Buradaki test sayıları ve commit referansları ilgili fazın kapanışına aittir. Güncel görev kanıtları [CURRENT-STATE.md](./CURRENT-STATE.md) dosyasındadır.

Bu belge, Faz 5 Menü ve Katalog çalışmasının yedi alt aşamasını izler.

---

## 1. Alt Faz Durumuna Genel Bakış

| Alt Faz | Başlık | Durum | Birincil Çıkış | Commit Referansı |
| :--- | :--- | :--- | :--- | :--- |
| **Aşama 5.0** | Teslimat Temeli ve Sertleştirme Hazırlığı | **Tamamlandı** | Temiz temel, güvenli RFC 7807 500'ler, korelasyon kimlikleri, uyarı gerektirmeyen React test paketi | `717348b` |
| **Aşama 5.1** | İşletme Kapsamlı Menü ve Kategori Yönetimi | **Tamamlandı** | Menu ve MenuCategory aggregate modelleri, şube kapsamlı katalog, slug benzersizliği, yaşam döngüsü, RLS, REST API | `3d09500` |
| **Aşama 5.2** | Menü Öğeleri, Porsiyonlar ve Değişken Fiyatlandırma Modelleri | **Tamamlandı** | MenuItem toplamı, ItemVariant fiyatlandırması, PriceAmount VO, tam tamsayı küçük birimler, REST API'ler, RLS | `e2791ca` |
| **Aşama 5.3** | Değiştirici Gruplar ve Özelleştirme Kuralları | **Tamamlandı** | Değiştirici gruplar/seçenekler, seçim kuralları, fiyat değişimleri, diyet/alerjen meta verileri | `8b3f250` |
| **Aşama 5.4** | Menü Kataloğu REST API'ler ve EF Core Kalıcılığı | **Tamamlandı** | REST uç noktalar, ETag/eşzamanlılık, ek geçişler, denetim etkinlikleri | `3d09500–8a0811c` |
| **Aşama 5.5** | Şube Kullanılabilirliği ve Hızlı 86 | **Tamamlandı** | Kullanılabilirlik API'leri, istasyon kapsamı, çalışma zamanı okuma modeli ve işlemsel giden kutusu kalıcılığı | `8a0811c` |
| **Aşama 5.6** | Yönetici Kataloğu Yönetimi Kullanıcı Arayüzü ve Sağlamlaştırmanın Kapatılması | **Tamamlandı** | Yönetici kataloğu düzenleyicisi, yeniden sıralama, i18n para birimi, şube yetki matrisi, Veritabanı kısıtlamaları, işlemsel giden kutusu ve PR CI | İlgili PR dalının HEAD kaydı |

---

## 2. Ayrıntılı Kilometre Taşı Teslimatları

### Aşama 5.0: Teslimat Temeli ve Sertleştirme Hazırlığı
- [x] **Depo ve Teslimat Hizalaması:**
  - Doğrulandı `main` şube Faz içerir 4 doğrulama kapatma commit kaydı `6ded881`.
  - Çalışma şubesi oluşturuldu `feat/phase-05-menu-catalog`.
  - Oluşturuldu `docs/PHASE-5-TRACKER.md` ve güncellendi `docs/ROADMAP.md` (Faz 5: Devam Ediyor).
  - Güncelleme Aşaması 4 izleyici ön uç test sayıları (Yönetici Web: 77 testler, Toplam ön uç: 259 testler).
- [x] **API Hata İşleme ve Güvenliği Güçlendirme:**
  - Ham kaldırıldı `Console.Error.WriteLine` ve yığın izleme günlüğü kaydı `RestaurantConfigEndpoints.cs`.
  - Oturum açmak için beklenen 4xx alan hatalarını yapılandırdı `Information` seviye yerine `Error`/critical.
  - Beklenmeyen garantili 500 yanıtlar hiçbir zaman istisna mesajlarını veya yığın izlerini arayanlara sızdırmaz.
  - Güvenli, standart hale getirilmiş RFC 7807 `ProblemDetails` ile `correlationId` hepsinde 500 hata yanıtları.
  - Global ara katman yazılımı istisna işleyicisi, işlenmeyen ardışık düzen istisnalarını güvenli bir şekilde yakalar.
- [x] **Ön Uç Kalitesi ve Uyarı Temizleme:**
  - Sabit `border` vs `borderColor` kısa/uzun özellik çarpışması `Button.tsx` ve `IconButton.tsx`.
  - Sabit `act(...)` Admin Web'de uyarı `ProtectedRoute` yükleme testi.
  - Sabit `act(...)` Admin Web'de uyarı `App.test.tsx` düzen ilk görünüm testleri.
  - Sıfır uyarı bastırma, sıfır test susturma ve sıfır kapsama azaltma.

### Aşama 5.1: İşletme Kapsamlı Menü ve Kategori Yönetimi
- [x] **Şube Kapsamlı Menü Toplama Kökü:**
  - İşletme izolasyonunu zorunlu kılan katı işletme ve şube kapsamı (`TenantId`, `BranchId`).
  - Menü alanları: `Id`, `TenantId`, `BranchId`, `Name`, `Slug`, `Description`, `Status`, `SortOrder`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`.
  - Durum makinesi: `Draft -> Active -> Archived`. `Archived` kesinlikle terminaldir (yeniden etkinleştirme yok, mutasyon yok, kalıcı silme yok).
  - Şube başına slug benzersizliği, pozitif sıralama düzenleri, HTML etiket temizliği.
- [x] **MenuCategory Varlığı ve Sıralama:**
  - Hiyerarşik gruplamanın kapsamı İşletme, Şube ve ana Menüye kadar uzanır.
  - Alanlar: `Id`, `TenantId`, `BranchId`, `MenuId`, `Name`, `Slug`, `Description`, `SortOrder`, `IsActive`, `ConcurrencyToken`, zaman damgaları.
  - Tüm kategorilerde atomik yeniden sıralama yetenekleri ve katı eşzamanlılık belirteci doğrulaması ile sipariş dizinini görüntüleyin.
  - Ebeveyn Menü kapsamı başına benzersiz slug.
  - Eşzamanlılık doğrulamasıyla Etkin/Etkin Değil geçişi.
  - Mutasyon koruması: Ana Menü arşivlenirse veya Şube kapatılırsa/askıya alınırsa değişiklikler engellenir.
- [x] **Kalıcılık ve Satır Düzeyinde Güvenlik:**
  - EF Core yapılandırmaları (`MenuConfiguration`, `MenuCategoryConfiguration`) içinde `tenancy` şemasında.
  - Bileşik yabancı anahtarlar (`tenant_id`, `branch_id`) için `branches` ve (`tenant_id`, `menu_id`) için `menus`.
  - PostgreSQL Satır Düzeyi Güvenliği etkin ve zorunlu (`FORCE ROW LEVEL SECURITY`).
  - İşletme izolasyon politikalarının kullanılması `tenancy.get_current_tenant_id()`.
  - Şuna verilen çalışma zamanı izinleri: `restaurant_app_runtime`.
  - EF Core geçişi `20261004140337_AddMenusAndCategories` Resmi EF araçlarıyla oluşturuldu ve doğrulandı `migration-ops.mjs`.
- [x] **REST API & RBAC Matris:**
  - Uç noktalar `/api/v1/catalog/branches/{branchId}/menus/...` Menüler ve Kategoriler için.
  - İzinler: `menu.catalog.view` (Müşteri dahil tüm roller), `menu.catalog.manage` (Restoran Yöneticisi, Şube Müdürü).
  - BranchManager erişimi, atananlara göre kesinlikle doğrulandı `BranchId` (şubeler arası getiriler 403).
  - SuperAdmin'in işletme bağlamını atlaması engellendi (işletmeler arası geri dönüşler) 403/404).
  - Eşzamanlılık jetonu istek gövdesi aracılığıyla kontrol edildi veya `If-Match` başlık (eksik -> 412 Önkoşul Başarısız, eski -> 409 Çatışma).
  - Standartlaştırılmış RFC 7807 ProblemDetails tüm hata dallarında geri döndü.
  - Denetim günlüğü şununla: `SecurityAuditEvent` tüm mutasyonlarda (`menu_created`, `menu_updated`, `menu_activated`, `menu_archived`, `menu_category_created`, vb.).
- [x] **Doğrulama ve Test Kapsamı:**
  - Etki alanı birimi testleri `MenuAndCategoryUnitTests.cs` (yaşam döngüsü, terminal durumu, giriş doğrulama, sıralama düzeni).
  - Uç nokta ve RBAC birim testleri `CatalogEndpointsUnitTests.cs` ve `CatalogEndpointsHandlerUnitTests.cs`.
  - Genel sorgu filtresi testleri `GlobalTenantQueryFilterTests.cs`.
  - Uçtan uca entegrasyon testleri `CatalogIntegrationTests.cs` ve `CatalogIntegrationTests.Categories.cs`.
  - 1051 birim testleri, 10 mimari testleri, 11 entegrasyon testlerinin hepsinin başarılı olduğu doğrulandı.

### Aşama 5.2: Menü Öğeleri, Porsiyonlar ve Değişken Fiyatlandırma Modelleri
- [x] **Alan Modelleri ve Fiyatlandırma:**
  - `MenuItem`, güçlü türlenmiş `MenuItemId` kullanan aggregate köküdür.
  - Alanlar: `TenantId`, `BranchId`, `MenuId`, `CategoryId`, `Name`, `Slug`, `ShortDescription`, `FullDescription`, `ImageUrl`, `BasePriceMinorUnits`, `SortOrder`, `IsActive`, `ConcurrencyToken`.
  - `ItemVariant` güçlü türü olan varlık `ItemVariantId`.
  - Alanlar: `MenuItemId`, `Name`, `Code`, `AbsolutePriceMinorUnits`, `SortOrder`, `IsDefault`, `IsActive`, `ConcurrencyToken`.
  - `PriceAmount` Negatif olmayan fiyatları uygulayan değişmez değer nesnesi, tavan sınırı (`1,000,000,000` küçük birimler), tamsayı aritmetiği (`+`, `-`, `*`) ve yerel ayar bağımlılıkları olmayan değişmez dize biçimlendirmesi.
  - Baştan sona katı tamsayı küçük birimler (hayır `float`, `double`veya `decimal`).
  - Değişmez kurallar: öğe başına en fazla bir etkin varsayılan değişken; Çeşitsiz ürünler taban fiyattan satılır; Varyant fiyatları açık ve mutlaktır (delta kayması yoktur).
- [x] **Güvenlik, URL Doğrulama ve Veri Bütünlüğü:**
  - `ImageUrl` yalnızca güvenli göreli yolları kabul eder (`/path`) veya HTTPS (`https://`), javascript, data, vbscript ve protokole bağlı şemaları tamamen reddediyor.
  - HTML adlarda, slug alanlarında ve açıklamalarda etiket reddi.
  - Yalnızca geçici silme (`Activate`/`Deactivate`).
  - Tüm mutasyonlarda eşzamanlılık belirteçleri gereklidir (`If-Match` başlık veya istek gövdesi).
  - Denetim günlüğü şununla: `SecurityAuditEvent` kayıt `OldPriceMinorUnits` ve `NewPriceMinorUnits` onsuz PII.
- [x] **Kalıcılık ve Satır Düzeyinde Güvenlik:**
  - EF Core yapılandırmaları (`MenuItemConfiguration`, `ItemVariantConfiguration`) bileşik yabancı anahtarlarla.
  - PostgreSQL Satır Düzeyi Güvenliği etkin ve zorunlu (`FORCE ROW LEVEL SECURITY`).
  - İşletme izolasyon politikaları (`menu_items_isolation_policy`, `item_variants_isolation_policy`).
  - Filtreli benzersiz dizin `(tenant_id, menu_item_id) WHERE is_default = true AND is_active = true` veritabanı düzeyinde tek aktif varsayılan değişkenin sağlanması.
  - Eklemeli EF Core geçişi `20261004142624_AddMenuItemsAndVariants`.
  - Taşıma tasarımcısı izin verilenler listesinde kayıtlı, bağımsız SQL paket şu adresten güncellendi ve doğrulandı: `migration-ops.mjs`.
- [x] **REST API & Granül RBAC:**
  - Altındaki uç noktalar `/api/v1/catalog/branches/{branchId}/menus/{menuId}/items` ve `.../variants`.
  - ayrılması `menu.catalog.manage` vs `menu.pricing.manage`: herhangi bir fiyat değişimi kesinlikle gerektirir `menu.pricing.manage`.
  - Öğe uç noktaları: fiyatı oluşturun, güncelleyin, güncelleyin, etkinleştirin, devre dışı bırakın, yeniden sıralayın.
  - Varyant uç noktaları: fiyatı oluşturun, güncelleyin, güncelleyin, etkinleştirin, devre dışı bırakın, yeniden sıralayın.
  - ETag başlıkları tüm öğe ve varyant mutasyonlarında yayınlandı ve doğrulandı.
- [x] **Doğrulama ve Test Kapsamı:**
  - `PriceAmountUnitTests.cs`: Sınır, taşma, negatif, aritmetik ve biçimlendirme testleri.
  - `MenuItemAndVariantUnitTests.cs`: Varyant varsayılan değişmezi, kod biçimlendirmesi, URL güvenlik, HTML etiket temizliği, yaşam döngüsü.
  - `CatalogItemEndpointsUnitTests.cs`: ETag başlıkları ve RBAC matris doğrulaması
  - `CatalogItemIntegrationTests.cs`: Tam yaşam döngüsü, fiyatlandırma izninin uygulanması, şubeler arası bloklar, ETag çakışması/önkoşul kontrolleri.
  - 1099 birim testleri, 10 mimari testleri, tüm entegrasyon testlerinin geçtiğini doğruladı.

### Aşama 5.3: Değiştirici Gruplar, Seçenekler, Diyet ve Alerjen Meta Verileri
- [x] **Değiştirici Modelleri ve Seçim Değişmezleri:**
  - `ModifierGroup` güçlü yazılmış kök ile aggregate kökü `ModifierGroupId`.
  - `ModifierOption` güçlü türü olan varlık `ModifierOptionId`.
  - `MenuItemModifierGroupAssignment` özelleştirilebilir ekrana sahip bağlantı varlığı `SortOrder`.
  - Katı seçim sınırları: `0 <= minSelections <= maxSelections`.
  - Gerekli tek seçim (`min=1, max=1`), isteğe bağlı tek seçim (`min=0, max=1`), isteğe bağlı çoklu seçim (`min=0, max>1`), çoklu seçim gerekli (`min>0, max>1`).
  - Etkin seçenekler sınır kontrolü (`maxSelections <= activeOptionsCount` grup etkin/atandığında).
  - Varsayılan seçenek sınırları kontrolü (`activeDefaultOptions <= maxSelections`).
  - Aynı değiştirici grup içinde büyük/küçük harfe duyarlı olmayan yinelenen seçenek adı önleme.
  - Negatif olmayan tamsayı küçük birim fiyat deltaları `PriceAmount` (`PriceAmount.Zero` ücretsiz seçenek için negatif fiyat deltaları reddedilir).
  - Yalnızca geçici silme (`Activate`/`Deactivate` Sabit silme yerine).
- [x] **Kapalı Diyet ve Alerjen Kataloğu ve Çelişki Politikası:**
  - Tip açısından güvenli 14 AB Gıda Alerjenleri (`AllergenTag` enum: Gluten, Crustaceans, Eggs, Fish, Peanuts, Soy, Milk, TreeNuts, Celery, Mustard, Sesame, Sulphites, Lupin, Molluscs).
  - Diyet Etiketler (`DietaryTag` numaralandırma: Vegetarian, Vegan, GlutenFree, Halal, Kosher, DairyFree).
  - Kapalı `SpicyLevel` değer nesnesi uygulaması `0..3` sınırlar (`None`, `Mild`, `Medium`, `Hot`).
  - Etki alanı çelişkisi doğrulaması `DietaryAndAllergenValidator`:
    - `GlutenFree` ile `Gluten` -> Reddedildi.
    - `DairyFree` ile `Milk` -> Reddedildi.
    - `Vegan` ile `Milk`, `Eggs`, `Fish`, `Crustaceans`veya `Molluscs` -> Reddedildi.
    - `Vegetarian` ile `Fish`, `Crustaceans`veya `Molluscs` -> Reddedildi.
  - Katalog-harici değerler şununla reddedildi: RFC 7807 alan hatası.
- [x] **İşletme ve Şube İzolasyonu:**
  - Aynı işletme ve daldaki birden fazla öğe arasında yeniden kullanılabilen değiştirici gruplar.
  - İşletmeler arası ve dallar arası değiştirici grup atamaları, alan ve veritabanı kısıtlama düzeylerinde kesinlikle engellendi.
  - PostgreSQL Satır Düzeyi Güvenliği her üç tabloda da etkinleştirildi ve zorunlu kılındı (`modifier_groups`, `modifier_options`, `menu_item_modifier_group_assignments`).
- [x] **REST API'ler ve Ayrıntılı RBAC:**
  - Değiştirici grubu CRUDaltında etkinleştirme ve devre dışı bırakma `/api/v1/catalog/branches/{branchId}/modifier-groups`.
  - Değiştirici seçeneği CRUDaltında etkinleştirme, devre dışı bırakma ve yeniden sıralama `.../{groupId}/options`.
  - Öğe değiştirici grup ataması, kaldırması ve yeniden sıralaması `/api/v1/catalog/branches/{branchId}/menus/{menuId}/items/{itemId}/modifier-groups`.
  - Öğe meta veri güncellemesi altında `.../items/{itemId}/metadata`.
  - İzin kontrolleri: `menu.catalog.manage` katalog yapısı için; `menu.pricing.manage` fiyat delta mutasyonları için.
  - ETag başlıkları yayınlandı ve eşzamanlılık belirteçleri tüm mutasyonlarda doğrulandı.
- [x] **Doğrulama ve Test Kapsamı:**
  - `ModifierAndMetadataUnitTests.cs`: Sınır kombinasyonları, seçenek değişmezleri, varsayılan seçenek sınırları, etiket çelişkileri, baharatlı sınırlar, işletmeler arası bloklar.
  - `ModifierEndpointsUnitTests.cs`: ETag başlıkları, eşzamanlılık belirteci çıkarma, RBAC matris, problem ayrıntıları.
  - `CatalogModifierIntegrationTests.cs`: Uçtan uca yaşam döngüsü, fiyatlandırma izninin uygulanması, işletmeler arası izolasyon, meta veri tutarlılığı.
  - 1141 birim testleri, 10 mimari testleri, entegrasyon test paketinin başarıyla geçtiği doğrulandı.

### Aşama 5.4: Şube Ürün Kullanılabilirliği ve Hızlı 86 (COMPLETED)
- [x] **Ayrılmış Envanter Kullanılabilirliği ve Alan Değişmezleri:**
  - `BranchItemAvailability` toplam ayrıştırma geçici stok durumu (`IsAvailable`) ürün yaşam döngüsünden (`IsActive`).
  - Kapalı `AvailabilityReasonCode` numaralandırma: `SoldOut`, `IngredientUnavailable`, `TemporarilyDisabled`, `KitchenCapacity`, `Manual`, `Restocked`.
  - Ürün ve varyant kullanılabilirliği bağımsızlığı:
    - Belirli bir varyantın 86 olması yalnızca o varyantın mevcut olmadığını gösterir.
    - Bir öğenin 86'lanması, tüm değişkenlerinin çalışma zamanı okuma modelinde kullanılamaz görünmesine neden olur.
  - Güvenli düz metin notu doğrulaması (HTML Etiketler kesinlikle reddedildi, 500 maksimum uzunluk).
  - `ExpectedAvailableAtUtc` gelecek tarih doğrulaması (geçmiş tarihler reddedildi) 422 İşlenemeyen Varlık).
  - İyimser eşzamanlılık kontrolü `ConcurrencyToken` tüm mutasyonlarda.
- [x] **Çok İşletmeli Yapı ve Kalıcılık:**
  - `branch_item_availabilities` PostgreSQL Satır Düzeyinde Güvenlik içeren tablo (`FORCE ROW LEVEL SECURITY`).
  - Derleme zamanı çevrilebilirliğiyle yapılandırılmış genel işletme sorgu filtresi.
  - Hazırlama istasyonu referansı (`PreparationStationId`) açık `MenuItem` şubeler arası istasyon bağlantılarının engellenmesini sağlamak.
  - Geri dönüşümlü EF Core geçişi `20261004151108_AddBranchItemAvailabilityAndStations`.
- [x] **Granül RBAC & İstasyon Kapsamı:**
  - `menu.inventory.quick86` izin uygulandı.
  - `RestaurantAdmin` ve `BranchManager` kendi branş kapsamlarında yetkilidir.
  - `Kitchen` istasyon kapsamı kısıtlaması: yalnızca Mutfak istasyonlarına atanan öğelerde izin verilir.
  - `Bar` istasyon kapsamı kısıtlaması: yalnızca Bar istasyonlarına atanan öğelerde izin verilir.
  - `Waiter` ve `Customer` mutasyon erişimi reddedildi.
  - Özellik bayrakları aracılığıyla sıfır bypass.
- [x] **Etkili Çalışma Zamanı Menü Okuma Modeli:**
  - Şube çalışma zamanı okuma modeli altında `/api/v1/catalog/branches/{branchId}/runtime-menu`.
  - Yalnızca etkin menüleri, kategorileri, öğeleri, çeşitleri, değiştirici grupları ve seçenekleri döndürür.
  - Hesaplamalar `is_available` öğe düzeyinde dinamik olarak 86 tüm varyantlara yayılma.
  - Şube para birimi kodunu içerir ve tüm idari, denetim ve dahili alanları ayırır.
- [x] **İşlemsel Giden Kutusu Etkinlik Sözleşmesi:**
  - Doğrudan/bellek içi ön commit etkinliği yayıncı çağrıları kullanılabilirlik hizmetinden tamamen kaldırıldı.
  - Kullanılabilirlik durumu değişiklikleri ve `CatalogAvailabilityOutboxMessage` Kayıtlar aynı veritabanı işleminde atomik olarak kalıcı hale getirilir. `RestaurantOrderDbContext`.
  - İşlemin geri alınması şunları garanti eder: eğer hizmet bir istisna atarsa veya yanıt verilirse 500, hem kullanılabilirlik değişiklikleri hem de giden kutusu kayıtları atomik olarak geri alınır.
  - arıza-kapalı RLS ve giden kutusu tablosuna uygulanan sorgu filtresi; SignalR aktarımı Faz olarak kalır 9 scope.
  - Giden kutusundaki eksiklik anahtarları, komut eşzamanlılık belirteci (`item-86-...`, `item-restock-...`, `variant-86-...`, `variant-restock-...`), yeniden denemelerde yinelenen giden kutusu girişlerinin önlenmesi.
- [x] **Güvenlik Denetimi Günlüğü:**
  - `ItemAvailabilityChanged`, `ItemRestocked`, `ItemVariantAvailabilityChanged`, `ItemVariantRestocked` Her mutasyona yazılan denetim olayları.
- [x] **Doğrulama ve Test Kapsamı:**
  - `BranchItemAvailabilityUnitTests.cs`: Yaşam döngüsü bağımsızlığı, HTML not reddi, geçmiş beklenen tarih reddi, yeniden stok geçişleri.
  - `CatalogAvailabilityEndpointsUnitTests.cs`: ETag başlıkları, eşzamanlılık belirteci çıkarma, 412 Ön Koşul Başarısız Oldu, 409 Çatışma, RBAC matrix.
  - `CatalogAvailabilityIntegrationTests.cs` & `CatalogAvailabilityOutboxIntegrationTests.*`: Hızlı 86, değişken yalıtımı, çalışma zamanı menüsündeki tüm değişkenlere öğe yayılımı, yeniden stoklama, eşzamanlılık çakışması, Kitchen/Bar için istasyon kapsamı, işlemsel geri alma, değişken giden kutusu kalıcılığı, ayrıcalıksız çalışma zamanı PostgreSQL rolü (`restaurant_app_user` NOSUPERUSER/NOBYPASSRLS) RLS izolasyon ve eş zamanlı idempotens.
### Aşama 5.5: Şube Kullanılabilirliği ve Hızlı 86 (COMPLETED)
- [x] **İşlemsel Giden Kutusu Etkinlik Sözleşmesi:**
  - ayrılmış `catalog_availability_outbox` tablo aynı veritabanı işleminde devam etti.
  - arıza-kapalı RLS ve giden kutusu tablosuna uygulanan sorgu filtresi; SignalR aktarımı Faz olarak kalır 9 scope.
  - Olay yükü, işletme, şube, öğe/varyant, kullanılabilirlik ve neden alanlarını benzersiz bir kimlik anahtarıyla taşır.
  - Sipariş ödeme doğrulaması ve `ITEM_OUT_OF_STOCK` negatif akış gelecekteki sipariş yaşam döngüsü aşamalarına aittir; Aşama 5 yalnızca katalog ve kullanılabilirlik altyapısı sağlar.

### Aşama 5.6: Yönetici Katalog Yönetimi Kullanıcı Arayüzü ve Sağlamlaştırma Kapanışı (COMPLETED)
- [x] **Yönetici Web Kataloğu Yönetimi ve Erişilebilirlik:**
  - Categories, MenuItems, Variants ve ModifierOptions için erişilebilir yukarı/aşağı yeniden sıralama kontrolleri (Aşama için sürükleyip bırakma planlanmıştır) 14).
  - Fotoğraf yüklemeli öğe düzenleyici modeli URL, değişken matrisi, alerjen geçişleri ve değiştirici seçici.
  - İlk Hızlı 86 Kullanılabilirlik kaydı henüz başlatılmadığında öğe eşzamanlılık belirtecine akış geri dönüşü.
  - Seçilen şubenin para birimini kullanan dinamik para birimi biçimlendirmesi (sabit kodlanmış öğelerin kaldırılması) TRY/₺) düzenleyiciler, listeler ve önizleme sayfaları arasında.
- [x] **Şubeler Arası Bilgi Bütünlüğü ve Arıza Kapatma RBAC:**
  - Şube yetkilendirme matrisi zorunlu olarak kapatıldı (`RestaurantAdmin` işletme içinde izin verilen çapraz şube; `BranchManager`, `Kitchen`, `Bar`, `Cashier`, `Waiter`, `Customer` kesinlikle kendi şubesine odaklanmış; `SuperAdmin` işletmeler arası bypass reddedildi).
  - Hizmet düzeyi RBAC: arızalı kapalı `EnsureCatalogManagePermission(actor)` Tüm Menü ve Kategori değiştirme yöntemlerinde veritabanı erişiminden önce eklenir (`CreateMenu`, `UpdateMenu`, `ActivateMenu`, `ArchiveMenu`, `CreateCategory`, `UpdateCategory`, `ActivateCategory`, `DeactivateCategory`, `ReorderCategories`).
  - Şubeler arası bozulmalara karşı fiziksel veritabanı kısıtlama korumasını güçlendiren alternatif anahtarlar ve bileşik yabancı anahtarlar. Kısıtlama adı iddialarıyla gerçek PostgreSQL Testcontainers örneğine karşı test edilen eksiksiz matris.
  - Hazırlık istasyonu FK açık `MenuItem` ile yapılandırılmış `DeleteBehavior.Restrict` düzeltici eklemeli geçiş yoluyla `20261005141523_HardenCatalogOutboxAndPreparationStationConstraints`, güvenli olmayan kademelendirmenin NOT NULL sütunlarına güvenli olmayan kademeli null atamasının engellenmesi.
  - Değiştirici seçeneği yeniden sıralama, boş olmayan isteği doğrular, gruba ait tüm seçenek kimliklerini doğrular, her seçenek için eşzamanlılık belirteçlerini gerektirir ve doğrular (412 boş GUID değerinde, 409 eski belirteçte) ve negatif veya yinelenen sıralama düzenlerini reddeder.
  - Mavi/yeşil geçiş denetleyicisi yeniden sıkılaştırıldı: izin verilmeyenler reddediliyor DROP CONSTRAINT (PRIMARY KEY, UNIQUE, CHECK, yabancı anahtarlar) ve doğrulanmış yerinde değiştirmeyi zorunlu kılar.
- [x] **Doğrulama ve Kalite Kontrolleri:**
  - Arka uç birimi (1223/1223), mimari (10/10) ve entegrasyon test paketleri (249/249) geçiyor.
  - Ön Uç Vitest paketleri geçen (295/295) kullanıcı arayüzünde ve tüm web uygulamalarında.
  - Playwright E2E geçen süitler (2/2).
  - Komut Dosyası ve Geçit doğrulama paketlerini geçen (115/115).
  - Dosya boyutu kapısı (< 600 satırlık üst sınır, < 450 uyarı eşiği).
  - Taşıma komut dosyası paketi şununla doğrulandı: `migration-ops.mjs` ve Blue/Green kareli.

---

## 3. Doğrulama Durumu (Temel ve Güçlendirme)

| Komut | Kapsam | Sonuç | Ayrıntılar |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln -c Release` | Arka Uç Çözümü | **PASS** | 0 uyarı, 0 hata |
| `dotnet test tests/unit/` | Birim Test Paketi | **PASS** | 1223 / 1223 geçti (100%) |
| `dotnet test tests/architecture/` | Mimari Süit | **PASS** | 10 / 10 geçti (100%) |
| `dotnet test tests/integration/` | Entegrasyon Paketi (Testcontainers) | **PASS** | 249 / 249 geçti (100%) |
| `pnpm --filter admin-web test` | Yönetici Web Vitest'i | **PASS** | 113 / 113 başarılı; 13 test dosyası (100%) |
| `pnpm test:unit:frontend` | Ön Uç Birim Süitleri | **PASS** | 295 / 295 paketler/kullanıcı arayüzünden geçti ve 3 web uygulamaları (100%) |
| `pnpm test:e2e` | Playwright E2E Süit | **PASS** | 2 / 2 geçti (100%) |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 uyarı, 0 hata |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 çalışma alanı projeleri temiz |
| `pnpm verify:gates` | Depo Kapıları ve Güvenliği | **PASS** | 115 / 115 geçilen testler (100%) |
| `node scripts/check-file-size.mjs` | Dosya Boyutu Kapısı | **PASS** | 0 İnsan tarafından yazılan dosyalar şunu aşıyor: 600 satırlık üst sınır |
| `node scripts/check-docs.mjs` | Belgeler ve Bağlantılar Kapısı | **PASS** | Doğrulanmış, 0 kırık bağlantılar |
| `node scripts/check-secrets.mjs` | Gizli Değer Taraması | **PASS** | Sıfır kimlik bilgisi veya anahtar açığa çıktı |
| `git diff --check origin/main` | Boşluk ve Biçimlendirme | **PASS** | 0 boşluk veya biçimlendirme anormallikleri |

---

## 4. Bilinen Riskler ve Azaltmalar

1. **Risk:** Varyant fiyatlandırmasında ve değiştirici eklemelerinde kayan nokta yuvarlama hataları.
   **Azaltma:** Tüm fiyatlar kesinlikle tamsayı küçük para birimleri (cent/kuruş) olarak saklanır. Vergi ve hizmet ücreti yüzdeleri tam sayı baz puanlarını kullanır (`BasisPointsRate`).
2. **Risk:** Stok tükenmesi (86) müşteri siparişi gönderimi ile mutfak stokunun tükenmesi arasındaki yarış koşulları.
   **Azaltma:** Aşama 5 katalog kullanılabilirliği okuma/yazma modeli ve Quick86 altyapısını sağlar. Gelecekteki sipariş gönderim hattı (Faz 7/8) bir siparişi işlemeden hemen önce işlemsel commit öncesi kullanılabilirlik doğrulaması gerçekleştirmelidir, bu da hızlı bir şekilde başarısız olur. `ITEM_OUT_OF_STOCK`. Bu sipariş gönderme davranışı gelecekteki bir gerekliliktir ve Aşamada ne uygulanır ne de doğrulanır 5Kapsamı kesinlikle katalog ve kullanılabilirlik altyapısıyla sınırlıdır.
3. **Risk:** Eş zamanlı yönetimsel değişikliklerin üzerine yazılan eski katalog güncellemeleri.
   **Azaltma:** İyimser eşzamanlılık belirteçleri ve `If-Match` Tüm katalog mutasyon uç noktalarında ETag'ler.
4. **Risk:** Bilgilerin açıklanması 500 hata yanıtları.
   **Azaltma:** Standart RFC 7807 `ProblemDetails` geri dönen korelasyon kimlikleri ile; tüm üretim ortamlarında istisna ayrıntıları ve yığın izleri kaldırıldı.

## 5. Katalog Teslimat Envanteri

- Eklemeli geçişler:
  - `20261004140337_AddMenusAndCategories`
  - `20261004142624_AddMenuItemsAndVariants`
  - `20261004144819_AddModifiersDietaryAndAllergens`
  - `20261004151108_AddBranchItemAvailabilityAndStations`
  - `20261004211028_AddCatalogCrossBranchReferentialConstraintsAndOutbox`
  - `20261005141523_HardenCatalogOutboxAndPreparationStationConstraints`
- API kök: `/api/v1/catalog/branches/{branchId}`. Uç nokta grupları menüleri kapsar; menü kategorileri ve yeniden sıralama; öğeler, meta veriler, fiyatlar ve yeniden sıralama; çeşitler ve fiyatlar; değiştirici gruplar/seçenekler ve atama; şube kullanılabilirliği, ürün/çeşit `quick-86` ve `restock`; ve `GET /runtime-menu`.
- Mutasyon yanıtları ETag'leri taşır. Eşzamanlılık önkoşullarının geri dönüşü eksik 412; eski durum ve benzersizlik çatışmaları geri dönüyor 409. Beklenmeyen hatalar genel kullanım RFC 7807 korelasyon kimlikleriyle yanıtlar.
- PR dalının son commit kaydı: `feat/phase-05-menu-catalog` şube başı kapatma.
