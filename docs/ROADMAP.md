# Ürün ve Teknik Yol Haritası (`docs/ROADMAP.md`)

## 1. Yol Haritasına Genel Bakış ve Aşamalı Kilometre Taşları

`restaurant-order` için 18 aşamalı geliştirme planı tanımlanmıştır. Her aşama, önceki kilometre taşlarında oluşturulan doğrulanmış mimariye, alan değişmezlerine ve kalite kontrollerine dayanır.

```text
Faz 0: Temel ve katkı kuralları [Tamamlandı]
  AGENTS.md, araç yönergeleri, monorepo ve kalite kontrolleri;
  React 19 / ASP.NET Core 10 kabukları, Docker Compose;
  dağıtılmış worker kirası ve hata halinde erişimi reddeden Blue/Green motoru.
        ↓
Faz 1: Tasarım sistemi ve uygulama kabukları [Tamamlandı]
  Ortak renk, tipografi, aralık değişkenleri; 44px dokunma hedefleri;
  güvenli alanlar, azaltılmış hareket ve erişilebilir ortak bileşenler.
        ↓
Faz 2: Veri ve çok işletmeli yapı [Tamamlandı]
  PostgreSQL 16, RLS, işletme çözümleme, EF Core 10;
  Tenant / Brand / Branch ve çalışma zamanı/geçiş rolü ayrımı.
        ↓
Faz 3: Kimlik doğrulama ve RBAC [Tamamlandı]
  Sekiz rol, JWT, güvenli çerezler, yenileme;
  güvenilir terminalde dört haneli PIN ve merkezi yetkilendirme.
        ↓
Faz 4: Restoran yapılandırması [Tamamlandı]
  Marka/şube, çalışma saatleri, vergi ve hizmet ücreti;
  yemek alanları, istasyonlar ve yetki kontrollü özellik bayrakları.
        ↓
Faz 5: Menü ve katalog [Tamamlandı]
  Kategori, ürün, porsiyon fiyatları ve seçenek grupları;
  zorunlu/isteğe bağlı, tek/çoklu, ücretsiz/ücretli seçimler.
        ↓
Faz 6: Masalar, QR ve Oturumlar [Tamamlandı — CI Doğrulandı]
  Masa yerleşimi, kapasite, şekiller, ETag eşzamanlılığı ve denetim izi;
  HMAC-SHA256 statik ve dinamik QR oluşturma ve rotasyonu;
  yemek oturumu durum makinesi (Açık -> Etkin -> Fatura İstendi -> Kapalı);
  ADM-04 Kat Planı Yönetimi ve CUST-01 Masa Karşılama arayüzü.
        ↓
Faz 7: Müşteri Deneyimi [Tamamlandı — CI Doğrulandı]
  Dijital menü, arama, filtreler, ürün özelleştirme ve sepet;
  servis talepleri ve hesap isteme akışı; 15 adımlı E2E doğrulandı.
        ↓
Faz 8: Sipariş Çekirdeği [Tamamlandı — PR #19 Birleşti, CI Doğrulandı]
  Order aggregate ve persistence, yetkili sunucu fiyatlandırması;
  müşteri/personel sipariş API'si, advisory lock eşzamanlılığı.
        ↓
Faz 9: Canlı Olaylar ve Bildirimler [Tamamlandı — CI Doğrulandı]
  SignalR / Redis backplane, transactional outbox, sürekli yetkilendirme,
  HTTP sync, FCM push cihaz kaydı, sesli uyarı ve p95 < 500ms yayılım.
```

---

## 2. Ayrıntılı Kilometre Taşı Teslimatları

### Aşama 0: Temel ve Katkı Kuralları (Durum: Tamamlandı)
- [x] Tek bağlayıcı hakikat kaynağı oluşturun: `AGENTS.md` ve ince adaptörler (`.AGENT.md`, `CLAUDE.md`, `GEMINI.md`).
- [x] Kapsamlı alan, ürün, mimari ve operasyonel belgeler (`docs/*`).
- [x] Monorepo iskelesi (`pnpm` çalışma alanları, ASP.NET Core 10 API, .NET 10 İşçi, React 19 ağ kabukları).
- [x] Redis, merkezi aktif slot durumu ve idempotency deposu ile dağıtılmış çalışan kiralama koordinasyonu.
- [x] Arıza durumunda kapatılan Blue/Green dağıtım motoru ve otomatik doğrulama paketi.
- [x] Otomatik kalite kontrolleri: dosya boyutu sınırları (450/600 satır), gizli tarama, bağlantı bütünlüğü ve 67 doğrulanmış testler

