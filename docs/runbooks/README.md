# Operasyon Runbook'ları Dizini (`docs/runbooks/README.md`)

## 1. Genel Bakış

Runbook'lar, işletim prosedürlerini, tanılama denetim listelerini ve kurtarma adımlarını sağlar. `restaurant-order` Sahneleme ve prodüksiyon ortamlarında platform.

---

## 2. Çekirdek Runbook'lar

| Runbook'u | Amaç | Hedef Kitle |
| :--- | :--- | :--- |
| [BLUE-GREEN-RUNBOOK.md](../BLUE-GREEN-RUNBOOK.md) | Sıfır kesinti süreli üretim dağıtımı, durum kontrolleri ve trafik aktarımı. | DevOps / Sürüm Sorumluları |
| [INCIDENT-RESPONSE.md](../INCIDENT-RESPONSE.md) | Olay ciddiyet sınıflandırması, üst kademeye iletme yolları ve olay sonrası iş akışları. | Nöbetçi Mühendisler / Yöneticiler |
| [database-migrations.md](./database-migrations.md) | Sıfır kesinti süreli Genişlet–Taşı–Daralt veritabanı geçişleri ve geri alma iş akışları. | DBA / Sürüm Sorumluları |

---

## 3. Planlanan Operasyonel Runbook'lar (Aşama 1 & 2)

İlgili özellikler uygulandıkça aşağıdaki runbook'lar oluşturulacaktır:

1. **`RUNBOOK-001: Database Backup & Restore`**
   - Otomatik gecelik yedeklemeler, Belirli Bir Noktadan Kurtarma (PITR) doğrulama ve olağanüstü durum kurtarma tatbikatları.
2. **`RUNBOOK-002: Branch Thermal Printer Troubleshooting`**
   - Ağ yazıcısı bağlantısını teşhis etme, ESC/POS yuva zaman aşımları ve kağıt biriktirici kuyruğu kurtarma.
3. **`RUNBOOK-003: Tenant Onboarding & Domain Binding`**
   - Yeni restoran organizasyonlarının sağlanması, özel alt alanların oluşturulması ve ilk şubelerin yapılandırılması.
4. **`RUNBOOK-004: Secret & API Key Rotation`**
   - Sıfır kesinti süreli rotasyon JWT sırlar, veritabanı kimlik bilgileri ve ödeme ağ geçidi anahtarları.

---

## 4. Runbook Yazma Standartları

Bu dizindeki tüm runbook'ların aşağıdaki yapıya uyması gerekir:
1. **Önkoşullar ve İzinler:** Gerekli kimlik bilgileri, araçlar ve erişim düzeyleri.
2. **Adım Adım Yürütme:** Beklenen çıktılara sahip, numaralandırılmış, kopyalanıp yapıştırılabilir komutlar.
3. **Doğrulama Adımı:** Başarılı bir şekilde tamamlandığını doğrulamak için açık komutlar.
4. **Arıza ve Geri Alma Prosedürü:** Herhangi bir adım başarısız olursa anında eylem.
