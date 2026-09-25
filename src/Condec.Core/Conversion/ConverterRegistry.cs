using Condec.Core.Formats;

namespace Condec.Core.Conversion;

/// <param name="DisabledReason">Why the format can't be chosen right now, shown next to it in the UI.</param>
public sealed record TargetOption(string Extension, string DisplayName, bool IsEnabled, string? DisabledReason);

/// <summary>
/// All converters and output validators. The UI builds its format list from <see cref="GetTargetOptions"/>
/// only, so a format appears exactly when some converter can produce it on this machine.
/// </summary>
public sealed class ConverterRegistry
{
    private readonly IReadOnlyList<IConverter> _converters;
    private readonly IReadOnlyList<IOutputValidator> _validators;

    public ConverterRegistry(IEnumerable<IConverter> converters, IEnumerable<IOutputValidator> validators)
    {
        _converters = [.. converters];
        _validators = [.. validators];
    }

    /// <summary>
    /// Target formats for a source extension, in registration order. When two converters offer the
    /// same target, an enabled one wins over a disabled one.
    /// </summary>
    public IReadOnlyList<TargetOption> GetTargetOptions(string sourceExtension)
    {
        var source = FileExtension.Normalize(sourceExtension);
        var options = new List<TargetOption>();
        var indexByExtension = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var converter in _converters)
        {
            var status = GetStatus(converter);
            foreach (var target in converter.GetTargets(source).Select(FileExtension.Normalize))
            {
                if (target == source)
                {
                    continue;
                }

                var option = new TargetOption(
                    target,
                    FormatCatalog.GetTargetLabel(target),
                    status.IsAvailable,
                    status.IsAvailable ? null : status.UnavailableReason);

                if (indexByExtension.TryGetValue(target, out var index))
                {
                    if (!options[index].IsEnabled && option.IsEnabled)
                    {
                        options[index] = option;
                    }
                }
                else
                {
                    indexByExtension[target] = options.Count;
                    options.Add(option);
                }
            }
        }

        return options;
    }

    /// <summary>The catalog extensions that at least one converter accepts as a source, for the open dialog filter.</summary>
    public IReadOnlyList<string> GetSourceExtensions() =>
        [.. FormatCatalog.KnownExtensions.Where(extension => GetTargetOptions(extension).Count > 0)];

    /// <summary>The first available converter for the pair, or null when none can run on this machine.</summary>
    public IConverter? FindConverter(string sourceExtension, string targetExtension)
    {
        var source = FileExtension.Normalize(sourceExtension);
        var target = FileExtension.Normalize(targetExtension);

        return _converters.FirstOrDefault(converter =>
            GetStatus(converter).IsAvailable
            && converter.GetTargets(source).Any(t => FileExtension.Normalize(t) == target));
    }

    public IOutputValidator? FindValidator(string targetExtension)
    {
        var target = FileExtension.Normalize(targetExtension);
        return _validators.FirstOrDefault(validator => validator.CanValidate(target));
    }

    private static ExternalToolStatus GetStatus(IConverter converter) =>
        converter is IExternalToolConverter tool ? tool.GetToolStatus() : ExternalToolStatus.Available;
}
