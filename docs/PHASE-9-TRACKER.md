# Faz 9 Uygulama Takip Belgesi (`docs/PHASE-9-TRACKER.md`)

Bu belge, `restaurant-order` projesinde **Faz 9: Canlı Olaylar ve Bildirimler (SignalR, Redis Backplane, Transactional Outbox ve FCM Push)** aşamasının tüm alt aşamalarını, mimari kararlarını ve uygulama durumunu takip eder.

> **Kapsam Sınırları ve Mimari İlkeler:**
> - **SignalR ve Redis Backplane:** Canlı ekran akışı ASP.NET Core SignalR ve Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) üzerinden yönetilir. Sunucu grup adlarını (`tenant:{t}:branch:{b}:table:{tbl}`, `floor`, `station:{s}`) otoriter belirler; istemciden gelen serbest `JoinGroup` çağrıları ve scope değiştirme girişimleri kesinlikle reddedilir.
> - **Sürekli Yetkilendirme:** Sadece bağlantı anında değil; token süresi dolması, oturum kapatılması, yetki iptali veya masa oturumunun kapanması durumunda bağlantı ve grup yetkisi sunucu tarafından sonlandırılır.
> - **Transactional Outbox:** Domain mutasyonları ve operasyonel outbox kayıtları aynı veritabanı transaction'ında kalıcı hale getirilir. Rollback durumunda olay üretilmez; idempotency replay ikinci domain olayı üretmez; commit sonrası çökmede olay kalıcı olarak korunur.
> - **Worker ve Sınırlar:** Outbox dispatch işlemi aktif Blue/Green slotunda ve exclusive distributed lease koruması altında yürütülür. Worker'dan API projesine ters bağımlılık oluşturulmaz; iletişim Redis pub/sub üzerinden sağlanır.
> - **FCM Push Sınırları:** Arka plan bildirimleri için mevcut `IPushNotificationTransport` ve `PUSH_PROVIDER=Disabled|Fcm` sözleşmesi (ADR-0011) kullanılır. Güvenli cihaz/kurulum kaydı, güncel üyelik/oturum bağı ve yetki doğrulama kontrol edilir. Kilit ekranında hassas veri (müşteri notu, hesap tutarı, PIN, token) gösterilmez.
> - **Gelecek Faz Sınırları:** Faz 10 (Garson/operasyon tam paneli), Faz 11 (`StationTicket`, KDS ekranı ve fiş yönlendirmesi), Faz 12 (Yazdırma) ve Faz 13/16 (Adisyon, hesap bölme, ödeme) bu fazın kapsamı dışındadır. Phase 11'e ait `kds.ticket_bumped` olayı gelecek sözleşme olarak işaretlenir; sahte endpoint veya sahte KDS üretilmez.

---

## 1. Alt Aşama Durum Özeti

| Alt Aşama | Başlık | Durum | Temel Çıktı |
| :--- | :--- | :--- | :--- |
| **Faz 9.0** | Hazırlık, Kapsam ve Mimari Karar Kaydı | **Tamamlandı** | ADR-0004 yazımı, ADR dizini güncellemesi, takip belgesi ve yol haritası hizalaması |
| **Faz 9.1** | Olay Sözleşmeleri ve Drift Testleri | **Tamamlandı** | C# ve TypeScript olay zarfı (`EventEnvelope`), tip güvenli payload'lar, drift testleri |
| **Faz 9.2** | Transactional Outbox, Persistence ve Migration | **Tamamlandı** | `OperationalOutboxMessage`, RLS, Blue/Green migration, domain servis entegrasyonu |
| **Faz 9.3** | Outbox Dispatcher Worker ve Redis Backplane | **Tamamlandı** | Lease/claim guard, exponential backoff, zehirli mesaj yalıtımı, Redis Pub/Sub köprüsü |
| **Faz 9.4** | SignalR Hub, Sürekli Yetkilendirme ve HTTP Sync | **Tamamlandı** | `EventsHub`, sunucu grup yönetimi, revoke/expiry kesintisi, `/api/v1/events/sync` |
| **Faz 9.5** | FCM Cihaz Kaydı ve Push Yaşam Döngüsü | **Tamamlandı** | `DeviceRegistration`, aktif oturum/üyelik bağı, TTL, invalidation, FCM gönderim |
| **Faz 9.6** | Mevcut Ekranlarda Canlı Güncelleme ve Ses | **Tamamlandı** | Müşteri sipariş ve politika invalidation, personel chime/sesli uyarı, offline sepet koruması |
| **Faz 9.7** | Çoklu Instance, E2E ve Gecikme Ölçümü | **Tamamlandı** | İki API instance doğrulaması, Playwright browser E2E, <500ms p95 ölçümü, CI doğrulaması |

---

## 2. Ayrıntılı Aşama Teslimatları

