# Test Stratejisi ve Doğrulama Standartları (`docs/TESTING.md`)

## 1. Doğrulanmamış Başarı Bildirilemez

Bu politika insan katkıcılar ve AI ajanları için zorunludur:

> İlgili test/derleme komutu ortamda gerçekten çalıştırılıp `0` çıkış koduyla tamamlanmadan uygulama, düzeltme veya test süiti için `PASS`, `SUCCESS`, `VERIFIED` ya da başarı anlamındaki başka bir ifade kullanmayın. Varsayım, teorik iddia veya kısmi doğrulamayı tam başarı gibi sunmak yasaktır.

## 2. Test Piramidi

```text
E2E: Arayüzler arası kullanıcı yolculukları (Playwright)
Entegrasyon: API, veritabanı RLS, yazdırma kuyruğu, ağ geçidi
Birim/durum: Alan varlıkları, durum makineleri, matematik, RBAC
```

### 2.1. Birim Testleri

Hızlı, yalıtılmış ve bellek içinde çalışır:

- **Alan ve hesaplamalar:** Kalem ara toplamı, ek seçenek fiyatı, vergi, hesap paylaşımı ve komisyon formülleri.
- **Durum geçişleri:** Her geçerli geçiş ve her geçersiz geçişin belirli hatası.
- **RBAC:** Sekiz rolün her yetenekte tam olarak izinli veya reddedilmiş olması.

### 2.2. Entegrasyon Testleri

- **PostgreSQL RLS:**
  - İşletme A, B'nin verisini okuyamaz, değiştiremez veya silemez.
  - Çalışma zamanı rolü `restaurant_app_user`, `NOSUPERUSER NOBYPASSRLS` olmalıdır.
  - `tenancy.get_current_tenant_id()` yoksa/geçersizse sorgu sıfır satır döndürür.
  - `IgnoreQueryFilters()` ve ham SQL, veritabanı RLS'sini aşamaz.
- **İşlem tutarlılığı ve bağlantı havuzu:** Sıralı/eşzamanlı yeniden kullanımda işletme oturum değişkeni temizlenir; geçişler temiz PostgreSQL 16 üzerinde doğrulanır.
- **Bağlam ve middleware:** HTTP işletme çözümü, eksik bağlamda RFC 7807 Problem Details, correlation ID aktarımı ve worker bağlamının kesin temizlenmesi test edilir.
- **Testcontainers:** `TestcontainersGuard`, CI'da Docker bulunmasını zorunlu kılar; yoksa test başarısız olmalıdır. Yerelde Docker olmadan yalnızca konteyner dışı testleri çalıştırmak isteyen geliştirici açıkça `SKIP_TESTCONTAINERS=true` seçebilir; bu, konteyner testlerini geçtiği anlamına gelmez.

### 2.3. Uçtan Uca Testler

Hedeflenen tam yemek yaşam döngüsü:

1. Müşteri QR'ı tarar, ürünleri sepete ekler.
2. Sipariş gönderilir ve mutfak KDS'de görünür.
3. Mutfak fişi `READY` yapar.
4. Garson uyarıyı alır ve masaya servis eder.
5. Kasa hesabı böler ve ödemeyi kaydeder.
6. Masa oturumu kapanır; masa `AVAILABLE` olur.

Bu hedef, mevcut E2E süitinin tüm akışı kapsadığı iddiası değildir; mevcut Playwright E2E testleri HTTP sağlık ve müşteri masa karşılama (CUST-01) QR akışlarını doğrular.

## 3. Zorunlu Kritik Yollar

Bu alanları değiştiren PR kapsamlı otomatik test olmadan kabul edilmez:

1. Sipariş/fiyat: Ek seçenekler, indirim ve ürün vergileri.
2. Finans: Hesap paylaşımı, bahşiş, ödeme bakiyesi ve iade denetim izi.
3. İşletme yalıtımı: İşletmeler arası veri sızıntısının engellenmesi.
4. Donanım: Yazıcı arızası ve açık yeniden yazdırma kuyruğu.
5. Eşzamanlılık: Aynı anda iki sipariş; yetkili personel iptali ile hazırlığın yarışı; doğrudan müşteri iptalinin RBAC ile reddi.

## 4. Test Verisi

- Gerçek müşteri adı, telefon, kart numarası veya canlı ödeme kimlik bilgisi test dosyalarında **asla kullanılamaz**.
- Gerçekçi restoran örnekleri için deterministik sentetik veri fabrikaları kullanılır.

## 5. Kapsama Gereksinimleri

Kritik iş mantığı için belirtilen eşikler:

- Alan varlıkları/hesaplamalar: en az %90 dal ve %95 ifade kapsamı.
- Durum makineleri: %100 geçiş kapsamı; geçerli ve korumayla reddedilen geçişlerin tamamı.
- RBAC: Sekiz rol boyunca %100 rol/yetki kapsamı.

## 6. Doğrulama Komutları

