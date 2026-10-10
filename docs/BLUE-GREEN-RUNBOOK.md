# Blue/Green Dağıtım Runbook'u (`docs/BLUE-GREEN-RUNBOOK.md`)

## 1. Genel Bakış ve Mimari

Yoğun servis saatlerinde %99,99 kullanılabilirlik hedefi için üretim dağıtımları iki eşdeğer yuvadan oluşan **Blue/Green Dağıtım** modelini kullanır:

```text
Nginx Ingress / Ters Proxy
  ├─ Canlı trafiğin %100'ü → Aktif yuva (Blue veya Green)
  └─ Dahili temel testler  → Aday yuva (Green veya Blue)
Her iki yuvada konteyner içi portlar: API 5000, Web 8080.
Bağlantı Docker DNS ile restaurant-order-*-blue / *-green adlarına yapılır.
```

Her iki yuva **değişmez imaj özetleriyle** çalışır. Ön kontrolde beş bileşenin özeti ayrı doğrulanır:
- `API_IMAGE_DIGEST`
- `WORKER_IMAGE_DIGEST`
- `CUSTOMER_WEB_IMAGE_DIGEST`
- `OPERATIONS_WEB_IMAGE_DIGEST`
- `ADMIN_WEB_IMAGE_DIGEST`

Eş zamanlı Blue/Green yürütme sırasında Docker Compose konteyner veya ağ çakışmalarını önlemek için bağımsız Compose proje adları sıkı bir şekilde uygulanır:
- Mavi Proje: `restaurant-order-blue`
- Yeşil Proje: `restaurant-order-green`
- Giriş ağı: `restaurant_order_ingress`, konteynerlere DNS üzerinden yönlendirme sağlayan köprü ağıdır.

---

## 2. Worker Güvenliği ve Merkezi Durum Yönetimi

Blue ve Green konteynerleri aynı anda çalışırken arka plan işlerinin yinelenmesini önlemek için şu denetimler uygulanır:

1. **Redis'teki Merkezi Yetkili Durum:**
   - Merkezi Redis anahtarı `restaurant-order:active-slot` aktif slot için tek gerçek kaynağı olarak hizmet eder.
   - `RedisWorkerActivationGuard` Redis'i sürekli sorgular. Slot eşleşirse worker `Active` durumuna geçer; aksi halde `Standby` durumunda kalır.
   - Geri alma sırasında Redis'in felaketle sonuçlanan kesintilerinde, `--emergency-override` flag, Nginx yönlendirmesini önceki yuvaya döndürerek operasyonel trafiğin kurtarılmasına izin verir. Ancak Redis güncellenemediği için bu, `CRITICAL_INCONSISTENT_STATE`: trafik yeniden sağlandı (`trafficRestored: true`), ancak Redis mutabakatı doğrulanmadı (`redisReconciled: false`). Worker işlemleri koruma altında kalır; `EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED` dağıtım günlüğüne kaydedilir ve Redis kurtarıldıktan sonra döndürülen mutabakat komutu aracılığıyla manuel mutabakat kesinlikle gereklidir.
2. **Dağıtılmış Kira Sözleşmesi (`RedisWorkerLeaseManager`):**
   - Aktif worker, `restaurant-order:lease:worker-leadership` anahtarında atomik `SET NX PX` ile özel liderlik kirası alır ve düzenli yeniler.
   - Kira yenileme başarısız olursa veya yuva aktif durumdan çıkarılırsa tüketim hemen durdurulur (arıza-kapanma).
3. **İdempotans Gereksinimi (`RedisIdempotencyStore`):**
   - Arka plan işleri (sipariş durumu, hesap ve bildirimler) yinelenen yan etkileri önlemek için idempotency anahtarı kullanmalıdır. Redis TTL rezervasyonu tek başına dış etkinin en fazla bir kez oluşmasını garanti etmez; kalıcı iş kaydı ve sağlayıcı mutabakatı açığı R07 kapsamında izlenir.
4. **Arıza Kapalı Davranışı:**
   - Hazırlama ve Üretimde, Redis'e erişilemiyorsa veya kimlik bilgileri/yuvalar geçersizse worker işlemleri hata durumuna geçer (`Status = Error`) ve kuyrukları işlemeyi reddeder.

---

## 3. Otomatik Blue/Green Operasyonel Komutlar

Tüm operasyon komutları **varsayılan olarak prova modunda** çalışır. Açık operatör onay işaretleri olmadan hiçbir canlı trafik kaydırılmaz (`--confirm-cutover`, `--execute`).

### 3.1. Komple İşlem Hattı (Orkestratör)

