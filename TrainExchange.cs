using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace HuntHelperEvolved;

/// <summary>
/// Mirrors Hunt Helper's HuntTrainMob JSON shape exactly — same property names,
/// same set — so codes exported here import cleanly into Hunt Helper and vice
/// versa. MapLink is [JsonIgnore] on their side, so it's absent here too.
/// </summary>
public class ExchangeMob
{
    public string Name { get; set; } = string.Empty;
    public uint MobID { get; set; }
    public string MapName { get; set; } = string.Empty;
    public DateTime LastSeenUTC { get; set; }
    public Vector2 Position { get; set; }
    public bool Dead { get; set; }
    public uint TerritoryID { get; set; }
    public uint MapID { get; set; }
    public uint Instance { get; set; }

    /// <summary>
    /// Our own extension — conductor-placed flags rather than detected marks.
    /// Hunt Helper's importer ignores fields it doesn't know, so adding this
    /// doesn't break compatibility either way.
    /// </summary>
    public bool IsCustom { get; set; }

    public string ZoneName { get; set; } = string.Empty;

    /// <summary>Our own extension — see DetectedMark.Spiced.</summary>
    public bool Spiced { get; set; }

    /// <summary>
    /// Our own extension — the world the mark was scouted on.
    ///
    /// Hunt Helper's shape has no room for this, because Hunt Helper does not
    /// treat the world as part of a mark's identity. This plugin does: the same
    /// mark is up on every world at once, and they are different marks. Without
    /// it every imported code had to be assumed to be for wherever the importer
    /// happened to be standing, which is wrong the moment two scouts on two
    /// worlds send their lists to one conductor — exactly the case a train is
    /// most likely to hit.
    ///
    /// Both id and name travel. The id is what identity is keyed on; the name
    /// is what a conductor reads on the row, and resolving it back from an id
    /// needs the importer to be able to see that world in its own data.
    /// </summary>
    public uint WorldId { get; set; }

    public string WorldName { get; set; } = string.Empty;
}

/// <summary>
/// Import/export of train lists using Hunt Helper's own encoding — gzip the
/// JSON, then base64 it. Adapted from HuntHelper/Utilities/ExportImport.cs
/// (img02/HuntHelper, MIT licensed).
///
/// The extra fields this adds ride along harmlessly in both directions:
/// Newtonsoft ignores properties it does not know, so a code from here still
/// imports into Hunt Helper, and one from Hunt Helper still imports here with
/// the extras simply left at their defaults.
/// </summary>
public static class TrainExchange
{
    public static string Export(IEnumerable<DetectedMark> marks)
    {
        var payload = marks.Select(m => new ExchangeMob
        {
            Name = m.Name,
            MobID = m.NameId,
            MapName = ExpansionData.Lookup(m.NameId)?.Location ?? string.Empty,
            LastSeenUTC = m.LastSeenUtc,
            Position = m.MapPosition,
            Dead = m.Dead,
            TerritoryID = m.TerritoryId,
            MapID = m.MapId,
            Instance = m.Instance,
            IsCustom = m.IsCustom,
            ZoneName = m.ZoneName,
            Spiced = m.Spiced,
            WorldId = m.WorldId,
            WorldName = m.WorldName,
        }).ToList();

        var json = JsonConvert.SerializeObject(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            input.CopyTo(gzip);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// Decodes an import code. Returns null if it isn't a valid code, rather
    /// than throwing — pasted codes are frequently truncated or mangled.
    /// </summary>
    public static List<DetectedMark>? Import(string code)
    {
        try
        {
            var bytes = Convert.FromBase64String(code.Trim());
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var json = reader.ReadToEnd();

            var mobs = JsonConvert.DeserializeObject<List<ExchangeMob>>(json);
            if (mobs == null) return null;

            return mobs.Select(m => new DetectedMark
            {
                Name = m.Name,
                NameId = m.MobID,
                TerritoryId = m.TerritoryID,
                MapId = m.MapID,
                Instance = m.Instance,
                MapPosition = m.Position,
                Dead = m.Dead,
                FirstSeenUtc = m.LastSeenUTC,
                LastSeenUtc = m.LastSeenUTC,
                DeathObservedAtUtc = m.Dead ? m.LastSeenUTC : null,
                IsCustom = m.IsCustom,
                ZoneName = m.ZoneName,
                Spiced = m.Spiced,
                // Zero for a Hunt Helper code, or one exported before this
                // field existed. Left as it arrives rather than guessed at
                // here — MarkDetector.Merge is where a world-less import gets
                // stamped with the importer's own, and it is the only place
                // that should be making that assumption.
                WorldId = m.WorldId,
                WorldName = m.WorldName,
            }).ToList();
        }
        catch
        {
            return null;
        }
    }
}
