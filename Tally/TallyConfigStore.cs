using System;
using System.IO;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HuntTally;

/// <summary>
/// Loads and saves the tally from Hunt Tally's own configuration file, not
/// Hunt Helper Evolved's.
///
/// The tally used to be a separate plugin, so its data already sits in
/// pluginConfigs/HuntTally.json — hundreds of kilobytes of per-character kill
/// history for anyone who has been running it. Keeping that file as the store
/// is what makes upgrading to the merged plugin lossless: there is no
/// migration step to get wrong, and nothing to import.
///
/// It also keeps the two configurations at their natural write rates. The
/// relay saves its in-progress train every ten seconds; folding a multi-
/// megabyte kill history into that file would mean rewriting the whole thing
/// on every one of those saves.
///
/// Dalamud's own loader is bypassed because it only ever reads the config file
/// named after the plugin. That costs us its serialiser settings, which are
/// reproduced here:
///
///   Reading  — type metadata is ignored outright. The file on disk names the
///              types as "HuntTally.Configuration, HuntTally", and the HuntTally
///              assembly no longer exists. The target type is known statically
///              anyway, so resolving it from the file was never needed.
///
///   Writing  — the root $type is written back exactly as Dalamud would have
///              written it, so a user who decides to go back to the standalone
///              plugin finds a file it can still load. Nested values are left
///              bare: their declared types are concrete, so Dalamud resolves
///              them without the hint.
/// </summary>
public static class TallyConfigStore
{
    /// <summary>The file name the standalone plugin used, kept verbatim.</summary>
    public const string FileName = "HuntTally.json";

    /// <summary>
    /// What the standalone plugin's assembly wrote for the root object. Dalamud
    /// deserialises the root as IPluginConfiguration, so this line is the only
    /// thing that makes the file loadable by it.
    /// </summary>
    private const string RootTypeName = "HuntTally.Configuration, HuntTally";

    private static readonly JsonSerializerSettings ReadSettings = new()
    {
        // The file names its types as "HuntTally.Configuration, HuntTally", and
        // that assembly no longer exists. None makes Newtonsoft skip resolving
        // them: the target type is known statically here, so the hints were
        // never needed.
        //
        // MetadataPropertyHandling stays at its default on purpose. Setting it
        // to Ignore also stops $type being recognised as metadata, and inside a
        // dictionary it then becomes an ordinary key - so Characters, keyed by
        // content id, fails on "could not convert '$type' to System.UInt64" and
        // takes the whole load down with it.
        TypeNameHandling = TypeNameHandling.None,
    };

    private static readonly JsonSerializerSettings WriteSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        Formatting = Formatting.Indented,
    };

    private static string? path;
    private static string? suspendedReason;

    /// <summary>Full path to the tally file, once <see cref="Load"/> has run.</summary>
    public static string? Path => path;

    /// <summary>Why writing is suspended, or null when it is not.</summary>
    public static string? SuspendedReason => suspendedReason;

    /// <summary>
    /// Stops this plugin writing the tally file at all.
    ///
    /// Used when the standalone Hunt Tally plugin turns out to still be
    /// installed. Both would then be counting the same kills into the same
    /// file on their own timers, and each save would overwrite whatever the
    /// other had written since it last read — so the two would not merely
    /// disagree, they would destroy each other's counts. Standing down is the
    /// only safe move: the plugin that has been keeping the file up to now
    /// keeps it, intact, until the user removes one of the two.
    /// </summary>
    public static void SuspendWrites(string reason)
    {
        suspendedReason = reason;
        Service.Log.Warning($"Not writing {FileName}: {reason}");
    }

    /// <summary>
    /// Reads the tally, or returns a fresh one when there is nothing to read.
    ///
    /// A file that exists but cannot be parsed is never silently replaced with
    /// an empty tally — it is moved aside first, so a bad read costs the user
    /// their session's counts rather than their entire history.
    /// </summary>
    public static Configuration Load(IDalamudPluginInterface pluginInterface)
    {
        // ConfigFile is <pluginConfigs>/HuntHelperEvolved.json, so its directory is
        // the shared pluginConfigs folder the standalone plugin also wrote into.
        var directory = pluginInterface.ConfigFile.Directory;
        if (directory is null)
        {
            Service.Log.Error("Could not locate the plugin config directory; the tally will not persist.");
            return new Configuration();
        }

        path = System.IO.Path.Combine(directory.FullName, FileName);

        if (!File.Exists(path))
        {
            Service.Log.Information($"No existing tally at {path}; starting a new one.");
            return new Configuration();
        }

        try
        {
            var text = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<Configuration>(text, ReadSettings);

            if (loaded is not null)
            {
                Service.Log.Information(
                    $"Loaded the tally from {FileName}: {loaded.Characters.Count} character(s).");
                return loaded;
            }

            Service.Log.Error($"{FileName} parsed to nothing.");
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Could not read {FileName}.");
        }

        Quarantine();
        return new Configuration();
    }

    /// <summary>
    /// Writes the tally, via a temporary file so an interrupted write cannot
    /// leave a truncated one in its place.
    /// </summary>
    public static void Save(Configuration config)
    {
        if (path is null || suspendedReason is not null)
            return;

        try
        {
            var json = Serialise(config);
            var temp = path + ".tmp";

            File.WriteAllText(temp, json);

            // Replace rather than Move: Move will not overwrite, and Delete
            // followed by Move leaves a window with no file at all.
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Could not write {FileName}; this session's counts may be lost.");
        }
    }

    /// <summary>
    /// Serialises with the root type hint Dalamud expects, so the standalone
    /// plugin could still load the file.
    /// </summary>
    private static string Serialise(Configuration config)
    {
        var root = JObject.FromObject(config, JsonSerializer.Create(WriteSettings));

        // AddFirst so it leads the object, matching what Dalamud produces.
        // Newtonsoft does not require the position, but a file that diffs
        // cleanly against the old one is easier to reason about.
        root.AddFirst(new JProperty("$type", RootTypeName));

        return root.ToString(Formatting.Indented);
    }

    /// <summary>
    /// Moves an unreadable file aside instead of overwriting it. Whatever went
    /// wrong, the user's history is still on disk and can be looked at.
    /// </summary>
    private static void Quarantine()
    {
        if (path is null || !File.Exists(path))
            return;

        try
        {
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target);
            Service.Log.Warning($"Moved the unreadable tally to {target}. A new one will be started.");
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Could not move the unreadable tally aside.");
        }
    }
}
