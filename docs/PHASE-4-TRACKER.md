# Aşama 4 Uygulama İzleyici (`docs/PHASE-4-TRACKER.md`)

> **Tarihsel kayıt:** Buradaki test sayıları ve commit referansları ilgili fazın kapanışına aittir. Güncel görev kanıtları [CURRENT-STATE.md](./CURRENT-STATE.md) dosyasındadır.

Bu belge, Faz 4 Restoran Yapılandırması çalışmasının altı alt aşamasını izler.

---

## 1. Alt Faz Durumuna Genel Bakış

| Alt Faz | Başlık | Durum | Birincil Çıkış | Commit SHA |
| :--- | :--- | :--- | :--- | :--- |
| **Aşama 4.1** | İşletme Kapsamlı Marka ve Şube Yönetimi | **Tamamlandı** | Marka ve şube CRUD API'ler, durum geçişleri, eşzamanlılık belirteçleri, ETag'ler, RBAC | 5547926 |
| **Aşama 4.2** | Güvenli İşletme Markalaması ve Tema Ayarları | **Tamamlandı** | Güvenli marka görünümü, şube teması geçersiz kılmaları, devralma, RLS, CSS belirteç eşleme | 95e0ee2 |
| **Aşama 4.3** | Yapılandırma Odaklı Dinamik Yönetici Kabuğu | **Tamamlandı** | Dinamik Üstbilgi, Kenar Çubuğu, Altbilgi, Gezinme Kaydı, Markalama Ayarları Ekranı, Tema Sağlayıcı | 2f5f24c |
| **Aşama 4.4** | Şube Çalışma Saatleri ve Finansal Yapılandırma | **Tamamlandı** | Şube mali ayarları, haftalık çalışma saatleri, baz puan oranları, RLS, RBAC | ad99ec3 |
| **Aşama 4.5** | Şube Yemek Alanları, Hazırlama İstasyonları ve Özellik Kontrolleri | **Tamamlandı** | İşletme açısından güvenli Yemek Alanları, Hazırlama İstasyonları, Tip Güvenli Özellik İşaret Kataloğu ve Yönetici Kullanıcı Arayüzü | 7a1e45f |
| **Aşama 4.6** | Nihai Sertleştirme, Doğrulama ve Birleştirmeye Hazır Olma | **Tamamlandı** | Eşzamanlılık yaşam döngüsü güçlendirme, geçiş paketi, negatif akış testleri; son itme ve çekme isteği CI'sı başarılı oldu | c77588c |

---

## 2. Ayrıntılı Kilometre Taşı Teslimatları

### Aşama 4.1: İşletme Kapsamlı Marka ve Şube Yönetimi
- [x] **Marka Yönetimi Uygulaması & API:**
  - `GET /api/v1/restaurant-config/brands`: Mevcut işletmedeki markaları listeleyin (`tenant.brands.manage`).
  - `GET /api/v1/restaurant-config/brands/{brandId}`: ETag ile tek marka detaylarını alın.
  - `POST /api/v1/restaurant-config/brands`: İşletmenin içinde benzersiz bir slug içeren marka yaratın.
  - `PUT /api/v1/restaurant-config/brands/{brandId}`: Adı iyimser eşzamanlılık belirteciyle güncelleyin.
  - `POST /api/v1/restaurant-config/brands/{brandId}/activate`: Etkin olmayan markayı etkinleştirin.
  - `POST /api/v1/restaurant-config/brands/{brandId}/deactivate`: Etkin markayı devre dışı bırakın.
- [x] **Şube Yönetimi Uygulaması & API:**
  - `GET /api/v1/restaurant-config/branches`: İşletmedaki şubeleri isteğe bağlı olarak şuna göre filtreleyerek listeleyin: `brandId`.
  - `GET /api/v1/restaurant-config/branches/{branchId}`: ETag ile şube ayrıntılarını alın.
  - `POST /api/v1/restaurant-config/branches`: İşletmeye ve markaya bağlı şube oluşturun (`tenant.branches.manage`).
  - `PUT /api/v1/restaurant-config/branches/{branchId}`: Eşzamanlılık kontrolüyle adı, saat dilimini ve para birimini güncelleyin.
  - `POST /api/v1/restaurant-config/branches/{branchId}/activate`: Askıya alınan şubeyi etkinleştirin.
  - `POST /api/v1/restaurant-config/branches/{branchId}/suspend`: Aktif şubeyi askıya alın.
  - `POST /api/v1/restaurant-config/branches/{branchId}/close`: Şubeyi kalıcı olarak kapat.
