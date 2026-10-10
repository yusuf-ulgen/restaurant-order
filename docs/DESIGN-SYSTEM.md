# Tasarım Sistemi ve Uygulama Kabukları (`docs/DESIGN-SYSTEM.md`)

## 1. Genel Bakış ve İlkeler

 `restaurant-order` Tasarım Sistemi (`packages/ui`), platformda kullanıcıya yönelik tüm ürün arayüzleri için birleşik, erişilebilir ve duyarlı bir temel sağlar.

### Temel İlkeler
1. **Önce Erişilebilirlik (WCAG 2.1 AA):** Tüm etkileşimli bileşenler minimum 44 piksellik dokunma hedeflerini, görünür odak ana hatlarını, klavye gezinmesini, ekran okuyucu semantiğini (`aria-*`) ve `prefers-reduced-motion` tercihine uyumu destekler.
2. **Nötr ve Yüksek Kontrastlı Palet:** Anlamsal durum göstergelerine sahip temiz beyaz/gri/siyah bir temel üzerine kurulmuştur (`success`, `warning`, `danger`, `primary`). Dekoratif degradeler ve dikkat dağıtıcı animasyonlar kesinlikle yasaktır.
3. **Mobil Öncelikli ve Güvenli Alan Farkındalığı:** 320 piksel ekran genişliğinde katı mobil taşma önleme özelliğine sahip duyarlı tasarım ve iOS/Android güvenli alanları için yerel destek (`env(safe-area-inset-*)`).
4. **Sıfır Ağır Dış Bağımlılıklar:** React 19, TypeScript ve CSS minimum paket boyutu ve maksimum performans sağlayan özel özellikler (tasarım belirteçleri).
5. **Yapılandırma Odaklı Kabuklar:** Kabuk düzenleri (üstbilgi, altbilgi, kenar çubuğu) tür açısından güvenli yapılandırma sözleşmeleri (`types.ts`). Dinamik ayarların kalıcılığı Faz 4 kapsamında uygulanmıştır.

---

## 2. Tasarım Token Sistemi

Tasarım belirteçleri merkezi olarak bildirilir `packages/ui/src/tokens/` ve aracılığıyla ihraç edildi CSS özel özellikler (`tokens.css`):

| Tasarım Değişkeni Kategorisi | CSS Değişkenler | Amaç |
| :--- | :--- | :--- |
| **Renkler** | `--ro-color-surface`, `--ro-color-background`, `--ro-color-border`, `--ro-color-primary`, `--ro-color-danger`, `--ro-color-success`, `--ro-color-warning` | Nötr arayüz ve metin renkleri, anlamsal vurgular |
| **Aralık** | `--ro-space-1` (4px)'e `--ro-space-16` (64 piksel) | Tutarlı 4px/8px aralık ızgarası |
| **Tipografi** | `--ro-font-sans`, `--ro-font-size-*`, `--ro-font-weight-*`, `--ro-line-height-*` | Ölçeklenebilir tür hiyerarşisi |
| **Kenarlıklar ve Yarıçaplar** | `--ro-radius-sm` (4px)'e `--ro-radius-full` (9999 piksel) | Bileşen köşeleri |
| **Yükseklik** | `--ro-shadow-sm`, `--ro-shadow-md`, `--ro-shadow-lg`, `--ro-shadow-xl` | Yükseklik ve derinlik |
| **Z-indeksi** | `--ro-z-sticky` (1100), `--ro-z-drawer` (1300), `--ro-z-modal` (1400), `--ro-z-toast` (1600), `--ro-z-tooltip` (1700) | Katman hiyerarşisi |
| **Dokunma Hedefleri** | `--ro-touch-target-min` (44 piksel), `--ro-touch-target-dense` (36 piksel) | WCAG hedeflere dokunma |
| **Güvenli Alanlar** | `--ro-safe-area-top`, `--ro-safe-area-bottom`, `--ro-safe-area-left`, `--ro-safe-area-right` | Mobil çentik ve hareket ekleri |

---

## 3. Bileşen Envanteri ve Sınırları