```bash
# Dosya, bağlantı, gizli bilgi ve kontrol betiği testleri
pnpm verify:gates

# Backend birim testleri
dotnet test tests/unit/RestaurantOrder.UnitTests.csproj

# Mimari sınır testleri
dotnet test tests/architecture/RestaurantOrder.ArchitectureTests.csproj

# Entegrasyon testleri
dotnet test tests/integration/RestaurantOrder.IntegrationTests.csproj

# Frontend bileşen testleri
pnpm --filter @restaurant-order/ui test

# Monorepo birim/mimari testleri
pnpm test

# Tam doğrulama
pnpm verify
```

## Faz 5 Katalog Doğrulaması

Alan/birim testleri, API işleyici/yetki testleri, PostgreSQL/Redis Testcontainers, yönetim arayüzü, E2E sağlık sorguları ve kapsama kontrolleri kullanılır. İşletme/şube/istasyon yalıtımı, yaşam döngüsü görünürlüğü, fiyat doğrulaması, benzersizlik, ETag önkoşulu/çatışması, bulunabilirlik eşzamanlılığı ve geri alma yolları test edilir. Docker yoksa entegrasyon testi başarısız olmalı; atlama başarı sayılamaz. Test ve kapsama sonuçlarını yalnızca ilgili kesin commit'in CI çalışmasına dayanarak kaydedin.

RFC 7807 hata sözleşmesi testlerinde `ProblemDetails` tür adı kullanılır.

## Faz 6 Kat Planı, Masa ve QR Doğrulaması

Kat planı, masa ve QR doğrulaması; arka uç alan/birim testlerini, mimari sınır testlerini, ön uç bileşen/birim testlerini (`packages/ui`, `apps/admin-web`, `apps/customer-web`, `apps/operations-web`), ön uç test kapsam kapılarını, E2E kullanıcı akışlarını ve PostgreSQL 16 Testcontainers entegrasyon testlerini kapsar. Entegrasyon testleri ayrıcalıksız çalışma zamanı rolü (`NOSUPERUSER NOBYPASSRLS` yetkili `restaurant_app_user`) ile gerçek PostgreSQL üzerinde yürütülerek RLS politikalarını, bileşik yabancı anahtar matrisini, kısmi benzersiz aktif oturum kısıtlarını, statik QR eşzamanlı oturum değişimini, dinamik QR süre aşımını ve oturum kapanışında belirteç iptalini doğrular. Testcontainers ortamında Docker erişilemezse testler başarısız olmalıdır (fail-closed); atlama başarı sayılamaz. Test ve kapsam sayıları yalnızca kesin commit için fiilen çalıştırılan CI sonuçlarından alınır; doğrulanmamış sayılar dokümantasyona eklenmez.

## Faz 7 Müşteri Deneyimi ve Hizmet Talebi Doğrulaması

Müşteri deneyimi ve hizmet talebi doğrulaması; TypeScript sözleşme ve drift testlerini, ön uç birim/bileşen testlerini (`apps/customer-web` ve `packages/ui`), arka uç birim ve Testcontainers entegrasyon testlerini (`RestaurantOrder.IntegrationTests`), ve Playwright E2E kullanıcı yolculuklarını kapsar.

- **Frontend Birim / Bileşen:** CUST-01 Karşılama, CUST-02 Menü ve Kategoriler, CUST-03 Ürün Özelleştirme ve Varyant/Modifier seçimi, CUST-04 Sepet İncelemesi ve `sessionStorage` draft kurtarma/temizleme, CUST-06 Hizmet Talepleri ve DiningSession fatura talebi akışları kapsamlı Vitest testleriyle doğrulanır.
- **E2E Playwright Doğrulaması (`tests/e2e`):** Deterministik sentetik seed (`e2e-catalog-seed.ts`) üzerinden tam 15 adımlı müşteri yolculuğu (`customer-journey.spec.ts`) ve olumsuz durum senaryoları (`customer-negative.spec.ts`) çalıştırılır. QR tarama, oturum kurtarma, menü filtreleme, ürün özelleştirme, sepet yönetimi, garson çağırma, duplicate koruması, iptal ve hesap isteme adımları gerçek tarayıcıda test edilir; raw token veya QR sızıntısı olmaması teyit edilir.
- **Sertleştirme ve Sınırlar:** Canlı KDS/garson SignalR bildirimleri Faz 9–11 kapsamındadır.

## Faz 8 Sipariş Çekirdeği Doğrulaması

Sipariş çekirdeği doğrulaması; arka uç alan/birim testlerini, PostgreSQL 16 Testcontainers entegrasyon testlerini, TypeScript sözleşme ve drift testlerini, ön uç bileşen testlerini (`apps/customer-web`) ve tam E2E Playwright kullanıcı ve olumsuz durum senaryolarını kapsar.