### Aşama 1: Tasarım Sistemi ve Uygulama Kabukları (Durum: Tamamlandı)
- [x] Merkezi, işletme tarafından genişletilebilir tasarım belirteci sistemi `packages/ui` (renkler, tipografi, aralık, yarıçap, yükseklik).
- [x] Semantik durum göstergelerine sahip nötr, modern renk paleti (dekoratif degradeler yok).
- [x] Temel küresel stiller: tutarlı kutu boyutlandırma, yazı tipi yığınları, odak görünürlüğü, mobil taşma koruması.
- [x] Minimum dokunma hedefi uygulaması (44 piksel) WCAG / iOS standardı) ve iOS güvenli alan desteği.
- [x] Geçişler ve animasyonlar arasında azaltılmış hareket erişilebilirliği hazırlığı.
- [x] Token yardımcı programları ve kapsamlı birim testi kapsamı.
- [x] Erişilebilir katman temel öğeleri (Modal, BottomSheet, Drawer, ConfirmationDialog, Toast).
- [x] Müşteri, Operasyonlar ve Yönetici arayüzlerinde duyarlı uygulama kabuğu düzenleri.
- [x] Entegrasyon `apps/customer-web`, `apps/operations-web`ve `apps/admin-web`.

### Aşama 2: Veri ve Çok İşletmeli Yapı (Durum: Tamamlandı)
- [x] Çok işletmeli PostgreSQL 16 Satır Düzeyinde Güvenlik ile şema tasarımı (RLS).
- [x] Varlık Çerçevesi Çekirdeği 10 kalıcılık seçimi ([ADR-0002](./adr/0002-persistence-selection.md) ACCEPTED).
- [x] İşletme, Marka ve Şube alan adı modelleri, katı yaşam döngüsü kuralları ve güçlü kimliklerle bir araya getirilir.
- [x] PostgreSQL eşlemesi, bileşik yabancı anahtarlar ve ilk EF Core geçişi (`001_initial_tenancy_schema.sql`).
- [x] Veritabanı rolü ayrımı: `postgres` taşıma sahibi vs `restaurant_app_user` (NOSUPERUSER, NOBYPASSRLS) çalışma zamanı rolü.
- [x] arıza-kapalı RLS olan politikalar `tenancy.get_current_tenant_id()` oturum değişkeni.
- [x] İşletme bağlam çözümleme ara yazılımı, RFC 7807 ProblemDetails ve korelasyon kimliği yayılımı.
- [x] Arka plan çalışan işletme bağlam yayılımı (`ITenantWorkerJobRunner`) ve önbellek anahtarı ad alanı (`TenantCacheKeyFactory`).
- [x] Veritabanı geçiş araçları (`scripts/migration-ops.mjs`) ve sıfır kesinti süresi Blue/Green genişletme-sözleşme güvenliği.
- [x] Tekrar çalıştırılması güvenli yerel örnek veri oluşturucu (`DevDataSeeder`).
- [x] İki işletmeli gerçek PostgreSQL Testcontainers entegrasyon testleri, katı işletme izolasyonunu doğrular.

### Aşama 3: Kimlik Doğrulama ve RBAC (Durum: Tamamlandı)
- [x] Kimlik ve Erişim Yönetimi (IAM) modülü ile 8 desteklenen roller
- [x] JWT kimlik doğrulama, belirteç yenileme akışları ve güvenli çerez depolama.
- [x] Hızlı 4-hane PIN Garson ve operasyon mobil terminalleri için kimlik doğrulama.
- [x] Rol Tabanlı Erişim Kontrolü (RBAC) yetkilendirme ara yazılımı ve izin matrisi.

### Aşama 4: Restoran Yapılandırması (Durum: Tamamlandı)
- [x] Marka görünümü ve şube teması geçersiz kılınır CSS sterilizasyon ve sözleşme doğrulama (Aşama) 4.1 & 4.2).
- [x] Yönetici gezinme öğeleri ve marka gezinme yapılandırması API/UI (Faz 4.3).
- [x] Şube mali ayarları (saat dilimi, para birimi, yerel ayarlar, vergi oranları, hizmet ücretleri) ve temel nokta doğruluğuyla çalışma saatleri (Aşama) 4.4).
- [x] Tahribatsız yaşam döngüsüne (Faz) sahip yemek alanları (Kapalı, Teras, Bahçe, BarArea, Diğer) ve hazırlama istasyonları (Mutfak, Bar, Diğer) 4.5).
- [x] Şube özelliği bayrakları, motoru katı bir şekilde değiştirir RBAC öncelik (Faz 4.5).
- [x] İşletme ve şube izolasyonu, RLS politikalar, bileşik yabancı anahtarlar ve arızalı güvenlik (Aşama) 4.6).

