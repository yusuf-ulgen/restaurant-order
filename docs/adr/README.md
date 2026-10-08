# Mimari Karar Kayıtları (ADR) Dizin (`docs/adr/README.md`)

## 1. Genel Bakış ve Amaç

Mimari Karar Kayıtları (ADR'ler), mimaride yapılan önemli teknik ve mimari seçimleri kaydeder. `restaurant-order`bağlamları, değiş tokuşları ve sonuçlarıyla birlikte.

Kararlar `PROPOSED` durumunda başlar. `ACCEPTED` olarak işaretlenmeden önce kullanıcı veya ekibin onayı gerekir.

---

## 2. ADR Yaşam Döngüsü

```
[ PROPOSED ] ──► [ ACCEPTED ] ──► [ SUPERSEDED (by ADR-XXX) ]
     │
     └──► [ REJECTED ]
```

- **PROPOSED:** Göz önünde bulundurulmakta ve incelenmekte olan bir mimari değişiklik.
- **ACCEPTED:** Karar, mevcut tüm uygulamalar için onaylanmış ve bağlayıcıdır.
- **SUPERSEDED:** Daha önceki bir kararın yerini daha yeni bir karar aldı ADR.
- **REJECTED:** Önerilen bir karar değerlendirildi ve aleyhine karar verildi.

---

## 3. Karar Kayıtları

| ADR kimlik | Başlık | Durum | Birincil Odak |
| :--- | :--- | :--- | :--- |
| [ADR-0001](./0001-technology-stack.md) | Çekirdek Teknoloji Yığını ve Monorepo Temeli | `ACCEPTED` | React 19, .NET 10, Modüler Monolit, SignalR, Redis, Postgres |
| [ADR-0002](./0002-persistence-selection.md) | Veri Kalıcılığı Kitaplığı Seçimi | `ACCEPTED` | EF Core, Dapper ve Marten'in karşılaştırmalı değerlendirmesi |
| `ADR-0003` | Çok İşletmeli Veri Yalıtım Stratejisi | `PROPOSED` | PostgreSQL Satır Düzeyinde Güvenlik ve İşletme Başına Şema Karşılaştırması |
| `ADR-0004` | Gerçek Zamanlı Olay Aktarım Mimarisi | `PROPOSED` | SignalR Hub'ları ve Redis Arka Panel Tasarımı |
| `ADR-0005` | ESC/POS Termal Baskı Entegrasyon Modeli | `PROPOSED` | Yerel Şube Yazdırma Aracısı ve Cloud Direct Soketi Karşılaştırması |
| `ADR-0006` | Dijital Ödeme Ağ Geçidi Entegrasyon Stratejisi | `PROPOSED` | Çoklu ağ geçidi soyutlama katmanı |
| [ADR-0007](./0007-health-checks-and-dependency-verification.md) | Altyapı Durum Denetimleri ve Bağımlılık Doğrulaması | `ACCEPTED` | Arıza kapalı canlılık/hazırlık ayrımı, sıfır sızıntılı durum kontrolleri |
| [ADR-0008](./0008-blue-green-compose-project-isolation-and-container-dns-ingress.md) | Blue/Green Compose Projesi İzolasyon ve Konteyner DNS Giriş Yönlendirme | `ACCEPTED` | Ayrı `-p` yuva başına proje, paylaşılan harici ağ, konteyner içinden trafik geçişi |
| [ADR-0009](./0009-authentication-and-session-strategy.md) | Kimlik Doğrulama, Oturum ve Çok İşletmeli Yetkilendirme Stratejisi | `ACCEPTED` | Personel ve Müşteri QR ayrımı, güvenilir terminaller, JWT/çerez rotasyonu, RBAC matris |
| [ADR-0010](./0010-least-privilege-iam-login-and-security-definer.md) | En Az Ayrıcalık IAM Giriş Arama ve SECURITY DEFINER Tehdit Modeli | `ACCEPTED` | SECURITY DEFINER işlev, sabitlenmiş search_path, çalışma zamanı SELECT yasağı |

| [ADR-0011](./0011-background-push-provider.md) | Personel ve müşteri için arka plan push sağlayıcısı | `ACCEPTED` | Kullanıcı FCM ile devam etmeyi onayladı; SignalR + Redis korunur |

---

## 4. Yeni ADR Oluşturma

Yeni bir mimari karar önermek için:
1. Şablonu [ADR şablonunu kopyalayın](../templates/ADR-TEMPLATE.md).
2. Farklı kaydet `docs/adr/ADR-XXX-<decision-title>.md`.
3. Başlangıç durumunu şu şekilde ayarla: `PROPOSED` ve PR yoluyla gönderin.

ADR-0004 ayrıntılı hub/grup tasarımı için öneridir; SignalR + Redis taşıma kararı ADR-0001 ile zaten kabul edilmiştir.
