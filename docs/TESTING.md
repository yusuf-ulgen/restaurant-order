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

Bu hedef, mevcut E2E süitinin tüm akışı kapsadığı iddiası değildir; mevcut iki E2E testi HTTP sağlık sorgularıdır.

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
