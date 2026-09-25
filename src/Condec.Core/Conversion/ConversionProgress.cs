namespace Condec.Core.Conversion;

/// <summary>The stages a converter reports. The pipeline adds the two verification stages itself.</summary>
public enum ConversionStage
{
    Decode,
    Encode,
}

/// <param name="Fraction">Progress within <paramref name="Stage"/>, from 0 to 1.</param>
public readonly record struct ConversionProgress(ConversionStage Stage, double Fraction, string? Detail = null);
