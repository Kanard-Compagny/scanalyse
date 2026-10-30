<img src="docs/logo.svg" alt="" width="96" align="left">

# Scanalyse

**The free toolbox for barcode technicians.**
Understand what a code contains, why a scanner reads it badly, and what a label printer is really going to print — on an ordinary Windows PC, with nothing to install.

[**⬇ Download the latest release**](https://github.com/Kanard-Compagny/scanalyse/releases/latest) · Windows 10 / 11 · a single `Scanalyse.exe` file, nothing to install

🌐 [Scanalyse website](https://kanard-compagny.github.io/scanalyse/en/) · 🇫🇷 [Lire en français](README.md)

---

## What is it for?

A customer calls: "the scanner won't read", "the label prints wrong", "the software receives garbage". Scanalyse helps you find **where** it goes wrong — in the printed code, in the scanner's settings or in the printer's program — and **prove** it with a report.

It is made for support technicians, integrators, IT and logistics teams, and anyone working with barcodes, scanners, mobile terminals and label printers, **whatever the brand**. The interface is available in English and French.

## Screenshots

<p align="center"><img src="site/captures/analyser.webp" alt="Analyze an image: every code read and explained" width="860"></p>

| Scanner test bench | Printer config |
|---|---|
| <img src="site/captures/banc.webp" alt="Scanner test bench: read compared with the real content" width="420"> | <img src="site/captures/conf.webp" alt="Printer config: file created from a form" width="420"> |
| **Printer label** | **Reading devices** |
| <img src="site/captures/etiquette.webp" alt="Printer label: template, preview and program" width="420"> | <img src="site/captures/appareils.webp" alt="Reading devices: terminal linked over Wi-Fi" width="420"> |

*Screenshots of the French interface; the program is also fully available in English.*

## What Scanalyse does

### 🔍 Analyze an image
- A customer's photo, screenshot or PDF: **every code is read and explained** (link, Wi-Fi, business card, GS1 data with their dates, batches, serial numbers…).
- **Errors are reported**: wrong check digit, badly built GS1 code, low contrast, quiet zone too small, inconsistent codes on the same label.
- **Several images or a multi-page PDF** at once, then a single report.

### 🧪 Scanner test bench
- Scan a code with the customer's scanner: Scanalyse **compares what it sends with the real content** and explains every difference (AZERTY/QWERTY keyboard, extra prefix or suffix, lost GS1 separator…).
- Ready-to-scan **configuration codes** to fix it: Honeywell, Datalogic and Zebra (factory defaults, keyboard layout, Enter key, USB interface…).

### 📱 Reading devices
- USB, serial (COM port) and HID POS scanners.
- **Android terminals** over USB or Wi-Fi: their reads reach the PC.
- **Phones and terminals without a scan engine**: their **camera works as a scanner**.

### 🖨️ Printer label
- **Read a customer's program** in ZPL (Zebra), TSPL (TSC), EPL2, DPL (Honeywell / Datamax), SBPL (SATO) or Fingerprint (Honeywell / Intermec): label preview, content of each code, errors.
- **Ready-made label templates** (SSCC pallet, parcel, product, location, inventory).
- **Send to the printer** (network or installed printer) and **maintain it**: read its status (paper, ribbon, head open), calibrate, print its configuration page.

### ⚙️ Printer config
- **Analyze a configuration file** before sending it: Zebra ZPL and SGD (`! U1 setvar`, JSON), TSPL, DPL, Fingerprint, SBPL, EPL2 and TPCL (Toshiba). Every line is explained, errors are highlighted and suggestions appear as you type; typos, out-of-range values, curly quotes copied from Word, a forgotten ^JUS… are reported.
- **Convert** a file to another brand's language, or **create one** from a simple form — Wi-Fi included, handy to prepare a printer over USB.
- **Printer recognition** (Zebra, TSC, Honeywell, SATO, Toshiba…): every setting is checked for that model before sending.

### ✏️ Create a code
- More than 25 types: custom QR (colors, logo), Data Matrix, GS1, EAN, Code 128, PDF417, MaxiCode…
- Export as an image and reprint directly.

### 📁 History and reports
- Every analysis is filed by **customer case**.
- **PDF report** with your letterhead and logo, to attach to the job.
- History can be shared on a network folder with the whole team.

### ⚙️ Everyday comfort
- **English or French**, light or dark theme, suited to large screens.
- **Automatic update** from this page, with SHA-256 check.
- **Team profile**: one file to set up a new PC in one click.
- **Everything stays on the PC**: no image or data is sent over the Internet.

## Up and running in 2 minutes

1. Download **`Scanalyse.exe`** from the [latest release](https://github.com/Kanard-Compagny/scanalyse/releases/latest).
2. Run it (if Windows shows "Windows protected your PC": *More info* → *Run anyway*).
3. Pick the language and your folders, then follow the short guided tour.

**Requirements:** Windows 10 (version 2004 or later) or Windows 11, 64-bit, with Microsoft Edge WebView2 (already present on up-to-date Windows).

**Uninstall:** *Settings → Uninstall* erases the settings (and your data if you tick it), then just delete the file.

## A problem, an idea?

Open an **[issue](https://github.com/Kanard-Compagny/scanalyse/issues/new/choose)** — in English or French: a problem, hardware to support (any brand) or an idea.
Tip: *Settings → About → Copy diagnostics* gives the useful information to attach.

## Privacy

Scanalyse runs entirely on the PC: **no image, no code read, no report and no customer data is sent over the Internet**, and there is no account, no ads and no analytics.
The only connection is the **update check** on GitHub (`api.github.com`, without any personal information); turn it off in *Settings → Updates* ("Check at startup"). The Android terminal relay stays on the local network.

## File safety

`Scanalyse.exe` is not digitally signed yet: on first launch Windows may show "Windows protected your PC" (*More info* → *Run anyway*).
Every release is built automatically by GitHub Actions from the code in this public repository; its **SHA-256 hash** is published with it (`Scanalyse.exe.sha256` file) and checked by the automatic update. To check a download yourself: `Get-FileHash Scanalyse.exe` in PowerShell.

## License

**Free software**, created by **Mr.Kanard**, under the [GNU GPL v3](LICENSE): anyone can use, share and improve it, provided their versions stay free. Provided without any warranty.
Third-party components: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Want to contribute? See [CONTRIBUTING.md](CONTRIBUTING.md).
