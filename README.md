# 🎛️ TabletDeck

**TabletDeck**, aynı Wi-Fi ağındaki Android tabletinizi (veya herhangi bir tarayıcıyı) bilgisayarınız için **Stream Deck (Makro & Kısayol Paneli)**, **Grafik Çizim Tableti / Touchpad** ve **Canlı Ekran / Oyun Envanteri Önizleyicisi** haline getiren açık kaynaklı bir masaüstü + mobil eşlikçi uygulamasıdır.

---

## ✨ Özellikler

- **🎛️ Kontrol Paneli (Macro Deck):**
  - **Hazır Uygulama & Oyun Profilleri:** Photoshop, Premiere Pro, After Effects, Illustrator, Blender, Stardew Valley, ARK: Survival Evolved, Valheim ve Masaüstü kısayolları.
  - **＋ Özel Tuş Ekleme:** İstediğiniz profile özel etiket ve tuş kombinasyonu (`CTRL+SHIFT+N`, `F5`, `[` vb.) ekleme.
  - **⚡ Hızlı Sistem & Medya Tuşları:** Ses kontrolü, medya oynat/durdur/geç, `Alt+Tab`, `Masaüstünü Göster` ve ekran görüntüsü alma.
  - **🎬 Makro Kaydedici:** Birden fazla tuş vuruşunu sırayla kaydedip tek dokunuşla zincirleme oynatma.
  - **🚀 Programı Aç / Öne Getir:** Seçili profilin uygulamasını PC'de başlatma veya açıksa penceresini öne getirme.

- **✏️ Ayrı Sekmede Gelişmiş Çizim & Touchpad Alanı:**
  - **🎯 Grafik Tablet (Mutlak) Modu:** Tablet çizim alanı PC ekranınızla birebir eşleşir.
  - **👆 Touchpad (Göreceli) Modu:** Hassasiyet ayarı (`0.5x – 4.0x`) ile laptop touchpad'i gibi imleç kontrolü.
  - **✏️ Çiz (Sol Tık) / 👀 Sadece İmleç (Hover):** Kalemi değdirince sol tık basılı tutarak çizim yapma veya tıklamadan sadece imleci gezdirme.
  - **🖥️ Canlı Ekran Altlığı & Görsel İz:** Kalem izini, basınç halkasını, kılavuz ızgarayı ve isteğe bağlı olarak PC ekran görüntüsünü çizim tuvalinin altında canlı görme.
  - **Sol/Sağ Hızlı Araç Çubukları & Tam Alan (Zen) Modu:** Sol tarafta çizim kısayolları (`Geri Al`, `Fırça`, `Silgi`, `Damlalık`, `Fırça Boyutu`), sağ tarafta **Sol/Çift/Sağ/Orta Tık** ve parmakla sürüklenebilen **Tekerlek Kaydırma (Scroll) Şeridi**.

- **🖼️ Canlı Ekran & Envanter Sekmesi:**
  - PC'de seçilen bölgenin (ör. oyun envanteri veya referans penceresi) canlı görüntüsünü tablette izleme.
  - **Dokunarak Tıklama:** Önizleme görüntüsü üzerinde dokunduğunuz noktaya PC ekranında otomatik tıklama gönderme.

- **⚙️ Uzaktan Ayarlar & PC Eşlikçisi Arayüzü:**
  - PC Eşlikçisi penceresinden **Ekrandan Bölge Seç...** ile fareyle sürükleyerek paylaşılacak ekran alanını belirleme.
  - Açık olan uygulamadan `.exe` yolunu otomatik algılama veya dosya seçici ile kaydetme.
  - Tek tıkla Windows Güvenlik Duvarı port izni (`TCP 47821` / `UDP 47820`) tanımlama.
  - Tabletten PC'ye doğrudan metin yazıp gönderme.

---

## 🚀 Kurulum ve Kullanım

### 1. PC Eşlikçisini Çalıştırma
Geliştirme ortamında derlemek veya çalıştırmak için (.NET 6.0+ SDK):
```powershell
cd pc-agent
dotnet run
```
Tek dosya `.exe` olarak yayınlamak için:
```powershell
dotnet publish pc-agent/TabletDeck.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o pc-agent/publish
```

### 2. Android Tablet Uygulaması (`TabletDeck-debug.apk`)
1. Depo ana dizinindeki [`TabletDeck-debug.apk`](./TabletDeck-debug.apk) dosyasını Android tabletinize kurun ve açın.
2. Tablet ve PC aynı Wi-Fi ağındayken uygulama PC'yi **UDP otomatik keşif** ile bulup bağlanır.
3. Sağ üstteki **🔑 PIN Eşleştir** düğmesine dokunun ve PC Eşlikçisi penceresinde yazan 6 haneli PIN kodunu girin.

---

## 📂 Proje Yapısı

- `pc-agent/` — C# (.NET Windows Forms + ASP.NET Core Kestrel + WebSocket + Win32 Input API) PC sunucusu ve masaüstü arayüzü.
- `pc-agent/wwwroot/` — Tablet arayüzünü oluşturan modern, sekmeli Web uygulaması (`index.html`, `style.css`, `app.js`).
- `tablet-android/` — Otomatik ağ keşfi (UDP Broadcast) ve tam ekran WebView barındıran yerel Android (Kotlin) uygulaması.
- `TabletDeck-debug.apk` — Kuruluma hazır Android APK paketi.
