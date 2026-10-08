# Aşama 3 Uygulama İzleyici (`docs/PHASE-3-TRACKER.md`)

> **Tarihsel kayıt:** Buradaki test sayıları ve commit referansları ilgili fazın kapanışına aittir. Güncel görev kanıtları [CURRENT-STATE.md](./CURRENT-STATE.md) dosyasındadır.

Bu belge, `restaurant-order` Faz 3 Kimlik Doğrulama ve RBAC çalışmasının yedi alt aşamasını izler.

---

## Alt Faz Durumuna Genel Bakış

| Alt Faz | Başlık | Durum | Birincil Çıkış |
| :--- | :--- | :--- | :--- |
| **Aşama 3.1** | IAM Mimarlık ve Yetki Sözleşmeleri | **COMPLETED** | ADR-0009, 8 Roller, Kapsam Modelleri, İzin Tescili, JWT Talep Modelleri, Birim Testleri |
| **Aşama 3.2** | IAM Kalıcılık, Oturumlar ve İşletme İzolasyonu | **COMPLETED** | EF Core IAM varlıklar, PostgreSQL `iam` şema, RLS politikalar, denetim günlükleri, Hasher sözleşmeleri |
| **Aşama 3.3** | Şifre Doğrulama, JWT & Oturum Akışını Yenile | **COMPLETED** | Oturum açma, yenileme, oturum kapatma uç noktaları, HttpOnly çerezleri, belirteç rotasyonu, yeniden kullanım tespiti |
| **Aşama 3.4** | Merkezi Yetkilendirme, RBAC & Doğrulanmış İşletme Bağlamı | **COMPLETED** | ASP.NET Core yetkilendirme işleyicisi, `RequirePermission`, kaynak sahipliğinin uygulanması |
| **Aşama 3.5** | Güvenli Personel PIN ve Güvenilir Terminal Kimlik Doğrulaması | **COMPLETED** | Kayıtlı güvenilir terminal modeli, 4-haneli pepper ile korunan PIN oturum açma, kaba kuvvetle geri çekilme ve kilitleme |
| **Aşama 3.6** | Personel Kimlik Yönetimi ve Ön Uç Kimlik Doğrulama Entegrasyonu | **COMPLETED** | Personel daveti, rol ataması, Yönetici ve Operasyonlar Web kimlik doğrulama entegrasyonu |
| **Aşama 3.7** | Kimlik Doğrulama ve RBAC Güvenlik Sağlamlaştırma ve Son Kapatma | **COMPLETED** | Güvenlik doğrulaması, dağıtılmış kimlik doğrulama düzeltmeleri, kapsamın kapatılması, denetim iyileştirmesi |

---

## Ayrıntılı Kilometre Taşı Kontrol Listesi

