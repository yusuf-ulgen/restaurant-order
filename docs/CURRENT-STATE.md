# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-20. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `d4a1255` (PR #19 birleşmesi ve Faz 8 kapanış kaydı).
- Faz 0-8 tamamlandı olarak kayıtlı; Faz 8 sipariş çekirdeği, otoriter fiyatlandırma motoru, müşteri sipariş arayüzü, personel sipariş altyapısı ve yaşam döngüsü `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15): FCM sunucu bildirim altyapısı ve sağlayıcıdan bağımsız taşıma sözleşmesi main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- [PR #17](https://github.com/yusuf-ulgen/restaurant-order/pull/17): Faz 7 müşteri deneyimi, sepet taslağı, servis talepleri ve güvenlik sertleştirmeleri main'e birleştirildi.
- [Issue #18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), [PR #19](https://github.com/yusuf-ulgen/restaurant-order/pull/19): Faz 8 sipariş çekirdeği, yetkili fiyatlandırma ve yaşam döngüsü altyapısı main'e birleştirildi.
- Tamamlanan kapsam: [Faz 6 takibi](./PHASE-6-TRACKER.md), [Faz 7 takibi](./PHASE-7-TRACKER.md), [Faz 8 takibi](./PHASE-8-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Tamamlanan Faz — Faz 8: Sipariş Çekirdeği

- Durum: `main` dalına birleştirildi (`cfb27e6`), PR #19 kapandı.
- GitHub Issue: [#18](https://github.com/yusuf-ulgen/restaurant-order/issues/18) (Kapatıldı).
- Takip belgesi: [Faz 8 Takibi](./PHASE-8-TRACKER.md).
- Özet:
  - Domain aggregate yapısı (`Order`, `OrderItem`, `OrderItemModifier`, `OrderAuditEntry`), PostgreSQL şeması ve RLS.
  - Sunucu tarafı otoriter `OrderPricingEngine`, minor-unit deterministik finans motoru, aktif katalog doğrulaması.
  - Müşteri sipariş onay ve gönderim deneyimi (`OrderConfirmationSheet`, `OrderSuccessSheet`, `OrderHistorySheet`), stabil idempotency key ve sepet politikası yeniden onay akışı.
  - Personel sipariş altyapısı (`StaffOrderEndpoints`), RBAC yetkilendirmesi, supervisor void ve PIN doğrulama akışı.
  - 15 adımlı tam E2E ve negatif testler, CI Quality & Verification Pipeline yeşil.

## Aktif Görev — Faz 8 İnceleme Düzeltmeleri

- [Issue #22](https://github.com/yusuf-ulgen/restaurant-order/issues/22), dal `fix/order-review-regressions`; taban main `d4a1255`.
- Kullanıcı 2026-10-20 tarihinde inceleme, bulguların giderilmesi ve başarılı kontrollerden sonra birleştirmeyle devam edilmesini onayladı. PR #19 inceleme sürerken başka bir işlemle main'e alınmıştı; takip düzeltmeleri ayrı dalda tutulur.
- Üç bulgu testlerle yeniden üretildi: oturum kapanışı/hesap isteme ile sipariş yarışı; kısmi hazırlıkta siparişin erken hazır sayılması; yanıt kaybından sonra yenilemede değişen sipariş anahtarı. Düzeltmeler ve kanıtlar: [Faz 8 incelemesi](./PHASE-8-REVIEW.md).
- Eski kodda 2 alan + 6 gerçek PostgreSQL + 1 yenileme regresyonu başarısız oldu. Son hedefli backend çalışmasında 155 birim ve 6 PostgreSQL testi geçti. Tam doğrulama ve güncel CI kanıtı takip PR'ında tutulur; güncel başlıkla eşleştiğini kontrol edin.

## Sıradaki Görev — Faz 9: SignalR Canlı Olaylar ve Bildirimler

- İlgili bağlayıcı belge: [docs/REALTIME-AND-NOTIFICATIONS.md](./REALTIME-AND-NOTIFICATIONS.md).
- Mevcut takip görevi [#20](https://github.com/yusuf-ulgen/restaurant-order/issues/20); aynı kapsam için açılan kopya #21 kapatıldı. Yeni bir Faz 9 issue'su açmayın.
- Kapsam: Masa oturumu, sipariş durumu güncellemeleri, garson çağrısı ve mutfak/bar KDS bildirimleri için SignalR hub altyapısı, Redis backplane ve FCM entegrasyonu.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.
- Bu makinede pnpm yardımcı yolu depo dışındaki sabit 11.10.0 kurulumuyla onarıldı. Yerel geçişler MIGRATION_DATABASE_URL ile ayrı geçiş rolünde uygulandı; runtime rolüne DDL yetkisi verilmedi.
- Firebase geliştirme projesi `restaurant-order-development` hazırdır; kullanıcı şimdilik yalnızca proje kurulumunu seçti. Web Push anahtarı, sunucu kimlik bilgisi ve gerçek cihaz testi yoktur; push kapalı kalır.

## Sonraki Somut Adım

Önce #22 takip PR'ının birleşme durumunu GitHub üzerinden doğrula. Birleşmemişse güncel başlığın kontrollerini tamamlayıp verilen kullanıcı onayı kapsamında birleştirme sürecini bitir. Birleştiyse mevcut #20 üzerinden Faz 9 çalışmasına geç; başka bir çalışma bu fazı başlatmışsa mevcut dal/PR'ı kontrol ederek çakışan iş açma. Firebase anahtarı oluşturma veya üretime yayın bu çalışmanın kapsamı değildir.
