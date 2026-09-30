using System.Linq;

namespace HuntHelperEvolved.TrainPresets;

internal enum PresetDraftSource { Local, Server, New }

internal sealed class PresetDraftSession
{
    private sealed record Replacement(TrainPreset Preset, PresetDraftSource Source, long Revision,
        bool Unsaved, uint? EarlierZone);

    private TrainPreset? _saved;
    private Replacement? _replacement;

    internal TrainPreset? Draft { get; private set; }
    internal PresetDraftSource Source { get; private set; }
    internal long ServerRevision { get; private set; }
    internal bool HasPendingReplacement => _replacement is not null;
    internal bool IsDirty => Draft is not null && !Equivalent(Draft, _saved);

    internal bool Open(TrainPreset preset, PresetDraftSource source, long revision,
        bool unsaved = false, uint? earlierZone = null, bool replaceCurrent = false)
    {
        if (!replaceCurrent && !unsaved && Draft?.Id == preset.Id && Source == source && IsDirty)
        {
            _replacement = null;
            if (earlierZone is { } zone) PresetEditing.MoveZone(Draft, zone, -1);
            return true;
        }

        var replacement = new Replacement(preset.Copy(), source, revision, unsaved, earlierZone);
        if (IsDirty)
        {
            _replacement = replacement;
            return false;
        }
        Load(replacement);
        return true;
    }

    internal void ConfirmReplacement()
    {
        if (_replacement is { } replacement) Load(replacement);
    }

    internal void CancelReplacement() => _replacement = null;

    internal void MarkSaved(TrainPreset saved, long? revision = null, PresetDraftSource? source = null)
    {
        if (Draft?.Id != saved.Id) return;
        _saved = saved.Copy();
        if (revision is { } acceptedRevision) ServerRevision = acceptedRevision;
        if (source is { } savedSource) Source = savedSource;
    }

    internal void MarkUnsaved(long? revision = null)
    {
        _saved = null;
        if (revision is { } acceptedRevision) ServerRevision = acceptedRevision;
    }

    private void Load(Replacement replacement)
    {
        Draft = replacement.Preset;
        Source = replacement.Source;
        ServerRevision = replacement.Revision;
        _saved = replacement.Unsaved ? null : Draft.Copy();
        _replacement = null;
        if (replacement.EarlierZone is { } zone) PresetEditing.MoveZone(Draft, zone, -1);
    }

    private static bool Equivalent(TrainPreset left, TrainPreset? right) => right is not null
        && left.Id == right.Id && left.Name == right.Name
        && left.RallyInInstancedZones == right.RallyInInstancedZones
        && left.ExcludedAetheryteIds.SequenceEqual(right.ExcludedAetheryteIds)
        && left.RallyBeforeExpansions.SequenceEqual(right.RallyBeforeExpansions)
        && left.Zones.Count == right.Zones.Count
        && left.Zones.Zip(right.Zones).All(pair => pair.First.TerritoryId == pair.Second.TerritoryId
            && pair.First.Strict == pair.Second.Strict
            && pair.First.EntryAetheryteId == pair.Second.EntryAetheryteId
            && pair.First.MarkOrder.SequenceEqual(pair.Second.MarkOrder));
}

internal static class PresetEditorPolicy
{
    internal static string? ServerMutationUnavailable(bool enabled, bool connected, bool supported,
        bool pending, bool trainBusy)
    {
        if (!enabled) return "Enable server sync before changing shared presets.";
        if (!connected) return "Connect to the server before changing shared presets.";
        if (!supported) return "Shared presets need server 0.3.26 or later.";
        if (pending) return "Wait for the pending preset change to finish.";
        if (trainBusy) return "Wait for the current train operation to finish.";
        return null;
    }
}
