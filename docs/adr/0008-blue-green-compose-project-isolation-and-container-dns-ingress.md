# ADR-0008: Blue/Green Compose Proje İzolasyon ve Konteyner DNS Giriş Yönlendirme

**Durum:** ACCEPTED
**Tarih:** 2026-09-19
**Bağlam:** Blue/Green Dağıtım Altyapısı Güçlendirme

---

## Bağlam

İlk Blue/Green dağıtım tasarımında her iki yuva için tek bir Docker Compose projesi kullanıldı. Bu kritik bir çarpışmaya neden oldu:

> `docker compose -f compose.yml -f compose.prod.blue.yml up -d` çalıştırıldığında Docker hizmeti proje ve hizmet adıyla tanımlar. Aynı projede `compose.prod.green.yml` ile yeniden çalıştırmak, `container_name` farklı olsa bile Blue konteynerlerinin Green konteynerleriyle değiştirilmesine yol açar.

Ayrıca ilk trafik aktarım betiği `nginx -t` ve `nginx -s reload` komutlarını ingress konteyneri yerine ana bilgisayardaki süreçte çalıştırıyordu. Bunun riskleri:
- Tarafından test edilen yapılandırma `nginx -t` yeniden yükleme sırasında yüklenen yapılandırmadan farklı olabilir.
- Bir Host nginx işleminin var olduğunun garantisi yoktu.
- Ana bilgisayar bağlantı noktası tabanlı yukarı akışlar (`127.0.0.1:5001`) her iki yuvanın da ana bilgisayara bağlanmasını gerektiriyordu, bu da salt okunur konteyner dosya sistemleriyle çelişiyordu.

## Karar

### 1. Yuva Başına Ayrı Docker Compose Projeleri

Her dağıtım yuvası bağımsız bir Compose projesi olarak çalışır:

```
docker compose -p restaurant-order-blue  -f compose.yml -f compose.prod.blue.yml  up -d --force-recreate
docker compose -p restaurant-order-green -f compose.yml -f compose.prod.green.yml up -d --force-recreate
```

Bu şunları garanti eder:
- `restaurant-order-blue` ve `restaurant-order-green` tamamen ayrı Docker ad alanlarıdır.
- Green projesinde `--force-recreate` çalıştırmak Blue konteynerlerini etkilemez.
- Birim ve ağ adları alan kapsamlıdır.

### 2. Giriş Yönlendirmesi için Paylaşılan Harici Docker Ağı

Her iki slot projesi de paylaşılan bir harici ağa bağlanır `restaurant_order_ingress`:

```bash
docker network create restaurant_order_ingress
docker compose -p restaurant-order-ingress -f compose.ingress.yml up -d
```

Giriş Nginx kapsayıcısı bu ağa katılır ve yuva kapsayıcılarını Docker tarafından çözer DNS konteyner adı:
- `restaurant-order-api-blue:5000`
- `restaurant-order-api-green:5000`
- `restaurant-order-customer-web-blue:8080`
- diğerleri.

**Ana bilgisayar bağlantı noktası bağlaması gerekmez** yuva kaplarının girişe erişilebilmesi için.

### 3. Geçiş yoluyla `docker exec` Giriş Konteynerinde

Şimdi kesme komut dosyası:
1. **Aday** upstream yapılandırmasını yerel olarak yazar (bağlama montajı aracılığıyla) `deploy/nginx/conf.d/`).
2. Doğrular: `docker exec restaurant-order-ingress nginx -t`.
3. Atomik olarak yeniden yüklenir: `docker exec restaurant-order-ingress nginx -s reload`.
4. Doğrulama veya yeniden yükleme başarısız olursa, yapılandırma dosyasını geri döndürür ve yeniden yüklemeyi yeniden yürütür.

Bu garanti eder **test edilen yapılandırma yeniden yüklenen yapılandırmayla aynı** — bunlar aynı dosyadır.

### 4. Bağımsız Bir Proje Olarak Ingress

Giriş Compose projesi (`restaurant-order-ingress`) slot projelerinden bağımsızdır:
- Blue veya Green projesinin yeniden oluşturulması ingress hizmetini yeniden başlatmaz veya etkilemez.
- Giriş, dağıtımlar arasında sürekli olarak çalışır.

### 5. Ön Kontrolde Beş İmaj Özetinin Zorunlu Olması

Üretim bildirimleri, beş imajın tamamı için değişmez sha256 özeti içermelidir:
`api`, `worker`, `customer-web`, `operations-web`, `admin-web`.

Bilinen boş içerik karması (`sha256:e3b0c44298...`) açıkça reddedilir; bu, görüntünün hiçbir zaman oluşturulmadığını gösterir.

## Sonuçlar

**Olumlu:**
- Mavi ve yeşil, sıfır parazitle tamamen eş zamanlı olarak çalışabilir.
- Geçiş daha güvenlidir: test edilmiş yapılandırma === yüklü yapılandırma.
- Ana bilgisayar nginx işlemi bağımlılığı yok.
- Sırlar, ana bilgisayar bağlantı noktası bağlamaları aracılığıyla açığa çıkmaz.

**Negatif:**
- Operatörler ilk dağıtımdan önce paylaşılan ağı oluşturmalıdır:
  `docker network create restaurant_order_ingress`
- Giriş, yuva dağıtımlarından önce ayrı olarak başlatılmalıdır.
-  `INGRESS_CONTAINER` isim korunması gereken bir kuraldır.

## Değişmezler

- `ACTIVE: docker compose -p restaurant-order-blue` konteynerler hiçbir zaman yeşil dağıtım tarafından yeniden oluşturulmaz.
- Giriş kapsayıcısında doğrulanan Nginx yapılandırması, yeniden yüklenen dosyayla aynıdır.
- Nginx yeniden yükleme tetikleyicilerinden sonra başarısız bir Redis durumu güncellemesi `CRITICAL_INCONSISTENT_STATE`, asla `PASS`.
