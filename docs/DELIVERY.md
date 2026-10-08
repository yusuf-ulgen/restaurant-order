# Teslimat ve Yayın Mühendisliği (`docs/DELIVERY.md`)

## 1. Dal Stratejisi

Issue, uygulama, commit, PR açıklaması ve kalıcı devir için [katkı akışını](./CONTRIBUTING-WORKFLOW.md) izleyin. Her görevin başında [güncel durumu](./CURRENT-STATE.md) okuyup yenileyin.

Depo, sürekli entegrasyona uygun kısa ömürlü özellik dalları kullanır:

```text
feat/özellik → PR / kod incelemesi → testler ve lint → korumalı main → staging → production
```

### 1.1. Dal Adları

- `feat/<feature-slug>`: Yeni kullanıcı/platform özelliği; örneğin `feat/kds-recall-ticket`.
- `fix/<issue-slug>`: Hata düzeltmesi; örneğin `fix/printer-spooler-timeout`.
- `docs/<doc-slug>`: Yalnızca dokümantasyon; örneğin `docs/add-adr-002`.
- `refactor/<refactor-slug>`: Davranışı değiştirmeden kod düzenleme.

### 1.2. Main Koruması ve Kalite Kontrolleri

- İlk depo kurulum commit'i dışında main'e doğrudan commit yasaktır.
- Her değişiklik başarılı otomatik test ve zorunlu ekip incelemesinden sonra PR ile main'e alınır.
- Main üzerinde `git push --force` ve geçmişi yıkıcı biçimde yeniden yazmak yasaktır.
- **Zorunlu CI kontrol sırası:**
  1. Dosya sınırları: 450 uyarı / 600 kesin üst sınır (`scripts/check-file-size.mjs`).
  2. Belge ve bağlantı bütünlüğü (`scripts/check-docs.mjs`).
  3. Gizli bilgi ve kimlik bilgisi taraması (`scripts/check-secrets.mjs`).
  4. Kontrol ve Blue/Green betiklerinin birim testleri (`node --test scripts/tests/`).
  5. ESLint 9 düz yapılandırma (`pnpm lint`).
  6. Sıkı TypeScript tür kontrolü (`pnpm typecheck`).
  7. .NET mimari sınır testleri (`dotnet test tests/architecture/...`).
  8. .NET ve React birim/entegrasyon testleri.
  9. .NET Release ve Vite üretim derlemeleri.
  10. Docker Compose temel ve tüm ortam ek yapılandırmalarının doğrulanması (`docker compose -f compose.yml -f compose.dev.yml config`).
- `continue-on-error` kullanılamaz; herhangi bir kontrol hatası hattı durdurur.
- PR oluşturmadan önce geliştirici ve ajanlar `pnpm verify` çalıştırmalıdır.

## 2. Sürümleme (SemVer 2.0.0)

`MAJOR.MINOR.PATCH` uygulanır:

1. **MAJOR (`vX.0.0`):** Uyumsuz API, kırıcı şema veya arayüz değişikliği.
2. **MINOR (`vx.Y.0`):** Geriye uyumlu yeni işlev.
3. **PATCH (`vx.y.Z`):** Geriye uyumlu hata düzeltmesi, performans ve belge iyileştirmesi.

## 3. Blue/Green Yayın Aşamaları

Sağlayıcıdan bağımsız on adım:

1. **Ön kontrol:** Etkin `blue`/`green` yuvası, boş hedef ve imaj özeti eşitliğini doğrula.
2. **Yapılandırma:** Compose ek ayarlarını ve `.env.example` sözleşmesini doğrula.
3. **Veritabanı güvenliği:** Genişlet-Taşı-Daralt uyumu ve yedek hazırlığını kontrol et.
4. **Boş yuvayı başlat:** Canlı trafik etkilenmeden hedef konteynerleri aç.
5. **Sağlık:** Hedefin `/health/live` ve `/health/ready` uçlarını kontrol et.
6. **Isıtma:** Önbellek, bağlantı havuzu ve JIT için uçları çalıştır.
7. **Temel işleyiş testi:** Durum değiştirmeyen PIN, KDS ve yazdırma kuyruğu kontrolleri.
8. **Trafiği geçir:** Ters vekil/ingress havuzunu yeni yuvaya yönlendir.
9. **Gözlem:** Hata oranını (`< 0.05%`) ve worker etkinliğini izle.
10. **Eski yuvayı boşalt:** Devam eden bağlantıların bitmesini bekle ve eski yuvayı durdur.

## 4. Kesintisiz Veritabanı Geçişleri

Şema değişiklikleri **Genişlet-Taşı-Daralt (Expand-Migrate-Contract)** yaklaşımını izler:

```text
1. EXPAND: Yeni sütunu nullable ekle
2. MIGRATE DATA: Eski satırları yeni alana taşı
3. CONTRACT: Eski sütunu kullanım dışı bırak ve uygun sonraki yayında kaldır
```

### 4.1. Güvenlik Kuralları

1. Trafik geçişinden önce `DROP TABLE`, `DROP COLUMN`, `RENAME COLUMN`, `TRUNCATE` yasaktır.
2. Blue ve Green aynı anda şemayla uyumlu kalmalıdır.
3. Yeni zorunlu sütunlar güvenli varsayılan değer taşımalıdır.
4. İndeksler eşzamanlı oluşturulur (`CREATE INDEX CONCURRENTLY`).
5. Trafik geçişi başarısızsa önceki uygulama aynı veritabanıyla çalışabilmelidir.
6. Araçlar:
   - `pnpm migration:validate`: Yıkıcı DDL desenlerini denetler.
   - `pnpm migration:script`: Denetlenebilir, tekrar çalıştırılabilir SQL üretir.
   - `pnpm migration:apply:dev`: Yalnızca yerel geliştirme veritabanına uygular.
   - Üretim API'si başlangıçta otomatik geçiş yapmaz; geçişler trafik öncesi ayrı işlem hattında uygulanır.
