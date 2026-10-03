# Pemberitahuan pihak ketiga (THIRD-PARTY-NOTICES)

Condec berlisensi **GPL-3.0-or-later** (lihat `LICENSE`, dan izin tambahan GPLv3 pasal 7 di `LICENSE-ADDITIONAL-PERMISSION.md`). Dokumen ini mencantumkan setiap komponen pihak ketiga yang dipakai Condec, versinya, lisensinya, dan perannya. Versi diambil dari `Directory.Packages.props`, `global.json`, `tools/fetch-libreoffice.ps1`, dan `tools/fetch-model.ps1`; kalau versi di sana berubah, perbarui dokumen ini.

Semua komponen open source di bawah ini berlisensi MIT, Apache-2.0, BSD-3-Clause, atau MPL-2.0, yang kompatibel dengan GPL-3.0. Komponen Microsoft yang proprietary (Windows App SDK, Windows SDK, dan DirectML) dicakup oleh izin tambahan GPLv3 pasal 7.

## 1. Komponen yang ikut dalam paket aplikasi (runtime)

| Komponen | Versi | Lisensi | Peran | Sumber |
|---|---|---|---|---|
| PdfPig (`UglyToad.PdfPig.*`) | 0.1.16 | Apache-2.0 | Membaca operasi grafis, teks, dan gambar halaman PDF untuk konversi PDF → DXF/DWG dan deteksi PDF hasil scan | https://github.com/UglyToad/PdfPig |
| ACadSharp | 3.8.0 | MIT | Menulis DXF/DWG dan membaca DWG (DWG → DXF untuk jalur DWG → PDF) | https://github.com/DomCR/ACadSharp |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Kerangka MVVM (`ObservableObject`, `RelayCommand`) di aplikasi WinUI | https://github.com/CommunityToolkit/dotnet |
| Windows App SDK (paket komponen `Microsoft.WindowsAppSDK.Runtime` 2.5.1, `.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9, `.Base` 2.0.4) | 2.5.1 | Microsoft Software License Terms (proprietary) | WinUI 3, jendela, picker file, dan runtime aplikasi. Dipasang sebagai framework package `Microsoft.WindowsAppRuntime.2` milik sistem, bukan disalin ke dalam paket Condec | https://github.com/microsoft/WindowsAppSDK |
| Microsoft Edge WebView2 (`Microsoft.Web.WebView2`) | 1.0.3719.77 | BSD-3-Clause (lihat lampiran C) | Dependensi transitif paket WinUI. Condec tidak menampilkan konten web dan tidak memakai WebView2 | https://aka.ms/webview |
| Windows SDK projection (`Microsoft.Windows.SDK.NET.Ref`) | 10.0.26100.57 | Microsoft Windows SDK License | Proyeksi .NET untuk API WinRT (`Windows.Graphics.Imaging`, `Windows.Data.Pdf`, `Windows.Media.*`). `Microsoft.Windows.SDK.NET.dll` dan `WinRT.Runtime.dll` ikut dalam paket | https://aka.ms/WinSDKLicenseURL |
| .NET Runtime dan Windows Desktop Runtime | 10.0 | MIT | Runtime aplikasi (framework-dependent) | https://github.com/dotnet/runtime |
| LibreOffice | 26.8.0 (x64) | MPL-2.0 (lihat lampiran D). Berisi komponen pihak ketiga dengan lisensinya masing-masing, dicantumkan di berkas lisensi LibreOffice sendiri | Dibawa di folder `LibreOffice\` dalam paket Condec x64 untuk konversi dokumen (DOCX/XLSX/PPTX → PDF, PDF → DOCX/PPTX, DXF → PDF, dan lain-lain). Dijalankan sebagai proses terpisah (`soffice.exe --headless`), tidak di-link | https://www.libreoffice.org/ — kode sumber: https://git.libreoffice.org/core dan https://download.documentfoundation.org/libreoffice/src/ |
| ONNX Runtime (`Microsoft.ML.OnnxRuntime.DirectML`, `Microsoft.ML.OnnxRuntime.Managed`) | 1.24.4 | MIT (lihat lampiran A), © Microsoft Corporation. `onnxruntime.dll` berisi komponen pihak ketiga dengan lisensinya masing-masing | Menjalankan model upscale di CPU, atau di GPU lewat DirectML (Upscale Image). `LICENSE.txt` dan `ThirdPartyNotices.txt` dari paket NuGet ikut di `Licenses\ONNXRuntime\` | https://github.com/microsoft/onnxruntime |
| DirectML (`Microsoft.AI.DirectML`) | 1.15.4 | Microsoft Software License Terms (proprietary; boleh disebarkan di dalam aplikasi untuk Windows) | Lapisan DirectX 12 yang dipakai ONNX Runtime untuk GPU. `DirectML.dll` ikut dalam paket, dengan `LICENSE.txt` dan `ThirdPartyNotices.txt` dari paket NuGet di `Licenses\DirectML\` | https://github.com/microsoft/DirectML |
| Model Real-ESRGAN x4plus (bobot `RealESRGAN_x4plus.pth` rilis v0.1.0, dalam bentuk ONNX) | v0.1.0 | BSD-3-Clause (lihat lampiran E), © 2021 Xintao Wang | Model upscale 4× gaya "Tajam" di `Models\realesrgan-x4plus\model.onnx`, dibawa tanpa diubah | https://github.com/xinntao/Real-ESRGAN — berkas ONNX: https://huggingface.co/skillsafe-ai/realesrgan-x4plus |
| Model Real-ESRNet x4plus (bobot `RealESRNet_x4plus.pth` rilis v0.1.1, dalam bentuk ONNX) | v0.1.1 | BSD-3-Clause (lihat lampiran E), © 2021 Xintao Wang | Model upscale 4× gaya "Setia" di `Models\realesrnet-x4plus\model.onnx`. Bobotnya dibawa tanpa diubah, di dalam graf ONNX model x4plus (lihat catatan di bawah) | https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.1.1 |

### Catatan tentang LibreOffice yang dibawa

- Salinannya diambil dari MSI resmi The Document Foundation (`LibreOffice_26.8.0_Win_x86-64.msi`, SHA-256 dipin di `tools/fetch-libreoffice.ps1`), diekstrak tanpa dipasang, lalu dipangkas oleh `tools/trim-libreoffice.ps1`. **Tidak ada berkas yang diubah isinya**, hanya folder dan berkas yang tidak dipakai yang dibuang, dan font serta runtime VC++ dipindah ke dalam folder LibreOffice.
- Berkas lisensi LibreOffice sendiri (`license.txt`, `LICENSE.html`, dan `NOTICE`, di root folder LibreOffice) **wajib tetap dipaketkan**. Berkas itu mencantumkan semua komponen pihak ketiga LibreOffice beserta lisensinya.
- Font yang ikut (di `LibreOffice\share\fonts\truetype`), antara lain Liberation, Carlito, Caladea, DejaVu, Noto, Linux Libertine G, dan Linux Biolinum G, berlisensi SIL Open Font License 1.1, lisensi Bitstream Vera, atau lisensi bebas lain sebagaimana tercantum di berkas lisensi LibreOffice. Font hanya dipakai oleh proses LibreOffice (didaftarkan privat), tidak dipasang ke Windows.
- Runtime Visual C++ (`vcruntime140.dll`, `msvcp140.dll`, dan berkas serumpun yang dipindah ke `LibreOffice\program`) adalah Microsoft Visual C++ Redistributable, yang boleh disebarkan bersama aplikasi menurut ketentuan Microsoft.
- MPL-2.0 pasal 3.2 mewajibkan pemberitahuan cara mendapatkan kode sumber. Kode sumber LibreOffice 26.8.0 tersedia di https://download.documentfoundation.org/libreoffice/src/26.8.0/ dan https://git.libreoffice.org/core.

### Catatan tentang model upscale

- **Asal berkas.** `model.onnx` adalah ekspor ONNX yang diterbitkan SkillSafe di Hugging Face (`skillsafe-ai/realesrgan-x4plus`; kartu modelnya menyatakan BSD-3-Clause), bukan oleh penulis Real-ESRGAN. `tools/fetch-model.ps1` mengunduhnya dan menolaknya bila SHA-256-nya bukan `4851ec156207d271f5328605d0582eeb851e656227da8aca093ced9e60789291`. Aplikasi memeriksa hash yang sama sebelum memakai model.
- **Sudah diverifikasi.** `tools/verify-upscale-model.py` membandingkan berkas itu dengan `RealESRGAN_x4plus.pth` dari rilis resmi xinntao/Real-ESRGAN v0.1.0: ke-702 tensor bobotnya identik bit demi bit, dan hasil PyTorch dan ONNX Runtime sama di atas 100 dB PSNR pada tiga masukan uji (2026-10-02). Jadi bobot yang dibawa adalah bobot Real-ESRGAN, dengan lisensi Real-ESRGAN (BSD-3-Clause), yang kompatibel dengan GPL-3.0.
- **Model "Setia" (Real-ESRNet x4plus).** Jaringannya sama persis dengan x4plus (RRDBNet, 702 tensor dengan nama yang sama); hanya bobotnya yang lain, dilatih tanpa loss adversarial. Tidak ada ekspor ONNX resminya, jadi `tools/make-faithful-model.py` (dijalankan oleh `tools/fetch-model.ps1`) menyalin `model.onnx` x4plus di atas dan menimpa byte setiap bobot di tempatnya dengan bobot dari `RealESRNet_x4plus.pth` rilis resmi v0.1.1 (SHA-256 `a820b9bde89a874d7599d545567308ce6c128fc8754a53208eda016d40aa81df`). Byte lain tidak berubah, jadi hasilnya berkas yang sama di setiap mesin: SHA-256 `dc6b06112170acc10c2e7ed740bffc6b28227a9b1288decb142bbac678be4d40`, dipin di skrip dan diperiksa aplikasi. `tools/verify-upscale-model.py` dengan berkas `.pth` itu: 702 dari 702 tensor identik bit demi bit, dan PyTorch dan ONNX Runtime sama di 127,7–130,0 dB PSNR (2026-10-03). Lisensinya sama: BSD-3-Clause.
- **Data latih.** Menurut makalah dan repo Real-ESRGAN, kedua model ini dilatih dengan gambar dari DIV2K, Flickr2K, dan OutdoorSceneTraining (OST). Kumpulan data itu punya ketentuan sendiri (DIV2K, misalnya, disediakan untuk riset akademis), dan repo Real-ESRGAN tidak menjelaskan bagaimana ketentuan itu berlaku pada bobot yang dirilisnya. Condec memakai bobot yang dirilis di bawah BSD-3-Clause apa adanya dan tidak membawa data latih apa pun. Catatan ini ditulis apa adanya supaya penyebar ulang bisa menilai sendiri; ini bukan nasihat hukum.
- **Tanpa jaringan.** Model berjalan sepenuhnya di perangkat. Condec mematikan event telemetri ONNX Runtime (`OrtEnv.DisableTelemetryEvents`) sebelum model dimuat. Ketentuan ONNX Runtime dan DirectML menyebut bahwa build resmi bisa menulis event lewat TraceLogging Windows; event seperti itu hanya diteruskan oleh Windows sendiri menurut pengaturan data diagnostik Windows, bukan oleh Condec.

### Dependensi transitif paket NuGet

ACadSharp menyatakan dependensi `System.Memory` 4.6.3 dan `System.Text.Encoding.CodePages` 9.0.6; PdfPig menyatakan `Microsoft.Bcl.HashCode` 6.0.0, `System.Memory` 4.6.0, dan `System.ValueTuple` 4.5.0. Semuanya MIT (bagian dari .NET, https://github.com/dotnet/runtime). Pada target .NET 10, semuanya sudah ada di shared framework, jadi tidak ada berkas tambahan yang disalin.

## 2. Komponen yang hanya dipakai saat build dan test (tidak ikut dalam paket)

| Komponen | Versi | Lisensi | Peran |
|---|---|---|---|
| .NET SDK | 10.0 | MIT | Compiler dan tooling build |
| xUnit v3 (`xunit.v3.mtp-v2` dan paket turunannya) | 4.0.1 | Apache-2.0 | Framework unit test |
| Microsoft.Testing.Platform (`Microsoft.Testing.Platform`, `.MSBuild`, `.Extensions.*`) | 2.4.0 | MIT | Runner test |
| Python dengan numpy dan onnx | diuji dengan Python 3.12.10, numpy 2.5.3, onnx 1.23.1 | PSF-2.0, BSD-3-Clause, Apache-2.0 | `tools/make-faithful-model.py`, langkah `tools/fetch-model.ps1` yang membuat model "Setia". Tidak ikut dalam paket |
| Windows SDK Build Tools (`Microsoft.Windows.SDK.BuildTools`, `.BuildTools.MSIX`) | 10.0.26100.4654, 1.7.251221100 | Microsoft Windows SDK License | Membuat PRI dan MSIX di Windows |

## 3. Desain dan aset

- Ikon aplikasi dan aset gambar di `src/Condec/Assets/` dibuat untuk proyek ini dari desain di `Condec.html` dan berlisensi sama dengan Condec (GPL-3.0-or-later).
- Ikon di antarmuka memakai ikon sistem (Segoe Fluent Icons) lewat `FontIcon`/`SymbolIcon` WinUI, yang tersedia di Windows dan tidak dibundel.

## 4. Kewajiban kalau Anda menyebarkan ulang Condec

- Sertakan `LICENSE`, `LICENSE-ADDITIONAL-PERMISSION.md`, dan dokumen ini.
- Sertakan berkas lisensi LibreOffice yang ada di folder `LibreOffice\` (untuk paket x64).
- Sertakan folder `Licenses\` (ONNX Runtime dan DirectML). Bila Anda menyebarkan model upscale, teks BSD-3-Clause di lampiran E harus ikut (dokumen ini sudah memuatnya).
- DirectML hanya boleh disebarkan di dalam aplikasi untuk Windows, bukan sebagai berkas lepas (ketentuan DirectML pasal 1a dan 3e).
- Sediakan kode sumber Condec yang sesuai, sebagaimana diwajibkan GPL-3.0.
- Jangan memakai merek dagang Microsoft dengan cara yang mengesankan aplikasi ini berasal dari atau didukung Microsoft (ketentuan Windows App SDK pasal 3c).

---

## Lampiran A. Lisensi MIT

Berlaku untuk ACadSharp (Copyright (c) Albert Domenech), CommunityToolkit.Mvvm ((c) .NET Foundation and Contributors), Microsoft.Testing.Platform (Copyright (c) Microsoft Corporation), dan .NET (Copyright (c) .NET Foundation and Contributors).

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Lampiran B. Apache License 2.0

Berlaku untuk PdfPig (Copyright UglyToad and contributors) dan xUnit (Copyright (C) .NET Foundation).

```

                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright [yyyy] [name of copyright owner]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
```

## Lampiran C. Lisensi Microsoft Edge WebView2 (BSD-3-Clause)

Disalin dari `LICENSE.txt` di paket NuGet `Microsoft.Web.WebView2` 1.0.3719.77.

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors 
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.```

## Lampiran D. Mozilla Public License 2.0

Berlaku untuk LibreOffice.

```
Mozilla Public License Version 2.0
==================================

1. Definitions
--------------

1.1. "Contributor"
    means each individual or legal entity that creates, contributes to
    the creation of, or owns Covered Software.

1.2. "Contributor Version"
    means the combination of the Contributions of others (if any) used
    by a Contributor and that particular Contributor's Contribution.

1.3. "Contribution"
    means Covered Software of a particular Contributor.

1.4. "Covered Software"
    means Source Code Form to which the initial Contributor has attached
    the notice in Exhibit A, the Executable Form of such Source Code
    Form, and Modifications of such Source Code Form, in each case
    including portions thereof.

1.5. "Incompatible With Secondary Licenses"
    means

    (a) that the initial Contributor has attached the notice described
        in Exhibit B to the Covered Software; or

    (b) that the Covered Software was made available under the terms of
        version 1.1 or earlier of the License, but not also under the
        terms of a Secondary License.

1.6. "Executable Form"
    means any form of the work other than Source Code Form.

1.7. "Larger Work"
    means a work that combines Covered Software with other material, in 
    a separate file or files, that is not Covered Software.

1.8. "License"
    means this document.

1.9. "Licensable"
    means having the right to grant, to the maximum extent possible,
    whether at the time of the initial grant or subsequently, any and
    all of the rights conveyed by this License.

1.10. "Modifications"
    means any of the following:

    (a) any file in Source Code Form that results from an addition to,
        deletion from, or modification of the contents of Covered
        Software; or

    (b) any new file in Source Code Form that contains any Covered
        Software.

1.11. "Patent Claims" of a Contributor
    means any patent claim(s), including without limitation, method,
    process, and apparatus claims, in any patent Licensable by such
    Contributor that would be infringed, but for the grant of the
    License, by the making, using, selling, offering for sale, having
    made, import, or transfer of either its Contributions or its
    Contributor Version.

1.12. "Secondary License"
    means either the GNU General Public License, Version 2.0, the GNU
    Lesser General Public License, Version 2.1, the GNU Affero General
    Public License, Version 3.0, or any later versions of those
    licenses.

1.13. "Source Code Form"
    means the form of the work preferred for making modifications.

1.14. "You" (or "Your")
    means an individual or a legal entity exercising rights under this
    License. For legal entities, "You" includes any entity that
    controls, is controlled by, or is under common control with You. For
    purposes of this definition, "control" means (a) the power, direct
    or indirect, to cause the direction or management of such entity,
    whether by contract or otherwise, or (b) ownership of more than
    fifty percent (50%) of the outstanding shares or beneficial
    ownership of such entity.

2. License Grants and Conditions
--------------------------------

2.1. Grants

Each Contributor hereby grants You a world-wide, royalty-free,
non-exclusive license:

(a) under intellectual property rights (other than patent or trademark)
    Licensable by such Contributor to use, reproduce, make available,
    modify, display, perform, distribute, and otherwise exploit its
    Contributions, either on an unmodified basis, with Modifications, or
    as part of a Larger Work; and

(b) under Patent Claims of such Contributor to make, use, sell, offer
    for sale, have made, import, and otherwise transfer either its
    Contributions or its Contributor Version.

2.2. Effective Date

The licenses granted in Section 2.1 with respect to any Contribution
become effective for each Contribution on the date the Contributor first
distributes such Contribution.

2.3. Limitations on Grant Scope

The licenses granted in this Section 2 are the only rights granted under
this License. No additional rights or licenses will be implied from the
distribution or licensing of Covered Software under this License.
Notwithstanding Section 2.1(b) above, no patent license is granted by a
Contributor:

(a) for any code that a Contributor has removed from Covered Software;
    or

(b) for infringements caused by: (i) Your and any other third party's
    modifications of Covered Software, or (ii) the combination of its
    Contributions with other software (except as part of its Contributor
    Version); or

(c) under Patent Claims infringed by Covered Software in the absence of
    its Contributions.

This License does not grant any rights in the trademarks, service marks,
or logos of any Contributor (except as may be necessary to comply with
the notice requirements in Section 3.4).

2.4. Subsequent Licenses

No Contributor makes additional grants as a result of Your choice to
distribute the Covered Software under a subsequent version of this
License (see Section 10.2) or under the terms of a Secondary License (if
permitted under the terms of Section 3.3).

2.5. Representation

Each Contributor represents that the Contributor believes its
Contributions are its original creation(s) or it has sufficient rights
to grant the rights to its Contributions conveyed by this License.

2.6. Fair Use

This License is not intended to limit any rights You have under
applicable copyright doctrines of fair use, fair dealing, or other
equivalents.

2.7. Conditions

Sections 3.1, 3.2, 3.3, and 3.4 are conditions of the licenses granted
in Section 2.1.

3. Responsibilities
-------------------

3.1. Distribution of Source Form

All distribution of Covered Software in Source Code Form, including any
Modifications that You create or to which You contribute, must be under
the terms of this License. You must inform recipients that the Source
Code Form of the Covered Software is governed by the terms of this
License, and how they can obtain a copy of this License. You may not
attempt to alter or restrict the recipients' rights in the Source Code
Form.

3.2. Distribution of Executable Form

If You distribute Covered Software in Executable Form then:

(a) such Covered Software must also be made available in Source Code
    Form, as described in Section 3.1, and You must inform recipients of
    the Executable Form how they can obtain a copy of such Source Code
    Form by reasonable means in a timely manner, at a charge no more
    than the cost of distribution to the recipient; and

(b) You may distribute such Executable Form under the terms of this
    License, or sublicense it under different terms, provided that the
    license for the Executable Form does not attempt to limit or alter
    the recipients' rights in the Source Code Form under this License.

3.3. Distribution of a Larger Work

You may create and distribute a Larger Work under terms of Your choice,
provided that You also comply with the requirements of this License for
the Covered Software. If the Larger Work is a combination of Covered
Software with a work governed by one or more Secondary Licenses, and the
Covered Software is not Incompatible With Secondary Licenses, this
License permits You to additionally distribute such Covered Software
under the terms of such Secondary License(s), so that the recipient of
the Larger Work may, at their option, further distribute the Covered
Software under the terms of either this License or such Secondary
License(s).

3.4. Notices

You may not remove or alter the substance of any license notices
(including copyright notices, patent notices, disclaimers of warranty,
or limitations of liability) contained within the Source Code Form of
the Covered Software, except that You may alter any license notices to
the extent required to remedy known factual inaccuracies.

3.5. Application of Additional Terms

You may choose to offer, and to charge a fee for, warranty, support,
indemnity or liability obligations to one or more recipients of Covered
Software. However, You may do so only on Your own behalf, and not on
behalf of any Contributor. You must make it absolutely clear that any
such warranty, support, indemnity, or liability obligation is offered by
You alone, and You hereby agree to indemnify every Contributor for any
liability incurred by such Contributor as a result of warranty, support,
indemnity or liability terms You offer. You may include additional
disclaimers of warranty and limitations of liability specific to any
jurisdiction.

4. Inability to Comply Due to Statute or Regulation
---------------------------------------------------

If it is impossible for You to comply with any of the terms of this
License with respect to some or all of the Covered Software due to
statute, judicial order, or regulation then You must: (a) comply with
the terms of this License to the maximum extent possible; and (b)
describe the limitations and the code they affect. Such description must
be placed in a text file included with all distributions of the Covered
Software under this License. Except to the extent prohibited by statute
or regulation, such description must be sufficiently detailed for a
recipient of ordinary skill to be able to understand it.

5. Termination
--------------

5.1. The rights granted under this License will terminate automatically
if You fail to comply with any of its terms. However, if You become
compliant, then the rights granted under this License from a particular
Contributor are reinstated (a) provisionally, unless and until such
Contributor explicitly and finally terminates Your grants, and (b) on an
ongoing basis, if such Contributor fails to notify You of the
non-compliance by some reasonable means prior to 60 days after You have
come back into compliance. Moreover, Your grants from a particular
Contributor are reinstated on an ongoing basis if such Contributor
notifies You of the non-compliance by some reasonable means, this is the
first time You have received notice of non-compliance with this License
from such Contributor, and You become compliant prior to 30 days after
Your receipt of the notice.

5.2. If You initiate litigation against any entity by asserting a patent
infringement claim (excluding declaratory judgment actions,
counter-claims, and cross-claims) alleging that a Contributor Version
directly or indirectly infringes any patent, then the rights granted to
You by any and all Contributors for the Covered Software under Section
2.1 of this License shall terminate.

5.3. In the event of termination under Sections 5.1 or 5.2 above, all
end user license agreements (excluding distributors and resellers) which
have been validly granted by You or Your distributors under this License
prior to termination shall survive termination.

************************************************************************
*                                                                      *
*  6. Disclaimer of Warranty                                           *
*  -------------------------                                           *
*                                                                      *
*  Covered Software is provided under this License on an "as is"       *
*  basis, without warranty of any kind, either expressed, implied, or  *
*  statutory, including, without limitation, warranties that the       *
*  Covered Software is free of defects, merchantable, fit for a        *
*  particular purpose or non-infringing. The entire risk as to the     *
*  quality and performance of the Covered Software is with You.        *
*  Should any Covered Software prove defective in any respect, You     *
*  (not any Contributor) assume the cost of any necessary servicing,   *
*  repair, or correction. This disclaimer of warranty constitutes an   *
*  essential part of this License. No use of any Covered Software is   *
*  authorized under this License except under this disclaimer.         *
*                                                                      *
************************************************************************

************************************************************************
*                                                                      *
*  7. Limitation of Liability                                          *
*  --------------------------                                          *
*                                                                      *
*  Under no circumstances and under no legal theory, whether tort      *
*  (including negligence), contract, or otherwise, shall any           *
*  Contributor, or anyone who distributes Covered Software as          *
*  permitted above, be liable to You for any direct, indirect,         *
*  special, incidental, or consequential damages of any character      *
*  including, without limitation, damages for lost profits, loss of    *
*  goodwill, work stoppage, computer failure or malfunction, or any    *
*  and all other commercial damages or losses, even if such party      *
*  shall have been informed of the possibility of such damages. This   *
*  limitation of liability shall not apply to liability for death or   *
*  personal injury resulting from such party's negligence to the       *
*  extent applicable law prohibits such limitation. Some               *
*  jurisdictions do not allow the exclusion or limitation of           *
*  incidental or consequential damages, so this exclusion and          *
*  limitation may not apply to You.                                    *
*                                                                      *
************************************************************************

8. Litigation
-------------

Any litigation relating to this License may be brought only in the
courts of a jurisdiction where the defendant maintains its principal
place of business and such litigation shall be governed by laws of that
jurisdiction, without reference to its conflict-of-law provisions.
Nothing in this Section shall prevent a party's ability to bring
cross-claims or counter-claims.

9. Miscellaneous
----------------

This License represents the complete agreement concerning the subject
matter hereof. If any provision of this License is held to be
unenforceable, such provision shall be reformed only to the extent
necessary to make it enforceable. Any law or regulation which provides
that the language of a contract shall be construed against the drafter
shall not be used to construe this License against a Contributor.

10. Versions of the License
---------------------------

10.1. New Versions

Mozilla Foundation is the license steward. Except as provided in Section
10.3, no one other than the license steward has the right to modify or
publish new versions of this License. Each version will be given a
distinguishing version number.

10.2. Effect of New Versions

You may distribute the Covered Software under the terms of the version
of the License under which You originally received the Covered Software,
or under the terms of any subsequent version published by the license
steward.

10.3. Modified Versions

If you create software not governed by this License, and you want to
create a new license for such software, you may create and use a
modified version of this License if you rename the license and remove
any references to the name of the license steward (except to note that
such modified license differs from this License).

10.4. Distributing Source Code Form that is Incompatible With Secondary
Licenses

If You choose to distribute Source Code Form that is Incompatible With
Secondary Licenses under the terms of this version of the License, the
notice described in Exhibit B of this License must be attached.

Exhibit A - Source Code Form License Notice
-------------------------------------------

  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.

If it is not possible or desirable to put the notice in a particular
file, then You may include the notice in a location (such as a LICENSE
file in a relevant directory) where a recipient would be likely to look
for such a notice.

You may add additional accurate notices of copyright ownership.

Exhibit B - "Incompatible With Secondary Licenses" Notice
---------------------------------------------------------

  This Source Code Form is "Incompatible With Secondary Licenses", as
  defined by the Mozilla Public License, v. 2.0.
```

## Lampiran E. Lisensi Real-ESRGAN (BSD-3-Clause)

Disalin dari `LICENSE` di https://github.com/xinntao/Real-ESRGAN. Berlaku untuk bobot model Real-ESRGAN x4plus yang dibawa Condec.

```
BSD 3-Clause License

Copyright (c) 2021, Xintao Wang
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