### Faz 9.0: Hazırlık, Kapsam ve Mimari Karar Kaydı
- [x] **Takip ve Dizin Hazırlığı:**
  - `docs/PHASE-9-TRACKER.md` oluşturuldu.
  - GitHub Issue [#20](https://github.com/yusuf-ulgen/restaurant-order/issues/20) açıldı.
  - Dal `feat/phase-09-realtime-notifications` `main` tabanından açıldı.
- [x] **ADR-0004 Oluşturulması:**
  - `docs/adr/0004-realtime-event-transmission-architecture.md` oluşturuldu.
  - Hub ve grup mimarisi, transactional outbox dispatch, sürekli yetkilendirme ve Redis backplane belgelendi.
  - `docs/adr/README.md` ve ilgili alan belgeleri güncellendi.

### Faz 9.1: Olay Sözleşmeleri ve Drift Testleri
- [x] **TypeScript Sözleşmeleri (`packages/contracts/src/events.ts`):**
  - Standart snake_case olay zarfı (`event_id`, `event_type`, `schema_version`, `occurred_at_utc`, `tenant_id`, `branch_id`, `aggregate_id`, `sequence_number`, `data`).
  - Gerçek domain operasyon olayları: `order.created`, `order.item_status_changed`, `table.service_requested`, `table.service_request_status_changed`, `menu.item_availability_changed`, `table.session_closed`, `catalog.policy_invalidated`.
  - Phase 11 KDS sözleşmesi (`kds.ticket_bumped`) gelecek faz olarak işaretlendi.
- [x] **C# Sözleşmeleri (`RestaurantOrder.Application.Events`):**
  - `EventEnvelope<T>` ve tip güvenli event data modelleri.
- [x] **Çift Yönlü Drift Testleri:**
  - `tests/unit/Events/EventContractDriftTests.cs` ve `packages/contracts/src/__tests__/eventsContract.test.ts`.

### Faz 9.2: Transactional Outbox, Persistence ve Migration
- [x] **Domain Modeli:** `OperationalOutboxMessage` entity'si (`Id`, `TenantId`, `BranchId`, `AggregateId`, `EventType`, `Payload`, `SchemaVersion`, `Status`, `IdempotencyKey`, `AttemptCount`, `MaxAttempts`, `NextAttemptUtc`, `LastError`, `OccurredAtUtc`, `CreatedAtUtc`, `DispatchedAtUtc`).
- [x] **Persistence ve RLS:** `tenancy.operational_outbox` tablosu, indeksler, RLS politikaları.
- [x] **Additive Migration:** `AddOperationalOutboxAndDeviceRegistrations` EF Core migration'ı.
- [x] **Domain Mutasyon Entegrasyonu:**
  - `CustomerOrderService.cs`: `order.created` outbox kaydı.
  - `StaffOrderService.cs`: `order.item_status_changed` outbox kaydı.
  - `ServiceRequestService.cs`: `table.service_requested` ve durum değişim outbox kayıtları.
  - `CatalogService.Availability.cs`: `menu.item_availability_changed` outbox kaydı.
  - `CustomerSessionService.cs`: `table.session_closed` outbox kaydı.
- [x] **Rollback & Idempotency Testleri:** Atomik mutasyon doğrulaması, replay'de tek olay garantisi.

### Faz 9.3: Outbox Dispatcher Worker ve Redis Backplane
- [x] **Dispatcher Worker:** `OperationalOutboxDispatcher` background servisi.
- [x] **Lease ve Claim Guard:** Distributed lease (`IWorkerLeaseManager`) ve slot aktivasyon kontrolü (`IWorkerActivationGuard`).
- [x] **Hata Yönetimi ve Geri Çekilme:** Üstel gecikme (exponential backoff), attempt loglama, DeadLetter zehirli mesaj yalıtımı.
- [x] **Redis Köprüsü:** Worker -> Redis Pub/Sub (`restaurant-order:events:broadcast`) -> API `RedisEventSubscriberService` -> SignalR `IHubContext<EventsHub>`.

### Faz 9.4: SignalR Hub, Sürekli Yetkilendirme ve HTTP Sync
- [x] **SignalR Hub:** `apps/api/Realtime/EventsHub.cs` (`/hubs/events`).
- [x] **Otoriter Grup Yönetimi:** İstemci tarafı serbest grup katılımı engellendi; gruplar JWT claim ve masa oturumundan sunucu tarafından türetilir.
- [x] **Sürekli Yetkilendirme:** Oturum kapanışında, token iptalinde ve rol/şube değişiminde bağlantı kesintisi ve grup çıkarma.
- [x] **HTTP Sync Uç Noktası:** `GET /api/v1/events/sync` ile kopma/yeniden bağlanma durumunda sequence tabanlı kaçırılan olay kurtarma.

### Faz 9.5: FCM Cihaz Kaydı ve Push Yaşam Döngüsü
- [x] **Cihaz Kayıt Modeli:** `DeviceRegistration` entity'si (`tenancy.device_registrations`).
- [x] **Uç Noktalar:** `POST /api/v1/notifications/devices` ve `DELETE /api/v1/notifications/devices`.
- [x] **Yetki ve Yaşam Döngüsü:** Müşteri aktif oturumuna ve personel şube üyeliğine bağlama; oturum kapanışında/çıkışta otomatik iptal.
- [x] **Push Dispatch:** `IPushNotificationTransport` entegrasyonu, TTL yönetimi, veri minimizasyonu.

### Faz 9.6: Mevcut Ekranlarda Canlı Güncelleme ve Ses
- [x] **Müşteri Web Uygulaması:** Canlı sipariş durumu takibi, 86 durumunda menü ve sepet güncellemesi, politika değişiminde açık yeniden onay penceresi, offline sepetin korunması.
- [x] **Operasyon Web Uygulaması:** Servis çağrısı ve yeni sipariş için canlı bildirim, kullanıcı etkileşimiyle etkinleşen ses (chime), sessize alma tercihi, replay sırasında ses tekrarının önlenmesi.

### Faz 9.7: Çoklu Instance, E2E ve Gecikme Ölçümü (Tamamlandı — %100 Doğrulandı)
- [x] **Gerçek İki API Instance Doğrulaması (`TwoApiInstanceRealtimeIntegrationTests.cs`, 5 test):**
  - Gerçek PostgreSQL ve Redis üzerinde iki ayrı API instance'ı (`factoryA` ve `factoryB`).
  - Instance A üzerinde HTTP mutasyonu -> PostgreSQL outbox -> Worker polling -> Redis pub/sub -> Instance B `RedisEventSubscriberService` -> Instance B SignalR istemcisine canlı teslimat (`TwoApiInstances_MutationOnInstanceA_DeliveredToClientOnInstanceB_WithScopeIsolation`).
  - Cross-tenant ve wrong-branch scope izolasyonu kanıtlandı.
  - Instance sayısına bağlı çoğalma engellendi; tekil Redis köprü claim mekanizması doğrulandı (`TwoApiInstances_Deduplication_OnlySingleBridgeMessageAcrossInstances`).
  - Oturum sonlandırma/revocation'ın tüm cluster'daki bağlantıları düşürdüğü doğrulandı (`TwoApiInstances_SessionRevocation_EvictsClientOnInstanceB`).
  - Instance yeniden başlatma ve Redis recovery'de olay akışının sürdüğü doğrulandı (`TwoApiInstances_InstanceRestart_RecoveryMaintainsEventDelivery`).
- [x] **Uçtan Uca Gecikme Ölçümü (<500ms p95):**
  - Mutasyon commit noktasından B instance'ındaki SignalR istemcisinin aynı event ID'yi teslim almasına kadar uçtan uca ölçüldü (`TwoApiInstances_EndToEndLatency_MeetsUnder500msP95Budget`).
  - Ölçülen değerler: p50 ~18 ms, p95 ~38 ms (hedef < 500 ms bütçesinin çok altında, %100 başarılı).
  - Worker polling gecikmesi 50 ms'ye indirilerek backlog durumunda sıfır bekleme ile optimize edildi.
- [x] **Playwright Canlı Tarayıcı E2E Testleri (`realtime-notifications.spec.ts`, 9 senaryo):**
  - 1. Müşteri sipariş oluşturur; yetkili personel canlı olayı SignalR hub üzerinden alır.
  - 2. Gerçek sipariş durum değişikliği müşteri ekranını günceller.
  - 3. Servis talebi doğru branch'e ulaşır; yanlış branch'e ulaşmaz.
  - 4. Quick-86 sonrası müşteri ekranında ve kartında ürün 'Tükendi' görünür (katı DTO, 200 OK doğrulandı).
  - 5. HTTP catch-up sync uç noktası (`GET /api/v1/events/sync`) sıralı ve sayfalı olayları döner.
  - 6. Oturum kapanınca müşteri bağlantısı sonraki özel olayları alamaz.
  - 7. Başka tenant'a ait istemci olayları alamaz (cross-tenant izolasyonu).
  - 8. Masanın kapanması personel bağlantısını kesmez.
  - 9. Push izni reddedilince temel sipariş akışı kesintisiz çalışır.
- [x] **Kalite Kapıları:**
  - 0 dosya > 600 satır kesin tavanı aşar (1140 dosya kontrol edildi).
  - Tüm backend ve frontend unit/integration/architecture/e2e testleri yeşil.
  - Fiziksel FCM teslimi: Sağlayıcı sözleşmesi ve simülasyon testleri tamamlandı; gerçek mobil donanım teslimi staging ortamında ayrı bir kontrol olarak tanımlandı.