### 3.1. Çekirdek İlkelleri (`packages/ui/src/components`)
- **`Button` & `IconButton`:** Varyantları destekleyen standartlaştırılmış etkileşimli tetikleyiciler (`primary`, `secondary`, `outline`, `ghost`, `danger`), döndürücü durumu yükleniyor (`aria-busy`) ve erişilebilir etiketler.
- **`Input`, `Textarea`, `Select`:** Geçersiz durum göstergesine sahip form kontrolleri (`aria-invalid`), devre dışı stil ve belirteç entegrasyonu.
- **`Checkbox`, `Switch`:** Erişilebilir geçiş durumlarına ve odak göstergelerine sahip ikili girişler.
- **`Badge`, `Card`, `Divider`:** Görsel gruplama ve durum görüntüleme temelleri.
- **`Spinner`, `Skeleton`:** Azaltılmış hareket tercihlerine göre durum göstergeleri yükleniyor.
- **`EmptyState`, `ErrorState`:** Sahte veriler veya yanıltıcı yer tutucular olmadan gerçek durumu yansıtan görünümler.
- **`FormField`, `FormError`:** Etiketleri, girişleri ve hata mesajlarını birbirine bağlayan anlamsal form sarmalayıcılar (`aria-describedby`).
- **`VisuallyHidden`:** Yalnızca ekran okuyucuyla erişilebilen metin yardımcısı.
- **`ErrorBoundary`:** Geri dönüş kullanıcı arayüzü ile istemci tarafı React hata sınırı.

### 3.2. Kaplamalar ve Diyaloglar (`packages/ui/src/overlay` & `components`)
- **`Portal`:** SSR-güvenli DOM portal montaj elemanlarını doğrudan `document.body`.
- **`useScrollLock`:** İç içe/sıralı kaplamaları destekleyen, referans sayılan arka plan kaydırma kilitleme.
- **`useFocusTrap`:** Klavye Sekme gezinmesini etkin katmanların içine hapseder, ilk odağı yönetir (`initialFocusRef`), Escape ile kapatmayı ve kapatılınca odağın tetikleyici öğeye dönmesini yönetir.
- **`Modal`:** Erişilebilir diyalog (`role="dialog"`, `aria-modal="true"`) masaüstü ve tablet akışları için.
- **`BottomSheet`:** Sürükleme kolu göstergesi, dahili kaydırma ve güvenli alan dolgusu ile mobil cihazlar için optimize edilmiş yukarı kayar sayfa.
- **`Drawer`:** Yan panel desteği `left` ve `right` yerleşimleri.
- **`ConfirmationDialog`:** Yıkıcı veya kritik eylemler için açık onaylama/iptal iletişim kutusu.

### 3.3. Geçici Bildirim Sistemi (`packages/ui/src/toast`)
- **`ToastProvider`:** Bildirim kuyruklarını, zamanlayıcıları ve otomatik kapatmayı yöneten React bağlam sağlayıcısı.
- **`ToastViewport`:** Jetonlarla konumlandırılan sabit konteyner, güvenli alan farkında.
- **`ToastItem`:** Destekleyen anlamsal bildirimler `success`, `error`, `warning`ve `info` olan türler `role="status"` ve `role="alert"`.
- **`useToast`:** Yardımcı kanca açığa çıkıyor `toast.success()`, `toast.error()`, `toast.warning()`ve `toast.info()`.

### 3.4. Uygulama Kabukları ve Düzeni (`packages/ui/src/shell`)
- **`AppShell`:** Üst düzey uygulama düzeni koordinatörü desteği `customer`, `operations`ve `admin` varyantları.
- **`AppHeader`:** Logo, başlık, alt başlık, eylem alanları ve mobil menü geçişi içeren duyarlı başlık.
- **`AppFooter`:** Telif hakkı, işletme bilgileri ve güvenli dahili/harici bağlantılar içeren erişilebilir altbilgi.
- **`Sidebar` & `SidebarSection`:** Katlanabilir masaüstü kenar çubuğu ve mobil çekmece entegrasyonu.
- **`MobileNavigation`:** Mobil cihazlar için güvenli alan desteğine sahip sabit alt gezinme çubuğu.
- **`PageHeader`, `PageContainer`, `ContentSection`:** İçerik hiyerarşisi, duyarlı maksimum genişlikler ve sıfır taşma garantisi.
- **`SkipLink`:** Doğrudan atlamak için klavye kısayolu bağlantısı `#main-content`.

