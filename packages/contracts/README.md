# @restaurant-order/contracts

## Amaç ve Sınır

Bu paket, `apps/api` ile `apps/*-web` istemcileri arasındaki sözleşme sınırıdır. Mimari politika, istemci türlerinin ASP.NET Core OpenAPI belgesinden otomatik üretilmesini gerektirir; yetkili sözleşme kaynağı sunucunun OpenAPI meta verileridir.

**Mevcut açık:** Üretim akışı henüz bağlanmamıştır; TypeScript sözleşmeleri elle tutulmaktadır. Bu durum politikanın yerine getirildiği anlamına gelmez. Tekrarlanabilir üretim ve CI sapma denetimi [R06 bulgusunda](../../docs/REVIEW-BACKLOG.md) izlenir. Alan uç noktaları için örneğin `openapi-typescript` kullanan üretim betiği, çalışan veya derlenmiş OpenAPI belgesini girdi olarak almalıdır.

## Sağlık Sözleşmesi ve Kapsam

Kuruluş aşamasının ilk uç noktaları `/health/live` ve `/health/ready` idi. Sonraki fazlarda IAM, restoran yapılandırması ve katalog sözleşmeleri eklendi. Güncel faz ve doğrulama kapsamı [CURRENT-STATE.md](../../docs/CURRENT-STATE.md) dosyasındadır.
