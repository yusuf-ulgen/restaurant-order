# ADR-0007: Altyapı Durum Denetimleri ve Bağımlılık Doğrulaması (`docs/adr/0007-health-checks-and-dependency-verification.md`)

- **Durum:** `ACCEPTED`
- **Karar Verenler:** Mimarlık Ekibi, Yusuf Ülgen
- **Tarih:** 2026-09-19
- **Teknik Hikaye:** Üretim Canlılığı ve Hazırlığı Ayırma ve Bağımlılık Doğrulaması

---

## 1. Bağlam ve Sorun Açıklaması

Esnek konteynerleştirilmiş bir uygulama, süreç canlılığı (uygulama çalışıyor mu) ile trafiğe hazır olma (uygulama trafiğe hizmet edebiliyor mu) arasında net bir ayrım gerektirir.

Önceden `/health/ready`, PostgreSQL veya Redis bağlantısını doğrulamadan `Healthy` döndürüyordu. Bu, veritabanı veya önbellek kesintileri sırasında müşteri trafiğinin sağlıksız örneklere yönlendirilme riskini yarattı. Ayrıca sistem durumu yanıtları hiçbir zaman hassas bağlantı ayrıntılarını, kimlik bilgilerini veya ana bilgisayar topolojilerini sızdırmamalıdır.

---

## 2. Karar

Ayrılmış, arıza durumunda kapatılmış bir durum denetimi mimarisini benimsiyoruz:

1. **Canlılık Probu (`/health/live`):**
   - Yalnızca ASP.NET Core süreç durumunu denetler.
   - Hiçbir zaman dış altyapıya (PostgreSQL, Redis) bağımlı olmaz.
   - HTTP işlem hattı çalıştığı sürece HTTP 200 döndürür.

2. **Hazırlık Probu (`/health/ready`):**
   - PostgreSQL bağlantısını `NpgsqlDatabaseHealthCheck` ile `SELECT 1;` çalıştırarak, Redis bağlantısını `StackExchangeRedisHealthCheck` ile ping göndererek doğrular.
   - Zorlar **arızalı kapalı** davranış: Hazırlama ve Üretimde herhangi bir bağlantı hatası veya eksik yapılandırma geri döner HTTP 503 Hizmet Kullanılamıyor.
   - Sıkı veri maskelemeyi zorunlu kılar: yanıtlar genel durum göstergelerini döndürür (`Healthy` / `Unhealthy`) bağlantı dizelerinin, parolaların veya ana bilgisayar adlarının sıfır açığa çıkmasıyla.

3. **Paket Seçimi & .NET 10 Uyumluluk:**
   - Çekirdek sürücüler `Npgsql` (v9.0+) ve `StackExchange.Redis` (v2.8+) doğrudan zaman aşımlarıyla (3s) kullanılır.
   - .NET 10 sürümlerinin gerisinde kalabilecek üçüncü taraf sarmalayıcı paketlerinden kaçınılır.

---

## 3. Sonuçlar

### Olumlu
- Kubernetes/Docker ters proxy'lerinin trafiği bozuk düğümlere yönlendirmesini önler.
- Veritabanının yeniden başlatılması sırasında yüksek kullanılabilirlik (kapsayıcılar canlılık araştırmaları tarafından öldürülmez, yalnızca hazırlık araştırmaları tarafından rotasyondan çıkarılır).
- Sistem durumu yanıtlarında veya hata günlüklerinde sıfır gizli sızıntı.

### Olumsuz / Takaslar
- Hazırlık araştırmaları ağ çağrılarını yürütür (aşağıdakilerle azaltılır) üç saniyelik zaman aşımları ve hafif ping sorguları).
