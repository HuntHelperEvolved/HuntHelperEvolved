using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private bool _arankSightingsDirty;
    private DateTime _nextARankSightingSave;

    internal void RememberARankSightings(IEnumerable<ARankSighting> sightings) =>
        _arankSightingsDirty |= ARankSightings.Merge(_config.ARankSightings, sightings, DateTime.UtcNow);

    private void SaveARankSightings(bool force = false)
    {
        if (!_arankSightingsDirty || !force && DateTime.UtcNow < _nextARankSightingSave) return;
        _config.Save();
        _arankSightingsDirty = false;
        _nextARankSightingSave = DateTime.UtcNow.AddSeconds(10);
    }
}
