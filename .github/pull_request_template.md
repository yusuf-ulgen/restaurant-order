## Açıklama

<!-- Değişikliği ve çözülen sorunu kısa ve açık biçimde yazın. -->

Closes #<!-- issue numarası -->

## Kapsam ve Devir Notu

- Tamamlanan davranış:
- Kapsam dışı işler / ertelenen kararlar:
- Kalan riskler veya engeller:
- Sonraki somut adım:
- [ ] [Güncel durum](../docs/CURRENT-STATE.md) gerçek doğrulama ve görev durumuyla yenilendi.
- [ ] Ertelenen bulgular issue'lara veya [inceleme listesine](../docs/REVIEW-BACKLOG.md) bağlandı.

## Değişiklik Türü

- [ ] `feat`: Yeni özellik veya kullanıcı yeteneği
- [ ] `fix`: Hata düzeltmesi
- [ ] `docs`: Dokümantasyon ekleme veya güncelleme
- [ ] `refactor`: Davranış değiştirmeden kod düzenleme
- [ ] `ci`: CI/CD akışı veya kalite kontrolü değişikliği
- [ ] `chore`: Bağımlılık güncellemesi veya derleme araçları

## Mimari ve Yönetişim Kontrolleri

- [ ] **Mimari karar kaydı (ADR):**
  - [ ] Bu değişiklik ADR gerektiriyor (yeni çerçeve, durum, depolama veya protokol).
  - [ ] ADR, `docs/adr/` altında `PROPOSED` veya `ACCEPTED` durumunda oluşturuldu.
  - [ ] Bu değişiklik için ADR gerekmiyor.
- [ ] **Dosya sınırları:**
  - [ ] Elle yazılan dosyalar 450 satırın altında (ve her durumda kesin sınırın altında).
  - [ ] 600 satırı aşan izinli dosyalar `scripts/file-size-allowlist.json` içinde belirtiliyor.
- [ ] **Gizli bilgi ve kişisel veri yok:**
  - [ ] API anahtarı, parola, JWT sırrı veya gerçek müşteri verisi commit'e/günlüklere eklenmedi.
- [ ] **Doğrulanmamış başarı yok:**
  - [ ] Bildirilen otomatik testler ortamda gerçekten çalıştırılıp doğrulandı.

## Test Kanıtı

<!-- Gerçek komutları, sonuçları ve çalıştırılmayan kontrolleri belirtin. -->

```bash
# Örnek: pnpm verify / dotnet test
```

## Dokümantasyon Eşzamanlılığı

- [ ] `docs/` altındaki ilgili belgeler değişiklikle birlikte güncellendi.
- [ ] Göreli Markdown bağlantıları `node scripts/check-docs.mjs` ile doğrulandı.

## Veritabanı ve Geçiş Etkisi

- [ ] **Veritabanına etkisi yok.**
- [ ] **Veritabanı değişikliği var:**
  - [ ] Geçiş, kesintisiz **Genişlet ve Daralt** yaklaşımına uyuyor (`docs/DELIVERY.md`).
  - [ ] İndeksler eşzamanlı oluşturuluyor (`CREATE INDEX CONCURRENTLY`).
  - [ ] `tenant_id` ve `branch_id` yalıtımı RLS ile uygulanıyor.
