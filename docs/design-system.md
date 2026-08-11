# ServerBackup — Arayüz Tasarım Dili

Bu belge, `ServerBackup.Service` içindeki Blazor yönetim panelinin **tasarım
dilidir**. Renk, tipografi, boşluk, bileşen davranışı ve dil kurallarının tek
kaynağıdır. Panelde bir şey çizerken burada karşılığı olmayan bir değer
kullanılmaz; ihtiyaç varsa önce bu belge güncellenir.

**Kapsam dışı:** Mobil / dar ekran düzenleri. Hedef, 1366px ve üzeri masaüstü
tarayıcıdır (yönetici iş istasyonu veya RDP oturumu). Duyarlılık yalnızca
"1366–2560 arası bozulmadan çalışsın" seviyesindedir; ayrı bir mobil sürüm
tasarlanmaz.

**Kapsam içi:** Faz 10'da var olan 6 sayfa + Faz 11/12'de gelecek yüzeyler
(denetim kaydı, ajanlar, USB rotasyonu, throttling, ayarlar).

---

## 1. Tasarım İlkeleri

Bu altı ilke, bir karar tartışmalı hale geldiğinde hakemdir.

### İ1 — Ürün sessiz olmalı, sadece sorun konuşmalı
Her şey yolundaysa arayüz nötrdür: gri, sakin, renksiz. Renk bir **sinyaldir**,
dekorasyon değil. Her satırı yeşil rozetle dolduran bir panelde yeşil hiçbir şey
ifade etmez; hata kırmızısı da gürültüye karışır. Vurgu bütçesi kısıtlıdır ve
öncelikle **başarısızlığa** harcanır.

### İ2 — Kullanıcının tek sorusu var: "Verim güvende mi?"
Genel Bakış sayfası bu soruya ilk 400 pikselde, tek cümleyle cevap verir.
Metrikler, tablolar, grafikler bu cevabın gerekçesidir — cevabın kendisi değil.

### İ3 — Yıkıcı eylem emek ister
Sil, buda (prune), üzerine yaz, depoyu kilidi aç. Bunlar rutin eylemlerle aynı
görsel sınıfta yer almaz, yan yana durmaz ve tek tıkla gerçekleşmez. Onay
diyaloğu **sonucu** anlatır ("47 snapshot kalıcı olarak silinecek"), eylemi
değil ("Emin misiniz?").

### İ4 — Yoğunluk bir özelliktir
Bu bir pazarlama sayfası değil, operasyon aracı. Sistem yöneticisi tabloya
bakar. Satırlar kompakt, sayılar hizalı, kimlikler monospace. Boş alan
cömertliği yerine **taranabilirlik** tercih edilir.

### İ5 — Süsleme yok
Gradyan yok, parıltı/glow yok, illüstrasyon yok, emoji yok, gereksiz animasyon
yok, "hoş görünsün" diye eklenmiş hiçbir öğe yok. Sofistike görünüm; doğru
kontrast, doğru hizalama ve tutarlı ritimden gelir, efektten değil.

### İ6 — Durum daima okunabilir olmalı
Kilitli bir depo, parolasız erişim açık bir anahtar, değişmezlik penceresi
içindeki bir snapshot — bunlar gizlenmez. Güvenlikle ilgili her durum, ilgili
nesnenin yanında ve **kelimeyle** görünür. Devre dışı bir butonun neden devre
dışı olduğu her zaman yazar.

---

## 2. Mevcut Panelde Tespit Edilen Tasarım Borcu

Yeniden tasarımın çözmesi gereken somut sorunlar (`wwwroot/app.css` ve
`Components/Pages/*.razor` üzerinden):

| # | Sorun | Nerede |
|---|---|---|
| 1 | Emoji ikon (`📁` / `📄`) — platforma göre değişiyor, hizalanmıyor, ciddiyet kırıyor | `Snapshots.razor:74` |
| 2 | `color: #666` koyu zeminde ~2,5:1 kontrast — pratikte okunmuyor | `Plans.razor:114` |
| 3 | Global `button { background: accent }` — her buton birincil, hiyerarşi yok | `app.css:155` |
| 4 | `:focus` stili hiç yok — klavyeyle gezinmek imkânsız | `app.css` geneli |
| 5 | Inline `style=` kullanımı bileşen sınırlarını eritiyor | `Plans.razor`, `Repositories.razor` |
| 6 | `.badge` bir banner yerine kullanılmış | `Snapshots.razor:36` |
| 7 | Zaman damgaları ham UTC (`ToString("u")`) | `Jobs`, `Snapshots`, `Restore` |
| 8 | Yükleniyor / boş / hata durumları her sayfada farklı ve çıplak `<p>` | tüm sayfalar |
| 9 | Sen/siz karışık ("izleyebilirsin" ↔ "seçin") | `Plans.razor:237` ↔ diğerleri |
| 10 | `StatusBadgeClass` iki sayfada kopyalanmış; `Running` "uyarı" rengi alıyor (yanlış anlam) | `Dashboard.razor:84`, `Jobs.razor:82` |
| 11 | Depo yolu tam haliyle metrik başlığı — uzun yolda düzen bozuluyor | `Dashboard.razor:24` |
| 12 | Diskte kalan boş alan hiçbir yerde görünmüyor (yedekleme ürünü için kritik) | — |

---

## 3. Renk

