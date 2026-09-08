using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace HuntHelperEvolved;

/// <summary>Serialized HHE train entry. Property names are retained for existing export codes.</summary>
public class ExchangeMob
{
    public string Name { get; set; } = string.Empty;
    public uint MobID { get; set; }
    public string MapName { get; set; } = string.Empty;
    public DateTime LastSeenUTC { get; set; }
    public Vector2 Position { get; set; }
    public bool Dead { get; set; }
    public DateTime? DeathObservedAtUtc { get; set; }
    public DateTime? SnipedAtUtc { get; set; }
    public uint TerritoryID { get; set; }
    public uint MapID { get; set; }
    public uint Instance { get; set; }

    /// <summary>A conductor-placed flag rather than a detected mark.</summary>
    public bool IsCustom { get; set; }

    public string ZoneName { get; set; } = string.Empty;

    /// <summary>Our own extension — see DetectedMark.Spiced.</summary>
    public bool Spiced { get; set; }

    /// <summary>The world is part of a mark’s identity.</summary>
    public uint WorldId { get; set; }

    public string WorldName { get; set; } = string.Empty;
}

/// <summary>
/// HHE train import/export using gzip-compressed JSON encoded as base64.
/// Encoding adapted from HuntHelper/Utilities/ExportImport.cs (img02/HuntHelper, MIT).
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
            DeathObservedAtUtc = m.DeathObservedAtUtc,
            SnipedAtUtc = m.SnipedAtUtc,
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
                DeathObservedAtUtc = m.Dead ? m.DeathObservedAtUtc : null,
                SnipedAtUtc = m.Dead ? m.SnipedAtUtc : null,
                IsCustom = m.IsCustom,
                ZoneName = m.ZoneName,
                Spiced = m.Spiced,
                // Zero for an older code exported before this
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