### Aşama 5: Menü & Katalog (Durum: COMPLETED — CI doğrulandı)
- [x] İşletme ve şube kapsamını, tamsayı küçük birim fiyatlarını, yaşam döngüsü kontrollerini, ETag eşzamanlılığını ve denetim kayıtlarını içeren menü/kategori/öğe/varyant kataloğu.
- [x] Değiştirici gruplar/seçenekler, seçim değişmezleri, fiyat deltaları, diyet/alerjen meta verileri ve bulunabilirlik kontrolleri.
- [x] Çelişki doğrulamalı diyet, alerjen ve baharatlı meta veriler.
- [x] Yönetici kataloğu düzenleyicisi ve filtrelenmiş çalışma zamanı kataloğu okuma modeli, hızlı86/yeniden stok kontrolleri.
- [x] Nihai sertleşme ve kapatma; push ve pull_request CI yeşil (218/218 entegrasyon testleri).

### Aşama 6: Masalar, QR ve Oturumlar (Durum: COMPLETED — CI Doğrulandı)
- Aşama 6 masaları, fiziksel kat planını, QR kimliğini ve yemek oturumu altyapısını kurar. Müşteri menüleri/sipariş (Aşama 7), sipariş çekirdeği (Aşama 8), gerçek zamanlı olaylar (Aşama 9), faturalandırma (Aşama 13) ve ödemeler (Aşama 16) sonraki aşamalardır.
- [x] Masa numaralandırma, kapasite, şekiller, fiziksel kat planı tuval/ızgara konumlandırması ve ETag iyimser eşzamanlılık.
- [x] HMAC-SHA256 imzası, kanonik veri yükü, sürüm iptali ve sabit zamanlı doğrulama ile dinamik ve statik QR kod üretimi (v2 şeması, `iat` zaman damgası zorunluluğu).
- [x] Masa başına tek aktif oturum kısıtlaması, anında önbellek geçersiz kılma ve güvenli müşteri oturum çerezleri ile yemek oturumu yaşam döngüsü durum makinesi (`Open` -> `Active` -> `BillRequested` -> `Closed`).
- [x] Yönetici kat ve QR yönetim paneli (ADM-04) ve müşteri masa karşılama açılış arayüzü (CUST-01).
- [x] Masa taşıma ve oturum birleştirme mekanikleri (Faz 6.4): Değişmez hareket geçmişi (`DiningSessionMovement`), 64 karakter SHA-256 istek fingerprint'i (`RequestFingerprint`), sabit zamanlı replay karşılaştırması, 409 Conflict çakışma tespiti, deterministik artan GUID satır kilitleme (deadlock önleme), kapasite ve durum kontrolleri, zorunlu idempotency anahtarı desteği ve kaynak oturum önbellek geçersiz kılma tamamlandı.
- [x] Veritabanı kısıtları ve sertleştirme (Faz 6.6): Doğrusal yaşam döngüsü (`Open -> Active -> BillRequested -> Closed`) ve kronoloji kısıtları (`ck_dining_sessions_status_timestamps`, `ck_dining_sessions_chronology`), masa şekil ve tuval sınır kısıtları (`ck_restaurant_tables_shape`, `ck_restaurant_tables_canvas_bounds`), `ck_restaurant_tables_public_code` kısıtı ve eklemeli migrasyonlar (`20261011140000`, `20261011160000`, `20261011180000`, `20261011210000`, `20261012010000`).
- [x] Müşteri JWT Bearer boru hattı ve önbellek güvenliği: GUID formatında olmayan `sub`, eksik `sub`, oturum claim uyuşmazlığı, gelecek tarihli `iat`, 4096 bayttan uzun token ve kapalı/birleştirilmiş oturum reddi entegrasyon testleriyle doğrulandı. Redis önbelleğinde negatif-yalnızca TTL stratejisiyle stale pozitif "1" yetkilendirmesi imkansız kılındı.
- [x] Birim, mimari, entegrasyon ve E2E testleri: 1.422 backend birim testi, Testcontainers PostgreSQL & Redis entegrasyon testleri, 10 mimari testi, 359 frontend birim testi (0 konsol uyarısı), 0 derleme uyarısı. Playwright E2E disposable servisler üzerinde 3 passed, 0 skipped olarak tamamlandı.

> **Not:** Aşama 7 (Müşteri Deneyimi) tamamlandı ve GitHub CI üzerinde doğrulandı (Run 39038756673); bkz. [Faz 7 Takibi](./PHASE-7-TRACKER.md).

