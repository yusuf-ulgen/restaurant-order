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

### Aşama 6: Masalar, QR ve Oturumlar (Durum: NEXT)
- [ ] Masa numaralandırma, kapasite ve fiziksel düzen konumlandırma.
- [ ] Kriptografik imzayla dinamik ve statik QR kodu oluşturma.
- [ ] Yemek oturumu yaşam döngüsü durum makinesi (Açık -> Etkin -> Fatura İstendi -> Kapalı).
- [ ] Masa taşıma ve birleştirme mekaniği.

### Aşama 7: Müşteri Deneyimi (Durum: Planlandı)
- [ ] Arayüz 1: QR Müşteri Web Uygulamasının tam uygulaması.
- [ ] Duyarlı menü taraması, alerjen filtreleme ve öğe arama.
- [ ] Etkileşimli öğe değiştirici yapılandırma modu.
- [ ] Sepet yönetimi, vergi hesaplaması ve sipariş gönderimi.
- [ ] Hizmet çağrısı talepleri ("Garson Çağır", "Islak Mendil İste", "Fatura İste").

### Aşama 8: Sipariş Çekirdeği (Durum: Planlandı)
- [ ] Sipariş varlığı yaşam döngüsü durumu makinesi (Taslak -> Gönderildi -> Kabul Edildi -> Hazırlanıyor -> Hazır -> Sunuldu -> Ücretli -> Kapalı).
- [ ] Satır öğesinin değişmezliği ve denetim günlüğünün düzenlenmesi.
- [ ] Sipariş fiyatı hesaplama motoru (taban, değiştiriciler, vergiler, hizmet ücretleri).
- [ ] Eş zamanlı masa siparişlerinde yarış durumunun önlenmesi.

### Aşama 9: Canlı Olaylar ve Bildirimler (Durum: Planlandı)
- [ ] ASP.NET Core SignalR hub'ı şu şekilde bölümlendirilmiştir: `tenant_id` ve `branch_id`.
- [ ] Misafirlerden/garsonlara gerçek zamanlı sipariş yayılımı (<500ms) KDS ve operasyonlar.
- [ ] Chime sesli uyarı gönderimi ve mobil anlık bildirimler.
- [ ] Bağlantı esnekliği, kalp atışı izleme ve otomatik yeniden bağlanma.

### Aşama 10: Garson ve Operasyon (Durum: Planlandı)
- [ ] Arayüz 2: Garson ve Operasyon Mobil Uygulamasının tam uygulaması.
- [ ] Renk kodlu tablo durumlarına sahip etkileşimli kat planı.
- [ ] Hızlı mobil sipariş girişi ve değiştirici seçimi.
- [ ] Konuk servis çağrıları ve hazır yemek uyarıları için bildirim çekmecesi.
- [ ] Masa transferi ve fatura ödeme tetikleyicileri.

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