### Aşama 3.1: IAM Mimarlık ve Yetki Sözleşmeleri
- [x] **ADR-0009 Kabul edildi:** [docs/adr/0009-kimlik doğrulama-ve-oturum-strategy.md](adr/0009-authentication-and-session-strategy.md)
- [x] **8 Tanımlanan Roller:** `SuperAdmin`, `RestaurantAdmin`, `BranchManager`, `Cashier`, `Kitchen`, `Bar`, `Waiter`, `Customer`
- [x] **Ana ve Kapsam Sözleşmeleri:** `PrincipalType`, `AuthenticationMethod`, `AuthorizationScopeType`, `AuthorizationScope`, `AuthenticatedPrincipal`
- [x] **İzin Kaydı:** Herkes için makine tarafından okunabilen sabitler 31 [docs/'deki izinlerROLES-AND-PERMISSIONS.md](ROLES-AND-PERMISSIONS.md) ile 100% matris kapsamı
- [x] **Varsayılan Olarak Reddetme Uygulaması:** Bilinmeyen roller, bilinmeyen izinler ve eksik kapsamlar kesinlikle reddedildi
- [x] **Kaynak Sahipliği Soyutlaması:** `IResourceOwnershipRequirement` için otomatik tam erişimin engellenmesi `OwnOrAssigned` (`O`) izinler
- [x] **JWT Talep Sözleşmeleri:** `JwtClaimNames`, `JwtClaimModel`ve `JwtClaimPrincipalParser` başarısızlıkla kapatılmış doğrulama ile
- [x] **Matris ve Kapsam Testleri:** Hepsini kapsayan birim testleri 31 tümünde izinler 8 roller ve olumsuz kapsam/talep senaryoları

### Aşama 3.2: IAM Kalıcılık, Oturumlar ve İşletme İzolasyonu
- [x] Kullanıcı/Hesap varlığı (`iam.users`)
- [x] İşletme/Şube Üyeliği varlığı (`iam.memberships`)
- [x] Yetkilendirme Oturumu varlığı (`iam.sessions`)
- [x] Aile takibi ile Token durumunu yenileyin (`iam.refresh_tokens`)
- [x] Güvenilir Terminal varlığı (`iam.trusted_terminals`)
- [x] Yalnızca Ekleme Güvenlik Denetim Günlüğü (`iam.audit_events`)
- [x] PostgreSQL Satır Düzeyinde Güvenlik `iam.*` tablolar
- [x] Şifre ve pepper ile korunan PIN karma sözleşmeleri

### Aşama 3.3: Şifre Doğrulama, JWT & Oturum Akışını Yenile
- [x] `POST /api/v1/auth/login` (E-posta + Şifre)
- [x] `POST /api/v1/auth/refresh` (Dönen yenileme belirteçleri)
- [x] `POST /api/v1/auth/logout` & `POST /api/v1/auth/logout-all`
- [x] `GET /api/v1/auth/session` & `GET /api/v1/auth/sessions`
- [x] HttpOnly, Güvenli, SameSite=Katı çerezler
- [x] Belirtecin yeniden kullanımının tespiti ve oturum ailesinin iptali

### Aşama 3.4: Merkezi Yetkilendirme, RBAC & Doğrulanmış İşletme Bağlamı
- [x] `PermissionRequirement` & `PermissionAuthorizationHandler`
- [x] `[RequirePermission(...)]` özellik ve politika sağlayıcısı
- [x] Kimliği doğrulanmış işletme bağlam çözümleyicisi (doğrulanmış JWT yalnızca iddialar)
- [x] Kaynak sahipliği ve istasyon atama doğrulayıcıları

### Aşama 3.5: Güvenli Personel PIN ve Güvenilir Terminal Kimlik Doğrulaması
- [x] Terminal kayıt kodu oluşturma ve etkinleştirme
- [x] 4-hane PIN sunucu tarafı pepper değeri ve yavaş karma ile kimlik doğrulama
- [x] Terminal kapsamlı kaba kuvvet geri tepmesi ve kilitleme koruması
- [x] Terminal iptal kademesi

### Aşama 3.6: Personel Kimlik Yönetimi ve Ön Uç Entegrasyonu
- [x] Personel daveti, aktivasyonu, askıya alınması API
- [x] Yönetici Web girişi, korumalı rotalar, oturumu geri yükleme
- [x] Operasyonlar Web terminalinin etkinleştirilmesi ve PIN giriş alt sayfası
- [x] Ön uçta tek uçuşlu yenileme belirteci kuyruğu HTTP müşteri

### Aşama 3.7: Güvenlik Sağlamlaştırma ve Son Doğrulama
- [x] Tam RBAC matris doğrulaması (tümü 31 izinler × 8 roller)
- [x] Varsayılan olarak reddetme ve başarısız şekilde kapatılan yetkilendirme doğrulaması
- [x] RFC 7807 Sorun Ayrıntıları uyumluluğu 401 Yetkisiz ve 403 Yasak
- [x] Şifre karma ve pepper ile korunan PIN hasher güvenlik özellikleri doğrulandı
- [x] Terminal aşamalı gecikme ve kaba kuvvet kilitleme doğrulandı
- [x] Tek uçuşlu eşzamanlı token yenileme kuyruğu doğrulandı
- [x] Deterministik geçiş yaşam döngüsü ve kimlik doğrulama öncesi önyükleme SECURITY DEFINER ağ geçidi
- [x] İşletme işlemi hatasıyla kapatılan sınır ve bağlantı havuzu bağlam yalıtımı doğrulandı
- [x] Özel global tablolarda platform oturumu ve belirteç kalıcılığını yenileme (`iam.platform_sessions`, `iam.platform_refresh_tokens`), işletme verilerini tamamen izole etme
- [x] PostgreSQL satır kilitlerini kullanarak dağıtılmış atomik jeton rotasyonu (`FOR UPDATE`) yeniden kullanım yarışında derhal aile iptali ile
- [x] Redis'te dağıtılmış geçici kimlik doğrulama durumu (oturum açma hızını sınırlayan Lua komut dosyaları, aşamalı terminal PIN deneme gecikmesi/kilitleme, tek kullanımlık kayıt `GETDEL`)
- [x] Arıza durumunda kapatılan Redis mimarisi: geri dönüşler HTTP 503 Redis kullanılamıyorsa güvenlik kapılarını atlamak yerine
- [x] Çok örnekli dağıtılmış entegrasyon test paketi (`PostgreSqlDistributedAuthIntegrationTests`) simüle edilmiş geçiş API örnekler
- [x] Arka uç birleştirilmiş test kapsamı >= 80% eşik kapanması (Satır: 94.16%, Dal: 83.02%, Yöntem: 96.00%)
- [x] Ön uç test kapsamı >= 80Tüm uygulama ve paketlerde eşik kapanma yüzdesi
- [x] Platform yenileme jetonunun yeniden kullanımı SQL sütun düzeltmesi ve doğrulama
- [x] Oturum iptali IDOR güvenlik açığı kapatma ve işletme/platform sahipliği entegrasyon testleri
- [x] PostgreSQL'de atomik hesap kilitleme sayacı
- [x] PIN dağıtılmış örneklerde hız sınırı e-postası/kullanıcı kimliği anahtarı tutarlılığı
- [x] JWT oturum-kullanıcı bağlama doğrulaması iam.validate_token_session (ileriye geçiş)
- [x] Platform oturumunda en az ayrıcalıklı doğrudan tablo erişiminin iptali
- [x] Sıfır dosya aşıldı 600 satır katı tavan
- [x] Sıfır sır ve temiz belge bütünlüğü doğrulandı

RFC 7807 yanıt türü `ProblemDetails` olarak korunur.
