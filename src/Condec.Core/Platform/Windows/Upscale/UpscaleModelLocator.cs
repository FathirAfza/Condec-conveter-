// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Security.Cryptography;

namespace Condec.Core.Upscale;

public enum UpscaleModelStatus
{
    Ready,
    Missing,
    Damaged,
}

/// <summary>
/// Finds the networks that ship with the app (<c>Models\&lt;name&gt;\model.onnx</c>, put there by tools\fetch-model.ps1) and
/// checks each is the file that was released, once per run.
/// </summary>
public static class UpscaleModelLocator
{
    public const string FileName = "model.onnx";

    /// <summary>SHA-256 of the ONNX export of Real-ESRGAN x4plus that Condec bundles (THIRD-PARTY-NOTICES.md).</summary>
    public const string SharpSha256 = "4851ec156207d271f5328605d0582eeb851e656227da8aca093ced9e60789291";

    /// <summary>SHA-256 of the Real-ESRNet x4plus file that tools\make-faithful-model.py makes (THIRD-PARTY-NOTICES.md).</summary>
    public const string FaithfulSha256 = "dc6b06112170acc10c2e7ed740bffc6b28227a9b1288decb142bbac678be4d40";

    private static readonly object Gate = new();
    // A file found good for one hash says nothing about another, so the pair is remembered.
    private static readonly HashSet<(string Path, string Sha256)> Ready = [];

    public static string DirectoryName(UpscaleStyle style) => style == UpscaleStyle.Faithful ? "realesrnet-x4plus" : "realesrgan-x4plus";

    public static string ExpectedSha256(UpscaleStyle style) => style == UpscaleStyle.Faithful ? FaithfulSha256 : SharpSha256;

    public static string ModelPath(UpscaleStyle style) => Path.Combine(AppContext.BaseDirectory, "Models", DirectoryName(style), FileName);

    /// <summary>Whether the network is there and intact. The first call reads the whole file (about 64 MB); later calls remember a good answer.</summary>
    public static UpscaleModelStatus GetStatus(UpscaleStyle style) => GetStatus(ModelPath(style), ExpectedSha256(style));

    internal static UpscaleModelStatus GetStatus(string path, string expectedSha256)
    {
        lock (Gate)
        {
            var key = (Path.GetFullPath(path).ToUpperInvariant(), expectedSha256.ToUpperInvariant());
            if (Ready.Contains(key))
            {
                return UpscaleModelStatus.Ready;
            }

            var status = Check(path, expectedSha256);
            if (status == UpscaleModelStatus.Ready)
            {
                Ready.Add(key);
            }

            return status;
        }
    }

    private static UpscaleModelStatus Check(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            return UpscaleModelStatus.Missing;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase)
                ? UpscaleModelStatus.Ready
                : UpscaleModelStatus.Damaged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpscaleModelStatus.Damaged;
        }
    }
}
