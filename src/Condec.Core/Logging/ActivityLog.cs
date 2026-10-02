// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Settings;

namespace Condec.Core.Logging;

/// <summary>
/// The activity log from Settings (DESIGN §6.4): what the app did, for finding problems. One text file per day in
/// <c>%LOCALAPPDATA%\Condec\logs</c>. It never holds file contents and is never sent anywhere. Writing is best effort:
/// a log that can't be written must not stop a conversion.
/// </summary>
public sealed class ActivityLog
{
    private readonly Func<bool> _isEnabled;
    private readonly Func<LogLevel> _level;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();

    /// <param name="isEnabled">Read on every write, so switching the log off in Settings applies at once.</param>
    /// <param name="level">The most detailed level that is written, read on every write.</param>
    public ActivityLog(string directory, Func<bool> isEnabled, Func<LogLevel> level, Func<DateTime>? now = null)
    {
        Directory = Path.GetFullPath(directory);
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _level = level ?? throw new ArgumentNullException(nameof(level));
        _now = now ?? (() => DateTime.Now);
    }

    public string Directory { get; }

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Debug(string message) => Write(LogLevel.Debug, message);

    /// <summary>Error is always within the level, Debug only when Debug is chosen.</summary>
    public bool IsWritten(LogLevel level) => _isEnabled() && level <= _level();

    public void Write(LogLevel level, string message)
    {
        if (!IsWritten(level))
        {
            return;
        }

        var now = _now();
        var line = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, string.Create(CultureInfo.InvariantCulture, $"condec-{now:yyyy-MM-dd}.log")), line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Deletes the log files. Returns false when one of them couldn't be deleted (open in an editor, for example).</summary>
    public bool Clear()
    {
        lock (_gate)
        {
            return FolderUsage.Clear(Directory, "*.log");
        }
    }
}
