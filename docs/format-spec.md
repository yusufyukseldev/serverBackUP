# ServerBackup Depo Formatı — v1

Bu belge deponun disk üzerindeki ikili/JSON formatının **normatif** tanımıdır.
Kod bu belgeyle çelişiyorsa kod hatalıdır. Format değişecekse önce
`formatVersion` artırılır, eski sürüm okuyucuları korunur, sonra bu belge
güncellenir.

## Dizin Yapısı

```
<repo>/
  config.json              Şifresiz: formatVersion, KDF parametreleri, chunker parametreleri
  keys/<keyid>.json        Her parola/kurtarma anahtarı için sarmalanmış master key
  data/<xx>/<packid>.pack  Pack dosyaları (~16-64 MB), <xx> = packid'nin ilk 2 hex karakteri
  locks/<lockid>.lock      Eşzamanlılık kilidi (PID + hostname + UTC timestamp)
  catalog.db               SQLite: blob→pack indeksi, snapshot listesi, plan/iş geçmişi
  catalog.db-wal
```

## Kriptografi Parametreleri (v1 — sabit)

| Adım | Algoritma | Parametre |
|---|---|---|
| Parola → KEK | Argon2id | salt 16 B, m=64 MiB, t=3, p=4, çıktı 32 B |
| Master key (MK) | `RandomNumberGenerator` | 32 B rastgele |
| MK sarmalama | AES-256-GCM(KEK) | nonce 12 B rastgele, tag 16 B → `keys/<id>.json` |
| Alt anahtarlar | HKDF-SHA256(MK, info) | `System.Security.Cryptography.HKDF` |
| Blob ID | HMAC-SHA256(K_id, düz metin chunk) | 32 B |
| Veri şifreleme | AES-256-GCM | bkz. "Nonce Yönetimi" |
| Gear tablosu tohumu | HKDF(MK, "gear-seed-v1") | 256 × uint64 |

Alt anahtar `info` string'leri: `"chunk-id-v1"` → K_id, `"pack-key-v1"` →
K_pack, `"gear-seed-v1"` → K_gear, `"meta-v1"` → K_meta.

### Nonce Yönetimi

Rastgele 96-bit nonce 2³² mesajdan sonra çakışma riski taşır — bunun yerine
deterministik sayaç kullanılır:

- Her pack'in rastgele 16 B `packSalt`'ı vardır (açık metin, dosyanın ilk 16 baytı).
- Pack anahtarı: `K = HKDF-SHA256(ikm: K_pack, salt: packSalt, info: "pack")`
- Pack içindeki i'nci blob'un nonce'u: `00 00 00 00 || BigEndian(uint64 i)` (12 B).

Aynı anahtar altında nonce tekrarı yapısal olarak imkânsızdır çünkü her pack
kapandıktan sonra asla append edilmez (yeni blob eklenmez).

### Servis İçin Parolasız Açılış

Zamanlanmış işlerin parola sormadan çalışabilmesi için MK, DPAPI
(`LocalMachine` scope) ile ikinci bir key dosyasına sarmalanabilir
(`--unattended` modu). Bu, SYSTEM yetkisi ele geçiren bir saldırganın depoyu
açabileceği anlamına gelir — bilinçli bir trade-off'tur, `--interactive`
modda (her seferinde parola) bu risk yoktur.

## Pack Dosyası İkili Düzeni

```
[0..16)      packSalt              16 B, açık metin
[16..H)      blob payload'ları     her biri: ciphertext || GCM tag (16 B)
[H..H+L)     şifreli header        AES-256-GCM, nonce = 00000000 || 0xFFFFFFFFFFFFFFFF
[son 4 B]    L = headerLength      uint32 LE (16 B tag dahil)
```

Header düz metni (JSON veya kompakt binary — implementasyon Faz 3'te
kararlaştırılır ve burada netleştirilir):
`count:uint32` + her blob için
`{ type:1B, blobId:32B, offset:uint64, lenStored:uint32, lenPlain:uint32, compression:1B }`.

`type` değerleri: `0 = data blob`, `1 = tree blob`.
`compression` değerleri: `0 = ham (sıkıştırma faydasız bulundu)`, `1 = zstd`.

Pack dosya adı: rastgele 128-bit ID'nin hex gösterimi (`data/<xx>/<32-hex>.pack`,
`<xx>` = ID'nin ilk 2 hex karakteri, dizin başına dosya sayısını sınırlamak için).
Pack dosyasının SHA-256'sı katalogda tutulur (bütünlük doğrulaması için).

Pack'ler asla append edilmez: bir kez kapatıldıktan sonra sadece okunur veya
(prune sırasında) tamamen silinir.

## Tree Nesneleri

Tree'ler de birer blob'dur (`type=tree`), dolayısıyla değişmeyen bir klasörün
tree'si de dedup'lanır. Tree JSON'u zstd ile sıkıştırılarak saklanır:

```json
{
  "nodes": [
    { "n": "rapor.xlsx", "t": "file", "sz": 51200, "mt": 638..., "attr": 32,
      "sddl": "O:BAG:BAD:(A;;FA;;;SY)", "c": ["<blobId hex>", "..."] },
    { "n": "alt", "t": "dir", "sub": "<tree blob id>" }
  ]
}
```

`mt` = dosyanın son değişim zamanı, `FileTime` (100ns tick, UTC) olarak.
`sddl` = `GetSecurityDescriptorBinaryForm` çıktısının SDDL string temsili.

## FastCDC Chunker Parametreleri (v1 — sabit)

```
MinSize     = 256 KiB
NormalSize  = 1 MiB
MaxSize     = 4 MiB
MaskS       = 22 bit set   (Min..Normal arası — zor sınır)
MaskL       = 18 bit set   (Normal..Max arası — kolay sınır)
Gear hash   : h = (h << 1) + GearTable[b];  sınır ⇔ (h & mask) == 0
```

`GearTable`, depo master key'inden `HKDF(MK, "gear-seed-v1")` ile türetilen
256 adet `uint64` değerden oluşur — bu sayede chunk sınırları depo dışından
tahmin edilemez (chunking attack savunması).

## Katalog (SQLite) — v1 Şema Özeti

Ayrıntılı EF Core modeli Faz 3'te kod ile birlikte gelir; burada sadece
tablo listesi ve amacı:

| Tablo | Amaç |
|---|---|
| `Packs` | packId, dosya SHA-256, boyut, oluşturulma zamanı |
| `Blobs` | blobId, packId, offset, lenStored, lenPlain, type, compression |
| `Snapshots` | snapshotId, planId, parentSnapshotId, başlangıç/bitiş zamanı, kök tree blob id |
| `SnapshotPaths` | snapshot başına yedeklenen kaynak yollar |
| `Plans` | yedekleme planı tanımı (kaynak, hedef repo, zamanlama, retention) |
| `Jobs` / `JobLogs` | çalıştırılan/zamanlanan işler ve logları |
| `AuditLog` | kim ne zaman ne sildi/geri yükledi (Faz 11) |

**Katalog kaybı felaket değildir:** pack header'ları kendi kendini
tanımladığı için `repo rebuild-index` komutu tüm pack'leri tarayarak
`Packs`/`Blobs` tablolarını yeniden üretebilir. `Snapshots` bilgisi de
tree/snapshot blob'larından geri kurulabilir (snapshot kök blob ID'leri özel
bir "snapshot listesi" blob'unda da ayrıca tutulur — Faz 3 detayı).
