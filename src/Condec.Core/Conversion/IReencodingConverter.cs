// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <summary>
/// A converter whose result can be in the same format as the source, because the point is to change how it is encoded:
/// a smaller video, a lower bitrate. For every other converter the source's own format is not offered as a target.
/// </summary>
public interface IReencodingConverter : IConverter
{
    /// <summary>True when a file of this extension can be converted to this extension again with other settings.</summary>
    bool CanReencode(string extension);
}