- [x] **Güvenlik, Yalıtım ve Yaşam Döngüsü Kısıtlamaları:**
  - Doğrulanmış'tan katı işletme kapsamı JWT ana ve ortam işletme bağlamı (gövde/sorgu atlaması yok).
  - BranchManager kısıtlamaları: marka/şube oluşturamaz, şubeleri değiştiremez, yalnızca atanan şubeyi görüntüleyebilir.
  - Süper Yönetici kısıtlaması: işletme izinlerini atlayamaz; Platformdan işletme açılışı Faz 15 kapsamındadır.
  - Eşzamanlılık belirteci / ETag uygulaması: eski belirteç geri dönüşleri `409 Conflict`, güncelleme iadelerinde jeton eksik `412 Precondition Failed`.
  - Terminal kapalı durumu: Kapalı şubeler değiştirilemez, etkinleştirilemez veya askıya alınamaz (`400 Bad Request`).
  - Aynı işletmede yinelenen slug önleme (`409 Conflict`), farklı işletmeler aynı slug değerini kullanabilir.
  - Marka ve şube yaşam döngüsü operasyonları için yalnızca eklenen güvenlik denetimi günlük kaydı.
  - Tam RFC 7807 `application/problem+json` her konuda uyum 400, 401, 403, 404, 409ve 412 yanıtlarında.
- [x] **Otomatik Testler:**
  - Marka yaşam döngüsü birim testleri (`BrandLifecycleUnitTests.cs`).
  - Şube yaşam döngüsü birim testleri (`BranchLifecycleUnitTests.cs`).
  - RBAC izin matrisi birim testleri (`RestaurantConfigRbacMatrixUnitTests.cs`).
  - Marka & şube iş akışlarına yönelik entegrasyon testleri (`RestaurantConfigIntegrationTests.cs`).
  - Entegrasyon testleri RBAC, kapsam, işletme izolasyonu ve CSRF (`RestaurantConfigRbacAndIsolationIntegrationTests.cs`).

### Aşama 4.2: Güvenli İşletme Markalaması ve Tema Ayarları
- [x] **Alan Modelleri ve Değer Nesneleri:**
  - `ColorHex`: Kısayı doğrular (`#RGB`) ve standart (`#RRGGBB`) hex kodları; normalleştirir `#rrggbb`; geçersiz uzunlukları, geçersiz onaltılık karakterleri, adlandırılmış renkleri ve RGB/HSL işlevlerini reddeder.
  - `AssetUrl`: Güvenli göreceli yolları doğrular (`/...`) dizin geçişi olmadan (`..`, `\`, `//`) veya güvenli HTTPS URL'ler (`https://...`); reddeder `http:`, `data:`, `javascript:`ve dosya protokolleri.
  - `BrandAppearance`: Markanın görünen adını, logo/favicon referanslarını, kontrollü tasarım belirteçlerini (birincil, birincilHover, ikincil, vurgu, arayüz, arka plan), alt bilgi metnini, kabuk üstbilgisini/alt başlığını ve iyimser eşzamanlılık belirtecini koruyarak bir araya getirin. Katı anti-HTML metin üzerinde doğrulama.
  - `BranchThemeOverride`: İyimser eşzamanlılık belirteciyle şubeye özgü geçersiz kılmalara (displayName, logoUrl, titleSubtitle, footerBranchInfo) izin veren varlık.
- [x] **Tema Devralma ve Çözümleme:**
  - `EffectiveThemeDto`: Tema değerlerini hiyerarşik olarak çözer; şube geçersiz kılmaları ayarlandığında öncelik alır ve sorunsuz bir şekilde marka görünümüne ve sistemden bağımsız varsayılanlara geri döner.
- [x] **RBAC Yetenekler ve İzinler:**
  - `tenant.branding.view`: Marka temasını ve etkili şube temasını görüntüleyin (tüm kullanıcılara verilir) 8 roller).
  - `tenant.branding.manage`: Marka görünümünü (RestaurantAdmin) veya şube geçersiz kılmalarını güncelleyin (RestaurantAdmin tam, yalnızca kendi şubeniz için BranchManager).
  - İzin kaydı güncellendi (`Permissions.All.Count == 33`) ve belge senkronizasyonu `docs/ROLES-AND-PERMISSIONS.md`.
