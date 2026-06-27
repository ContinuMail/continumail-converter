// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Threading;

namespace Mail2Pst.Core.Scanning;

internal sealed class ScanProgressAggregator
{
    private readonly long _total;
    private readonly Action<ScanProgress>? _onProgress;
    private readonly long _threshold;
    private readonly object _emitLock = new();
    private long _bytes;        // Interlocked
    private long _lastEmitted;  // guarded by _emitLock

    public ScanProgressAggregator(long totalBytes, Action<ScanProgress>? onProgress, long thresholdBytes)
    {
        _total = totalBytes; _onProgress = onProgress; _threshold = thresholdBytes;
    }

    public void Add(long deltaBytes)
    {
        if (_onProgress is null || deltaBytes <= 0) return;
        long now = Interlocked.Add(ref _bytes, deltaBytes);
        lock (_emitLock)
        {
            if (now - _lastEmitted < _threshold) return;
            _lastEmitted = now;
            _onProgress(new ScanProgress(Math.Min(now, _total), _total)); // snapshot read in-lock; monotonic
        }
    }

    public void EmitFinal()
    {
        if (_onProgress is null) return;
        lock (_emitLock)
        {
            if (_lastEmitted >= _total) return;     // already at 100%
            _lastEmitted = _total;
            _onProgress(new ScanProgress(_total, _total));
        }
    }
}
