# Katkıcı ve AI Ajanı Çalışma Akışı

İnsanlar ve AI ajanları için geçerlidir. [AGENTS.md](../AGENTS.md) bağlayıcı kaynaktır; [DELIVERY.md](./DELIVERY.md) dal, doğrulama ve yayın kurallarını yönetir. Doküman, issue/PR, commit açıklaması ve devir notları Türkçe yazılır; teknik adlar ve araç sözdizimi korunur.

## Başlama ve Devam Etme

1. AGENTS.md, [güncel durum](./CURRENT-STATE.md), ilgili faz takibi ve alan belgelerini okuyun. Gerçek dalı, kaydedilmemiş değişiklikleri, son commit'leri, issue ve PR'ı kontrol edin; devir notu bağlam sağlar, güncel durumu kanıtlamaz.
2. Uygulamadan önce GitHub issue'su açın veya mevcut olanı kullanın. Sorun, tekrar üretim örneği, kabul ölçütleri, kapsam dışı işler ve gerekli kararları yazın. Kopya issue açmamak için mevcutları arayın. GitHub erişilemiyorsa [görev şablonuyla](./templates/TASK-TEMPLATE.md) yerel kayıt oluşturup henüz eşitlenmediğini belirtin.
3. Güncel main'den kısa ömürlü `fix/`, `feat/`, `docs/` veya `refactor/` dalı açın ya da issue'nun dalına devam edin. İlgisiz değişiklikleri ezmeyin; main'e doğrudan commit atmayın. Birleşmemiş PR'a bağımlı işte taban dalını ve birleştirme sırasını açıkça kaydedin.
4. Issue ve dalı CURRENT-STATE.md içine bağlayın. Önerileri öneri olarak işaretleyin; mimari anlaşma değişiyorsa ADR kullanın. Issue veya öneri tek başına mimari onayı değildir.

## Uygulama ve Doğrulama

1. Önce hatayı yeniden üretin. Eski davranışta başarısız, düzeltmede başarılı olan regresyon testleri ekleyin; hata akışlarını ve değişmez kuralları kapsayın.
2. Commit'leri odaklı tutun. Davranış, test, etkilenen sözleşme ve dokümantasyonu birlikte güncelleyin. İşletme yalıtımı ve yetki sınırlarını koruyun.
3. Geliştirme sırasında hedefli testleri, PR öncesinde DELIVERY.md gereği `pnpm verify` çalıştırın. Gerçek komutları, ortam varsayımlarını, sayıları, hataları ve çalıştırılmayan kontrolleri kaydedin. Sağlık sorgularını tarayıcı iş akışı kapsamı saymayın.
4. Son farkı ve çalışma ağacını inceleyin. Gizli bilgi, ilgisiz değişiklik, üretilmiş çıktı ve eski dokümantasyon arayın. Test başarısı iş kurallarının incelemesinin yerini almaz.
5. Açıklayıcı commit kullanın: `fix(pricing): hesap paylaşımında negatif tutarı önle`. Gövdeye `Refs #<issue>` ekleyin. Makineye özel yardımcıları, kimlik bilgilerini ve test günlüklerini commit'e eklemeyin.

## İncelemeye Sunma

1. Dalı push edin ve [PR şablonuyla](../.github/pull_request_template.md) PR açın. `Closes #<issue>`, kullanıcının gördüğü sorun/sonuç, gerçek doğrulama kanıtı, kalan riskler ve ilgili karar/listelerle bağlantıları ekleyin.
2. Zorunlu kontroller başarısızsa veya iş bitmediyse taslak PR tercih edin. Engeli açıkça yazın; PR açılmasını birleşme veya yayın gibi göstermeyin.
3. CI'ı kontrol edin ve kapsamdaki hataları çözün. Birleştirme için zorunlu ekip incelemesi ve başarılı kontroller gerekir. Açık yetki olmadan kendi PR'ınızı birleştirmeyin veya yayına çıkmayın.

## Kalıcı Devir Notu

Engellenme veya kesinti dâhil işi bırakmadan önce CURRENT-STATE.md güncellenir:

- Issue, dal, PR ve taban commit; yerel/push edilmiş/incelemeye hazır/birleşmiş/yayınlanmış durumları ayrı belirtin.
- Tamamlananlar ve tarihli gerçek doğrulama kanıtları.
- Kalan işler, sınırlamalar, açık kararlar ve sonraki somut adım.
- Gizli bilgi veya gerçek müşteri verisi olmadan ortam önkoşulları.

Büyük işlerde bütün geçmişi kopyalamak yerine faz veya görev kaydına bağlanın. Ertelenen bulguları [inceleme listesinde](./REVIEW-BACKLOG.md) tutun. Birleştirmeden sonra sıradaki görev Git/GitHub durumuyla bu notu yeniler. Commit'in kendi hash'ini içermesi gerekmez; revizyon geçmişinin kaynağı Git ve PR'dır.