- [x] **Kalıcılık ve Çok İşletmeli Yapı:**
  - PostgreSQL tabloları `brand_appearances` ve `branch_theme_overrides` bileşik yabancı anahtarlarla (`(tenant_id, brand_id)` ve `(tenant_id, branch_id)`).
  - PostgreSQL Satır Düzeyinde Güvenlik (RLS) etkin ve zorunlu (`FORCE ROW LEVEL SECURITY`) çalışma zamanı izolasyon politikaları ve en az ayrıcalıklı hibelerle `restaurant_app_runtime`.
  - EF Core Genel Sorgu Filtresi açık `RestaurantOrderDbContext` yaptırım `t.TenantId == CurrentTenantId`.
  - Eklemeli, geri döndürülebilir geçiş: `20261002100000_AddBrandAppearanceAndBranchThemeOverrides`.
- [x] **Markalama ve Tema REST API:**
  - `GET /api/v1/restaurant-config/branding/brand/{brandId}`: ETag ile marka temasını edinin.
  - `PUT /api/v1/restaurant-config/branding/brand/{brandId}`: Eşzamanlılık kontrolüyle marka görünümünü güncelleyin.
  - `GET /api/v1/restaurant-config/branding/branch/{branchId}/effective`: Müşteri/garson uygulamaları için etkili tema edinin.
  - `GET /api/v1/restaurant-config/branding/branch/{branchId}/override`: Şube geçersiz kılmayı alın.
  - `PUT /api/v1/restaurant-config/branding/branch/{branchId}/override`: Şube geçersiz kılmayı güncelleyin.
  - `DELETE /api/v1/restaurant-config/branding/branch/{branchId}/override`: Şube geçersiz kılmayı temizleyin.
  - Yalnızca eklemeli güvenlik denetim günlüğü kaydı: `BrandThemeUpdated`, `BranchThemeOverrideUpdated`, `BranchThemeOverrideCleared`.
- [x] **Ön Uç Paylaşılan Sözleşmeler ve Token Entegrasyonu:**
  - `EffectiveTenantTheme` sözleşme yapmak `packages/ui/src/tokens/types.ts`.
  - `mapEffectiveThemeToOverrides()` içinde `packages/ui/src/tokens/theme.ts` etkili temanın eşleştirilmesi `applyTenantTheme()` CSS değişkenlerine.
- [x] **Otomatik Testler:**
  - Değer nesnesi ve toplam birim testleri (`BrandingLifecycleUnitTests.cs`).
  - RBAC izin matrisi birim testleri güncellendi (`PermissionRegistryMatrixTests.cs`).
  - Kapsamlı entegrasyon testleri (`RestaurantConfigBrandingIntegrationTests.cs`): varsayılan tema, marka güncellemesi, şubeleri geçersiz kılma, geçersiz kılmayı temizleme, eski eşzamanlılık jetonu, yetkisiz roller, şubeler arası değişiklik önleme, güvenli olmayan giriş reddi.

### Aşama 4.3: Yapılandırma Odaklı Dinamik Yönetici Kabuğu
- [x] **Güvenli Kod İçi Gezinme Kaydı:**
  - Statik tür açısından güvenli gezinme kaydı (`NAVIGATION_REGISTRY`) kapsayan 12 çekirdek alanlar: `dashboard`, `brand-settings`, `branch-settings`, `operating-hours`, `dining-areas`, `preparation-stations`, `feature-settings`, `staff`, `menu`, `tables`, `printers`, `reports`.
  - Gerçek `href`, bileşenler ve gerekli RBAC izinler (`requiredPermission`) kesinlikle kodda tanımlanmıştır.
  - Veritabanı yapılandırması yalnızca şunları geçersiz kılabilir: `isVisible`, `order` (1-100), güvenli düz metin `labelOverride` (hayır HTML/script etiketleri) ve `disabled` status.
  - Özellik bayrakları/geçersiz kılmaları RBAC denetimini atlayamaz (`hasNavigationAccess`). Bilinmeyen gezinme kimlikleri güvenli bir şekilde reddedildi.
- [x] **Dinamik Kabuk Sağlayıcısı (`AdminConfigProvider` & `useAdminConfig`):**
  - Kullanıcı kimlik doğrulamasından sonra etkili temayı ve dalları getirir.
  - Tasarım belirteçlerini uygular (`applyTenantTheme`) kökü belgelemek için CSS değişkenler (`--ro-color-*`).
  - İşletme değişikliği, şube değişikliği ve oturum kapatma durumunda belirteçleri ve durumu temizler (`clearTenantTheme`).
  - Tam yükleme döndürücü (`data-testid="admin-loading"`), yeniden denemeyi içeren hata başlığı (`data-testid="admin-error-retry"`) ve varsayılan kabuğa güvenli geri dönüş.
  - Şube değiştirici desteği (`selectedBranchId`, `selectBranch`) dinamik başlık entegrasyonu ile.