### Aşama 7: Müşteri Deneyimi (Durum: Tamamlandı — GitHub CI Doğrulandı)
- [x] Arayüz 1: QR Müşteri Web Uygulaması ve oturum kurtarma altyapısı (Faz 7.0).
- [x] Duyarlı menü taraması, alerjen filtreleme ve öğe arama CUST-02 (Faz 7.1).
- [x] Etkileşimli öğe değiştirici yapılandırma modalı CUST-03 (Faz 7.2).
- [x] Sepet yönetimi, güvenli draft persistence ve fiyat önizlemesi CUST-04 (Faz 7.3). Not: Gerçek sipariş oluşturma ve yetkili fiyatlandırma Faz 8'e aittir.
- [x] Hizmet çağrısı talepleri ("Garson Çağır", "Su İste", "Peçete İste", "Yardım İste", "Hesap İste") CUST-06 (Faz 7.4).
- [x] Sertleştirme, erişilebilirlik, 15 adımlı Playwright E2E ve negatif güvenlik denetimi (Faz 7.5).

> **Not:** Sipariş durum uyuşmazlığı giderilmiştir: `Order/OrderItem` yalnızca `Draft`, `Submitted`, `InPreparation`, `Ready`, `Served`, `Cancelled` durumlarını izler. İstasyon fişi kabulü (`Acknowledged`) `StationTicket`'a, `Paid` ve `Closed` ise `Bill` ve `DiningSession` yaşam döngülerine aittir. KDS, SignalR, yazdırma ve ödeme sonraki fazlara aittir. Bkz. [Faz 8 Takibi](./PHASE-8-TRACKER.md).

### Aşama 8: Sipariş Çekirdeği (Durum: COMPLETED — CI Doğrulandı)
- [x] Hazırlık, kapsam sınırları, durum makinesi hizalaması ve takip belgesi (Faz 8.0).
- [x] Sipariş varlığı yaşam döngüsü durum makinesi (`Draft` -> `Submitted` -> `InPreparation` -> `Ready` -> `Served`, `Cancelled`) ve persistence (Faz 8.1).
- [x] Otoriter sunucu fiyatlandırma motoru (Pricing Engine) ve Hızlı-86 katalog doğrulaması (Faz 8.2).
- [x] Müşteri sipariş API'si, PostgreSQL advisory lock eşzamanlılığı ve idempotency (Faz 8.3).
- [x] Müşteri sipariş gönderme ve sepet onaylama arayüzü (Faz 8.4).
- [x] Personel sipariş API'si ve yaşam döngüsü altyapısı (Faz 8.5).
- [x] Negatif akışlar, E2E browser doğrulaması ve güvenlik sertleştirmesi (Faz 8.6).

### Aşama 9: Canlı Olaylar ve Bildirimler (Durum: COMPLETED — PR Açık, CI Doğrulandı)
- [x] ASP.NET Core SignalR hub'ı (`/hubs/events`) ve Redis backplane (`tenant_id`, `branch_id`, masa ve istasyon grupları).
- [x] Olay zarfı (`EventEnvelope`), C# ve TypeScript çift yönlü drift testleri.
- [x] Transactional Outbox (`tenancy.operational_outbox`), atomik mutasyon, distributed claim/lease ve Redis Pub/Sub köprüsü.
- [x] Sürekli yetkilendirme (continuous authorization), token/oturum iptalinde anında kesinti.
- [x] Kopma sonrası sequence tabanlı HTTP eşitleme (`/api/v1/events/sync`).
- [x] FCM cihaz kaydı (`DeviceRegistration`), aktif oturum ve üyelik bağı, güvenli bildirim gönderimi (ADR-0011).
- [x] Müşteri ve operasyon ekranlarında canlı güncellemeler, sesli uyarı (chime), offline sepet koruması.
- [x] Çoklu instance teslimi, Playwright E2E ve <500 ms p95 yayılım gecikmesi doğrulaması.