- **Alan ve Yetkili Fiyatlandırma Motoru:** İstemciden gelen birim fiyat, ara toplam, vergi, servis ücreti veya genel toplam asla yetkili kabul edilmez; sunucu tarafı katalog ve şube ayarlarından tek veritabanı işleminde hesaplanır. `clientPricingPolicyFingerprint` ve `clientExpectedTotalMinorUnits` zorunludur; eksiklik veya geçersizlikte `400 Bad Request` (sıfır persistence), politika uyuşmazlığında `409 ORDER_PRICING_POLICY_CHANGED`, toplam uyuşmazlığında `409 ORDER_TOTAL_CHANGED` döner.
- **İdempotency ve Deterministik Parmak İzi:** `Idempotency-Key` başlığı ve SHA-256 tabanlı kanonik istek parmak izi ile deterministik yeniden oynatma (`200 OK` / `201 Created`) garanti edilir. Aynı anahtarla farklı gövde gönderilirse `409 Conflict` üretilir.
- **Eşzamanlılık ve Güvenlik:** Kapanmış veya fatura istenmiş (`BillRequested`) masalarda sipariş oluşturma reddedilir. Masadan gelen eşzamanlı iki sipariş iki ayrı round (`RoundNumber`) olarak sıra numarası alır. Ham SQL unique violation hataları dışarı 500 sızdırılmaz; `CONCURRENCY_CONFLICT` (409) Problem Details olarak döner.
- **E2E 15 Adımlı Yolculuk ve Negatif Akışlar (`tests/e2e`):** Deterministik katalog seed üzerinden QR tarama, oturum girişi, menü, ürün özelleştirme, sepet, onay modalı, backend `201 Created` doğrulaması, sipariş turu görüntüleme, sepet temizliği, sayfa yenileme, sipariş geçmişinden görüntüleme ve veritabanında tek sipariş oluştuğunun doğrulanması (15 adım) eksiksiz test edilir. Negatif akışlarda gevşek durum kodları yerine kesin sözleşme kodları (`401`, `400`, `409`) assert edilir.
- **Personel Siparişi, İptal ve Supervisor PIN Concurrency:** Waiter ve yetkili roller için hazırlık öncesi kalem iptali (`orders.items.cancel_pre_prep`) ve hazırlık aşamasında yönetici yetkili void (`orders.items.void_in_prep`) kuralları doğrulanmıştır. 5 ayrı masa oturumu altında aynı BranchManager kimliği üzerinden eşzamanlı isteklerle `iam.pin_credentials` satır kilidi (`FOR UPDATE`), 15 dakikalık kilit, rollback'ten bağımsız sayaç kalıcılığı ve doğru PIN ile güvenli sıfırlama PostgreSQL üzerinde test edilmiştir. Müşteri notları ve PIN değerleri audit log'da kesinlikle yer almaz.

## Faz 9 Canlı Olaylar ve Bildirimler Doğrulaması

Güncel çalışma ağacının gerçek komut sonuçları, eski ölçümlerin sınırları ve kalan dış bağımlılıklar [Faz 9 inceleme kaydında](./PHASE-9-REVIEW.md) tutulur. Uzak CI yerel düzeltmelerin kanıtı değildir.

- `WorkerGatewayPrivilegeTests`, `WorkerPushRlsTests`: gerçek NOSUPERUSER/NOBYPASSRLS API ve worker rolleri, global claim reddi, iki tenant batch'i ve bağlam temizliği.
- `WorkerPushRecoveryTests`, `WorkerClaimFencingTests`: staging sonrası hata/çökme, rollback, replay, unique görev, eşzamanlı staging, Redis lease kaybı ve eski workerın geç sağlayıcı sonucu.
- `TwoApiInstanceRealtimeIntegrationTests`: gerçek authenticated HTTP mutasyonu → çalışan worker hostunun slot/lease/polling yolu → API B istemcisi; cross-tenant/wrong-branch, gerçek askıya alma ve session revoke, kaçırılan invalidation, restart ve HTTP recovery. Auth/membership için sürekli true mock yoktur.
- `PostgreSqlOutboxSafeWatermarkIntegrationTests`: kopya SQL yerine gerçek HTTP sync, ters XID/sequence ve commit/rollback yarışı.
- Frontend hook testleri: canlı 102/eksik 101, farklı aggregate, boş yetki sayfası, dedup, pagination ve scope reset. Snapshot yenilemesi başarısızsa HTTP cursorı ilerletilmez; hata tekrar denenir.
- Browser E2E: gerçek sipariş/menü değişikliğinin kullanıcı ekranına yansıması ve istemci terminal mesajı görmezden gelse bile gerçek WebSocket transport kapanması. Testteki personel kimlikleri gerçek sentetik kullanıcı/üyelik/session kayıtlarıyla eşleşir.
- FCM SDK adaptörü, izin/banner, hedef yenileme/unregister ve service worker testleri: hassas veri minimizasyonu, görünür uygulamada ikinci uyarının bastırılması, mevcut kök rotaya tıklama. Gerçek FCM cloud/fiziksel cihaz teslimi ayrı HTTPS ve sağlayıcı erişimi gerektirir.
