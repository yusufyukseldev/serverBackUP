# ServerBackup — Proje Talimatları

Bu dosya, bu depoda çalışan her modelin (kapasitesi ne olursa olsun) uyması gereken
**bağlayıcı** kurallardır. Kararların gerekçesi için `C:\Users\bilgiislemstajyer\.claude\plans\imdi-seninle-bir-sunucuda-quirky-lecun.md`
dosyasındaki yol haritasına bakılabilir; ama bu dosyadaki kurallar önceliklidir.

## Proje Nedir

Windows Server üzerinde çalışan, **tamamen yerel** (buluta hiçbir şey göndermeyen),
CloudBerry/Acronis tarzı dosya/klasör yedekleme sistemi. C# / .NET 10. Depo modeli
restic/borg tipi: içerik adresli, chunk seviyesinde dedup'lı, uçtan uca şifreli,
forever-incremental (her snapshot mantıken tamdır, fiziksel olarak sadece yeni
chunk'lar yazılır).

İki eşit öncelik: (1) şirkette kullanılabilir bir ürün, (2) geliştiricinin bu
alanı öğrenmesi. Bu yüzden hazır bir yedekleme kütüphanesi (restic'i çağırmak,
vs.) SARILMAZ — motor sıfırdan yazılır. Ama *algoritmalar* yeniden icat
EDİLMEZ: FastCDC, Argon2id, HKDF, AES-GCM, GFS retention gibi literatürde
kanıtlanmış tasarımlar uygulanır.

## Değişmez Kurallar (asla ihlal etme)

1. **Kriptografi parametreleri sabit.** Argon2id (m=64MiB, t=3, p=4), HKDF-SHA256
   alt anahtar hiyerarşisi, AES-256-GCM, HMAC-SHA256 blob ID — bu tabloyu
   `docs/format-spec.md`'de bulacaksın. Bu parametreleri değiştirmeden önce
   `formatVersion`'ı artır ve spec'i güncelle. Sessizce değiştirme.
2. **Kendi kriptografik primitifini yazma.** Sadece `System.Security.Cryptography`
   ve `Konscious.Security.Cryptography.Argon2` kullan. AES/HMAC/HKDF'i elle
   implemente etmeye çalışma.
3. **Katman kuralı:** `ServerBackup.Core` hiçbir projeye referans vermez (dış
   bağımlılığı da minimum tutulur: sadece BCL + Zstd/Argon2 gibi saf managed
   paketler). `Engine` → `Core` + `Data`. `Cli`/`Service` → hepsine. Bu yönü
   asla tersine çevirme.
4. **Sıcak yolda allocation yapma.** Chunker, crypto, pack yazma/okuma
   kod yollarında `ArrayPool<byte>.Shared` kullan; döngü içinde `new byte[]`
   yazma.
5. **Depoyu bozabilecek her işlem write-then-delete sırasıyla çalışır.**
   Prune, repack, index rebuild gibi işlemler yeni veriyi tam yazıp commit
   ETMEDEN eski veriyi silmez. Yarım kalan işlem depoyu bozmamalı.
6. **Test yazılmadan faz kapanmaz.** Her yeni public tip/davranış için en az
   bir test olmadan ilgili faz "tamamlandı" sayılmaz.
7. **Buluta hiçbir şey gönderilmez.** Ağ çağrısı gerektiren hiçbir depo
   backend'i (S3, Azure Blob, vs.) eklenmez. `IStorageBackend`
   implementasyonları yalnızca yerel disk / SMB / USB olabilir.
8. **Onaylanmamış bağımlılık ekleme.** `docs/format-spec.md` ve plan
   dosyasında listelenmeyen yeni bir NuGet paketi eklemeden önce kullanıcıya
   sor.
9. **Migration'ı elle düzenleme.** EF Core migration dosyaları `dotnet ef
   migrations add` ile üretilir, sonradan elle değiştirilmez.
10. **Testi `[Skip]` ile geçirme.** Bir test yazılamıyorsa/geçmiyorsa, testi
    atlamak yerine kodu düzelt ya da kullanıcıya durumu açıkça bildir.

## Şu An Neredeyiz

Faz tablosunu ve her fazın "Definition of Done"ını, dosya konumlarını ve
test stratejisini **plan dosyasından** oku:
`C:\Users\bilgiislemstajyer\.claude\plans\imdi-seninle-bir-sunucuda-quirky-lecun.md`

Fazlar sırasıyla: 0 İskele → 1 Kriptografi → 2 FastCDC → 3 Depo Formatı →
4 Tarayıcı/Ağaç → 5 Yedekleme Motoru → 6 Geri Yükleme/Doğrulama → 7 VSS →
8 Retention/Prune → 9 Windows Service/Scheduler → 10 Blazor Panel →
11 Sertleştirme → 12 Opsiyonel genişlemeler.

Bir faza başlamadan önce plan dosyasındaki o fazın bölümünü oku. Faz
bittiğinde: `dotnet test` yeşil → commit (bkz. aşağı) → bu dosyadaki
"Şu An Neredeyiz" güncellenmez (faz durumu git log'dan ve TaskList'ten
takip edilir, burada tekrarlanmaz).

## Komutlar

```powershell
dotnet build                                          # tüm solution
dotnet test                                            # tüm testler
dotnet test --filter Category!=RequiresAdmin           # yönetici gerektirmeyenler (CI/normal geliştirme)
dotnet run --project src/ServerBackup.Cli -- <komut>    # CLI çalıştır
dotnet run --project tests/ServerBackup.Benchmarks -c Release  # benchmark
```

## Kod Stili

- `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`,
  `LangVersion=latest`. Bunlara aykırı kod build'i kırar — bu istenen
  davranıştır, gevşetme.
- File-scoped namespace (`namespace Foo;`), `sealed` varsayılan (miras için
  açık bir sebep yoksa), `async` metotlar `Async` son ekiyle.
- Kod ve kod içi yorumlar **İngilizce**. Kullanıcıya görünen CLI/UI metinleri
  **Türkçe**.
- Yorum sadece WHY için (gizli bir kısıt, ince bir invariant, bilinen bir
  workaround). WHAT'i anlatan yorum yazma — isimler zaten anlatmalı.
- Üç benzer satır, erken bir soyutlamadan iyidir. YAGNI.

## Commit Kuralı

Conventional Commits: `feat:`, `fix:`, `test:`, `chore:`, `docs:`,
`refactor:`. Her faz sonunda commit atılır. Faz içinde riskli/büyük bir
değişiklikten önce de ara commit atılır. Commit mesajı Türkçe veya İngilizce
olabilir, tutarlı ol.

## Yapma Listesi

- Buluta bir şey gönderme (kod, veri, telemetri fark etmez — bu ürünün var
  olma sebebi budur).
- Plan dosyasında/`format-spec.md`'de listelenmeyen NuGet paketi ekleme;
  önce sor.
- Migration'ı elle değiştirme.
- Testi atlayarak/skip ederek "geçirme".
- `Core` projesine `Engine`/`Data`/`Service`'e referans ekleme.
- Kriptografi parametrelerini format sürümü artırmadan değiştirme.