### Aşama 10: Garson ve Operasyon (Durum: COMPLETED — Dalda Doğrulandı, İncelemeye Hazır)
- [x] Arayüz 2: Garson ve Operasyon Mobil Uygulamasının (WAIT-01–WAIT-07) tam uygulaması (`apps/operations-web`).
- [x] Renk ve metin/ikon kodlu türetilmiş masa durumlarına sahip aranabilir kat planı ve ızgara görünümü (WAIT-02).
- [x] Masa oturum detayı, sipariş turları, oturma süresi ve servis çağrısı yönetimi (WAIT-03).
- [x] Kompakt hızlı mobil sipariş girişi, varyant/modifier seçici, taslak ve Idempotency-Key koruması (WAIT-04).
- [x] Pre-prep gerekçeli iptal ve in-prep/ready/served için yönetici/supervisor PIN void akışı (`CancelItemModal`).
- [x] Çakışma korumalı, artan GUID satır kilitlemeli ve açık onaylı masa taşıma ve birleştirme (WAIT-05).
- [x] WAIT-06 Sınırı: Gerçek sipariş toplamı önizlemesi (minor-unit) ve `request-bill` tetikleyicisi (Fatura bölme ve tahsilat motoru Faz 13 ve Faz 16'ya aittir).
- [x] Otoriter sayaçlı, 3 sekmeli bildirim çekmecesi (`OperationsCallsDrawer`), KDS hazır yemek uyarıları ve realtime koordinatör (WAIT-07).
- [x] 13 kabul kriterinin tamamı, 1.862 .NET testi, 441 frontend testi ve Playwright E2E browser testleriyle doğrulandı.

### Aşama 11: KDS ve Yönlendirme (Durum: Planlandı)
- [ ] Arayüz 3: Mutfak & Bar KDS tam uygulama.
- [ ] İstasyona özel hazırlık fişi yönlendirme (Yiyecek -> Mutfak, İçecek -> Bar).
- [ ] Görsel hazırlık zamanlayıcı kartları (Yeşil <10 m, Sarı 10–20m, Kırmızı >20m).
- [ ] Tek dokunuşla hazırlık fişini ilerletme (Hazırlık Aşamasında -> Hazır) ve hazırlık fişi geri çağırma modu.
- [ ] Doğrudan tek dokunuşla öğe 86 işaretleme işlemi.

### Aşama 12: Yazdırma (Durum: Planlandı)
- [ ] ESC/POS mutfak fişleri ve misafir faturaları için termal baskı motoru.
- [ ] Soket zaman aşımı yönetimi ve üstel yeniden deneme özelliklerine sahip ağ yazıcısı biriktiricisi.
- [ ] Kategoriden yazıcıya yönlendirme kuralları.
- [ ] Yük devretme yazdırma arabelleğe alma ve manuel yeniden yazdırma çekmecesi.

### Aşama 13: Hesap Motoru (Durum: Planlandı)
- [ ] Fatura oluşturma, ayrıntılı sipariş özetleri ve vergi dökümü.
- [ ] Fatura bölme matematiği (eşit olarak bölme, öğeye göre bölme, özel tutarları bölme).
- [ ] Bahşiş tahsis motoru ve personelin vardiya bazında bahşiş takibi.
- [ ] Gün Sonu Z raporu oluşturma ve denetim mutabakatı.

### Aşama 14: Yönetim ve Analiz (Durum: Planlandı)
- [ ] Arayüz 4: Restoran Yönetici Paneli tam uygulaması.
- [ ] Sürükle ve bırak özellikli menü kataloğu ve değiştirici grup düzenleyicisi.
- [ ] Personel dizini, rol ataması ve PIN yönetimi.
- [ ] Operasyonlar kontrol paneli: canlı ciro, istasyon gecikmesi ve en çok satan ürünler.
- [ ] Termal yazıcı IP yapılandırması ve yönlendirme yönetimi.

### Aşama 15: Platform Süper Yönetici (Durum: Planlandı)
- [ ] Arayüz 5: Platform Süper Yönetici Paneli'nin tam uygulaması.
- [ ] İşletme organizasyonu yaşam döngüsü (yerleşik, özel etki alanlarını yapılandırma, askıya alma).
- [ ] Abonelik katmanı yönetimi ve platform komisyon takibi.
- [ ] Küresel denetim günlüğü, durum izleme ve sistem ölçümleri.

### Aşama 16: Ödemeler (Durum: Planlandı)
- [ ] Çoklu sağlayıcılı ödeme ağ geçidi entegrasyonu (Stripe, Iyzico) ([ADR-0006](./adr/README.md)).
- [ ] Tüm finansal ödeme uç noktalarında Idempotency anahtar uygulaması.
- [ ] POS kart okuyucu entegrasyonu ve yazar kasa mutabakatı.
- [ ] İade iş akışları ve kısmi ödeme mutabakatları.

### Aşama 17: Üretime Çıkış (Durum: Planlandı)
- [ ] Uçtan uca çok arayüzli yemek yaşam döngüsü doğrulaması.
- [ ] Sahnelemede Blue/Green sıfır kesinti süreli yayın provası.
- [ ] En yüksek simüle edilmiş restoran hacmi altında performans ve yük testi.
- [ ] Üretim güvenliği denetimi, sızma testi ve canlı yayına geçiş onayı.