```bash
# Güvenli prova denetimi (her dağıtım öncesinde önerilir)
pnpm blue-green:check
# Doğrudan çalıştırma:
node scripts/blue-green/orchestrator.mjs --dry-run

# Canlı üretimde çalıştırma (operatör onayı gerekir)
node scripts/blue-green/orchestrator.mjs --execute --confirm-cutover
```

### 3.2. Bireysel Komuta Sınırları

| Adım | Komut | Açıklama |
| :--- | :--- | :--- |
| **1. Ön kontrol** | `node scripts/blue-green/preflight.mjs` | Yuvaları, dosyaları oluşturmayı ve görüntü özetini doğrular |
| **2. Yapılandırma Doğrulama**| `node scripts/blue-green/config-validate.mjs` | Ortam sözleşmelerini ve güvenlik kurallarını doğrular |
| **3. Geçiş Kontrolü**| `node scripts/blue-green/migration-check.mjs` | Genişlet–Taşı–Daralt uyumluluğunu doğrular |
| **4. Etkin Değil'i Dağıt**| `node scripts/blue-green/deploy-inactive.mjs` | Boşta kalan yuva kaplarını başlatır (varsayılan deneme çalıştırması) |
| **5. Sağlık Kontrolü** | `node scripts/blue-green/health-check.mjs` | Problar `/health/live` ve `/health/ready` |
| **6. Isınma** | `node scripts/blue-green/warmup.mjs` | Isınmak için uç noktaları çalıştırır JIT & bağlantı havuzları |
| **7. Temel İşlev Testi** | `node scripts/blue-green/smoke.mjs` | Aday yuvada veri değiştirmeyen temel işlev testleri yapar |
| **8. Geçiş** | `node scripts/blue-green/cutover.mjs --confirm-cutover` | Canlı trafiği yukarı yönde yeni yuvaya kaydırır |
| **9. Gözlemle** | `node scripts/blue-green/observe.mjs` | Geçiş sonrası metrikleri izler (< 0.05% hata oranı) |
| **10. Eski Yuvayı Boşalt** | `node scripts/blue-green/drain-old.mjs` | Bağlantıları boşaltır ve kullanımdan kaldırılan yuvayı durdurur |

---

## 4. Derhal Acil Durum Geri Alma (< 60 Saniye)

Geçişten sonra hata oranı `0.05%` eşiğini aşarsa veya KDS akışı bozulursa:

```bash
# Güvenli geri alma provası
node scripts/blue-green/rollback.mjs --dry-run

# Canlı acil geri alma
node scripts/blue-green/rollback.mjs --execute --confirm-rollback
```

### 4.1. Geri Alma Değişmezleri
1. **Anında Giriş Anahtarı:** Trafiği önceki güvenli yuvaya geri döndürür 60 saniye içinde.
2. **Adli Koruma:** Başarısız yuva, bellek dökümü ve günlük analizi için **izole durumda tutulur**.
3. **Olay Beyanı:** Takip et [INCIDENT-RESPONSE.md](./INCIDENT-RESPONSE.md).

### 4.2. Acil Durum Geçersiz Kılma ve Manuel Redis Mutabakatı
Geri alma sırasında Redis'e erişilemediğinde, trafiğin geri döndürülmesi normalde iki yuvanın farklı duruma düşmesini önlemek için reddedilir. Acil kesintide operatör `--emergency-override` seçeneğiyle yalnızca trafiğin geri dönmesini sağlayabilir; seçenek terminalden ve program üzerinden desteklenir:

```bash
# Redis erişilemediğinde acil geri alma (yalnızca Nginx trafiğini değiştirir)
node scripts/blue-green/rollback.mjs --execute --confirm-rollback --emergency-override
```

**Sonuçlar ve Durum Garantileri:**
- Nginx trafiği önceki güvenli yuvaya geri yüklenir (`trafficRestored: true`).
- İşlem `success: false` ve `CRITICAL_INCONSISTENT_STATE` döndürür.
- Durum dosyası doğrulanmamış yuva verileriyle **güncellenmez**.
- `EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED` olayı `DEPLOYMENT_JOURNAL_FILE` dosyasına yazılır (veya varsayılan `.deployment-journal.jsonl`).
- Arka plan çalışanları gözetim altında kalır ve doğrulanmamış durumdaki kuyrukları işlemeyi reddederler.

**Zorunlu Manuel Mutabakat Adımı:**
Redis bağlantısı yeniden sağlandığında operatör merkezi durumu derhal eşitlemelidir:
```bash
# rollback.mjs çıktısındaki mutabakat komutunu çalıştırın:
redis-cli -u $REDIS_URL SET restaurant-order:active-slot <restored_slot>
```
Etkin yuva değerini doğrulayın:
```bash
redis-cli -u $REDIS_URL GET restaurant-order:active-slot
```
