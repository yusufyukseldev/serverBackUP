# ServerBackup — Tehdit Modeli

Bu belge, ServerBackup'ın güvenlik tasarımının **neden** böyle olduğunu
açıklar. Kod ile bu belge çelişirse kod hatalıdır — ama bu belge de kodun
gerekçesini kaybettiği anda güncellenmelidir.

## Birincil Tehdit: Fidye Yazılımı

Yerel bir sunucuda çalışan, buluta hiçbir şey göndermeyen bir yedekleme
sisteminin **gerçek dünyadaki #1 riski fidye yazılımıdır** — özellikle
saldırganın zaten sunucuya (veya yedekleme servis hesabına) eriştiği
senaryo. Klasik saldırı zinciri:

1. Saldırgan sunucuya erişim kazanır (RDP, kimlik bilgisi hırsızlığı, vb.).
2. Kaynak dosyaları şifreler.
3. **Yedekleri de bulur ve siler/şifreler** — böylece kurbanın geri dönüş
   şansı kalmaz.

ServerBackup'ın Faz 11 sertleştirmesi bu üçüncü adımı engellemeyi
hedefler. Hiçbir önlem "saldırgan SYSTEM/Administrator oldu" senaryosunu
%100 engelleyemez (bu makinede çalışan bir yazılımın doğal sınırıdır) —
amaç, saldırının **maliyetini ve görünürlüğünü artırmak**, tek bir komutla
her şeyin silinmesini imkansız hale getirmek.

## Savunma Katmanları

### 1. Immutability penceresi ve append-only mod

`RepositoryConfig.ImmutabilityWindowDays` ve `RepositoryConfig.AppendOnly`
— bkz. `docs/format-spec.md` ve `PruneEngine`. Bu ayarlar **repo
oluşturulurken** (`repo init --immutability-days N` veya `--append-only`)
belirlenir ve `config.json`'da saklanır.

**Kritik tasarım kararı:** Bu koruma `PruneEngine`'in KENDİ İÇİNDE
uygulanır — CLI'de veya panelde bir "force delete" bayrağı YOKTUR. Bir
saldırgan retention politikasını `keep-last: 0` yapıp prune çalıştırsa
bile, `PruneEngine.RunAsync` immutability penceresi içindeki veya
append-only'deki snapshot'ları `keepIds` kümesine ekleyip hiç
dokunmadan bırakır. Bunu atlatmanın tek yolu:

- Doğrudan dosya sistemi üzerinden pack dosyalarını silmek (bu durumda
  `verify`/`ls`/`restore` hatası verir — sessiz veri kaybı olmaz), veya
- `config.json`'u elle değiştirip immutability/append-only ayarlarını
  kapatmak (bu da ACL sertleştirmesiyle zorlaştırılır, aşağıya bakın).