- [x] **Dinamik Yönetici Kabuğu Bileşenleri:**
  - **Başlık:** Marka/şube logosu, dinamik kabuk başlığı, şube adı, rol rozeti, şube değiştirici açılır menüsü, mobil menü hamburgeri.
  - **Kenar çubuğu:** Çözümlenenler tarafından yönlendirilen gezinme öğeleri ve RBAC-filtrelenmiş yapılandırma, bölüm gruplaması (`main`, `operations`, `settings`, `system`), etkin öğe göstergesi, masaüstü daraltma düğmesi, odak tuzağına sahip mobil çekmece iletişim kutusu.
  - **Altbilgi:** Dinamik iş metni, şube bilgileri, platform telif hakkı, doğrulanmış dış bağlantılar, koşullu görünürlük.
- [x] **Kurumsal Markalama Ayarları Ekranı (`BrandingSettingsView`):**
  - için özel konfigürasyon ekranı `RestaurantAdmin`.
  - Modüler alt bileşenler 450 satır sınırı (`LiveThemePreview`, `NavigationConfigTable`, `ThemeColorFields`, `ThemeBrandIdentityFields`, `ThemeTextHeaderFooterFields`).
  - Canlı önizleme modeli (kullanarak `BottomSheet`), mevcut yapılandırmaya sıfırlayın ve şube geçersiz kılmayı marka görünümüne geri döndürün.
  - Kolay hatalarla form doğrulama (örneğin onaltılık renkler, güvenli URL'ler) ve 409 eski eşzamanlılık çakışması mesajları.
- [x] **Otomatik Testler:**
  - 15 kapsamlı yönetici yapılandırma kabuk birimi ve entegrasyon testleri (`apps/admin-web/src/__tests__/admin-config-shell.test.tsx`): başarılı yapılandırma yükleme, yükleme durumu, hata ve yeniden deneme, güvenli varsayılan geri dönüş, oturum kapatıldığında tema uygulaması ve temizleme, işletme/şube değişikliği, izin filtreleme, özellik işareti RBAC yaptırım, bilinmeyen kimlik reddi, masaüstü kenar çubuğunun daraltılması, mobil çekmece, klavye erişilebilirliği, 320 piksel görüntü alanı taşması, kaydetme mutasyonu ve 409 çatışma yönetimi.
  - Arka uç entegrasyon testleri güncellendi (`RestaurantConfigBrandingIntegrationTests.cs`) navigasyon yapılandırmasını kapsayan JSON kalıcılık, dal devralma ve bilinmeyen kimlik reddi.
  - Hepsi 37 yönetici-web testleri, 894 arka uç birim testleri ve 7 markalaşma entegrasyon testleri geçiyor.

### Aşama 4.4: Şube Çalışma Saatleri ve Finansal Yapılandırma
- [x] **Şube Mali ve Operasyonel Ayarlar Alan Modeli:**
  - `BranchSettings` aggregate kökü ve `BranchSettingsId` güçlü yazılan tanımlayıcı.
  - `BasisPointsRate` değer nesnesi: tam sayı temel noktaları (0-10,000 vergi için bps, 0-5,000 servis ücreti için bps; sıfır kayan nokta hassasiyeti sorunları).
  - `SupportedLocales` değer nesnesi: BCP-47 doğrulama, benzersizlik, varsayılan yerel ayarın dahil edilmesi değişmez.
  - Tam operasyonel alanlar: Saat dilimi, Para birimi, Varsayılan yerel ayar, Desteklenen yerel ayarlar, Vergi dahil etme geçişi, Varsayılan vergi oranı, Hizmet ücreti geçişi ve oranı, Sipariş alma geçişi, Görünen ad, İletişim telefonu, e-posta, adres ve eşzamanlılık belirteci.
- [x] **Haftalık Çalışma Saatleri ve Program Alan Modeli:**
  - `BranchOperatingHours` aggregate kökü ve `BranchOperatingHoursId`.
  - `TimeSlot` değer nesnesi: Duvar saati `TimeOnly` çiftler, gece aralığı tespiti (`CloseTime < OpenTime`), sınır desteğine dokunarak (`[08:00-14:00)` ve `[14:00-22:00)`), gün içi ve gün içi gece boyunca yayılma örtüşmesi tespiti.
  - `OperatingDaySchedule` & `WeeklySchedule`: Kesinlikle 7 günler, kapalı günler değişmez (sıfır aralık içermelidir), şube saat diliminde kalıcı olmayan duvar saati korunur UTC destruction.
- [x] **Veritabanı Şeması, RLS & İşletme İzolasyonu:**
  - PostgreSQL tabloları: `branch_settings` ve `branch_operating_hours` bileşik yabancı anahtarlarla `branches(tenant_id, id)`.
  - PostgreSQL Satır Düzeyinde Güvenlik (RLS) etkin ve zorunlu (`FORCE ROW LEVEL SECURITY`) çalışma zamanı izolasyon politikaları ve en az ayrıcalıklı hibelerle `restaurant_app_runtime`.
  - EF Core Genel Sorgu Filtresi açık `RestaurantOrderDbContext` yaptırım `t.TenantId == CurrentTenantId`.
  - Eklemeli, geri döndürülebilir geçiş: `20261002140000_AddBranchSettingsAndOperatingHours`.
- [x] **RBAC & Yetki Matrisi:**
  - Kanonik izinler: `branch.configuration.view` ve `branch.configuration.manage`.
  - Matrix: RestaurantAdmin, işletmesı içindeki tüm şubeleri yönetebilir; BranchManager yalnızca kendisine atanmış şubeyi yönetebilir; diğer roller (Kasiyer, Mutfak, Bar, Garson, Müşteri) normalleştirilmiş okuma modellerine salt okunur erişime sahiptir; SuperAdmin reddedildi.
- [x] **REST API Uç noktalar:**
  - `GET /api/v1/restaurant-config/branches/{branchId}/settings`: ETag ile etkili ayarlar elde edin.
  - `PUT /api/v1/restaurant-config/branches/{branchId}/settings`: Eşzamanlılık doğrulamasıyla ayarları güncelleyin ve CSRF korumasıyla.
  - `GET /api/v1/restaurant-config/branches/{branchId}/operating-hours`: ETag ile çalışma saatlerini alın.
  - `PUT /api/v1/restaurant-config/branches/{branchId}/operating-hours`: Eşzamanlılık doğrulaması ile haftalık programı güncelleyin ve CSRF korumasıyla.
  - Yalnızca eklemeli güvenlik denetim günlüğü kaydı: `BranchSettingsUpdated`, `BranchOperatingHoursUpdated`.
- [x] **Yönetici Web Kullanıcı Arayüzü Bileşenleri:**
  - `BranchSettingsView` için sekmeli yapılandırma kapsayıcısı `branch-settings` ve `operating-hours` navigasyon rotaları.
  - `BranchFinancialSettingsForm` ve `BranchOperatingHoursForm` altında ayrıştırılmış 450 satır sınırı.
  - Kaydedilmemiş değişiklikler rozeti, 409 yeniden yükleme düğmesiyle eşzamanlılık çakışması uyarısı, mobil BottomSheet özeti.
- [x] **Otomatik Testler:**
  - 18 alan birimi testleri (`BranchSettingsAndOperatingHoursUnitTests.cs`).
  - 290 izin matrisi birim testleri güncellendi.
  - 11 ön uç birim testleri `admin-web` (`branch-settings.test.tsx`).
  - 13 arka uç entegrasyon testleri `RestaurantConfigBranchSettingsIntegrationTests.cs` ve `RestaurantConfigOperatingHoursIntegrationTests.cs`.
  - Hepsi 928 arka uç birim testleri, 48 yönetici web testleri ve 230 ön uç birim testleri geçiyor.

### Aşama 4.5: Şube Yemek Alanları, Hazırlama İstasyonları ve Özellik Kontrolleri
- [x] **Şube Yemek Alanı Alan Modeli:**
  - `DiningArea` aggregate kökü; güçlü türlenmiş kimlik: `DiningAreaId`.
  - Alanlar: Id, TenantId, BranchId, Name, Code/slug, AreaType (`Indoor`, `Terrace`, `Garden`, `BarArea`, `Other`), SortOrder, IsActive, CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken.
  - Yumuşak yaşam döngüsü (`Activate()`, `Deactivate()`) idempotens ile; sert silme yok.
  - Bu aşamada tablo veya tablo düzeni yok (kesinlikle Aşama 6 kapsam).
- [x] **Şube Hazırlama İstasyonu Alan Modeli:**
  - `PreparationStation` aggregate kökü; güçlü türlenmiş kimlik: `PreparationStationId`.
  - Alanlar: Id, TenantId, BranchId, Code (şube içinde benzersiz, küçük harfle normalleştirilmiş), DisplayName, StationType (`Kitchen`, `Bar`, `Other`), SortOrder, IsActive, ConcurrencyToken.
  - Yumuşak yaşam döngüsü (`Activate()`, `Deactivate()`) idempotens ile.
  - Yazıcı IP'si yok, ESC/POS veya bu aşamada yönlendirme (Aşama 11-12 kapsam).
- [x] **Tip Güvenli Özellik Bayrak Sistemi:**
  - Desteklenen katalog anahtarları: `CustomerQrOrdering`, `CustomerServiceRequests`, `Tips`, `SplitBilling`, `OnlinePayments`, `KitchenDisplay`, `BarDisplay`, `Reservations`, `KioskMode`.
  - Katı alan doğrulaması, bilinmeyen veya rastgele dize anahtarlarını reddeder.
  - Güvenli varsayılanlar: uygulanmayan özellikler (finansal, KDS, rezervasyonlar, kiosk) varsayılan olarak devre dışıdır.
  - Hiyerarşik çözüm: İşletme varsayılanları + Şube geçersiz kılmaları -> Etkili hesaplanmış yapılandırma.
  - Açık mimari sınırlar: özellik bayrakları atlanamaz RBAC, işletme yalıtımı veya gizli uç noktaları açığa çıkarma.
- [x] **Kalıcılık, Şema ve Çok İşletmeli Yapı:**
  - PostgreSQL tabloları: `dining_areas`, `preparation_stations`, `tenant_feature_flags`, `branch_feature_flags` bileşik yabancı anahtarlarla `branches(tenant_id, id)`.
  - PostgreSQL RLS etkin ve zorunlu (`FORCE ROW LEVEL SECURITY`) çalışma zamanı izolasyon politikaları ve en az ayrıcalıklı hibelerle `restaurant_app_runtime`.
  - Eklemeli, geri döndürülebilir geçiş: `20261003140048_AddDiningAreasStationsAndFeatureFlags`.
- [x] **REST API Uç noktalar:**
  - Yemek Alanları: Listele, Oluştur, Güncelle, Yeniden Sırala, Etkinleştir, Devre Dışı Bırak (`/api/v1/restaurant-config/branches/{branchId}/dining-areas`).
  - Hazırlık İstasyonları: Listeleme, Oluşturma, Güncelleme, Yeniden Sıralama, Etkinleştirme, Devre Dışı Bırakma ve Mutfak/Bar çalışma zamanı okuma modeli (`/stations/runtime`).
  - Özellik Bayrakları: İşletme varsayılanları (`GET/PUT /api/v1/restaurant-config/tenant/features`), Şube geçersiz kılmaları (`GET/PUT/DELETE /api/v1/restaurant-config/branches/{branchId}/features/override`), Etkili (`GET /api/v1/restaurant-config/branches/{branchId}/features/effective`).
  - Tüm mutasyonlar için yalnızca güvenlik denetim günlüğü kaydı ekleyin.
- [x] **Yönetici Web Kullanıcı Arayüzü Ekranları:**
  - `DiningAreasView.tsx` listeleme, ekleme, düzenleme, sürüm/belirteç doğrulama ile yeniden sıralama, etkinleştirme/devre dışı bırakma ve mobil `BottomSheet`.
  - `PreparationStationsView.tsx` listeleme, ekleme, düzenleme, sürüm/belirteç doğrulama ile yeniden sıralama, etkinleştirme/devre dışı bırakma ve mobil `BottomSheet`.
  - `FeatureFlagsView.tsx` Etkili Bayraklar, Şube Geçersiz Kılmaları ve İşletme Varsayılanları (yalnızca Restoran Yöneticisi) sekmeleri ile mobil `BottomSheet`.
  - Gezinme kaydı güncellendi (`dining-areas`, `preparation-stations`, `feature-settings` RestaurantAdmin ve BranchManager için etkinleştirildi).
- [x] **Otomatik Testler:**
  - Etki alanı birimi testleri (`DiningAreasAndStationsUnitTests.cs`).
  - RBAC izin matrisi birim testleri güncellendi (`RestaurantConfigRbacMatrixUnitTests.cs`).
  - Entegrasyon testleri (`RestaurantConfigDiningAreasAndStationsIntegrationTests.cs` ve `RestaurantConfigConcurrencyIntegrationTests.cs`).
  - Ön uç birim testleri (`dining-areas-and-stations.test.tsx` ile 10 testler).
  - Hepsi 973 arka uç birim testleri, 10 mimari testleri ve 58 admin-web hızı testleri (240 toplam ön uç testleri) geçme.

### Aşama 4.6: Son Sertleştirme, Doğrulama ve Birleştirmeye Hazır Olma
- [x] **Kapsamlı Kod ve Mimari İncelemesi:**
  - İşletme ve şube yalıtımı tüm yapılandırma tablolarında doğrulandı (`brand_appearances`, `branch_settings`, `dining_areas`, `preparation_stations`, `branch_feature_flags`).
  - Bileşik yabancı anahtar referansı `(tenant_id, brand_id)` ve `(tenant_id, branch_id)` işletmeler arası atamayı fiziksel olarak önleyin.
  - PostgreSQL Satır Düzeyinde Güvenlik (RLS) politikaları ve EF Core genel sorgu filtreleri tamamen uyumlu hale getirildi ve uygulandı `FORCE ROW LEVEL SECURITY`.
  - Tam RBAC genelinde uygulanan matris 8 varsayılan olarak reddetme davranışına sahip roller ve RFC 7807 Sorun Ayrıntıları yanıtları.
  - Eşzamanlılık jetonu / ETag doğrulaması, tüm varlıklardaki sessiz güncelleme kayıplarını önler (ilk oluşturma, ana jetonu doğrular; sonraki güncellemeler, toplam jetonu doğrular; eksik jeton getirileri) 412, eski sürümde 409 döndürür).
  - Dinamik kabuk girişi temizliği kesinlikle keyfi reddeder CSS, ifadeler, harici komut dosyası ekleme ve bilinmeyen yollar.
  - Yetkilendirmeden ayrılan özellik bayrakları; özellik geçiş durumuna bakılmaksızın izinler kesinlikle gereklidir.
  - Genişletme sözleşmesi geçiş kuralları, idempotent ile tahribatsız olarak doğrulandı SQL paket doğrulama (`deploy/migrations/latest_bundle.sql`).
  - Sıfır sır veya PII kodda, git geçmişinde veya uygulama günlüklerinde tespit edildi.
