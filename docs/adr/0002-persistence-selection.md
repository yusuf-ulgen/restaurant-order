# ADR-0002: Veri Kalıcılığı Kitaplığı Seçimi (`docs/adr/0002-persistence-selection.md`)

- **Durum:** `ACCEPTED`
- **Karar Verenler:** Mimarlık Ekibi, Yusuf Ülgen
- **Tarih:** 2026-09-20
- **Teknik Hikaye:** Veri Erişimi ve Kalıcılık Stratejisi Değerlendirmesi (Faz 2)

---

## 1. Bağlam ve Sorun Açıklaması

`restaurant-order` Birincil işlem veritabanı olarak PostgreSQL'e güvenir. Sistem, finansal defterler, fatura hesaplamaları, siparişler ve hazırlık fişleri için belirleyici durum geçişleri ve kuruluşlar, markalar ve şubeler arasında satır düzeyinde katı işletme izolasyonu için güçlü ilişkisel tutarlılık gerektirir.

Bir kalıcılık kitaplığı, aşağıdaki gereksinimleri karşılarken alan modeli ile PostgreSQL arasında köprü kurmalıdır:
1. Sıkı alan modeli kapsülleme (Zengin Alan Varlıkları, Değer Nesneleri, özel ayarlayıcılar).
2. CI/CD'ye entegre edilmiş otomatik şema taşıma yaşam döngüsü.
3. PostgreSQL Satır Düzeyi Güvenliği (RLS).
4. Yoğun yemek dönemlerinde öngörülebilir performans ve kaynak kullanımı.

---

## 2. Karar Etkenleri

- **Alan Modeli Bütünlüğü:** Kapsüllenmiş durum makinelerine ve sızıntı olmadan özel ayarlayıcılara sahip zengin alan varlıkları ORM alanına öznitelikler.
- **Taşıma ve Şema Güvenliği:** Sıfır kesinti süreli genişletme ve daraltma dağıtımlarını destekleyen sağlam şema gelişimi.
- **Çok İşletmeli Yapı ve Satır Düzeyinde Güvenlik (RLS):** PostgreSQL oturum değişkenleriyle uyumluluk (`SET LOCAL app.current_tenant_id`) ve bağlantı havuzu oluşturma.
- **Geliştirici Üretkenliği ve Sürdürülebilirliği:** Tür açısından güvenli sorgu soyutlamaları, derleme zamanı sorgu doğrulaması ve bakımı yapılabilir birim/entegrasyon testleri.
- **Performans ve Genişletilebilirlik:** Standart CRUD/aggregate işlemlerinde yüksek verim; ölçümle gerekçelendirildiğinde kontrollü ham SQL kullanımı.

---

## 3. Karar

`restaurant-order` için birincil ORM ve şema geçişi aracı olarak **Entity Framework Core 10 (EF Core 10)** ve **Npgsql sağlayıcısı (`Npgsql.EntityFrameworkCore.PostgreSQL`)** seçilmiştir.

### Bu Kararın Temel İlkeleri:
1. **Birincil ORM ve Taşımalar:** EF Core 10 şema geçişleri ve ilişkisel haritalama için tek doğruluk kaynağıdır.
2. **Güvenlik Sınırı — PostgreSQL Satır Düzeyinde Güvenlik (RLS):** Kesin çok işletmeli güvenlik sınırı PostgreSQL'dir RLS. Her veritabanı bağlantısında `app.current_tenant_id` bağlantı/işlem kapsamında ayarlanır.
3. **Derinlemesine Savunma Sorgu Filtreleri:** EF Core genel sorgu filtreleri işletme kapsamlı varlıklarda ikinci savunma katmanıdır; PostgreSQL RLS sınırının yerini almaz.
4. **Kontrollü Ham SQL Kullanımı:** Karmaşık analitik/yüksek hacimli sorgularda LINQ dönüşümünün maliyeti ölçülüp belgelendirilirse `FromSqlInterpolated` veya `ExecuteSqlInterpolatedAsync` ile kontrollü ham SQL kullanılabilir.
5. **MVP İçin Marten ve Event Sourcing Seçeneğinin Reddi:** Marten ve tam event sourcing reddedildi MVP. İş alanı (restoran yemekleri, masa değişimleri, bölünmüş faturalandırma), toplu olarak anında ilişkisel tutarlılık ve basit raporlama gerektirir. Olay kaynağı kullanımı, mevcut aşama için orantısız olan operasyonel ve projeksiyon karmaşıklığını artırıyor.
6. **Dapper'ın Birincil Kalıcılık Katmanı Olarak Reddedilmesi:** Dapper, otomatik değişiklik izlemenin bulunmaması, yerel şema taşıma araçlarının bulunmaması ve zengin alan adı kümelerini haritalamak için artan ortak metin nedeniyle birincil kalıcılık katmanı olarak reddedilir. Dapper, ihtiyaç duyulması halinde gelecek aşamalarda yalnızca özelleştirilmiş, performans ölçümlü okuma projeksiyonları için düşünülebilir.

---

## 4. Sonuçlar ve Ödünleşimler

### Olumlu Sonuçlar
- **Sağlam Alan Eşlemesi:** EF Core 10 özel kurucuları, alanları, sahip olunan varlıkları (Değer Nesneleri) ve alan modellerini çerçeve nitelikleriyle kirletmeden karmaşık özellik dönüşümlerini destekler.
- **Sıfır Kesinti Süreli Geçiş Araçları:** EF Core geçişleri Blue/Green akışı için belirlenebilir SQL üretimi, geri alma ve tekrar çalıştırılması güvenli betikler sağlar.
- **Çok İşletmeli Güvenlik:** Veritabanıyla birleştirilmiş Küresel Sorgu Filtreleri RLS işletmeler arası veri sızıntısına karşı çift katmanlı savunma sağlar.
- **Zengin Ekosistem ve Test Konteynerleri Desteği:** Npgsql, PostgreSQL ile birinci sınıf uyumluluk 16ve entegrasyon testi için Test kapsayıcıları.

### Olumsuz Sonuçlar / Takaslar ve Azaltmalar
- **Tahsis Giderleri:** EF Core değişiklik takibi, mikro ORM'lerden daha yüksek bellek yüküne neden olur.
  *Azaltma:* Kullanım `.AsNoTracking()` tüm salt okunur sorgular için.
- **LINQ Çeviri Tuzakları:** Karmaşık çoklu birleştirme sorguları optimumun altında sonuçlar üretebilir SQL izlenmiyorsa.
  *Azaltma:* Etkinleştir `ThrowIdentityMappingWarning` / `QuerySplittingBehavior`, yavaş sorguları günlüğe kaydedin ve kontrollü hamdan yararlanın SQL profil oluşturmayla gerekçelendirildiğinde.

---

## 5. Referanslar

- [ADR-0001: Teknoloji Yığını](./0001-technology-stack.md)
- [ARCHITECTURE.md](../ARCHITECTURE.md)
- [MULTI-TENANCY.md](../MULTI-TENANCY.md)
