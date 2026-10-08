# Dokümantasyon Dizini

Bu dizin `restaurant-order` ürün kapsamını, mimari kararlarını, iş kurallarını ve operasyon adımlarını içerir. Önce [AGENTS.md](../AGENTS.md), [katkı iş akışı](./CONTRIBUTING-WORKFLOW.md) ve [güncel durum](./CURRENT-STATE.md) okunur.

## Ürün ve Alan

- [PRODUCT.md](./PRODUCT.md): Ürün vizyonu ve beş kullanıcı arayüzü.
- [DOMAIN.md](./DOMAIN.md): Varlıklar, ilişkiler ve alan sınırları.
- [GLOSSARY.md](./GLOSSARY.md): Türkçe açıklamalı ortak terminoloji; İngilizce teknik karşılıklar.
- [SCREEN-INVENTORY.md](./SCREEN-INVENTORY.md): Ekranlar, roller ve eylemler.

## Mimari ve İş Kuralları

- [ARCHITECTURE.md](./ARCHITECTURE.md): Mimari ve kabul edilmiş teknoloji kararları.
- [DESIGN-SYSTEM.md](./DESIGN-SYSTEM.md): Tasarım değişkenleri, erişilebilir bileşenler ve uygulama kabukları.
- [REPOSITORY-STRUCTURE.md](./REPOSITORY-STRUCTURE.md): Depo düzeni, modül sınırları ve dosya boyutu kuralları.
- [ROLES-AND-PERMISSIONS.md](./ROLES-AND-PERMISSIONS.md): Sekiz rol için izin matrisi.
- [STATE-MACHINES.md](./STATE-MACHINES.md): Masa, sipariş, hazırlık fişi ve ödeme durumları.
- [NEGATIVE-FLOWS.md](./NEGATIVE-FLOWS.md): Hatalar, zaman aşımı ve eşzamanlılık senaryoları.
- [MULTI-TENANCY.md](./MULTI-TENANCY.md): İşletmeler arasında veri ve erişim yalıtımı.
- [PAYMENTS-TIPS-COMMISSIONS.md](./PAYMENTS-TIPS-COMMISSIONS.md): Ödeme, hesap bölme, bahşiş ve komisyon.
- [ORDER-ROUTING-AND-PRINTING.md](./ORDER-ROUTING-AND-PRINTING.md): İstasyon yönlendirme ve ESC/POS yazdırma.
- [REALTIME-AND-NOTIFICATIONS.md](./REALTIME-AND-NOTIFICATIONS.md): Canlı olaylar, push bildirimleri ve sesli uyarılar.
- [NOTIFICATIONS-PLAN.md](./NOTIFICATIONS-PLAN.md): Personel ve müşteri push kapsamı; FCM uygulama aşamaları.
- [FCM-SETUP.md](./FCM-SETUP.md): İlk sunucu uyarlayıcısının yapılandırması, testleri ve kalan bağımlılıklar.

## Kalite, Operasyon ve Planlama

- [CONTRIBUTING-WORKFLOW.md](./CONTRIBUTING-WORKFLOW.md): Issue, dal, commit, doğrulama, PR ve devir akışı.
- [CURRENT-STATE.md](./CURRENT-STATE.md): Etkin görev, doğrulama kanıtları, sınırlar ve sonraki adım.
- [REVIEW-BACKLOG.md](./REVIEW-BACKLOG.md): İnceleme bulguları ve gelecekte değerlendirilecek öneriler.
- [TESTING.md](./TESTING.md): Test stratejisi ve doğrulanmamış başarı bildirimi yasağı.
- [SECURITY.md](./SECURITY.md): Güvenlik ve gizli değer yönetimi.
- [ENVIRONMENTS.md](./ENVIRONMENTS.md): Ortamlar ve yapılandırma sözleşmesi.
- [DELIVERY.md](./DELIVERY.md): Sürüm, CI ve veritabanı geçişi kuralları.
- [BLUE-GREEN-RUNBOOK.md](./BLUE-GREEN-RUNBOOK.md): Üretim dağıtımı ve geri alma adımları.
- [INCIDENT-RESPONSE.md](./INCIDENT-RESPONSE.md): Olay seviyeleri, müdahale ve olay sonrası inceleme.
- [ROADMAP.md](./ROADMAP.md): Geliştirme aşamaları.
- [FOUNDATION-VALIDATION.md](./FOUNDATION-VALIDATION.md): Temel doğrulama matrisi ve tarihsel kanıtlar.
- Faz izleme kayıtları: [Faz 3](./PHASE-3-TRACKER.md), [Faz 4](./PHASE-4-TRACKER.md), [Faz 5](./PHASE-5-TRACKER.md).

## Karar Kayıtları ve Şablonlar

- [ADR dizini](./adr/README.md) ve [operasyon rehberleri](./runbooks/README.md).
- [Görev](./templates/TASK-TEMPLATE.md), [ADR](./templates/ADR-TEMPLATE.md), [özellik](./templates/FEATURE-TEMPLATE.md) ve [olay](./templates/INCIDENT-TEMPLATE.md) şablonları.

## Belge Kuralları

1. Belgeler ve yapay zekâ tarafından üretilen açıklamalar Türkçedir; teknik tanımlayıcılar korunur.
2. Davranış ve sözleşme değişikliklerinin belgeleri aynı commit/PR içinde güncellenir.
3. Dosya başına 450 satır uyarı, 600 satır üst sınırdır.
4. Onaylanmamış mimari tercihler `[Proposed / ADR Required]` veya `[TBD]` olarak işaretlenir; kabul edilmiş ADR'ler geçerlidir.
5. İç bağlantılar göreli Markdown bağlantıları kullanır ve doğrulanır.
6. Tarihsel test sayıları güncel çalıştırma sonucu gibi sunulmaz.