- [x] **Negatif Akış Doğrulaması:**
  - İşletmeler arası yapılandırmada değişiklik yapılması önlendi.
  - BranchManager tarafından dallar arası mutasyon reddedildi 403 Forbidden.
  - Yetkisiz rollerin reddedilen yapılandırma yazma işlemleri.
  - Eksik veya çözülemeyen işletme bağlamında arıza kapatma davranışı.
  - Terminal kapalı şube değişmezliği uygulandı.
  - Atomik veritabanı işlemleri, arızalarda sıfır kısmi veya bozuk durum sağlar.
  - Ön uç yönetici kabuğu, ağdaki varsayılan belirteçlere zarif bir şekilde geri döner/API failure.
- [x] **Dokümantasyon ve Yol Haritası Kapanışı:**
  - `ROADMAP.md` güncellendi: Aşama 4 işaretlenmiş `COMPLETED`, Faz 5 işaretlenmiş `NEXT`.
  - `DOMAIN.md`, `ARCHITECTURE.md`, `MULTI-TENANCY.md`, `DESIGN-SYSTEM.md`, `ROLES-AND-PERMISSIONS.md`, `SCREEN-INVENTORY.md`, `NEGATIVE-FLOWS.md`ve `PHASE-4-TRACKER.md` aktif uygulamayla senkronize edilir.

