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
/// Finds the Real-ESRGAN x4plus network that ships with the app (<c>Models\realesrgan-x4plus\model.onnx</c>, put there by
/// tools\fetch-model.ps1) and checks it is the file that was released, once per run.
/// </summary>
public static class UpscaleModelLocator
{
    public const string FileName = "model.onnx";

    /// <summary>SHA-256 of the ONNX export of Real-ESRGAN x4plus that Condec bundles (THIRD-PARTY-NOTICES.md).</summary>
    public const string ExpectedSha256 = "4851ec156207d271f5328605d0582eeb851e656227da8aca093ced9e60789291";

    private static readonly object Gate = new();
    private static (string Path, UpscaleModelStatus Status)? _checked;

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Models", "realesrgan-x4plus");

    public static string ModelPath => Path.Combine(Directory, FileName);

    /// <summary>Whether the model is there and intact. The first call reads the whole file (about 64 MB); later calls remember a good answer.</summary>
    public static UpscaleModelStatus GetStatus() => GetStatus(ModelPath);

    internal static UpscaleModelStatus GetStatus(string path)
    {
        lock (Gate)
        {
            if (_checked is { } known && known.Path == path && known.Status == UpscaleModelStatus.Ready)
            {
                return known.Status;
            }

            var status = Check(path);
            _checked = (path, status);
            return status;
        }
    }

    private static UpscaleModelStatus Check(string path)
    {
        if (!File.Exists(path))
        {
            return UpscaleModelStatus.Missing;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase)
                ? UpscaleModelStatus.Ready
                : UpscaleModelStatus.Damaged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpscaleModelStatus.Damaged;
        }
    }
}
