# Worker Koordinasyonu ve Dağıtılmış Kiralama Runbook'u (`docs/runbooks/WORKER-COORDINATION-RUNBOOK.md`)

## 1. Genel Bakış ve Mimari

Blue/Green dağıtım modelimizde, sürüm doğrulaması sırasında hem Mavi hem de Yeşil çalışan konteynerleri eş zamanlı olarak çalışır. Kuyruk olaylarının, termal makbuz yazdırmanın ve zamanlanmış işlerin yinelenen yürütülmesini önlemek için:

1. **Merkezi Aktif Slot Durumu (`restaurant-order:active-slot`):** Redis'te saklanır. Yalnızca bu anahtarla eşleşen yuvanın işi işleme yetkisi vardır.
2. **Dağıtılmış Liderlik Kiralaması (`restaurant-order:lease:worker-leadership`):** `RedisWorkerLeaseManager`, atomik `SET NX + TTL` ve Lua karşılaştırma/yenileme/silme betikleriyle yönetir.
3. **Idempotency Koruması (`IIdempotencyStore`):** Kira korumasına ek olarak her iş idempotency anahtarı kullanır. Dış etkinin en fazla bir kez oluşması yalnızca Redis TTL ile kanıtlanamaz; kalıcı kayıt ve mutabakat tasarımı R07 içinde açıktır.

---

## 2. Bölünmüş Beyin Belirtileri ve Tespiti

Hem Mavi hem de Yeşil çalışanların aynı anda işleri işlemesi durumunda bölünmüş beyin durumu ortaya çıkar.

### 2.1. Temel Göstergeler
- **Yinelenen Termal Baskılar:** Aynı hazırlık fişi/makbuz mutfak veya bar istasyonlarında birden çok kez basılıyor.
- **Eşzamanlı Çalışan Aktif Günlükleri:** Hem `restaurant-order-worker-blue` hem `restaurant-order-worker-green` günlüklerinde aynı anda `[WORKER ACTIVE]` görülür.
- **Çoklu Kira Sahibi:** Redis'te tutarsız anahtar sahipliği algılandı.

### 2.2. Muayene Komutları
```bash
# Merkezi aktif yuvayı kontrol edin
redis-cli GET restaurant-order:active-slot

# Liderlik kirasını tutan worker örneğini kontrol edin
redis-cli GET restaurant-order:lease:worker-leadership

# Liderlik kirasının kalan süresini saniye cinsinden kontrol edin
redis-cli TTL restaurant-order:lease:worker-leadership

# Etkin Nginx upstream yönlendirmesini inceleyin
cat deploy/nginx/conf.d/upstream.conf
```

---

## 3. Redis Kesintisi ve Arıza Kapatma Davranışı

Redis bölümlendiğinde veya erişilemediğinde:
1. **Acil Arıza-Kapalı:** `RedisWorkerActivationGuard.IsActiveSlotAsync` geri döner `false`.
2. **Anında Kira Kaybı:** `RedisWorkerLeaseManager.RenewLeaseAsync` geri döner `false`.
3. **Tüketimin Durdurulması:** Çalışan döngüleri kuyruk tüketimini, cron programlarını ve termal yazdırma biriktirmeyi anında duraklatır.
4. **Kuyruk Arabelleğe Alma:** İşlenmekte olan kuyruk mesajları, Redis kurtarılıncaya kadar kalıcı depolama alanında ara belleğe alınmış olarak kalır. Hiçbir veri kaybolmaz.

---

## 4. Hatalı Worker İşlemini Acil Durdurma

Hatalı bir çalışan örneği, geçişten sonra veya bölünmüş beyin araştırması sırasında işleri tüketmeye devam ederse:

### 4.1. Belirli Çalışan Konteynerini Durdur
```bash
# Green worker işlemini hemen durdurun
docker compose -p restaurant-order-green -f compose.yml -f compose.prod.green.yml stop worker

# Blue worker işlemini hemen durdurun
docker compose -p restaurant-order-blue -f compose.yml -f compose.prod.blue.yml stop worker
```

### 4.2. Zorunlu Serbest Bırakma Eski Dağıtılmış Kiralama
Worker, kirasını bırakmadan çöktüyse ve liderliği yeniden atamak gerekiyorsa, önce eski worker işleminin durduğunu doğrulayın:
```bash
# Bekleyen örneğin alabilmesi için eski kira anahtarını kaldırın
redis-cli DEL restaurant-order:lease:worker-leadership
```

---

## 5. Güvenli Kurtarma İş Akışı

Çalışan koordinasyonunun bozulmasından kurtulmak için şu adımları izleyin:

1. **Redis Sağlığını Doğrulayın:**
   ```bash
   redis-cli PING
   # Beklenen: PONG
   ```

2. **Merkezi Aktif Slotu Girişle Uzlaştırın:**
   Nginx upstream yapılandırmasındaki aktif konteyner adını kontrol edin (blue veya green) ve Redis değerini aynı yuvaya ayarlayın. Konteyner DNS topolojisinde API iç portu her iki yuvada 5000'dir:
   ```bash
   # Nginx Blue yuvasına yönlendiriyorsa:
   redis-cli SET restaurant-order:active-slot blue

   # Nginx Green yuvasına yönlendiriyorsa:
   redis-cli SET restaurant-order:active-slot green
   ```

3. **Çalışan Günlüklerini Doğrulayın:**
   Yalnızca aktif yuvanın `[WORKER ACTIVE]` kaydı ürettiğini ve liderlik kirasını aldığını doğrulayın:
   ```bash
   docker logs --tail 50 -f restaurant-order-worker-blue
   docker logs --tail 50 -f restaurant-order-worker-green
   ```

---

## 6. Koordineli Çalışan Geri Alma Prosedürü

Acil durum geri alma işlemi başlatıldığında:

1. **Geri Alma Komutunu Yürütün:**
   ```bash
   node scripts/blue-green/rollback.mjs --execute --confirm-rollback
   ```

2. **Yürütülen Otomatik Adımlar:**
   - Yukarı akış girişi güvenli yuvaya geri döner (örneğin Blue yuvası).
   - Şununla doğrulandı: `nginx -t` ve yeniden yüklendi `nginx -s reload`.
   - Redis anahtarı `restaurant-order:active-slot` geri yüklenen yuvaya güncellenir (`blue`).
   - Başarısız slot çalışanı durum değişikliğini fark eder ve kira kontratını derhal iptal eder.
   - Geri yüklenen slot çalışanı aktif durumu tespit eder, kirayı alır ve tüketimi devam ettirir.
   - Başarısız slot konteyneri adli analiz için çevrimiçi kalır.