---

## 3. Nihai Doğrulama Durumu (Aşama 4.6)

Son doğrulama şu tarihte tamamlandı: 2026-10-04 için `c77588c0f842ae2191e8b97871097eb07882ff99`. Her iki GitHub Eylemi çalıştırması da tamamen geçti: [push run 37203196434](https://github.com/yusuf-ulgen/restaurant-order/actions/runs/37203196434) ve [çekme isteği çalıştırması 37203198903](https://github.com/yusuf-ulgen/restaurant-order/actions/runs/37203198903). CI entegrasyon paketi başarılı oldu **201/201 testler**; CI arka uç birimi testleri geçti **1001/1001**, mimari testleri geçti, ön uç testleri ve kapsam geçti ve tüm derleme, Docker, güvenlik ve depo kapıları geçti.

Kapsam hatası `3aeee15` admin-web fonksiyonu kapsamından kaynaklandı 66.66%, altında 80% eşik. Eklenen davranış odaklı yönetici testleri, yerel yönetici web işlevi kapsamını artırdı 82.99%; son CI kapsama kapısı her iki iş akışında da geçti.

## 3. Daha Önce Doğrulanmış Komut Sonuçları (Faz 4.6)

| Komut | Kapsam | Sonuç | Ayrıntılar |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln` | Arka Uç Çözümü | **PASS** | 0 uyarı, 0 hata |
| `dotnet test tests/unit/` | Birim Test Paketi | **PASS** | 973 / 973 geçti (100%) |
| `dotnet test tests/architecture/` | Mimari Süit | **PASS** | 10 / 10 geçti (100%) |
| `pnpm --filter admin-web test` | Yönetici Web Vitest'i | **PASS** | 77 / 77 başarılı; 7 test dosyası (100%) |
| `pnpm test:unit:frontend` | Ön Uç Birim Süitleri | **PASS** | 259 / 259 paketler/kullanıcı arayüzünde geçirilen testler ve 3 web uygulamaları (100%) |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 uyarı, 0 hata |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 çalışma alanı projeleri temiz |
| `node scripts/check-file-size.mjs` | Dosya Boyutu Kapısı | **PASS** | 0 dosyalar aşıyor 600 satırlık üst sınır |
| `node scripts/check-docs.mjs` | Belgeler ve Bağlantılar Kapısı | **PASS** | 52/52 Dokümanlar doğrulandı, 0 kırık bağlantılar |
| `node scripts/check-secrets.mjs` | Güvenlik Tarayıcısı | **PASS** | 0 sırlar veya özel anahtarlar açığa çıktı |
| `pnpm verify:gates` | Kalite Kontrolleri (yerel çevre) | **NOT PASS** | Tek kullanımlık Blue/Green konteyner akışı için Docker arka plan programı kullanılamıyordu; tam CI geçidi her iki son Eylem çalıştırmasında da geçti. |
| `node scripts/migration-ops.mjs validate` | Taşıma Kuralları | **PASS** | 34 / 34 tahribatsız geçişler |
| `node scripts/migration-ops.mjs script` | Taşıma Paketi | **PASS** | `deploy/migrations/latest_bundle.sql` oluşturulan |

---

## 4. Bilinen Riskler ve Azaltmalar

1. **Risk:** BranchManager, çok şubeli organizasyonlarda şube ayarlarını değiştirmeye çalışıyor.
   **Azaltma:** `tenant.branches.manage` kesinlikle verilir `RestaurantAdmin`. BranchManager'ın bu özelliği reddedilir ve uç noktalar yetkisiz arayanları şu şekilde reddeder: RFC 7807 `403 Forbidden`.
2. **Risk:** Kalıcı olarak kapatılan şubelerin kazara değiştirilmesi.
   **Azaltma:** Etki alanı değişmezi `Branch.EnsureNotClosed()` terminalde değişmezliği zorlar `Closed` Birim ve entegrasyon testleriyle doğrulanan durum.
3. **Risk:** Eş zamanlı yönetimsel düzenlemelerin üzerine yazılan eski güncellemeler.
   **Azaltma:** Her mutasyonda iyimser eşzamanlılık belirteçleri kontrol edildi. Eksik jeton getirileri `412 Precondition Failed`; eşleşmeyen token getirileri `409 Conflict`.
4. **Risk:** Kötü amaçlı veya hatalı biçimlendirilmiş CSS/HTML markalama alanları aracılığıyla enjeksiyon.
   **Azaltma:** Yalnızca kontrollü tasarım belirteçleri (onaltılık renkler kesinlikle aracılığıyla doğrulanır) `ColorHex`; URL'ler kesinlikle şu şekilde doğrulandı: `AssetUrl`; metin çıkarıldı HTML alan değeri nesneleri aracılığıyla etiketler). Keyfi CSS veya HTML/JS kabul edilmez ya da çalıştırılmaz.
5. **Risk:** İşletmeler arası markalama verileri sızıntısı.
   **Azaltma:** PostgreSQL Satır Düzeyinde Güvenlik (RLS) zorla `brand_appearances` ve `branch_theme_overrides`EF Core genel işletme filtreleri ve ortam onaylı işletme bağlamı çözümü.

---

## 5. Bekleyen Mimari Kararlar

- Faz için Yok 4.2. Çekirdek alan modelleri (`Brand`, `Branch`) ve PostgreSQL RLS Fazdan itibaren 2 değişiklikleri bozmadan yeniden kullanılır ve genişletilir.

Hata yanıtlarında teknik sözleşme adı `ProblemDetails` olarak korunur.
