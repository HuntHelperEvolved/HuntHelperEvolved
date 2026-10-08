using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

/// <summary>Tracks server revisions and acknowledgements without replaying offline timer edits.</summary>
internal sealed class ARankReportSharing
{
    private sealed class Submission
    {
        public string RequestId = string.Empty;
        public DateTime SentAt;
        public bool Waiting;
        public string Status = string.Empty;
    }

    private readonly Dictionary<(uint Name, uint World, uint Instance), long> _revisions = new();
    private readonly Dictionary<(uint Name, uint World, uint Instance), Submission> _submissions = new();

    internal void Reset()
    {
        _revisions.Clear();
        foreach (var submission in _submissions.Values)
        {
            if (submission.Waiting) submission.Status = "Saved locally; sharing was not confirmed. Submit again after reconnecting.";
            submission.Waiting = false;
            submission.RequestId = string.Empty;
        }
    }

    internal void Observe(IEnumerable<ARankKill> kills)
    {
        foreach (var kill in kills)
        {
            var key = (kill.NameId, kill.WorldId, kill.Instance);
            if (!_revisions.TryGetValue(key, out var revision) || kill.Revision > revision)
                _revisions[key] = kill.Revision;
        }
    }

    internal ARankReportMessage? Begin(ARankKill report, bool connected, bool supported, bool sharing, DateTime now)
    {
        var key = (report.NameId, report.WorldId, report.Instance);
        var submission = new Submission { SentAt = now };
        _submissions[key] = submission;
        if (!sharing) submission.Status = "Saved locally; train sharing is off.";
        else if (!connected) submission.Status = "Saved locally; connect and submit again to share this time.";
        else if (!supported) submission.Status = "Saved locally; the server needs an update to share A-rank timer corrections.";
        else
        {
            submission.RequestId = Guid.NewGuid().ToString("N");
            submission.Waiting = true;
            submission.Status = "Saved locally; waiting for the server to confirm sharing…";
            return new ARankReportMessage
            {
                RequestId = submission.RequestId,
                BaseRevision = _revisions.GetValueOrDefault(key),
                Report = report,
            };
        }
        return null;
    }

    internal void Apply(ARankUpdatesBroadcast update)
    {
        Observe(update.Kills);
        if (string.IsNullOrEmpty(update.RequestId)) return;
        foreach (var (key, submission) in _submissions)
        {
            if (submission.RequestId != update.RequestId) continue;
            // The server may have expired the previous 14-day history row.
            // A retry then needs the absent-row revision instead of the old one.
            if (!update.Accepted && !update.Kills.Exists(k => (k.NameId, k.WorldId, k.Instance) == key))
                _revisions.Remove(key);
            submission.Waiting = false;
            submission.Status = update.Accepted
                ? "Timer shared with the group."
                : "The shared timer changed before this report was accepted. Review the time and submit again.";
            break;
        }
    }

    internal void Check(bool connected, DateTime now)
    {
        foreach (var submission in _submissions.Values)
        {
            if (!submission.Waiting || connected && now - submission.SentAt < TimeSpan.FromSeconds(15)) continue;
            submission.Waiting = false;
            submission.Status = "Saved locally; sharing was not confirmed. Submit again to retry.";
        }
    }

    internal string Status(uint nameId, uint worldId, uint instance) =>
        _submissions.TryGetValue((nameId, worldId, instance), out var submission) ? submission.Status : string.Empty;
}
