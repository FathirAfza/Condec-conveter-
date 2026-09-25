namespace Condec.Core.Documents;

/// <summary>
/// The formats converted through LibreOffice, grouped by the LibreOffice module that opens them.
/// A document converts to PDF and to the other editable formats of its own module.
/// </summary>
public static class DocumentFormats
{
    /// <param name="ImportFilter">
    /// The LibreOffice import filter (<c>--infilter</c>) when the default one is wrong for these targets.
    /// A PDF opens in Draw by default, which can't save as DOCX or PPTX.
    /// </param>
    private sealed record Family(string[] Sources, string[] Targets, string? ImportFilter = null);

    private static readonly Family[] Families =
    [
        new([".docx", ".doc", ".odt", ".rtf", ".txt"], [".pdf", ".docx", ".doc", ".odt", ".rtf"]),
        new([".xlsx", ".xls", ".ods"], [".pdf", ".xlsx", ".xls", ".ods"]),
        new([".pptx", ".ppt", ".odp"], [".pdf", ".pptx", ".ppt", ".odp"]),

        // Drawings open in Draw through its DXF import. DWG has no LibreOffice import filter, so
        // LibreOfficeConverter turns it into DXF with ACadSharp first.
        new([".dxf", ".dwg"], [".pdf"]),

        // PDF text lands in positioned text boxes, one page per page or slide: editable, not reflowed.
        // LibreOffice has no PDF import for Calc, so there is no spreadsheet target.
        new([".pdf"], [".docx", ".doc", ".odt"], "writer_pdf_import"),
        new([".pdf"], [".pptx", ".ppt", ".odp"], "impress_pdf_import"),
    ];

    /// <param name="sourceExtension">A normalized extension, such as ".docx".</param>
    public static IReadOnlyList<string> GetTargets(string sourceExtension) =>
        [.. Families.Where(family => family.Sources.Contains(sourceExtension)).SelectMany(family => family.Targets).Distinct()];

    public static string? GetImportFilter(string sourceExtension, string targetExtension) =>
        Families.FirstOrDefault(family => family.Sources.Contains(sourceExtension) && family.Targets.Contains(targetExtension))?.ImportFilter;
}