### 3.1 Yaklaşım
Tek bir token seti, iki tema. **Varsayılan koyu tema** (altyapı araçlarının
yerleşik dili, RDP'de göz yormuyor), **açık tema eş zamanlı** teslim edilir
(aydınlık ofis, projeksiyon, ekran görüntüsü paylaşımı). Tema `:root` üzerinde
`data-theme` ile değişir; bileşenler asla ham renk kullanmaz, yalnızca semantik
token kullanır.

Nötr rampa hafif mavi dökümlüdür (~220° ton). Ürünün karakteri nötrlerden
gelir; vurgu rengi karakter taşımaz, sadece eylem işaretler.

### 3.2 Nötr rampa

| Token | Koyu | Açık | Kullanım |
|---|---|---|---|
| `--sidebar` | `#07090C` | `#E7EBF0` | Kenar çubuğu — **en derin katman** |
| `--canvas` | `#0C0F13` | `#EFF2F5` | İçerik zemini |
| `--surface` | `#151920` | `#FFFFFF` | Kart, panel |
| `--surface-sunken` | `#10141A` | `#F3F5F8` | Kart içindeki çökük şerit: tablo başlığı, log, önizleme |
| `--surface-hover` | `#1B2029` | `#EEF1F5` | Satır/öğe hover |
| `--surface-raised` | `#1E242D` | `#FFFFFF` | Popover, dropdown, modal |
| `--border-subtle` | `#1E232B` | `#E3E7EC` | Tablo satır ayracı |
| `--border` | `#2A3039` | `#CFD5DD` | Kart, input kenarı |
| `--border-strong` | `#3A424E` | `#ADB6C1` | Bölüm ayracı, tablo başlık altı |
| `--text-muted` | `#6B7480` | `#79818C` | Üçüncül; asla tek başına anlam taşımaz |
| `--text-secondary` | `#98A2AF` | `#55606B` | Etiket, meta, tablo başlığı |
| `--text` | `#E4E8ED` | `#1A1F25` | Gövde metni |
| `--text-strong` | `#F5F7FA` | `#0B0F13` | Başlık, metrik değeri |

Doğrulanmış kontrastlar — koyu tema: `--text` / `--surface` = **14,3:1**;
`--text-secondary` / `--sidebar` = **7,7:1**; `--text-muted` / `--surface` =
**3,7:1**. Açık tema: sırasıyla **16,6:1**, **5,3:1**, **3,9:1**.

Açık temada katmanlar gölgeyle değil zeminin bir kademe koyulaşmasıyla ayrılır
(`sidebar < canvas < sunken < surface`); ilk sürümde canvas beyaza fazla
yakındı ve kart/zemin ayrımı yalnızca 1px kenarlığa kalıyordu.

**Üç kademeli derinlik.** Kenar çubuğu zeminden koyu, kart zeminden açıktır:
`sidebar < canvas < surface`. Bu, gölge kullanmadan katman hissi üretir ve
arayüzün "düz/boş" okunmasını engeller — koyu temada gölge çamur ürettiği için
(§5.3) derinliğin tek meşru kaynağı budur.

### 3.3 Semantik renkler

Her semantik rengin **üç** rolü vardır ve fazlası yoktur: `-fg` (metin/ikon),
`-bg` (yüzey, %12–14 alfa), `-border`.

| Rol | Koyu `-fg` | Açık `-fg` | Nerede |
|---|---|---|---|
| `--accent` | `#5B93F5` | `#2563C9` | Birincil buton, odak halkası, bağlantı, aktif nav, devam eden iş |
| `--success` | `#3FB950` | `#1A7F37` | Başarılı iş, doğrulanmış depo |
| `--warning` | `#D29922` | `#9A6700` | Kısmi başarı, süre dolmak üzere, disk azalıyor |
| `--danger` | `#F04E4E` | `#C62828` | Başarısız iş, yıkıcı eylem, anomali |

Kontrastlar (koyu, `--canvas` üzerinde): accent **6,4:1**, success **7,7:1**,
warning **7,7:1**. Hepsi metin için kullanılabilir.

**Kurallar:**
- Vurgu rengi (`--accent`) sadece dört yerde: birincil eylem, odak halkası,
  bağlantı, aktif navigasyon. Başlıkta, ikonda, çerçevede, arka planda **asla**.
- Beşinci bir semantik renk eklenmez. "Kilitli / değişmez" durumu ayrı renk
  değil, **nötr + kilit ikonu** ile anlatılır.
- Renk hiçbir zaman tek bilgi taşıyıcısı olamaz (bkz. §11).

### 3.4 Durum sözlüğü — tek kaynak

Uygulamadaki tüm durumlar bu tabloya uyar. `StatusBadgeClass` benzeri kopya
mantık kalmaz; tek bir `StatusChip` bileşeni bu haritayı uygular.

| Durum | Anlam | Renk | Biçim |
|---|---|---|---|
| `Succeeded` / Hazır / Doğrulandı | Tamam | success | ● nokta + metin, **dolgusuz** |
| `Running` | Sürüyor | accent | ● nokta + nabız + metin |
| `Pending` / Kuyrukta | Bekliyor | muted | ○ boş halka + metin |
| `Warning` / Kısmi | Dikkat | warning | **dolgulu rozet** |
| `Failed` | Hata | danger | **dolgulu rozet** |
| `Cancelled` / Atlandı | Nötr | muted | ● nokta + metin |
| Kilitli / Değişmez | Koruma altında | text-secondary | 🔒 ikon + metin |

Sadece `Warning` ve `Failed` dolgulu rozet alır — İ1'in somut hali. Yeşil
dolgulu rozet yoktur.

---

## 4. Tipografi

### 4.1 Yazı tipleri

```
--font-ui:   "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
--font-mono: "Cascadia Mono", Consolas, "Segoe UI Mono", monospace;
```

**CDN'den font çekilmez.** Bu, CLAUDE.md kural 7'nin (buluta hiçbir şey
gönderilmez) doğrudan sonucudur — `fonts.googleapis.com` bir ağ çağrısıdır.
Hedef Windows Server olduğu için Segoe UI Variable zaten yereldir, sıfır bayt
maliyeti vardır ve modern okunur. İleride Inter istenirse `wwwroot/fonts/`
altına `.woff2` olarak **gömülür**.

Monospace kullanım alanı sabittir: dosya yolları, snapshot/iş/blob kimlikleri,
hash'ler, cron ifadeleri, log konsolu. Başka hiçbir yerde.

### 4.2 Ölçek

Yedi adım. Fazlası tutarsızlık üretir.

| Token | Boyut / satır | Ağırlık | Kullanım |
|---|---|---|---|
| `--fs-display` | 22 / 28, `-0.01em` | 600 | Sayfa başlığı |
| `--fs-metric` | 28 / 32, `tabular-nums` | 600 | Metrik değeri |
| `--fs-title` | 16 / 24 | 600 | Kart/bölüm başlığı |
| `--fs-body` | 14 / 22 | 400 | Gövde, form değeri |
| `--fs-sm` | 13 / 20 | 400 | Tablo hücresi, ikincil metin |
| `--fs-xs` | 12 / 16, `+0.01em` | 500 | Etiket, tablo başlığı, rozet |
| `--fs-mono` | 13 / 20 | 400 | Yol, kimlik, log |

**Üç ağırlık:** 400, 500, 600. 700 kullanılmaz (mevcut `.brand` düzeltilecek).

**BÜYÜK HARF kullanılmaz** — mevcut `th { text-transform: uppercase }`
kaldırılır. Tablo başlığı: `--fs-xs`, 500, `--text-secondary`. Daha sakin ve
daha modern.

**Sayılar:** hizalanan her yerde `font-variant-numeric: tabular-nums`. Metrik
değerleri, tablo sayı sütunları, boyutlar, süreler.

---

## 5. Uzay, Geometri, Katman

### 5.1 Boşluk
4px taban ızgara. İzinli adımlar: `4, 8, 12, 16, 24, 32, 48`. Ara değer yok.

```
--space-1: 4px    --space-4: 16px
--space-2: 8px    --space-5: 24px
--space-3: 12px   --space-6: 32px   --space-7: 48px
```

### 5.2 Köşe yarıçapı
Sofistike/ciddi görünümün en güçlü kaldıracı. Mevcut 6–8px yumuşaklığı
düşürülür:

```
--radius-sm: 3px   /* buton, input, rozet, checkbox */
--radius-md: 6px   /* kart, panel, modal, drawer */
--radius-full       /* yalnızca durum noktası ve avatar */
```

12px ve üzeri yarıçap, "pill" biçimli butonlar yoktur.

### 5.3 Yüzey ve yükseklik
Dört katman, fazlası yok:

| Katman | Koyu temada ayrım | Açık temada ayrım |
|---|---|---|
| 0 — canvas | — | — |
| 1 — kart/panel | `--surface` + 1px `--border` | `--surface` + 1px `--border` |
| 2 — dropdown/popover | `--surface-raised` + 1px `--border-strong` | beyaz + `0 4px 12px rgba(16,24,40,.08)` |
| 3 — modal/drawer | `--surface-raised` + 1px `--border-strong` + arka plan örtüsü | beyaz + `0 16px 40px rgba(16,24,40,.16)` |

**Koyu temada gölge kullanılmaz.** Koyu zeminde gölge çamur üretir; ayrım
kenarlık ve bir kademe açıklıkla yapılır. Örtü (overlay): `rgba(0,0,0,.55)`
koyu, `rgba(16,24,40,.32)` açık.

---

## 6. Hareket

| Token | Değer | Nerede |
|---|---|---|
| `--dur-fast` | 120ms | hover, basma, renk geçişi |
| `--dur-base` | 180ms | drawer/modal açılış |
| `--dur-exit` | 100ms | kapanış (girişten hızlı) |
| `--ease-out` | `cubic-bezier(.2,0,0,1)` | giriş |
| `--ease-in` | `cubic-bezier(.4,0,1,1)` | çıkış |

**Kurallar**
- Yalnızca `opacity` ve `transform` animasyonlanır. `height`, `width`, `top`
  animasyonlanmaz.
- Liste/tablo satırları girerken animasyon yapmaz (veri titremesi yaratır).
- "Sürüyor" nabzı: tek noktada `opacity 1 → .45`, 2s, `ease-in-out`, sonsuz.
  Sayfada aynı anda en fazla bir grup nabız olur.
- Spinner yoktur. Sayfa yüklemesi = iskelet (skeleton); işlem = belirsiz ilerleme
  çubuğu (panelin üstünde 2px).
- `prefers-reduced-motion: reduce` → tüm süreler 0ms; yalnızca belirli
  (determinate) ilerleme çubuğu hareket etmeye devam eder.

---

## 7. Kabuk ve Sayfa Anatomisi

```
┌──────────────┬────────────────────────────────────────────────────────┐
│ SERVERBACKUP │  Sayfa başlığı                      [ikincil] [birincil]│  ← 56px sayfa başlığı
│              ├────────────────────────────────────────────────────────┤
│ GENEL        │                                                        │
│  Genel Bakış │   (varsa) sistem durumu bandı                          │
│              │                                                        │
│ KORUMA       │   içerik — --w-wide (1440px) veya --w-form (640px)     │
│  Planlar     │                                                        │
│  Depolar     │                                                        │
│              │                                                        │
│ VERİ         │                                                        │
│  Snapshot'lar│                                                        │
│  Geri Yükleme│                                                        │
│              │                                                        │
│ KAYITLAR     │                                                        │
│  İş Geçmişi  │                                                        │
│  Denetim ⏳  │                                                        │
├──────────────┤                                                        │
│ ⚙ Ayarlar    │                                                        │
│ DOMAIN\yusuf │                                                        │
│ v0.10.0      │                                                        │
└──────────────┴────────────────────────────────────────────────────────┘
```

### 7.1 Kenar çubuğu (232px, sabit)
- Marka: 14px/600, `--text-strong`, ikon yok, alt çizgi yok.
- Gruplar: `--fs-xs`, `--text-muted`, 500. Grup başlıkları tıklanamaz.
- Öğe: 32px yüksek, 12px yatay dolgu, `--radius-sm`, `--text-secondary`.
  - hover → `--surface-hover` + `--text`
  - aktif → `--surface-hover` + `--text-strong` + **sol 2px accent şerit**
    (mevcut "tüm satır mavi dolgu" yerine; daha sakin, İ1 uyumlu)
- **Sayaçlar:** `İş geçmişi` ve `Uyarılar` öğeleri sağa yaslı bir sayaç taşır.
  Açık uyarı varsa sayaç danger tonundadır — ürünün tek kalıcı alarm işareti.
- **Depo şeridi:** navigasyonun altında, `DEPOLAR` başlığıyla, depo başına tek
  satır: durum noktası + ad + disk doluluk yüzdesi. Her sayfada görünür.
- Alt bölge: Ayarlar, oturum açan kullanıcı (Windows Auth kimliği), sürüm.
- Kenar çubuğu daralmaz/gizlenmez. Tek bir düzen var, tek bir davranış var.
- **Arka planda desen yoktur** (bkz. §15 karar 6). Kenar çubuğunun dolu
  görünmesi dekorasyondan değil, yukarıdaki iki bilgi bloğundan gelir. Boşluk
  hissi bir bilgi eksikliği belirtisidir; desenle örtülmez, bilgiyle doldurulur.

### 7.2 Sayfa başlığı (56px)
Solda başlık (`--fs-display`) ve gerekiyorsa kırıntı yolu; sağda sayfa
eylemleri (en fazla bir birincil buton). Sticky. Altında 1px `--border`.

### 7.3 İçerik genişlikleri
```
--w-wide: 1440px   /* tablo sayfaları */
--w-form:  640px   /* form ve sihirbaz — satır uzunluğu kontrolü */
```
Mevcut 1100px sınırı 1920px ekranda tabloyu gereksiz sıkıştırıyor; formlar için
ise fazla geniş. İki container ile ikisi de çözülür.

---

## 8. Bileşen Kitaplığı

Hepsi `Components/Ui/` altında Razor bileşeni olur. **Kural: sayfa dosyalarında
`style=` özniteliği bulunmaz.**

### B1 — Button
| Varyant | Görünüm | Kullanım |
|---|---|---|
| `primary` | accent dolgu, beyaz metin | Görünür bölge başına **en fazla bir tane** |
| `default` | `--surface` + `--border` | Standart eylemler |
| `subtle` | şeffaf, hover'da `--surface-hover` | Tablo satır eylemleri, ikincil |
| `danger` | danger dolgu | Yalnızca onay diyaloğundaki son onay |
| `danger-outline` | danger metin + danger kenar | Yıkıcı eylemi **başlatan** buton |

Boy: `sm` 28px, `md` 32px. İkon+metin arası 6px. Tam genişlik yalnızca modal
altbilgisinde ve dar formlarda. Devre dışı buton `opacity .5` + `cursor:
not-allowed` + **her zaman** bir `title` ile gerekçe.

### B2 — Field (Label + Input/Select/Textarea)
32px yükseklik, 1px `--border`, `--radius-sm`, iç dolgu 8/10px.
Odak: `outline: 2px solid --accent; outline-offset: 1px` (glow değil).
Etiket üstte `--fs-xs` `--text-secondary`; yardım metni altta `--fs-xs`
`--text-muted`; hata altta `--danger-fg` + kenar `--danger-border`.

Genişlik sınıfları — global `width:100%` kaldırılır:
`--field-xs` 80px (saat, sayı) · `--field-sm` 160px · `--field-md` 280px ·
`--field-lg` %100 (yol, uzun metin).

Yol alanları `--font-mono` kullanır.

### B3 — Table
- Başlık satırı sticky, `--surface`, `--fs-xs`/500/`--text-secondary`, altında
  1px `--border-strong`.
- Satır **32px (kompakt — varsayılan)** / 40px (rahat, kullanıcı ayarı). Zebra
  **yok** — satır ayracı 1px `--border-subtle`. Hover `--surface-hover`.
- Hizalama: metin sola, **sayı sağa + tabular**, durum sola.
- Kimlik/yol sütunları monospace, **ortadan kısaltılır** (`C:\Veri\…\Muhasebe`),
  tam değer `title`'da.
- Satıra tıklamak sayfa değiştirmez; sağdan **drawer** açar (bağlam kaybolmaz).
- Toplu eylem gereken tablolarda (snapshot silme) checkbox sütunu; başka yerde
  yok.
- Boş / yüklenen / hatalı durum tablo gövdesinin **içinde** render edilir.

### B4 — Card / Panel
Başlık satırı (`--fs-title`) + isteğe bağlı açıklama (`--fs-sm`,
`--text-muted`) + sağda eylemler. Gövde dolgusu 16px. Gölge yok,
`--radius-md`.

### B5 — MetricTile
Üstte etiket (`--fs-xs`, `--text-muted`), ortada değer (`--fs-metric`), altta
bağlam satırı (`--fs-xs`). Izgara: `repeat(auto-fit, minmax(220px,1fr))`, en
fazla 4 sütun. Başlangıçta grafik yok.

### B6 — StatusChip
§3.4 tablosunu uygular. Nokta 6px `--radius-full`; rozet 20px yükseklik,
`--fs-xs`/500, 6px yatay dolgu, `--radius-sm`. Metin daima vardır.

### B7 — Banner (satır içi uyarı)
Container genişliğinde; 1px kenar + %12 alfa zemin, solda 16px ikon, başlık
(`--fs-sm`/600) + gövde (`--fs-sm`), sağda isteğe bağlı eylem bağlantısı. Dört
ton. Kullanım: "Depo append-only modda", "Son 3 gündür başarılı yedek yok",
"Değişmezlik penceresi aktif", "Disk %92 dolu".

### B8 — Toast
Sağ alt, 320px, üst üste en fazla 3. Başarı/bilgi 5sn sonra kaybolur; hata
kalıcıdır ve kapatma butonu vardır. **Eylem gerektiren hiçbir şey toast
değildir.**

### B9 — Dialog (modal)
480px (onay) / 640px (form). Başlık + gövde + altbilgi. Altbilgi eylemleri
sağa yaslı, birincil en sağda, "Vazgeç" solunda. Esc ve dış tık kapatır —
**yıkıcı diyaloglarda dış tık kapatmaz.**

Yıkıcı onay şablonu:
> **47 snapshot kalıcı olarak silinecek**
> `D:\Yedek\Muhasebe` deposundaki 2024-01-03 – 2024-08-01 aralığındaki
> snapshot'lar ve bunlara özel 12,4 GB veri geri döndürülemez şekilde silinir.
> Depo adını yazarak onaylayın: `[__________]`
> `[Vazgeç]` `[47 snapshot'ı sil]`

Depo seviyesi işlemlerde ad yazma zorunludur; snapshot seviyesinde değildir.

### B10 — Drawer
Sağdan, 480px (detay) / 720px (log içeren detay). Modal değil — arkadaki tablo
görünür ve okunabilir kalır (hafif örtü yok, sadece kenarlık + gölge/katman).
URL'ye parametre yazar (`/jobs?job=ab12…`) → paylaşılabilir. Esc kapatır.

### B11 — Progress
Belirli: 4px yükseklik, `--radius-full`, accent dolgu. **Altında her zaman
metin:** `1.284 / 3.204 dosya · 1,2 GB / 4,8 GB · ~3 dk kaldı`. Çubuk göz
içindir, anlam metindedir.
Belirsiz: panelin üst kenarında 2px kayan şerit.

### B12 — LogConsole
`--font-mono` 12,5/18, `--canvas` zemin, 1px `--border`, `--radius-sm`,
`max-height: 360px`, `overflow-y: auto`. Zaman damgası `--text-muted`; yalnızca
`HATA`/`UYARI` satırları renklenir, normal satırlar renksizdir. Sağ üstte
"otomatik kaydır" anahtarı ve "kopyala" butonu. `<br/>` ile değil, satır başına
bir eleman olarak render edilir.

### B13 — FileBrowser (Snapshot gezgini)
Tek panel. Üstte kırıntı yolu (ortadan kısaltmalı), altında satırlar:
`[ikon] ad · boyut(sağ,tabular) · değiştirilme · [satır eylemleri]`.
Klavye: `↑↓` gezin, `→`/`Enter` klasöre gir, `←`/`Backspace` üst klasör,
`Space` seç, `Ctrl+A` tümünü seç. Seçim varsa altta sabit bir çubuk:
"3 öğe seçildi · 412 MB — **Seçilenleri geri yükle**".

### B14 — Stepper (Geri yükleme sihirbazı)
Üstte yatay adımlar: `1 Depo — 2 Snapshot — 3 Kapsam — 4 Hedef — 5 Onay`.
Tamamlanan adım tıklanabilir, gelecek adım değil. Aktif adım `--text-strong`,
diğerleri `--text-muted`, bağlayıcı çizgi `--border`.

### B15 — EmptyState / Skeleton / ErrorState
Her veri görünümü **üç durumu da** tanımlamak zorundadır:
- **Yükleniyor:** 5 satırlık iskelet (nabızsız, sadece `--surface-hover` blok).
  "Yükleniyor..." metni kullanılmaz.
- **Boş:** tek satır açıklama + tek birincil eylem. Örn: "Bu depoda henüz
  snapshot yok. — **İlk yedeği al**"
- **Hata:** ne başarısız oldu + **Tekrar dene** + "Ayrıntıları kopyala".
  İstisna metni gövdede değil, ayrıntılar altında.

### B16 — SecretField
Maskeli input + göz ikonuyla göster/gizle. Yanında bağlam: depo parolasız
erişime açıksa alan hiç render edilmez, yerine kilit-açık ikonu + "Bu depo
gözetimsiz erişime açık (DPAPI)" satırı gelir. Güvenlik durumu daima yazıyla
görünür (İ6).

### B17 — İkon seti
16px ve 20px, 1,5px kontur, tek kaynak (Lucide tarzı), `wwwroot/icons.svg`
içinde **gömülü SVG sprite** olarak (CDN yok). Başlangıç seti (~20):
`database, hard-drive, calendar-clock, history, folder, file, chevron-right,
chevron-down, arrow-left, lock, unlock, shield-check, alert-triangle,
alert-octagon, check, x, play, pause, refresh-cw, download, trash-2, settings,
copy, search, more-horizontal`.
**Emoji kullanılmaz.**

---

## 9. Veri Gösterim Kuralları

| Veri | Kural | Örnek |
|---|---|---|
| Bayt | tr-TR, 1 ondalık, tabular, birim ayrık | `4,8 GB` · `912,4 MB` |
| Sayı | tr-TR binlik ayracı `.` | `3.204 dosya` |
| Tarih (tablo) | Yerel saat, mutlak, sabit format | `07.08.2026 03:00` |
| Tarih (başlık/özet) | Göreli + tooltip'te mutlak | `12 dk önce` |
| Süre | En anlamlı iki birim | `1 sa 12 dk` · `47 sn` |
| Yol | monospace, ortadan kısaltma, tam değer tooltip'te | `C:\Veri\…\Muhasebe` |
| Kimlik | monospace, ilk 8 karakter + kopyala ikonu | `ab12cd34` |
| Oran | Yüzde tam sayı + ham değerler yanında | `%38 (4,8 / 12,6 GB)` |

Ham UTC hiçbir yerde gösterilmez. Sunucu farklı saat diliminde çalışıyorsa
tablo başlığında bir kez saat dilimi belirtilir.

---

## 10. Dil ve Ton

### 10.1 Hitap
**Siz.** Tutarlı, profesyonel, mesafeli. Mevcut "izleyebilirsin" gibi kullanımlar
düzeltilir. Kişilik yok, şaka yok, ünlem yok.

### 10.2 Yazım
- **Cümle kılıfı** (sentence case) her yerde: başlık, buton, tablo başlığı.
  Başlık Kılıfı ve BÜYÜK HARF yok.
- Butonlar fiil: "Plan oluştur", "Geri yükle", "Doğrula". "Tamam"/"Gönder" gibi
  içeriksiz etiketler yok.
- Nokta: tam cümlelerde var, etiket ve butonlarda yok.

### 10.3 Hata mesajı şablonu
`[Ne oldu] + [Neden] + [Ne yapmalı]`

> ✗ `Hata: The process cannot access the file...`
> ✓ **Yedekleme tamamlanamadı** — `C:\Veri\db.mdf` başka bir işlem tarafından
> kilitli. VSS'i etkinleştirin veya bu yolu hariç tutun. *Ayrıntılar ▾*

Ham istisna metni her zaman "Ayrıntılar" altında; asla birincil mesaj olarak
değil.

### 10.4 Terim sözlüğü (bir kavram = bir kelime)

| Kavram | Kullanılacak | Kullanılmayacak |
|---|---|---|
| repository | **Depo** | havuz, arşiv, repo |
| snapshot | **Snapshot** | anlık görüntü, yedek noktası |
| job | **İş** | görev, task |
| plan | **Plan** | zamanlama, iş tanımı |
| retention | **Saklama politikası** | tutma, retention |
| prune | **Budama** | temizleme, GC |
| verify | **Doğrulama** | kontrol, sağlama |
| immutability window | **Değişmezlik penceresi** | kilit süresi |
| audit log | **Denetim kaydı** | log, günlük |
| source path | **Kaynak yol** | hedef, klasör |
| target | **Hedef dizin** | çıkış yolu |

`Pack`, `Blob`, `Chunk` gibi format terimleri yalnızca teknik detay
bölümlerinde (depo detay drawer'ı, doğrulama çıktısı) görünür; genel akışta
kullanıcıya gösterilmez.

---

## 11. Erişilebilirlik ve Klavye

- **Odak daima görünür:** 2px `--accent` outline, 1px offset. `outline: none`
  yazmak yasaktır.
- **Renk tek başına anlam taşımaz:** her durum noktası/rozeti yanında metin
  etiketi taşır.
- **Kontrast hedefleri:** gövde metni ≥ 7:1, ikincil ≥ 4,5:1, üçüncül ≥ 3:1
  (yalnızca kritik olmayan bilgi), kenarlıklar komşu yüzeye karşı ≥ 1,5:1.
- **Klavye:** `Tab` sırası görsel sırayla aynı; `Esc` katman kapatır;
  `Enter` form gönderir; tabloda `↑↓` satır gezinme; hiçbir yerde klavye tuzağı
  yok.
- **Dokunma/tıklama hedefi:** kompakt modda bile ≥ 28px yükseklik.
- Canlı güncellenen bölgeler (`İş Geçmişi` tablosu, log konsolu)
  `aria-live="polite"`.
- Tüm ikon-only butonlarda `aria-label`.

---

## 12. Sayfa Sayfa Tasarım

### 12.1 Genel Bakış (`/`)

**Cevaplaması gereken soru:** Verim güvende mi?

```
┌────────────────────────────────────────────────────────────┐
│ ✓ 3 deponun 3'ü korunuyor · en son 12 dk önce               │  ← karar bandı
├────────────────────────────────────────────────────────────┤
│ [Korunan veri] [Depoda kapladığı] [Son 24s iş] [En eski snapshot]│
│  1,4 TB        412 GB (×3,4)      18 ✓ / 0 ✗   93 gün       │
├──────────────────────────────┬─────────────────────────────┤
│ Son işler                    │ Depolar                     │
│ (son 8, kompakt tablo)       │ (depo başına: son başarı,   │
│                              │  disk doluluk çubuğu, kilit)│
└──────────────────────────────┴─────────────────────────────┘
```

- **Karar bandı** ürünün en önemli tasarım öğesi. Üç hali var: tamam (nötr
  zemin + success nokta), dikkat (warning banner), sorun (danger banner + ilk
  eylem). Hesabı: her deponun son başarılı işi, planın beklenen sıklığına göre
  gecikmiş mi?
- Metrik başlığı depo yolu **değil**; depo görünen adı. Yol altta monospace ve
  kısaltılmış.
- Disk doluluk çubuğu her depo satırında — %85 üstü warning, %95 üstü danger.
  (Şu an hiçbir yerde yok; yedekleme ürünü için kabul edilemez bir eksik.)
- **Son 90 gün şeridi** (sayfanın altında, tam genişlik): depo başına 90 hücre,
  gün başına bir yedekleme sonucu. **Başarılı gün nötr gridir**, yalnızca kısmi
  (warning) ve başarısız (danger) günler renklidir; yedek alınmayan gün soluk
  kalır. 90 hücreyi yeşile boyamak İ1'i çiğner ve tek bir kırmızı günü görünmez
  kılar. Bu şerit aynı zamanda sayfanın alt boşluğunu dekorasyonsuz doldurur.

### 12.2 Depolar (`/repositories`)

Tablo: `Ad · Yol · Durum · Erişim · Snapshot · Boyut · Disk · Son doğrulama`.
Satır tıklaması → **drawer**:
- Özet: format sürümü, oluşturma tarihi, şifreleme parametreleri (salt okunur,
  monospace)
- Kullanım: pack / blob / dedup oranı, disk doluluk
- Güvenlik: gözetimsiz erişim durumu, append-only, değişmezlik penceresi, ACL
  durumu — hepsi yazıyla (İ6)
- Doğrulama geçmişi
- Eylemler: `Yedek al` · `Doğrula` · `Buda` (danger-outline)

Mevcut "satırın altına form açan" desen kaldırılır; `Yedek al` bir dialog
açar (kaynak yollar + parola + başlat).

### 12.3 Planlar (`/plans`)

Tablo: `Plan · Depo · Zamanlama (insan diliyle) · Kaynaklar · Saklama · Son çalışma · [⋯]`.

**Zamanlama sütunu ham cron göstermez.** "Hafta içi 08:00–20:00 arası 2 saatte
bir" yazar; cron ifadesi tooltip'te. Bu, mevcut `CronBuilder` çalışmasının
görünür karşılığıdır.

`Yeni plan` → 640px dialog, üç bölüm:
1. **Kaynaklar** — satır listesi (ekle/çıkar), her satır bir yol + hariç tutma
   deseni. Virgülle ayrılmış tek metin kutusu kaldırılır.
2. **Zamanlama** — varsayılan basit mod (gün çipleri + saat aralığı + sıklık).
   Cron, "Gelişmiş" başlığı altında gizli. Her iki modda da altta canlı
   önizleme: **"Sonraki çalışmalar: bugün 14:00 · 16:00 · 18:00"** — kullanıcı
   doğrulamasını cron sözdiziminden değil sonuçtan yapar.
3. **Saklama** — GFS alanları tek satırda: `son N · saatlik · günlük · haftalık
   · aylık · yıllık`. Altında canlı önizleme: **"Bu politikayla mevcut 47
   snapshot'ın 12'si korunur, 35'i budanır."** Sayı hesaplanamıyorsa alan boş
   kalır, tahmin uydurulmaz.

### 12.4 İş Geçmişi (`/jobs`)

Üstte filtre çubuğu: depo · durum · tür · tarih aralığı · arama.
Sağ üstte yenileme göstergesi: "5 sn'de bir yeniliyor · **duraklat**" — sessiz
otomatik yenileme yerine görünür ve durdurulabilir davranış.

Tablo: `Durum · İş · Plan · Depo · Başlangıç · Süre · Okunan · Yazılan`.
Hata metni tabloda **görünmez** (satırı bozuyor); durum rozeti danger olur,
satır tıklaması drawer açar:
- Özet istatistikler: taranan/okunan/yazılan/dedup edilen, ortalama hız
- Canlı log konsolu (B12)
- Hata varsa: şablonlu mesaj + "Ayrıntılar" + "Tekrar dene"

### 12.5 Snapshot Gezgini (`/snapshots`)

Depo seçici sayfa başlığındaki kontrol çubuğuna taşınır (kart içinde değil).
Depo kilitliyse tablo yerine **kilit durumu**: kilit ikonu + "Bu depo parola
korumalı" + `Kilidi aç` → dialog. Mevcut `<p class="badge badge-err">` hata
gösterimi kaldırılır, Banner (B7) kullanılır.

Snapshot listesi: `Tarih · Plan · Dosya · Boyut · Değişim · Koruma · [⋯]`.
"Değişim" sütunu önceki snapshot'a göre eklenen/değişen dosya sayısı — Faz 11
anomali tespitinin görsel zeminini şimdiden hazırlar. "Koruma" sütunu
değişmezlik penceresi içindeki snapshot'larda kilit ikonu gösterir.

Snapshot açılınca FileBrowser (B13). Satır eylemleri: `Bu dosyayı geri yükle`,
`Sürüm geçmişi` (drawer: aynı yolun tüm snapshot'lardaki halleri, boyut ve
tarih ile). Çoklu seçim → alt çubuk → Geri yükleme sihirbazına kapsam devrederek
geçiş.

### 12.6 Geri Yükleme (`/restore`)

640px, 5 adımlı sihirbaz (B14):

1. **Depo** — kilitliyse burada açılır
2. **Snapshot** — tarih listesi + "belirli bir tarihteki hali" seçeneği
3. **Kapsam** — tümü / seçili yollar (Gezgin'den gelinmişse ön dolu)
4. **Hedef** — orijinal konum ⚠️ / alternatif dizin. Üzerine yazma politikası
   radyo grubu, her seçeneğin sonucu düz Türkçe yazılı:
   - "Var olan dosyaları atla" — hedefteki dosyalara dokunulmaz
   - "Yalnızca daha eskiyse üzerine yaz"
   - "Her zaman üzerine yaz" — *hedefteki değişiklikler kaybolur*
5. **Onay** — **güvenlik anı.** Yapılacak işlemin tam dökümü: kaynak snapshot,
   dosya sayısı ve boyut, hedef yol, üzerine yazma politikası, ACL'lerin
   uygulanıp uygulanmayacağı. Tek birincil buton: `Geri yüklemeyi başlat`.

Çalışırken: ilerleme (B11) + canlı log (B12). Bitince sonuç paneli: yazılan
dosya sayısı, boyut, süre, atlanan/başarısız dosyalar ve `Doğrula` eylemi.

---

## 13. Uygulama Yol Haritası

Her adım kendi başına sevk edilebilir; panel hiçbir adımda bozuk kalmaz.

### R0 — Token katmanı
`wwwroot/app.css` → `wwwroot/css/tokens.css` + `base.css`.
Tüm renk/tipografi/boşluk/yarıçap token'ları, iki tema, reset, odak stilleri,
`prefers-reduced-motion`. Görsel yeniden tasarım yok; yalnızca değerler
token'lara taşınır. **Çıktı:** §3–6'nın kod karşılığı.
*Yan fayda: §2'deki 2, 4, 10 numaralı borçlar burada kapanır.*

### R1 — Kabuk
Kenar çubuğu grupları ve aktif durum şeridi, sayfa başlığı bileşeni, iki
container genişliği, ikon sprite'ı, tema anahtarı. Emoji ikonlar kaldırılır.

### R2 — Bileşen kitaplığı
`Components/Ui/` altında B1–B16. Sayfalar bileşenlere geçirilir, tüm inline
`style=` temizlenir, `StatusChip` iki kopya `StatusBadgeClass` mantığını
değiştirir. Kritik bileşenler için bUnit testleri (Faz 10 DoD gereği).

### R3 — Sayfaların yeniden yapımı
§12 sırasıyla: Genel Bakış (karar bandı + disk doluluk) → Depolar (drawer) →
Planlar (dialog + sonraki çalışmalar + GFS önizleme) → İş Geçmişi (filtre +
drawer) → Snapshot Gezgini (klavye + sürüm geçmişi) → Geri Yükleme (5 adım).

### R4 — Cila
Açık tema kontrast denetimi, tr-TR biçimlendirme yardımcıları (bayt/tarih/
sayı tek yerden), tüm görünümlerde üç durumun (boş/yüklenen/hata) tamamlanması,
yoğunluk anahtarı (rahat/kompakt), klavye gezinme denetimi, terim sözlüğü
uyumu için metin taraması.

---

## 14. Gelecek Özelliklerin Tasarımı (Faz 11 · 12)

Bu yüzeyler henüz kodlanmadı; ama tasarım dili bugünden onları kaldıracak
şekilde kurulmalı. Aşağıdakiler §8'deki bileşenlerin ötesinde **yeni** desen
gerektirenlerdir.

### G1 — Denetim kaydı *(Faz 11)*
Yeni sayfa: `KAYITLAR > Denetim`. Tablo: `Zaman · Kullanıcı · Eylem · Nesne ·
Kaynak IP`. Satır → drawer'da tam kayıt (monospace).
**Tasarım kuralı:** bu sayfada hiçbir silme/düzenleme yüzeyi bulunmaz ve
üstünde kalıcı bir nötr banner durur: "Denetim kayıtları değiştirilemez." Bir
şeyin değişmez olduğunu iddia eden ürün, arayüzünde de değiştirme yolu
sunmamalıdır. Dışa aktarma (CSV) tek eylemdir.

### G2 — Append-only ve değişmezlik penceresi *(Faz 11)*
- Depo satırında ve depo drawer'ında: `🔒 Kilitli · 14 gün` chip'i.
- Pencere içindeki snapshot'larda kilit ikonu; sil butonu **gizlenmez, devre
  dışı bırakılır** ve tooltip gerekçeyi söyler: "3 Eylül 2026'ya kadar
  silinemez (değişmezlik penceresi: 14 gün)". Görünmeyen kısıt, kullanıcının
  ürüne güvenmesini engeller.
- Pencere ayarı Ayarlar > Güvenlik altında; azaltma yönünde değişiklik ek onay
  ister.

### G3 — Anomali / fidye yazılımı uyarısı *(Faz 11)*
Üründeki **en yüksek önem seviyesi**. Tek yerde durmaz:
- Onaylanana kadar **her sayfada** sayfa başlığının üstünde danger banner.
- Yeni `Uyarılar` sayfası: her uyarı bir kart — kanıtla birlikte. "Bu
  snapshot'ta 12.400 dosyanın 11.200'ü değişti (önceki ortalama: 340). Yeni
  uzantı dağılımı: `.locked` %94."
- **Grafiğin hak ettiği tek yer:** snapshot başına değişim yoğunluğu — son 30
  snapshot'ın küçük bir sütun grafiği, anomali sütunu danger renginde.
- İki eylem: `İşi durdur ve depoyu kilitle` (primary danger) ·
  `Yoksay ve devam et` (subtle, **gerekçe metni zorunlu**, denetim kaydına yazılır).

### G4 — Bildirimler *(Faz 11)*
Ayarlar > Bildirimler. Kart değil **matris tablo**: satırlar olay (iş
başarısız, iş uyarılı, anomali, doğrulama hatası, disk azaldı, sertifika
bitiyor), sütunlar kanal (E-posta, Windows Event Log). Hücreler checkbox.
Altında SMTP ayarları + `Test bildirimi gönder` + son gönderim durumu.

### G5 — Ayarlar *(Faz 11)*
Sayfa içi sol alt-navigasyon: `Genel · Güvenlik · Bildirimler · Kaynak ve IO ·
Servis · Hakkında`. Her ayar satırı: solda etiket + tek satır açıklama, sağda
kontrol. **Otomatik kayıt yok** — değişiklikler biriktirilir, altta sabit bir
çubuk çıkar: "3 değişiklik · `Vazgeç` `Kaydet`". Güvenlik ayarları sessizce
kaydedilmemelidir.

### G6 — Uygulama tutarlılığı / SQL Server VSS *(Faz 12-1)*
Plan dialog'una dördüncü bölüm: **Uygulama tutarlılığı**. Sunucuda bulunan VSS
writer'ları listelenir (checkbox). Servis hesabının yetkisi yoksa bölüm
başında warning banner: neyin eksik olduğu + hangi yetkinin gerektiği.

### G7 — USN Journal hızlı tarama *(Faz 12-2)*
Plan başına bir anahtar + tek satır açıklama. **Asıl tasarım işi iş
detayında:** "Tarama: USN Journal · 12 sn (tam tarama tahmini: ~4 dk)".
Performans özelliği, kazancını göstermezse kullanıcı ona güvenmez ve kapatır.

### G8 — Depo hedefleri: SMB/NAS ve rotasyonlu USB *(Faz 12-3)*
Yeni **depo oluşturma sihirbazı** — tür seçimiyle başlar:
`Yerel disk · SMB paylaşımı · Rotasyonlu USB seti`.

Rotasyonlu USB, üründeki en yeni arayüz yüzeyi:
```
Disk seti "Haftalık Rotasyon"                       [Disk ekle]
┌────────────────────────────────────────────────────────────┐
│ ● HAFTA-1  takılı    son yazım 12 dk önce   03.05–07.08     │
│ ○ HAFTA-2  çıkarıldı son yazım 7 gün önce   26.04–31.07     │
│ ○ HAFTA-3  çıkarıldı son yazım 14 gün önce  19.04–24.07     │
└────────────────────────────────────────────────────────────┘
```
Beklenen disk takılı değilse iş "Disk bekleniyor" durumunda kalır — bu, durum
sözlüğüne eklenecek **yeni bir durumdur** (nötr, boş halka, açıklama metniyle).

### G9 — IO / bant genişliği kısıtlama *(Faz 12-4)*
Zamana bağlı bir ayar, zamana benzeyen bir kontrol ister: **24×7 ızgara**
(sütun = saat, satır = gün). Hücrenin üç hali: tam hız (nötr) · kısıtlı
(accent, açık) · duraklat (çapraz tarama). Sürükleyerek boyanır. Altında özet:
"Hafta içi 09:00–18:00 kısıtlı (20 MB/s), diğer saatler tam hız."

### G10 — Uzak ajanlar *(Faz 12-5)*
Yeni üst seviye nav öğesi: `Ajanlar`. Tablo: `Makine · Sürüm · Son görülme ·
Sertifika bitişi · Durum`. Kayıt akışı: tek kullanımlık token üretimi →
ajanın parmak izini gösteren **doğrulama ekranı** (kullanıcı iki tarafta aynı
parmak izini görüp onaylar) → kabul. Sertifika bitişine 30 gün kala depo
satırında ve Genel Bakış'ta warning banner.

### G11 — System State *(Faz 12-6)*
Plan kaynakları bölümünde yol değil, **tür**: "Sistem durumu (kayıt defteri,
AD, sertifika deposu)" onay kutusu + tahmini boyut.

### G12 — Komut paleti (`Ctrl+K`) *(nav öğesi 8'i geçince)*
Plan, depo, snapshot, dosya yolu araması + eylem çalıştırma ("yedek al",
"doğrula"). Navigasyon sayısı azken erken bir soyutlamadır; eklenmez.

### G13 — Depo sağlık takvimi
Depo drawer'ında son 90 günün doğrulama sonuçları: gün başına küçük bir kare,
üç hal (doğrulandı / doğrulanmadı / başarısız). Doğrulama geçmişi aksi halde
tamamen görünmez kalır; bu şerit onu tek bakışta anlatır.

---

## 15. Kararlar

Tamamı `docs/ui-prototype.html` üzerinden karşılaştırılarak verildi.

| # | Karar | Sonuç |
|---|---|---|
| 1 | Tema | **Çift tema**, koyu varsayılan. Token disiplini varken maliyeti düşük; tek temaya sonradan inmek kolay, çıkmak zor. |
| 2 | Yazı tipi | **Sistem yazı tipi** (Segoe UI Variable + Cascadia Mono). Sıfır bayt, sıfır ağ çağrısı, Windows'ta yerel. |
| 3 | Varsayılan yoğunluk | **Kompakt (32px satır)**. Rahat (40px) kullanıcı ayarı olarak kalır. |
| 4 | "Snapshot" Türkçeleştirilsin mi? | **Hayır** — hedef kitle terimi bu haliyle kullanıyor, `docs/format-spec.md` ile tutarlı kalır. |
| 5 | Grafik/chart | **Üç yerde:** Genel bakış 90 gün şeridi (§12.1), anomali değişim yoğunluğu (G3), depo sağlık takvimi (G13). Başka hiçbir yerde yok. |
| 6 | Kenar çubuğu arka planı | **Desen yok.** Gerekçe aşağıda. |
| 7 | Vurgu rengi · nav aktif · durum · yarıçap · kart | Sırasıyla **mavi · sol şerit · nokta+metin · keskin (3/6px) · kenarlıklı**. |

**Karar 6'nın gerekçesi.** Kenar çubuğu ilk taslakta boş hissettiriyordu ve
arkasına desen koymak gündeme geldi. Reddedildi, çünkü:

- Tekrar eden bir desen İ5'i (süsleme yok) doğrudan çiğner ve tasarımın en
  hızlı eskiyen parçası olur.
- Desen, kenar çubuğundaki *en düşük kontrastlı* metinle — nav öğeleriyle —
  yarışır.
- Grain/gürültü dokusu ayrıca teknik risk taşır: RDP'nin video sıkıştırması
  ince gürültüyü bantlamaya çevirir ve bu ürün büyük ölçüde RDP üzerinden
  kullanılır.

Boşluk hissi bir **bilgi eksikliği** belirtisiydi, dekorasyon eksikliği değil.
Üç yapısal düzeltmeyle kapatıldı: nav sayaçları + depo sağlık şeridi (§7.1),
üç kademeli yüzey derinliği (§3.2), Genel bakış'ta 90 gün şeridi (§12.1).

İleride yine de bir doku istenirse tek savunulabilir seçenek, kenardan taşan
**kırpılmış marka glifi** (%5 opaklık, tekrarsız) olur — desen değil marka
olduğu için. Nokta ızgarası ve grain kalıcı olarak elenmiştir.
