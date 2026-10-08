# Sipariş Yönlendirme ve ESC/POS Ağ Yazdırma (`docs/ORDER-ROUTING-AND-PRINTING.md`)

## 1. İstasyon Yönlendirme Mimarisi

Hem yiyecek hem de içecekleri içeren bir sipariş verildiğinde sistem, menü öğesi kategorisi eşlemelerine dayalı olarak siparişi otomatik olarak özel istasyon hazırlık fişlerine böler:

```text
Müşteri / Garson Siparişi: 2 burger, 1 makarna, 2 kokteyl
                ↓                         ↓
         Mutfak İstasyonu             Bar İstasyonu
         2 burger, 1 makarna          2 kokteyl
                ↓                         ↓
       Mutfak KDS + ESC/POS         Bar KDS + ESC/POS
       ekranı ve yazıcısı           ekranı ve yazıcısı
```

---

## 2. ESC/POS Termal Yazdırma Protokolü

Ağ termal yazıcıları genellikle `9100` portunda ham TCP soketleri üzerinden iletişim kurar.

### 2.1. Standart ESC/POS Komut Dizileri
- **Yazıcıyı Başlat:** `ESC @` (`\x1B\x40`)
- **Metin Hizalaması:** `ESC a n` (`\x1B\x61\x00` sol, `\x01` merkez, `\x02` sağ)
- **Metin Vurgusu (Kalın):** `ESC E n` (`\x1B\x45\x01` açık, `\x00` kapalı)
- **Karakter Boyutlandırması (Çift Genişlik/Yükseklik):** `GS ! n` (`\x1D\x21\x11`)
- **Satır Besleme ve Kesme Kağıdı:** `GS V m` (`\x1D\x56\x41\x03` ile tam kesim 3-satır beslemesi)

### 2.2. Basılı Hazırlık Fişinin Yapısı
Standart bir mutfak hazırlık fişi şunları içerir:
1. **Başlık:** Şube Adı, İstasyon Adı (KITCHEN / BAR), Sipariş Numarası, Masa Numarası.
2. **Meta:** Zaman Damgası, Garson Adı, Sipariş Turu (örneğin Tur 2).
3. **Ürün Satırları:** Miktar, Öğe Adı (Kalın), Değiştiriciler girintili (örneğin *Soğan Yok*, *Orta Az Pişmiş*).
4. **Özel Notlar:** Vurgulanan kenarlıklarla basılmış Müşteri/Garson notları.
5. **Altbilgi:** Barkod / QR kodu `order_id` hızlı tarayıcı araması için.

---

## 3. Yazdırma Mimarisi Seçenekleri

| Mimari Modeli | Açıklama | Artıları | Eksileri | Durum |
| :--- | :--- | :--- | :--- | :--- |
| **Modeli 1: Doğrudan Buluttan Yazıcıya TCP** | Bulut arka ucu, yazıcının dış IP veya VPN adresine doğrudan TCP bağlantısı açar. | Yerel yazılıma gerek yok. | Statik genel IP'ler gerektirir veya VPN şube başına tüneller; güvenlik duvarı engelleri. | `[Alternative / ADR]` |
| **Modeli 2: Yerel Şube Baskı Köprüsü** | Şube içi yerel bir mini PC/Raspberry Pi üzerinde çalışan hafif arka plan programı LAN. | Güvenli, genel IP'ye gerek yok; yerel websocket komutlarını yoklar veya alır. | Her şubede yerel bir ana makine gerektirir. | `[Proposed / ADR Required]` |
| **Modeli 3: Tarayıcı Web Baskısı API** | İstemci uygulaması, tarayıcı yazdırma iletişim kutusu aracılığıyla yazdırılır. | Sıfır kurulum. | Verimsiz; mutfak yazıcılarını müşteri telefonlarından sessizce tetikleyemez. | Atıldı |

---

## 4. Yazdırma Kuyruğunun Dayanıklılığı ve Hata İşleme

1. **Engellenmeyen Yürütme:** Termal yazıcı arızaları siparişin veritabanına kaydedilmesini veya KDS fişinin gösterilmesini asla geciktirmemelidir.
2. **Yazdırma Kuyruğu Durum Makinesi:**
   - `PENDING` -> `SENDING` -> `SUCCESS`
   - Soket hatası veya zaman aşımı varsa (>3000 ms): -> `FAILED_RETRYING`
   - Üstel bekleme ile yeniden deneme (60 saniye içinde üç deneme).
   - Tüm yeniden denemeler başarısız olursa: -> `OFFLINE_ALERT`.
3. **Manuel Yeniden Yazdırma Eylemi:** Garson ve kasiyerler Masa Detay ekranındaki "Hazırlık Fişini Yeniden Yazdır" eylemiyle ESC/POS verisini yeniden kuyruğa alabilir.
