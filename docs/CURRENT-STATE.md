# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-16. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `d88471b` (PR #15 `feat/fcm-bildirim-altyapisi` birleşmesi).
- Faz 0-6 tamamlandı olarak kayıtlı; Faz 6 masalar, QR ve oturum yönetimi ile ilk FCM bildirim altyapısı `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15): FCM sunucu bildirim altyapısı ve sağlayıcıdan bağımsız taşıma sözleşmesi main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- Tamamlanan kapsam: [Faz 5 takibi](./PHASE-5-TRACKER.md), [Faz 6 takibi](./PHASE-6-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Aktif Görev — Faz 7: Müşteri Deneyimi (Geliştirme Tamamlandı — GitHub CI Doğrulaması Bekleniyor)

- Aktif dal: `feat/phase-07-customer-experience`.
- Takip belgesi: [Faz 7 Takibi](./PHASE-7-TRACKER.md).
- Tamamlanan Kapsam:
  - CUST-02 Dijital Menü ve Kategoriler (çalışan runtime menu, Türkçe normalize arama, diyet/alerjen filtreleme).
  - CUST-03 Ürün Özelleştirme (varyant seçimi, zorunlu/isteğe bağlı değiştirici kuralları, deterministik imza, saf integer minor-unit fiyat önizlemesi, müşteri notu).
  - CUST-04 Sepet ve Fiyat Önizleme (sepet store/reducer, güvenli sessionStorage taslak persistence, menü tazelik/fiyat doğrulayıcı).
  - CUST-06 Hizmet Talepleri ve Hesap İsteme (ServiceRequest aggregate, PostgreSQL RLS ve partial unique index, CSRF/Idempotency, 15s cooldown ve sliding window rate limiter, DiningSession.RequestBillAsync entegrasyonu, ServiceRequestSheet).
  - 15 adımlı Playwright E2E customer journey (`customer-journey.spec.ts`) ve negatif güvenlik senaryoları (`customer-negative.spec.ts`).
- Kapsam Sınırları ve Mimari İlkeler:
  - CUST-02, CUST-03, CUST-04 ve CUST-06 tamamlandı.
  - Gerçek sipariş aggregate'i (`Order`) kalıcılığı ve yetkili sipariş oluşturma Faz 8 (Sipariş Çekirdeği) kapsamındadır.
  - Canlı sipariş takibi (CUST-05) Faz 8/9 SignalR kapsamındadır.
  - Garson / Bar / Mutfak KDS canlı bildirimleri (SignalR/FCM) Faz 9–11 kapsamındadır (servis talepleri DB'de kalıcıdır, canlı iletim sonraki fazlarda bağlanacaktır).
  - Sahte başarılı sipariş gönderimi veya mock üretim API'si bulunmamaktadır.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

Faz 7 geliştirmesi ve yerel kapı kontrolleri tamamlandı; GitHub CI doğrulaması bekleniyor. CI başarıyla tamamlandıktan ve PR incelendikten sonra `main` dalına birleştirilerek Faz 8: Sipariş Çekirdeği (Order Aggregate, Yetkili Fiyatlandırma, Sipariş Gönderimi) geliştirmesine başlanacaktır.
