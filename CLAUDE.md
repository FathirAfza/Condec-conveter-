# Condec
- Baca DESIGN.md sebelum mengubah UI atau perilaku. DESIGN.md adalah sumber kebenaran.
- Perubahan UI/perilaku wajib mengubah DESIGN.md + entri changelog di commit yang sama.
- Bertentangan dengan DESIGN.md? Berhenti dan tanya pemilik. Jangan menyimpang diam-diam.
- Hanya kontrol stock WinUI 3. Warna lewat ThemeResource, tanpa hardcode hex.
- Jangan mengarang API: cek dokumentasi resmi dulu.
- Offline: tanpa telemetry atau jaringan.
- Build: `dotnet build`. Test: `dotnet test`. Tahap selesai kalau keduanya lulus.
- Bahasa: kode Inggris, teks UI Indonesia.
