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
- Son İnceleme Düzeltmeleri (Final Review Polish):
  - Finansal onay korumasındaki bypass tamamen kapatıldı: `clientPricingPolicyFingerprint` ve `clientExpectedTotalMinorUnits` zorunlu kılındı. Eksik/null/boş değerler `400 Bad Request` ile reddedilir ve sıfır persistence uygulanır. Politika uyuşmazlığında `409 ORDER_PRICING_POLICY_CHANGED`, toplam uyuşmazlığında `409 ORDER_TOTAL_CHANGED` döner.
  - Canlı runtime menü ve yeniden onay UX akışı: Checkout öncesi doğrulama, sessiz `.catch` kaldırılması, menü yüklenemediğinde veya doğrulanamadığında sipariş CTA'sının kilitlenmesi, çalışan retry butonu ve conflict durumunda sepet açılarak açık kullanıcı onayı alma akışı tamamlandı.
  - Frontend idempotency key üretimi `computeOrderPayloadSignature` kanonik formatıyla deterministik kılındı; aynı payload ağ tekrarlarında aynı key'i korurken, politika/toplam/seçim değişiminde yeni key üretir.
  - TypeScript typecheck ve fixture drift hataları hiçbir alan optional yapılmadan ve `any` kullanılmadan gerçek tiplerle düzeltildi.
  - Supervisor PIN entegrasyon testleri IAM gerçek şeması (`created_at_utc`, enum int), gerçek ikinci şube ve hazırlık sonrası taze ETag ile düzeltildi; `WASTE_CANCEL` ve aktör denetimleri tenant context içinde yapıldı.
  - PostgreSQL üzerinde 5 ayrı masa oturumu ile advisory lock'tan bağımsız olarak `iam.pin_credentials` satır kilidi (`FOR UPDATE`), 15 dakikalık kilit penceresi ve rollback'ten bağımsız sayaç kalıcılığı kanıtlandı.
  - `StaffOrderService` içindeki alternatif test yolu ve fallback scope factory kaldırılarak üretim DI ile zorunlu `ISupervisorPinService` bağımlılığı bağlandı.
  - `OrderSheetsBranchCoverage.test.tsx` içindeki React `act(...)` test uyarısı giderildi.
  - Müşteri fiyatlandırma politikası takibi gerçek menü ürün ekleme akışına bağlandı (`DigitalMenuView` -> `ActiveSessionView` -> `cart.addLine(line, currentFingerprint)`); 409 conflict sonrası açık yeniden onay durumu `requiresReconfirmation` ile hem hafıza hem `sessionStorage` seviyesinde zorunlu kılındı.
- Sırada: PR #19 (https://github.com/yusuf-ulgen/restaurant-order/pull/19) exact HEAD CI doğrulaması ve kullanıcı onayı sonrasında Faz 9 hazırlığı.

## Ortam ve Sınırlamalar

- .NET 10, sabit pnpm 11.10.0 ile Node, PostgreSQL 16, Redis 7; entegrasyon ve CI E2E için Docker kullanılır.
- Bu makinedeki `.local` başlatma yardımcıları ve depo dışı gizli bilgi deposu taşınabilir kurulum sözleşmesi değildir; R10 bunu izler.
- Entegrasyon kimlik testleri geliştirme JWT anahtarını bekliyor: yalnızca test sürecinde özel `JWT_SECRET` kaldırılır, yerel altyapı ayarları korunur. `DOTNET_PROCESSOR_COUNT=2`, Docker yoklamaları için eşzamanlılığı sınırlar. Üretim ayarlarını kaldırmayın; yerel sırları commit'e eklemeyin.
- Docker daemon yerelde çalışmıyorsa Testcontainers testleri `SKIP_TESTCONTAINERS=true` ile atlanabilir; bu durum CI konteyner doğrulamasının yerine geçmez.
- Mevcut uyarılar: bir eski geçiş dosyası 450 satır uyarı eşiğini, operasyon/yönetim paketleri Vite'ın 500 kB öneri eşiğini aşıyor.

## Sonraki Somut Adım

Son inceleme düzeltmelerinin mantıksal commit'lerle `feat/phase-08-order-core` dalına push edilmesi ve PR #19 üzerindeki exact HEAD GitHub Actions CI akışının yeşil olduğunun doğrulanması gerekmektedir. CI yeşil olduktan sonra kullanıcı onayı ile `main` dalına merge süreci işletilecektir (otomatik merge yapılmaz). Ardından Faz 9 (SignalR Canlı Olaylar ve Bildirimler) başlatılacaktır.
