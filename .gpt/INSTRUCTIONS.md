# AI Aracı Yönerge Uyarlayıcısı — .gpt/INSTRUCTIONS.md

> **Bağlayıcı kaynak:** Herhangi bir işlemden veya kod üretiminden önce [AGENTS.md](../AGENTS.md) okunmalı ve tüm kuralları uygulanmalıdır. Bu dosya bağımsız bir kural kaynağı değildir.

## Çalışma Kuralları

1. Kodlama, test, güvenlik, mimari, Blue/Green ve dosya sınırları (450 satır uyarı / 600 satır kesin üst sınır) AGENTS.md tarafından yönetilir.
2. Görevle ilgili alan belgelerini [docs/](../docs/) altında okuyun; mimari, alan modeli, durum makineleri, hata akışları, roller, arayüzler, ortamlar ve donanım belgelerini görevin kapsamına göre seçin.
3. İlgisiz dosyaları ve kullanıcı değişikliklerini koruyun. Gizli bilgi veya gerçek müşteri verisi eklemeyin. Çalıştırılmamış test ve işlemleri başarılı göstermeyin; açık mimari kararlarını öneri/ADR gerekli olarak işaretleyin.
4. AGENTS.md içindeki Türkçe dil kuralını uygulayın: doküman, issue/PR, commit açıklaması ve devir notları Türkçe olmalıdır; teknik tanımlayıcıları ve komutları koruyun. Ana kuralları çoğaltmayın veya onlarla çelişmeyin.
