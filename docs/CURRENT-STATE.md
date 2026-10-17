# Güncel Durum ve Devir Notu

Güncelleme: 2026-10-17. Devam ederken Git/GitHub üzerinden yenileyin; eski faz takiplerini silmeyin veya yeniden yazmayın.

## Ürün Tabanı ve Birleşen Çalışmalar

- Main tabanı: `aaa906d` (PR #17 `feat(customer): Faz 7 musteri deneyimi (menu, sepet, servis talepleri) ve guvenlik sertlestirmeleri` birleşmesi).
- Faz 0-7 tamamlandı olarak kayıtlı; Faz 7 dijital menü, ürün özelleştirme, sepet, servis talepleri ve Redis rate limiting `main` dalına başarıyla birleştirildi.
- [Issue #8](https://github.com/yusuf-ulgen/restaurant-order/issues/8), [PR #9](https://github.com/yusuf-ulgen/restaurant-order/pull/9): negatif hesap payı düzeltmesi, RBAC örnekleri ve katkı/devir akışı main'e birleştirildi.
- [Issue #10](https://github.com/yusuf-ulgen/restaurant-order/issues/10), [PR #12](https://github.com/yusuf-ulgen/restaurant-order/pull/12): takip edilen tüm Markdown belgeleri Türkçeleştirildi; AGENTS.md içinde kalıcı Türkçe kuralı.
- [PR #13](https://github.com/yusuf-ulgen/restaurant-order/pull/13): personel/müşteri push planı ve öneri ADR'si main'e birleştirildi.
- [PR #15](https://github.com/yusuf-ulgen/restaurant-order/pull/15): FCM sunucu bildirim altyapısı ve sağlayıcıdan bağımsız taşıma sözleşmesi main'e birleştirildi.
- [PR #16](https://github.com/yusuf-ulgen/restaurant-order/pull/16): Faz 6 masa düzeni, kriptografik QR ve oturum yaşam döngüsü sertleştirmeleri main'e birleştirildi.
- [Issue #18](https://github.com/yusuf-ulgen/restaurant-order/issues/18), [PR #17](https://github.com/yusuf-ulgen/restaurant-order/pull/17): Faz 7 müşteri deneyimi, sepet taslağı, servis talepleri ve güvenlik sertleştirmeleri main'e birleştirildi.
- Tamamlanan kapsam: [Faz 6 takibi](./PHASE-6-TRACKER.md), [Faz 7 takibi](./PHASE-7-TRACKER.md). Açık öneriler: [inceleme listesi](./REVIEW-BACKLOG.md).

## Aktif Görev — Faz 8: Sipariş Çekirdeği (Tamamlandı)

- Aktif dal: `feat/phase-08-order-core`.
- GitHub Issue: [#18](https://github.com/yusuf-ulgen/restaurant-order/issues/18).
- Takip belgesi: [Faz 8 Takibi](./PHASE-8-TRACKER.md).
- Tamamlanan Kapsam:
  - **Faz 8.0:** Hazırlık, kapsam sınırları, durum makinesi hizalaması ve takip belgesi.
  - **Faz 8.1:** `Order`, `OrderItem`, `OrderItemModifier`, `OrderAuditEntry` domain aggregate'i, PostgreSQL şeması, RLS ve audit immutability trigger'ı.
  - **Faz 8.2:** Sunucu otoriter `OrderPricingEngine`, minor-unit deterministik finans motoru, aktif katalog doğrulama, Hızlı-86 ve fiyat değişikliği koruması.
  - **Faz 8.3:** `POST /api/v1/customer/orders`, `GET /api/v1/customer/orders`, `GET /api/v1/customer/orders/{orderId}`, advisory transaction lock, round number determinizmi, RFC 7807 hata yanıtları.
  - **Faz 8.4:** Müşteri sipariş onay ve gönderim deneyimi (`OrderConfirmationSheet`, `OrderSuccessSheet`, `OrderHistorySheet`), stabil idempotency key, sepet koruması ve temizliği.
  - **Faz 8.5:** Personel sipariş uç noktaları (`StaffOrderEndpoints`), RBAC matrisi (`orders.staff.create`, `orders.items.cancel_pre_prep`, `orders.items.void_in_prep`), deterministik `Order.DeriveStatus`, supervisor void ve PIN doğrulama akışı.
  - **Faz 8.6:** 15 adımlı pozitif E2E akışı (DB tek sipariş doğrulaması dahil), negatif güvenlik ve eşzamanlılık testleri, kalite kapıları (Gate 1–6) doğrulaması.
- Sırada: PR #19 oluşturulması, CI doğrulaması ve Faz 9 (Canlı Olaylar ve Bildirimler) hazırlığı.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

Faz 8 başarıyla tamamlanmıştır. Son commit'lerin `feat/phase-08-order-core` dalına push edilmesi ve `main` hedefine PR açılması gerekmektedir. PR'ın CI üzerinden yeşil geçmesinden sonra Faz 9 (SignalR Canlı Olaylar ve Bildirimler) başlatılacaktır.
