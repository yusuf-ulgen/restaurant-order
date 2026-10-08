# INC-YYYYMMDD-XXX: [Olayın Kısa Başlığı] (`docs/templates/INCIDENT-TEMPLATE.md`)

- **Olay tarihi:** [YYYY-MM-DD]
- **Önem düzeyi:** `[Sev-1 | Sev-2 | Sev-3 | Sev-4]`
- **Olay sorumlusu:** [Ad]
- **Durum:** `[RESOLVED | MONITORING]`
- **Etkilenen hizmetler / arayüzler:** [Örneğin mutfak KDS, ödeme, QR menü]

## 1. Yönetici Özeti ve Etki

- **Kesinti süresi:** [X saat Y dakika]
- **Etkilenen işletmeler / şubeler:** [Liste veya sayı]
- **Finansal / operasyonel etki:** [Kaybedilen siparişler, geciken fişler, başarısız işlemler]

## 2. Zaman Çizelgesi (UTC)

- `HH:MM` — Otomatik uyarı veya kullanıcı bildirimiyle anormallik algılandı.
- `HH:MM` — Olay sorumlusu Sev-X ilan etti; müdahale ekibi toplandı.
- `HH:MM` — X hizmetinde/bileşeninde kök neden belirlendi.
- `HH:MM` — Etki azaltma adımı uygulandı (örneğin Blue/Green geri dönüşü).
- `HH:MM` — Sağlık ölçümleri dengelendi; trafiğin sağlıklı olduğu doğrulandı.
- `HH:MM` — Olay resmen çözüldü olarak işaretlendi.

## 3. Kök Neden Analizi (5 Neden)

1. **Hata neden oluştu?** [Doğrudan neden]
2. **Neden?** [Altta yatan teknik etken]
3. **Neden?** [Süreç veya mimari eksikliği]
4. **Neden?** [Test veya doğrulama eksikliği]
5. **Neden?** [Kurumsal ya da tasarımsal sistemik etken]

## 4. Olay Sonrası Değerlendirme

### 4.1. İyi Gidenler

- [Hızlı geri dönüş, açık iletişim vb.]

### 4.2. Kötü Gidenler

- [Uyarı gecikmesi, anlaşılmaz günlükler vb.]

### 4.3. Şanslı Olduğumuz Noktalar

- [Yoğun olmayan saatte gerçekleşmesi vb.]

## 5. Önleyici İşler

| İş | Tür | Sorumlu | Son tarih | Durum |
| :--- | :--- | :--- | :--- | :--- |
| Sınır durumu için otomatik entegrasyon testi ekle | Test | [Ad] | [YYYY-MM-DD] | `TODO` |
| Hazır olma kontrolü eşiğini güncelle | Yapılandırma | [Ad] | [YYYY-MM-DD] | `TODO` |
| İzleme uyarı eşiklerini iyileştir | İşletim | [Ad] | [YYYY-MM-DD] | `TODO` |