**Takas:** Immutability penceresi içindeki snapshot'lar meşru bir sebeple
de silinemez (örn. yanlışlıkla hassas veri yedeklendiyse). Bu bilinçli
bir tercih — GDPR/KVKK "unutulma hakkı" gibi senaryolar için ayrı bir
mekanizma (repo'yu tamamen yeniden oluşturmak) gerekir, bu v1 kapsamı
dışındadır.

### 2. Depo dizini ACL sertleştirmesi

`RepositoryManager.InitializeAsync` → `HardenRepositoryAcl`: depo dizini
oluşturulduğu anda inheritance kapatılır (`SetAccessRuleProtection`) ve
sadece üç kimlik FullControl alır: depoyu oluşturan kullanıcı,
`BUILTIN\Administrators`, `NT AUTHORITY\SYSTEM`. Miras alınan geniş
gruplar (Users, Authenticated Users, Everyone) **hiçbir zaman** erişim
kazanmaz.

**Neden önemli:** Sunucudaki normal bir kullanıcı hesabı (fidye
yazılımının genelde çalıştığı bağlam) depo dizinini okuyamaz, silemez,
config.json'u değiştiremez. Yalnızca servis hesabı (veya SYSTEM/Admin)
depoya dokunabilir.

**Takas:** Servis hesabının kendisi ele geçirilirse bu koruma işe
yaramaz — bu yüzden servis hesabının **kendisi de düşük yetkili**
olmalıdır (bkz. `scripts/install-service.ps1`: adanmış yerel hesap,
Administrators grubunda DEĞİL, sadece depo yollarına ve VSS için gereken
"Back up/Restore files" kullanıcı haklarına sahip).

### 3. Anomali tespiti (hacim + uzantı bazlı)

`AnomalyDetector` — `BackupEngine` her incremental çalışmada (parent
snapshot varsa) iki sinyali kontrol eder:

- **Hacim sinyali:** Değişen + silinen dosya sayısı / önceki toplam dosya
  sayısı oranı bir eşiği (varsayılan %50) aşarsa. Küçük depolarda yanlış
  alarmı önlemek için minimum dosya sayısı eşiği vardır (varsayılan 20).
- **Uzantı sinyali:** Bilinen fidye yazılımı uzantılarıyla (`.locked`,
  `.encrypted`, `.crypt`, vb. — yapılandırılabilir) biten YENİ/DEĞİŞEN
  dosyalar varsa, hacimden bağımsız olarak tetiklenir.

**Zamanlanmış işlerde varsayılan: `AbortOnDetection = true`.** İnsan
gözetimi olmadığı için (bkz. `BackupSchedulerService`), anomali tespit
edilirse backup **snapshot'ı hiç commit etmeden** durur — pending pack
`PackFileSet`'in abort-on-dispose mantığıyla diskten silinir, katalog
hiç değişmez. Bu, "temiz" veriyi zaten şifrelenmiş içerikle ezmeyi
önler: mevcut en son temiz snapshot dokunulmadan kalır.

CLI/manuel kullanımda varsayılan davranış anomali tespiti KAPALIdır
(`anomalyPolicy: null`) — interaktif kullanıcı zaten sonucu görüp karar
verebilir.

**Takas:** Yanlış pozitif mümkündür (örn. büyük bir toplu dosya
yeniden adlandırma/taşıma işlemi). Bu durumda kullanıcı `verify`/`ls`
ile durumu inceleyip `--anomaly-threshold` benzeri bir ayarla yeniden
dener (Faz 12+ CLI desteği).

### 4. Audit log

`AuditLogger` — `PruneEngine` (silme/repack) ve `RestoreEngine`
(geri yükleme) her çalıştırıldığında `AuditLogEntity`'ye kim
(`DOMAIN\user`, işlemi çalıştıran OS kimliği), ne zaman, ne yaptığı
yazılır. Depo kendi kullanıcı hesap sistemine sahip olmadığı için "kim"
her zaman işletim sistemi kimliğidir — CLI'de interaktif kullanıcı,
zamanlanmış işlerde servis hesabı.

**Bilinçli sınırlama:** Audit log katalogda (`catalog.db`) tutulur —
katalog silinirse audit geçmişi de kaybolur. Bu, ACL sertleştirmesiyle
birlikte kabul edilebilir bir risk: katalogu silebilen biri zaten
depoya tam erişime sahiptir.

### 5. Bildirimler

`INotifier` / `WindowsEventLogNotifier` — iş başarısız olduğunda veya
anomali tespit edildiğinde Windows Application Event Log'a yazılır
(Event Viewer'dan veya merkezi bir SIEM'den izlenebilir).

**Bilinçli olarak yapılmayan:** E-posta bildirimi. Bu ortamda test
edilebilecek bir SMTP sunucusu yoktu; doğrulanamayan ağ kodu
(kimlik bilgileri, sunucu adresi) göndermek "çalışıyor" izlenimi
verip production'da sessizce başarısız olabilir. `INotifier` arayüzü
bu genişleme için hazır — bir `SmtpNotifier` implementasyonu gerçek bir
SMTP sunucusuyla test edilerek eklenebilir.

## İkincil Tehditler

### Yanlışlıkla veri kaybı (kullanıcı hatası)

- `PruneEngine.RunAsync`'in `dryRun` parametresi **varsayılan `true`'dur**
  — kazara silme CLI'de `--apply` bayrağı olmadan asla gerçekleşmez.
- `RestoreEngine`'in `OverwritePolicy.Fail` seçeneği, var olan dosyaların
  üzerine yanlışlıkla yazılmasını engeller.
- Her prune öncesi/sonrası: kalan TÜM snapshot'ların tam restore
  edilebilir olduğu test paketinde doğrulanır (bkz. `PruneEngineTests`).

### Bozuk/eksik veri (disk hatası, kesinti)

- Pack'ler write-once'tır; kapatılmamış (kill edilmiş process'ten kalan)
  pack `rebuild-index` tarafından sessizce atlanır, çökme yaratmaz.
- Her yazma işlemi write-then-delete sırasıyla yapılır (yeni veri commit
  edilmeden eski veri silinmez) — bkz. `CLAUDE.md` kural 5.
- `verify --full` her blob'u çözüp içerik kimliğini yeniden hesaplayarak
  sessiz bit çürümesini (bit rot) yakalar.

### Depo içi şifreleme anahtarı sızıntısı

- Master key parola ile (Argon2id, m=64MiB) veya DPAPI ile (unattended
  mod, LocalMachine kapsamı) sarmalanır — hiçbir zaman düz metin
  diskte durmaz.
- Unattended mod bilinçli bir takas içerir: SYSTEM yetkisi ele geçiren
  biri DPAPI korumalı anahtarı açabilir. Bu, "insan olmadan zamanlanmış
  yedekleme çalışsın" gereksinimiyle "anahtar asla diskte açık durmasın"
  gereksinimi arasındaki kaçınılmaz gerilimdir — belgelenmiştir, gizli
  değildir (bkz. `docs/format-spec.md`).

## Kapsam Dışı (v1)

- **Ağ üzerinden saldırılar** — depo bu makinede yerel dosya sistemi
  üzerinden erişilir, ağ protokolü yoktur (plan Faz 12'de uzak ajan
  desteği eklenirse yeniden değerlendirilmelidir).
- **Donanım/firmware seviyesi saldırılar** (rootkit, vs.) — işletim
  sistemi güvenilir kabul edilir.
- **Yan kanal saldırıları** (timing, vs.) kriptografik primitiflere karşı
  — `System.Security.Cryptography`'nin kendi garantilerine güvenilir.
