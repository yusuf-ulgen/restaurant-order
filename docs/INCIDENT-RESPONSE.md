# Olay Müdahalesi ve Olay Sonrası İnceleme Süreci (`docs/INCIDENT-RESPONSE.md`)

## 1. Olay Sınıflandırması ve Önem Düzeyleri

| Şiddet | Tanım ve Etki | Hedef Tepki | Hedef Çözüm Süresi |
| :--- | :--- | :--- | :--- |
| **Sev-1 (Kritik)** | Tüm şubelerde toplam platform kesintisi, işletmeler arası veri sızıntısı, yaygın sipariş verme hatası veya ödeme işleminde tam kesinti. | `< 15 Minutes` | `< 2 Hours` |
| **Sev-2 (Yüksek)** | Bir şube için büyük kapasite kesintisi (örneğin KDS ekranların donması, tüm yazıcıların çevrimdışı olması veya bir konumdaki tüm kart ödemelerinin başarısız olması). | `< 30 Minutes` | `< 4 Hours` |
| **Sev-3 (Orta)** | Engellenmeyen bozulma (örneğin gecikmeli anlık bildirimler, aralıklı raporlama dışa aktarma zaman aşımı veya tek öğe 86 geçiş başarısız). | `< 4 Hours` | `< 24 Hours` |
| **Sev-4 (Küçük)** | Yönetim ayarlarında kozmetik kusur, küçük kullanıcı arayüzü stili hatası veya işlevsel olmayan yazım hatası. | `< 1 Business Day` | Sonraki Sprint |

---

## 2. Olay Müdahale Yaşam Döngüsü

```text
Tespit → Ön Değerlendirme → Etkiyi Sınırlama → Düzeltme → Olay Sonrası İnceleme
Uyarı    Önem seviyesi     Geri alma/durdur  Test/doğrula  Kişileri suçlamayan inceleme
```

### 2.1. Çağrı Sırasındaki Roller ve Sorumluluklar
- **Olay Komutanı (IC):** Müdahaleyi yönlendirir, ekip eylemlerini koordine eder ve üst kademeye iletme kararlarının sahibidir.
- **Teknik Lider / Operasyonlar:** Temel nedeni araştırır, tanılamayı çalıştırır ve düzeltme veya geri alma işlemini gerçekleştirir.
- **İletişim Lideri:** Durum sayfalarını günceller ve etkilenen restoran sahipleri/yöneticileriyle doğrudan iletişim kurar.

### 2.2. Olay Savaş Odası ve Eskalasyon
- **Sev-1** ve **Sev-2** olaylarında hemen ortak sesli görüşme/sohbet kanalı açılır.
- Eğer olaya yeni bir sürüm neden olduysa, derhal varsayılan kontrol altına alma eylemi uygulanır. **Blue/Green Geri Alma**:
  ```bash
  node scripts/blue-green/rollback.mjs --execute --confirm-rollback
  ```
- Bkz. [BLUE-GREEN-RUNBOOK.md](./BLUE-GREEN-RUNBOOK.md) tüm yürütme parametreleri ve adli saklama ayrıntıları için.

---

## 3. Suçsuz Olay Sonrası İnceleme Politikası

Her Sev-1 ve Sev-2 olay için çözümden sonraki **48 saat** içinde kişileri suçlamayan resmi olay sonrası inceleme yapılır:

1. **İnsanlara Değil Sistemlere Odaklanın:** Amaç, başarısızlığın oluşmasına hangi sistemik, mimari veya prosedürsel boşlukların izin verdiğini anlamaktır.
2. **Zaman Çizelgesi Analizi:** İlk tespitten tam kurtarmaya kadar olayların saniye saniye detaylı kronolojisi.
3. **Eylem Öğeleri:** Tekrarlanmayı önlemek için son teslim tarihlerine sahip takip edilen, atanan görevler (örneğin otomatik testler ekleme, uyarıları iyileştirme).
4. **Standart Şablon:** [Standart olay inceleme şablonunu kullanın](./templates/INCIDENT-TEMPLATE.md).
