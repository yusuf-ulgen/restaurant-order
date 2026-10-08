# FEAT-XXX: [Özellik Adı] (`docs/templates/FEATURE-TEMPLATE.md`)

- **Durum:** `[DRAFT | IN_REVIEW | APPROVED | IN_DEV | VERIFIED | RELEASED]`
- **Yazar:** [Ad / AI ajanı]
- **Hedef arayüzler:** `[QR Müşteri | Garson | Mutfak/Bar KDS | Restoran Yönetimi | Süper Yönetim]`
- **Hedef roller:** `[Süper Yönetici | Restoran Admini | Şube Müdürü | Operasyon/Kasa | Mutfak | Bar | Garson | Müşteri]`

## 1. Özet ve Kullanıcıya Değer

[Özelliği ve restoran işletmesi veya müşteri için çözdüğü sorunu kısaca açıklayın.]

## 2. Kullanıcı Hikâyeleri ve Kabul Ölçütleri

### Kullanıcı Hikâyesi 1: [Başlık]

> Bir `[rol]` olarak `[fayda]` için `[eylemi]` gerçekleştirmek istiyorum.

**Kabul ölçütleri:**

- [ ] Ölçüt 1: Başarılı akış davranışı.
- [ ] Ölçüt 2: Hata yönetimi / başarısız akış.
- [ ] Ölçüt 3: RBAC yetkilerinin uygulanması.

## 3. Alan Modeli ve Şema Değişiklikleri

- **Yeni / değişen varlıklar:** [`docs/DOMAIN.md` üzerindeki etkilenen varlıklar]
- **Veritabanı geçişleri:** [Genişlet ve daralt yaklaşımıyla şema değişiklikleri]
- **Çok işletmeli yapı:** [`tenant_id`, `branch_id` indeksleri ve RLS politikasını doğrulayın]

## 4. Durum Makinesi ve Etkileşim Etkileri

- [`docs/STATE-MACHINES.md` üzerindeki etkilenen makineler]
- [Yeni durumlar, geçişler veya koruma koşulları]

## 5. Hata Akışları ve Sınır Durumları

- [Sınır durumu 1: Örneğin eşzamanlı değişiklikler]
- [Sınır durumu 2: Donanım veya ağ bağlantısının kopması]
- [Kullanıcıya gösterilen hata mesajları ve kurtarma adımları]

## 6. Test ve Doğrulama Planı

- [ ] **Birim testleri:** [Alan mantığı, hesaplamalar, durum geçişleri]
- [ ] **Entegrasyon testleri:** [API uçları, sorgular, RLS yalıtımı]
- [ ] **Uçtan uca / elle doğrulama:** [Arayüzler arası iş akışları]

> **Hatırlatma:** [AGENTS.md](../../AGENTS.md) gereği otomatik testler çalıştırılıp doğrulanmadan başarı bildirilemez.
