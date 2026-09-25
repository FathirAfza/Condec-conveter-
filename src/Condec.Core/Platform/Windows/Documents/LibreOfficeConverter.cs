// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;
using System.Xml;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Pdf;

namespace Condec.Core.Documents;

/// <summary>
/// Office documents through an installed LibreOffice, run as
/// <c>soffice --headless --norestore --convert-to &lt;ext&gt; --outdir &lt;dir&gt; &lt;file&gt;</c>.
/// LibreOffice isn't bundled: when it is missing, the formats are listed disabled with the reason.
/// </summary>
public sealed class LibreOfficeConverter : IExternalToolConverter
{
    public const string ToolName = "LibreOffice";

    private readonly Func<string?> _locate;
    private readonly string _profileDirectory;
    private readonly string _stagingRoot;

    /// <param name="profileDirectory">
    /// A LibreOffice user profile used only by Condec. LibreOffice runs outside the MSIX package, so its writes
    /// aren't redirected like Condec's own: pass a folder the package owns, so uninstalling removes it.
    /// </param>
    public LibreOfficeConverter(string profileDirectory)
        : this(LibreOfficeLocator.FindSoffice, profileDirectory, Path.Combine(Path.GetTempPath(), "Condec"))
    {
    }

    /// <param name="locate">Returns the full path of soffice.exe, or null when LibreOffice isn't installed.</param>
    /// <param name="profileDirectory">A LibreOffice user profile used only by Condec.</param>
    /// <param name="stagingRoot">Where each conversion gets its own folder for the input copy and the output.</param>
    internal LibreOfficeConverter(Func<string?> locate, string profileDirectory, string stagingRoot)
    {
        _locate = locate;
        _profileDirectory = profileDirectory;
        _stagingRoot = stagingRoot;
    }

    public ExternalToolStatus GetToolStatus() =>
        _locate() is not null
            ? ExternalToolStatus.Available
            : ExternalToolStatus.Unavailable("Format ini perlu LibreOffice, yang belum terpasang di komputer ini. Pasang LibreOffice, lalu pilih file lagi.");

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        DocumentFormats.GetTargets(FileExtension.Normalize(sourceExtension));

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var soffice = _locate() ?? throw new ExternalToolException(ToolName, "LibreOffice is not installed.");
        var source = FileExtension.Normalize(request.SourceExtension);
        var target = FileExtension.Normalize(request.TargetExtension);

        var staging = Path.Combine(_stagingRoot, "lo-" + Guid.NewGuid().ToString("N"));
        var inputDirectory = Path.Combine(staging, "in");
        var outputDirectory = Path.Combine(staging, "out");
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            progress.Report(new ConversionProgress(ConversionStage.Decode, 0, "Membuka dokumen di LibreOffice"));

            var input = await PrepareInputAsync(request.SourcePath, source, inputDirectory, ct).ConfigureAwait(false);
            var arguments = BuildArguments(input, target, outputDirectory, _profileDirectory, DocumentFormats.GetImportFilter(source, target));

            TryDisableUpdateCheck();
            await RunAsync(soffice, arguments, ct).ConfigureAwait(false);
            TryDisableUpdateCheck();
            progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

            var output = Path.Combine(outputDirectory, "dokumen" + target);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                // Password-protected and damaged documents end here: headless LibreOffice can't ask for a password.
                throw new ExternalToolException(ToolName, "LibreOffice finished without writing the output file.");
            }

            progress.Report(new ConversionProgress(ConversionStage.Encode, 0));
            await using (var result = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            {
                await result.CopyToAsync(request.Output, ct).ConfigureAwait(false);
            }

            progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// LibreOffice works on a private copy: it never writes a lock file next to the user's document, and odd
    /// characters in the original name can't confuse its command line. A DWG is copied as DXF, the only CAD
    /// format LibreOffice imports.
    /// </summary>
    private static async Task<string> PrepareInputAsync(string sourcePath, string source, string inputDirectory, CancellationToken ct)
    {
        if (source == ".pdf")
        {
            // Refuse protected PDFs here: headless LibreOffice would only fail without saying why.
            _ = PdfInspector.CountPages(sourcePath);
        }

        if (source == ".dwg")
        {
            var dxf = Path.Combine(inputDirectory, "dokumen.dxf");
            var drawing = await Task.Run(() => CadFiles.Read(sourcePath, ".dwg"), ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(dxf, PdfToCadConverter.Write(drawing, ".dxf"), ct).ConfigureAwait(false);
            return dxf;
        }

        var input = Path.Combine(inputDirectory, "dokumen" + source);
        File.Copy(sourcePath, input);
        return input;
    }

    internal static IReadOnlyList<string> BuildArguments(string input, string target, string outputDirectory, string profileDirectory, string? importFilter = null) =>
    [
        "-env:UserInstallation=" + new Uri(Path.GetFullPath(profileDirectory)).AbsoluteUri,
        "--headless",
        "--norestore",
        .. importFilter is null ? Array.Empty<string>() : ["--infilter=" + importFilter],
        "--convert-to",
        target[1..],
        "--outdir",
        outputDirectory,
        input,
    ];

    private static async Task RunAsync(string soffice, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(soffice)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new ExternalToolException(ToolName, "LibreOffice did not start.");
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // soffice.exe starts soffice.bin, which does the work, so the whole tree goes.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new ExternalToolException(ToolName, $"LibreOffice exited with code {process.ExitCode}.");
        }
    }

    private void TryDisableUpdateCheck()
    {
        try
        {
            LibreOfficeProfile.DisableUpdateCheck(_profileDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            // The setting is a second line of defence (see LibreOfficeProfile); a profile file that can't be
            // read or written must not stop the conversion.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover staging folder in %TEMP% holds only a copy; Windows' own cleanup removes it.
        }
    }
}
