# Ödemeler, Bahşişler ve Platform Komisyonları (`docs/PAYMENTS-TIPS-COMMISSIONS.md`)

## 1. Genel Bakış

Hesap alt sistemi birden fazla ödeme yöntemi, esnek hesap paylaşımı, isteğe bağlı bahşiş ve platform komisyonlarını kapsar.

## 2. Ödeme Yöntemleri

| Yöntem | Tür | Açıklama | Durum |
| :--- | :--- | :--- | :--- |
| Nakit | Fiziksel | `billing.payment.cash` yetkili roller tahsil eder; kasa çekmecesi muhasebesi tutulur. Garsonda bu yetki yoktur. | Planlandı |
| Harici POS terminali | Fiziksel kart | Yetkili personel ayrı banka terminalinin ödemesini kaydeder; garson `billing.payment.pos_card` ile yalnızca kendi kapsamındadır. | Planlandı |
| Entegre dijital ağ geçidi | Çevrimiçi kart | Müşteri telefonundan Stripe/Iyzico gibi sağlayıcıyla öder. | `[Proposed / ADR Required]` |
| Oda / cari hesap | Hesaba borç | Müşteri cari hesabına veya otel odası hesabına kaydedilir. | `[Proposed / ADR Required]` |

## 3. Hesap Paylaşımı

120.00 tutarındaki hesap; 40.00 nakit, 40.00 POS, 40.00 POS şeklinde ödenebilir. Kalan borç sırasıyla 80.00, 40.00 ve 0.00 olur; son adımda hesap kapanır.

### 3.1. Paylaşım Yöntemleri

1. **Tutara göre:** Müşteri kalan borca karşı ödeyeceği tutarı seçer.
2. **Eşit paylaşım:** Toplam borç N kişiye bölünür. Mevcut iki ondalıklı yardımcı yalnızca tam kuruş/sent tutarlarını kabul eder. Taban pay aşağı yuvarlanır; kalan kuruşlar sondan başlayarak kişi başına en fazla bir kuruş dağıtılır. `100.01 / 3` sonucu `33.33, 33.34, 33.34`; `0.02 / 4` sonucu `0.00, 0.00, 0.01, 0.01` olur. Paylar negatif olamaz, toplamları hesaba eşit olmalı ve aralarındaki fark en fazla bir kuruş olmalıdır. Sıfır pay bir dağılım sonucudur; sıfır tutarlı ödeme işlemi değildir. Genel para birimi desteği [inceleme listesinde](./REVIEW-BACKLOG.md) izlenir.
3. **Ürüne göre:** Belirli sipariş kalemleri ödenir; kalan kalemler açık kalır.

## 4. Bahşiş Mimarisi

### 4.1. Toplama ve Dağıtma

- **Yüzde seçenekleri:** QR ve mobil ödeme ekranları %5, %10, %15, %20 ve özel bahşiş alanı sunar.
- **Doğrudan garsona:** Bahşiş masanın birincil garsonuna yazılır.
- **Ortak havuz (Tronc):** Vardiya bahşişleri birleştirilir; mutfak, bar ve salon personeline çalışılan saate göre dağıtılır.
- **Raporlama:** Günlük Z raporları ve vardiya özetleri satış ile bahşişi ayırarak bordro/vergi muhasebesini kolaylaştırır.

## 5. Platform Komisyonu

Dijital ağ geçidinden geçen ödemelerde:

```text
Platform ücreti = (İşlem tutarı × Komisyon oranı %) + Sipariş başına sabit ücret
```

### 5.1. Muhasebe ve Mutabakat

- Kesinleşen dijital ödeme için mevcut taslak iki alacak kaydı tanımlar:
  - `credit`: İşletmeye ödenecek hesap; net = brüt - komisyon.
  - `credit`: Platform gelir hesabı; komisyon tutarı.
- Komisyonlar abonelik anlaşmasına göre haftalık veya aylık olarak mutabakata bağlanır.
- Bu metin muhasebe alt sisteminin uygulanmış olduğunu göstermez; defterin karşı borç kaydı ve iade modeli ödeme fazında kesinleştirilmelidir.

## 6. İade ve İptal Yönetimi

- İade için yönetici PIN doğrulaması gerekir; garson iade yapamaz.
- İade ödemenin tamamına veya belirli bir sipariş kalemine uygulanabilir; tutar asıl işlem toplamını aşamaz.
- Her iptal/iade değiştirilemez denetim kaydı üretir:

```text
{ timestamp, tenant_id, branch_id, bill_id, supervisor_id, reason, original_amount, refund_amount }
```
