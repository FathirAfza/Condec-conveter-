// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Upscale;

/// <summary>
/// Which of the two bundled networks enlarges the picture (DESIGN §6.2). Both are the same RRDBNet, so they run at the same
/// speed and need the same memory; only the weights differ.
/// </summary>
public enum UpscaleStyle
{
    /// <summary>Real-ESRGAN x4plus: trained to add believable detail, which on a small or heavily compressed picture can be invented texture.</summary>
    Sharp,

    /// <summary>Real-ESRNet x4plus: the same network trained without the adversarial loss, so it keeps to the shapes in the picture.</summary>
    Faithful,
}
