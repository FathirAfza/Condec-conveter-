// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Media;

/// <summary>
/// Windows could not convert the audio or video file: it can't read it (damaged, empty, not really media, or a codec that is
/// not installed), or it failed while converting. Windows gives no more reason than that, so neither does this.
/// </summary>
public sealed class MediaConversionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
