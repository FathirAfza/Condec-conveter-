// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;

namespace Condec.ViewModels;

/// <summary>Turns a failed conversion into a sentence that says what happened and what to do next.</summary>
internal static class ErrorMessages
{
    private const string TryAnotherDrive = "Coba lagi, atau simpan ke drive lain.";

    public const string LockedPdf =
        "PDF ini dikunci dengan kata sandi atau pembatasan izin. Condec tidak membuka proteksi PDF, jadi file ini tidak bisa dikonversi.";

    /// <param name="stage">The last stage the pipeline reported before failing.</param>
    public static string Describe(Exception exception, PipelineStage stage, string targetLabel) => exception switch
    {
        VerificationFailedException { Failure: VerificationFailure.ChunkMismatch } v =>
            $"Chunk {v.ChunkNumber} berbeda saat dibaca ulang dari disk, jadi file tidak disimpan. {TryAnotherDrive}",
        VerificationFailedException { Failure: VerificationFailure.LengthMismatch } =>
            $"Ukuran file di disk tidak sama dengan yang ditulis, jadi file tidak disimpan. {TryAnotherDrive}",
        VerificationFailedException { Failure: VerificationFailure.FileHashMismatch } =>
            $"SHA-256 file di disk tidak cocok dengan yang ditulis, jadi file tidak disimpan. {TryAnotherDrive}",
        VerificationFailedException =>
            $"Hasilnya tidak bisa dibuka ulang sebagai {targetLabel}, jadi file tidak disimpan.",
        FileNotFoundException =>
            "File sumber tidak ditemukan. Mungkin sudah dipindah atau dihapus.",
        DirectoryNotFoundException =>
            "Folder tujuan tidak ditemukan. Pilih lokasi simpan lain.",
        UnauthorizedAccessException =>
            "Condec tidak punya izin menulis ke folder tujuan. Pilih lokasi simpan lain.",
        // The pipeline's only ArgumentException for a job: the output would overwrite the source.
        ArgumentException { ParamName: "job" } =>
            "File tujuan sama dengan file sumber. Simpan dengan nama lain.",
        LockedPdfException => LockedPdf,
        NothingToConvertException =>
            "Halaman ini tidak berisi garis, teks, atau hasil scan yang bisa diubah menjadi gambar CAD. Pilih halaman lain.",
        ExternalToolException tool =>
            $"{tool.ToolName} tidak bisa mengonversi file ini. File mungkin rusak atau dilindungi kata sandi.",
        _ when stage == PipelineStage.Decode =>
            "File sumber tidak bisa dibaca. File mungkin rusak, atau codec untuk format ini belum terpasang di Windows.",
        IOException =>
            "File tidak bisa ditulis. Pastikan disk tidak penuh dan file tujuan tidak sedang dibuka aplikasi lain.",
        _ =>
            $"Terjadi kesalahan yang tidak terduga ({exception.GetType().Name}), jadi file tidak disimpan.",
    };
}
