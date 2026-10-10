# ADR-0001: Çekirdek Teknoloji Yığını ve Monorepo Temeli (`docs/adr/0001-technology-stack.md`)

- **Durum:** `ACCEPTED`
- **Karar Verenler:** Mimari Ekibi, Yusuf Ülgen
- **Tarih:** 2026-09-19
- **Teknik Hikaye:** Proje Temelinin Başlatılması

---

## 1. Bağlam ve Sorun Açıklaması

 `restaurant-order` platform, çok işletmeli bir restoran yönetim sistemidir. 5 kullanıcıya bakan arayüzler (Müşteri QR Web, Garson Mobil, Mutfak/Bar KDS, Restoran Yöneticisi, Süper Yönetici), fiziksel donanım yazdırma, gerçek zamanlı sipariş yönlendirme ve finansal işlemler.

Hızlı özellik sunumunu, yoğun hizmet saatlerinde yüksek güvenilirliği, düşük operasyonel gecikmeyi ve temiz modüler sınırları desteklemek için tutarlı, yüksek performanslı ve birleşik bir teknoloji yığını gerekir.

---

## 2. Karar Etkenleri

- **Bir Saniyenin Altında Gerçek Zamanlı Gecikme:** Kat personeli ve mutfakta anında hazırlık fişi güncellemeleri KDSve misafirler.
- **Mikro Hizmet Karmaşıklığı Olmadan Katı Modülerlik:** Etki alanı sınırlarını temiz tutarken hızlı geliştirme ve tek dağıtım basitliği.
- **Tip Güvenliği ve Geliştirici Ergonomisi:** Arka uç API'leri ve ön uç istemcileri arasında uçtan uca tür güvenliği.
- **Çok İşletmeli Veri Yalıtımı:** Güçlü veritabanı düzeyinde izolasyon ve ilişkisel işlem bütünlüğü.
- **Sıfır Kesinti Süresi Güvenilirliği:** Blue/Green dağıtımlar ve ayrılabilir arka plan çalışanları için destek.

---

## 3. Karar Sonucu

**Seçilen Mimari ve Yığın:**

1. **Çalışma Alanı ve Depo:**
   - **Monorepo Mimarisi:** Tüm uygulamaları ve paketleri yöneten tek birleştirilmiş depo.
   - **Paket Yöneticisi:** `pnpm` çalışma alanlarıyla (`pnpm-workspace.yaml`) ve tek bir kök kilit dosyası (`pnpm-lock.yaml`).
2. **Ön Uç Uygulamaları:**
   - **Çerçeve ve Araçlar:** React 19, Vite, TypeScript katı modda.
   - **Form Faktörü:** Uygulama mağazası zorunluluğu olmadan mobil, tablet ve masaüstünde çalışan, PWA öncelikli duyarlı mimari.
3. **Arka Uç Mimarisi:**
   - **Platform ve Çalışma Zamanı:** .NET 10 ASP.NET Core.
   - **Mimari Stil:** Modüler Monolit (alanına dayalı tasarım, temiz dikey dilimler; mikro hizmetler bu aşama için açıkça reddedildi).
   - **API Protokol:** Versiyonu oluşturuldu REST OpenAPI / Swagger spesifikasyonlarına sahip uç noktalar.
   - **Gerçek Zamanlı Taşıma:** Redis backplane kullanan ASP.NET Core SignalR.
4. **Veri Kalıcılığı ve Koordinasyonu:**
   - **Birincil İlişkisel Veritabanı:** PostgreSQL (Satır Düzeyinde Güvenlik ile çok işletmeli izolasyon).
   - **Dağıtılmış Önbellek ve Koordinasyon:** Önbelleğe alma, dağıtılmış kilitleme ve SignalR ölçeklendirme için Redis.
   - **Arka Plan İşleme:** Özel Çalışan ana bilgisayarı (`apps/worker`), mimari olarak ayrılabilir API ev sahibi (`apps/api`).
5. **Konteynerizasyon ve Dağıtım:**
   - **Konteynerler:** Yerel geliştirme bağımlılıkları ve üretim paketleme için Docker ve Docker Compose.

---

## 4. Sonuçlar ve Ödünleşimler

### Olumlu Sonuçlar
- **Ortak Araçlar:** Tek depo, ön uç paketlerini, arka uç hizmetlerini ve test paketlerini koordine eder.
- **Yüksek Verim:** .NET 10 HTTP ve WebSocket/SignalR için yüksek performans sağlar.
- **Operasyonel Basitlik:** Modüler bir monolit, ağ bölümü arızalarını, dağıtılmış işlem yükünü ve mikro hizmetlere özgü dağıtım karmaşıklığını önler.
- **PWA Taşınabilirlik:** Uygulama mağazasında gecikmeler olmadan konuk akıllı telefonlara ve personel cihazlarına anında dağıtım.

### Olumsuz Sonuçlar / Ödünleşimler
- **Çok Dilli Monorepo:** Her ikisini de yönetmek.NET ve tek bir depodaki Node/pnpm, geliştirici makinelerde ve CI/CD'de ikili araç zinciri gerektirir.
  - *Azaltma:* Kök komut dosyalarını temizle (`pnpm verify`) ve Docker Compose ortamları her iki yığını da sorunsuz bir şekilde düzenler.

---

## 5. Doğrulama ve Test Planı

- Kök `pnpm install`, `pnpm build`, `pnpm test`ve `dotnet build`, `dotnet test` temiz bir şekilde yürütülmelidir.
- Durum denetimi uç noktaları (`/health/live`, `/health/ready`) otomatik entegrasyon testleri aracılığıyla doğrulanmıştır.