---

## 4. Uygulama Entegrasyon Matrisi

| Arayüz | Kabuk Varyantı | Kenar Çubuğu Davranışı | Başlık Eylemleri | Mobil Navigasyon |
| :--- | :--- | :--- | :--- | :--- |
| **`apps/customer-web`** | `customer` | **Yok** (İhmal edildi) | Masa durumu, aktif oturum rozeti | Doğrudan eylem düğmeleri + Alt Sayfa |
| **`apps/operations-web`** | `operations` | **Yok** (Mobil/tablet odaklı) | Vardiya durumu, garson modu rozeti | Alt `MobileNavigation` çubuk |
| **`apps/admin-web`** | `admin` | **Masaüstü Yapışkan + Mobil Çekmece** | Yönetici rozeti, işlem düğmeleri | Çekmece hamburger aracılığıyla açıldı |

---

## 5. Dinamik Markalama ve Restoran Yapılandırması (Aşama 4 Tamamlandı)

1. **Dinamik Tema Özelleştirme ve Güvenli Token Enjeksiyonu:**
   - Marka görünümü ve şube teması geçersiz kılınır, onaylanmış belirteçler enjekte edilir (`--ro-color-primary`, `--ro-radius-md`, vb.) doğrudan içine DOM `:root` veya konteyner stilleri.
   - Katı CSS temizleme normal ifade kuralları keyfi yasaklar CSS, URL'ler, `@import`, ifadeler veya HTML etiketlerini.
   - İşletmeleri veya şubeleri değiştirirken, işletmeler arası görsel sızıntıyı önlemek için önceki işletmenin özel özellikleri güvenilir bir şekilde temizlenir.
2. **Dinamik Yönetici Gezinme Kaydı:**
   - Marka yöneticileri, etkin gezinme öğelerini ve etiketlerini aracılığıyla yapılandırır `NavigationConfigView`.
   - Kabuk navigasyon doğrulayıcısı, navigasyonun ele geçirilmesini önlemek için kayıtlı olmayan rotaları, rastgele URL'leri ve komut dosyalarını reddeder.
3. **Yönetici Yapılandırma Görünümleri (`apps/admin-web`):**
   - **`ThemeSettingsView`:** Anında erişilebilen kontrast doğrulamayla marka görünümü ve şube temasının özelleştirilmesi.
   - **`NavigationConfigView`:** Güvenli rota bağlama özelliğine sahip, yeniden sıralanabilir, değiştirilebilir gezinme öğeleri.
   - **`BranchSettingsView`:** Finansal ayarlar, temel vergi/hizmet ücreti oranları, para birimi, yerel ayarlar ve haftalık çalışma saatleri programı.
   - **`DiningAreasView`:** Aktif/pasif durum geçişleriyle yemek alanı bölgesi yönetimi (İç Mekan, Teras, Bahçe, BarArea, Diğer).
   - **`PreparationStationsView`:** Şube kapsamlı benzersiz kod kuralıyla hazırlık istasyonu yönetimi (Mutfak, Bar, Diğer).
   - **`FeatureFlagsView`:** Ayrıntılı geçiş kontrollerine sahip şube özelliği bayrakları panosu ve RBAC öncelik uygulaması.
4. **Arızaya Karşı Güvenli Kabuk Varsayılanları:**
   - İşletme yapılandırması varsa API çağrı başarısız olursa veya zaman aşımına uğrarsa, uygulama kabuğu, kullanıcı arayüzü oluşturmayı bozmadan hemen güvenli varsayılan belirteçlere ve temel gezinmeye geri döner.
