// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;

namespace Condec.ViewModels;

/// <summary>Turns a failed conversion into a sentence that says what happened and what to do next.</summary>
internal static class ErrorMessages
{
    public static string LockedPdf => Loc.Get("Error.LockedPdf");

    /// <param name="stage">The last stage the pipeline reported before failing.</param>
    public static string Describe(Exception exception, PipelineStage stage, string targetLabel) => exception switch
    {
        VerificationFailedException { Failure: VerificationFailure.ChunkMismatch } v =>
            Loc.Format("Error.ChunkMismatch", v.ChunkNumber, Loc.Get("Error.TryAnotherDrive")),
        VerificationFailedException { Failure: VerificationFailure.LengthMismatch } =>
            Loc.Format("Error.LengthMismatch", Loc.Get("Error.TryAnotherDrive")),
        VerificationFailedException { Failure: VerificationFailure.FileHashMismatch } =>
            Loc.Format("Error.HashMismatch", Loc.Get("Error.TryAnotherDrive")),
        VerificationFailedException =>
            Loc.Format("Error.CannotReopen", targetLabel),
        FileNotFoundException =>
            Loc.Get("Error.SourceMissing"),
        DirectoryNotFoundException =>
            Loc.Get("Error.FolderMissing"),
        UnauthorizedAccessException =>
            Loc.Get("Error.NoWriteAccess"),
        // The pipeline's only ArgumentException for a job: the output would overwrite the source.
        ArgumentException { ParamName: "job" } =>
            Loc.Get("Error.SameFile"),
        LockedPdfException => LockedPdf,
        ImageTooLargeException tooLarge =>
            Loc.Format("Error.ImageTooLarge", tooLarge.Megapixels),
        NothingToTraceException =>
            Loc.Get("Error.NothingToTraceImage"),
        NothingToConvertException =>
            Loc.Get("Error.NothingToConvertPdf"),
        UnsupportedCadVersionException =>
            Loc.Get("Error.UnsupportedCad"),
        ExternalToolException tool =>
            Loc.Format("Error.ExternalTool", tool.ToolName),
        _ when stage == PipelineStage.Decode =>
            Loc.Get("Error.Decode"),
        IOException =>
            Loc.Get("Error.Io"),
        _ =>
            Loc.Format("Error.Unexpected", exception.GetType().Name),
    };
}
