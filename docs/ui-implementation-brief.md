# Arayüz Uygulama Brief'i — Sonnet için

Bu belge, `docs/design-system.md`'de tanımlanan tasarım dilinin panele
uygulanma **sırasıdır**. Amaç: uygulayan modelin hiçbir tasarım kararı
vermek zorunda kalmaması.

**Girdi dosyaları**
- `docs/design-system.md` — tasarım dilinin tamamı (ilkeler, token'lar, bileşen
  spec'leri, sayfa sayfa tasarım). **Tek doğruluk kaynağı budur.**
- `docs/ui-prototype.html` — çalışan prototip. İçindeki `.sb-*` CSS'i ve HTML
  yapısı, porte edilecek koddur. `.rv-*` ile başlayan her şey inceleme
  sayfasının kendi kabuğudur — **porte edilmez.**
- Bu belge — hangi sırayla, hangi dosyaya, hangi imzayla.

---

## SEÇİMLER — kilitli

```
Tema             Koyu varsayılan + açık tema (ikisi de teslim)
V1 Vurgu rengi   Mavi (#5B93F5 koyu / #2563C9 açık)
V2 Nav aktif     Sol şerit — 2px accent
V3 Durum         Nokta + metin (yalnızca hata/uyarı dolgulu)
V4 Yoğunluk      Kompakt — 32px satır (rahat 40px kullanıcı ayarı olarak kalır)
V5 Yarıçap       Keskin — 3px / 6px
V6 Kart          Kenarlıklı kart
V7 Kenar çubuğu  Düz — desen yok
```

Bu tablo `docs/design-system.md` §15 ile aynıdır. **Sapma yok.** Bir varyantın
kodda karşılığı yoksa prototipe (`docs/ui-prototype.html`) bak; orada da yoksa
dur ve kullanıcıya sor.

**Silinecek varyant dalları.** Prototip yedi varyantın hepsini içeriyor.
Porte ederken yalnızca yukarıdaki dal tutulur; şunlar **silinir**:
`[data-nav="fill"]`, `[data-nav="quiet"]`, `[data-status="badge"]`,
`[data-status="stripe"]`, `[data-radius="soft"]`, `[data-card="flat"]`,
`[data-sidebar="*"]` ve `.sb-side-glyph`.
**Kalır:** `[data-density="comfy"]` — 40px satır bir kullanıcı ayarıdır (R4).
Kompakt taban değerdir, `comfy` onu ezer.

---

## Değişmez Kurallar (bu iş için)

Bunlar `CLAUDE.md`'nin doğrudan sonuçlarıdır, gevşetilmez:

1. **Yeni NuGet paketi yok.** Tailwind, Bootstrap, MudBlazor, Radzen, Blazorise
   — hiçbiri eklenmez. Düz CSS + Razor bileşeni. Bir kütüphane gerektiğini
   düşünüyorsan **önce kullanıcıya sor** (CLAUDE.md kural 8).
2. **CDN yok.** Font, ikon, script, stil — hiçbiri dış URL'den çekilmez
   (CLAUDE.md kural 7). İkonlar `wwwroot/icons.svg` içinde gömülü SVG sprite.
3. **Kullanıcıya görünen metin Türkçe, kod ve yorumlar İngilizce.**
4. **`TreatWarningsAsErrors=true`.** Build uyarısız geçmek zorunda.
5. **Test yazılmadan adım kapanmaz** (CLAUDE.md kural 6). Bileşenler için bUnit.
6. **Yorum sadece WHY için.** `// button component` gibi WHAT yorumu yazma.
7. **YAGNI.** Üç benzer satır, erken bir soyutlamadan iyidir. Bu yüzden tablo
   için generic bir `SbDataTable<T>` **yazılmaz** — tablolar düz markup + CSS
   sınıfıdır.

## Karar Verme Yasağı

Aşağıdakiler hakkında karar verme; `docs/design-system.md`'ye bak. Orada da
yoksa **dur ve kullanıcıya sor**:

- Renk değeri, yazı tipi boyutu, boşluk, yarıçap
- Bir durumun hangi renge karşılık geldiği (§3.4 durum sözlüğü)
- Türkçe terim seçimi (§10.4 terim sözlüğü)
- Bir sayfada hangi bilginin gösterileceği (§12)
- Yeni bir bileşen türü ekleme

---

## R0 — Token katmanı

**Dosyalar**
- Sil: `src/ServerBackup.Service/wwwroot/app.css`
- Oluştur: `wwwroot/css/tokens.css` — `docs/ui-prototype.html` içindeki
  `1. TOKENS` bloğunun birebir kopyası, seçilen varyanta göre sadeleştirilmiş
  (bkz. aşağı)
- Oluştur: `wwwroot/css/base.css` — prototipteki `2. TEMEL` bloğu
- Güncelle: `Components/App.razor` — iki stil dosyasını sırayla ekle

**Varyant sadeleştirmesi:** SEÇİMLER bölümündeki silme listesini uygula.

**Yoğunluk tersine çevrilir.** Prototipte taban `--row-h: 40px` ve
`[data-density="compact"]` onu 32px'e indiriyor. Üründe **tam tersi** olacak:
```css
:root                      { --row-h: 32px; --row-py: 6px; }   /* kompakt = taban */
:root[data-density="comfy"]{ --row-h: 40px; --row-py: 10px; }  /* kullanıcı ayarı */
```

**Yeni token:** `--sidebar` (koyu `#07090C` / açık `#EFF1F5`) ve güncellenmiş
`--canvas` / `--surface` değerleri — `design-system.md` §3.2. Üç kademeli
derinlik (`sidebar < canvas < surface`) gölge kullanmadan katman hissi üretir;
bu bir ince ayar değil, tasarımın "düz/boş" okunmasını engelleyen yapısal
karardır.

**Kabul kriterleri**
- Panel açılıyor, hiçbir sayfa görsel olarak bozulmuş değil
- `:focus-visible` her etkileşimli öğede görünür
- `data-theme="light"` ve `data-theme="dark"` ikisi de çalışıyor
- `Plans.razor:114`'teki `color: #666` kaldırıldı
- `prefers-reduced-motion` bloğu var

**Commit:** `refactor(ui): tasarım token katmanı — tokens.css + base.css`

---

## R1 — Kabuk

**Dosyalar**
- `Components/Layout/MainLayout.razor` — `.sb-app` ızgarası
- `Components/Layout/NavMenu.razor` — gruplu navigasyon (design-system §7.1),
  alt bölgede Windows Auth kimliği + sürüm
- Oluştur: `Components/Layout/PageHeader.razor`
  ```
  [Parameter] string Title
  [Parameter] RenderFragment? Actions
  ```
- Oluştur: `wwwroot/icons.svg` — prototipteki `<defs>` bloğunun tamamı
- Oluştur: `Components/Ui/SbIcon.razor`
  ```
  [Parameter, EditorRequired] string Name    // "db", "lock", ...
  [Parameter] bool Small
  ```

**Kenar çubuğu bilgi taşır** (`design-system.md` §7.1) — bu R1'in yarısıdır,
opsiyonel süs değil:
- `İş geçmişi` ve `Uyarılar` öğelerinde sağa yaslı sayaç; açık uyarı varsa
  sayaç danger tonunda
- Navigasyonun altında `DEPOLAR` şeridi: depo başına durum noktası + ad + disk
  doluluk yüzdesi, her sayfada görünür
- Arka planda desen **yok** (§15 karar 6)

**Kabul kriterleri**
- Kenar çubuğu grupları: GENEL / KORUMA / VERİ / KAYITLAR / DEPOLAR + alt bölge
- Nav sayaçları ve depo şeridi canlı veriden besleniyor (sabit değer değil)
- Aktif nav öğesi SEÇİMLER'deki V2 davranışını gösteriyor
- `Snapshots.razor`'daki `📁` / `📄` emoji'leri `SbIcon` ile değişti
- İçerik `--w-wide` (1440px) veya `--w-form` (640px) container'ında

**Commit:** `feat(ui): uygulama kabuğu — gruplu navigasyon, sayfa başlığı, ikon seti`

---

## R2 — Bileşen kitaplığı

Hepsi `Components/Ui/` altında. İmzalar **aynen** bunlar:

```
SbButton.razor      Variant(Primary|Default|Subtle|Danger|DangerOutline)=Default
                    Size(Sm|Md)=Md, Icon(string?), Disabled(bool),
                    DisabledReason(string?)  → title özniteliği, boş bırakılmaz
                    OnClick(EventCallback), ChildContent

SbField.razor       Label(string), Hint(string?), Error(string?),
                    Width(Xs|Sm|Md|Lg)=Lg, ChildContent

SbStatusChip.razor  Status(string), Text(string?)   // Text null ise Türkçe karşılık
SbCard.razor        Title(string?), Description(string?), HeaderActions(RenderFragment?),
                    Flush(bool)=false, ChildContent
SbMetric.razor      Label(string), Value(string), Sub(string?)
SbBanner.razor      Tone(Ok|Warn|Err|Neutral), Title(string), Body(string?),
                    Icon(string?), Action(RenderFragment?)
SbDrawer.razor      Title(string), Open(bool), OnClose(EventCallback), ChildContent
SbDialog.razor      Title(string), Description(string?), Open(bool),
                    OnClose(EventCallback), Size(Sm|Md)=Md,
                    Footer(RenderFragment), ChildContent
SbConfirmDialog.razor  Title, Consequence(string), ConfirmWord(string?),
                    ConfirmLabel(string), OnConfirm(EventCallback)
                    // ConfirmWord doluysa kullanıcı onu yazmadan onay butonu açılmaz
SbProgress.razor    Value(long), Max(long), Text(string)
SbLogConsole.razor  Lines(IReadOnlyList<LogLine>)   // record LogLine(DateTimeOffset At, string Level, string Message)
SbEmptyState.razor  Message(string), ActionText(string?), OnAction(EventCallback)
SbSkeleton.razor    Rows(int)=5
```

**Tablolar bileşen değildir** — `docs/ui-prototype.html`'deki `.sb-table`
markup'ı ve sınıfları doğrudan sayfalarda kullanılır.

**Ek dosya:** `src/ServerBackup.Service/Formatting/Fmt.cs` — statik sınıf,
`CultureInfo.GetCultureInfo("tr-TR")` ile:
```
Bytes(long) → "4,8 GB"
Number(long) → "3.204"
DateTime(DateTimeOffset) → "07.08.2026 03:00"   // yerel saat, UTC değil
Relative(DateTimeOffset) → "12 dk önce"
Duration(TimeSpan) → "1 sa 12 dk"
TruncatePath(string, int) → "C:\Veri\…\Muhasebe"
```

**Durum eşlemesi:** `Dashboard.razor` ve `Jobs.razor`'daki kopya
`StatusBadgeClass` metotları silinir; tek kaynak `SbStatusChip`. Eşleme
`design-system.md` §3.4 tablosudur — `Running` **accent**'tır, warning değil.

**Kabul kriterleri**
- Sayfa dosyalarında `style=` özniteliği kalmadı (grep ile doğrula)
- `SbButton` devre dışıyken `DisabledReason` boşsa build/test hatası verir
- bUnit testleri: `SbStatusChip` yedi durumu doğru sınıfa eşliyor;
  `SbConfirmDialog` yanlış kelimeyle onaylanmıyor; `Fmt` tr-TR biçimleri
- `dotnet test` yeşil

**Commit:** `feat(ui): bileşen kitaplığı + tr-TR biçimlendirme yardımcıları`

---

## R3 — Sayfaların yeniden yapımı

Her sayfanın hedefi `design-system.md` §12'de. Sıra ve dikkat noktaları:

| # | Sayfa | Yeni davranış | §12 |
|---|---|---|---|
| 1 | Genel Bakış | Karar bandı (3 hâl) + 4 metrik + son işler + depo sağlığı **disk doluluk çubuğuyla** + **90 gün şeridi** (başarılı gün nötr gri, yalnızca sorunlu gün renkli) | 12.1 |
| 2 | Depolar | Satır → drawer; satır-altı form kaldırılır, `Yedek al` bir dialog | 12.2 |
| 3 | Planlar | `Yeni plan` dialog'a taşınır; zamanlama sütunu **insan diliyle**; "Sonraki çalışmalar" önizlemesi; GFS saklama editörü + korunacak snapshot önizlemesi | 12.3 |
| 4 | İş Geçmişi | Filtre çubuğu; görünür/duraklatılabilir yenileme; hata metni tablodan drawer'a | 12.4 |
| 5 | Snapshot Gezgini | Kilit durumu ekranı; klavye gezinme; sürüm geçmişi; çoklu seçim çubuğu | 12.5 |
| 6 | Geri Yükleme | 5 adımlı sihirbaz; adım 5 = tam özet; sonuç paneli | 12.6 |

**Her sayfa için zorunlu:** yükleniyor (`SbSkeleton`), boş (`SbEmptyState`),
hata (§10.3 şablonu) — üçü de tanımlı olacak.

**Yeni motor davranışı gerekirse:** disk boş alan sorgusu, "sonraki N cron
çalışması", "bu politikayla kaç snapshot korunur" hesabı — bunlar `Engine`
tarafında saf, test edilebilir metotlar olarak yazılır ve **testi yazılır**.
UI'da hesap yapılmaz.

**Commit (sayfa başına bir tane):** `feat(ui): <sayfa> yeniden tasarım`

---

## R4 — Cila

- Açık tema kontrast denetimi; ölçülen değerler `design-system.md` §3.2
  tablosuna yazılır
- Yoğunluk anahtarı (rahat / kompakt), tercih `localStorage`'da
- Klavye turu: Tab sırası, Esc, tablo ok tuşları, klavye tuzağı yok
- Terim sözlüğü taraması (§10.4) — tek kavram tek kelime
- Hitap taraması: her yerde **siz** (`Plans.razor`'daki "izleyebilirsin" gibi
  kalıntılar)
- Tüm `ToString("u")` kullanımları `Fmt.DateTime` ile değişti

**Commit:** `polish(ui): erişilebilirlik, tr-TR tutarlılığı, yoğunluk anahtarı`

---

## Bitti Sayılma Kriteri

- `dotnet build` uyarısız
- `dotnet test` yeşil, atlanmış test yok
- Panelde `style=` özniteliği yok, emoji yok, ham UTC yok, `#hex` sabiti yok
- Koyu ve açık tema ikisi de tam
- `docs/design-system.md` §2'deki 12 tasarım borcunun tamamı kapalı
