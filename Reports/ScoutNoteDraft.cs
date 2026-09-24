namespace HuntHelperEvolved;

/// <summary>Session-local scout notes, with explicit ownership by the current train.</summary>
public sealed class ScoutNoteDraft
{
    private long? _generation;
    private long _editRevision;

    public string Text { get; private set; } = string.Empty;
    public string DetachedText { get; private set; } = string.Empty;
    // Changes whenever preview-relevant state changes; reading it allocates nothing.
    public long Revision { get; private set; }

    public sealed record ReportCapture(string Text, long Generation, long Revision);
    public sealed record UndoCapture(string Text, string DetachedText, long EditRevision);

    public void ObserveTrain(long generation)
    {
        if (_generation == generation) return;
        DetachText();
        _generation = generation;
        Revision++;
    }

    public void SetText(string? text, long generation)
    {
        ObserveTrain(generation);
        // Preserve spaces while typing; sending applies the final trim.
        var normalized = ScoutingReport.NormalizeNotes(text, preserveOuterWhitespace: true);
        if (Text == normalized) return;
        Text = normalized;
        Edited();
    }

    public ReportCapture CaptureForReport(long generation)
    {
        ObserveTrain(generation);
        return new(ScoutingReport.NormalizeNotes(Text), generation, Revision);
    }

    public bool CompleteReport(ReportCapture capture, bool allDestinationsSucceeded, long generation)
    {
        ObserveTrain(generation);
        if (!allDestinationsSucceeded || capture.Generation != generation || capture.Revision != Revision
            || Text.Length == 0) return false;
        Text = string.Empty;
        Edited();
        return true;
    }

    /// <summary>Retire a finished train even when removing its last marks did not change its generation.</summary>
    public void DetachCurrent(long generation)
    {
        ObserveTrain(generation);
        DetachText();
        Revision++;
    }

    public bool ReuseDetached(long generation)
    {
        ObserveTrain(generation);
        if (Text.Length != 0 || DetachedText.Length == 0) return false;
        Text = DetachedText;
        DetachedText = string.Empty;
        Edited();
        return true;
    }

    public void ClearText(long generation)
    {
        ObserveTrain(generation);
        if (Text.Length == 0) return;
        Text = string.Empty;
        Edited();
    }

    public void ClearDetached()
    {
        if (DetachedText.Length == 0) return;
        DetachedText = string.Empty;
        Edited();
    }

    public UndoCapture CaptureUndo(long generation)
    {
        ObserveTrain(generation);
        return new(Text, DetachedText, _editRevision);
    }

    /// <summary>Undo restores the old draft only if it cannot overwrite an intervening edit or sent draft.</summary>
    public bool TryRestoreUndo(UndoCapture capture, long generation)
    {
        ObserveTrain(generation);
        if (capture.EditRevision != _editRevision) return false;
        Text = capture.Text;
        DetachedText = capture.DetachedText;
        Edited();
        return true;
    }

    private void DetachText()
    {
        // Keep the most recently detached nonempty note. Repeated empty resets
        // must not erase the draft waiting for an explicit Reuse or Clear.
        if (ScoutingReport.NormalizeNotes(Text).Length > 0) DetachedText = Text;
        Text = string.Empty;
    }

    private void Edited()
    {
        _editRevision++;
        Revision++;
    }
}
