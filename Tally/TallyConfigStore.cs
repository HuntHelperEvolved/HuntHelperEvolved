using System;
using System.IO;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HuntTally;

/// <summary>
/// Reuses pluginConfigs/HuntTally.json to preserve existing counts without migration.
/// Keeping it separate avoids rewriting kill history on each train save.
/// </summary>
public static class TallyConfigStore
{
    /// <summary>The file name the standalone plugin used, kept verbatim.</summary>
    public const string FileName = "HuntTally.json";

    /// <summary>
    /// Preserves the root type needed by Dalamud to load this file in standalone Hunt Tally.
    /// </summary>
    private const string RootTypeName = "HuntTally.Configuration, HuntTally";

    private static readonly JsonSerializerSettings ReadSettings = new()
    {
        // Skip legacy assembly resolution; the target type is known.
        // Keep default metadata handling so $type is not parsed as a numeric character ID.
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
    /// Stops writes when standalone Hunt Tally is installed, preventing competing saves from losing counts.
    /// </summary>
    public static void SuspendWrites(string reason)
    {
        suspendedReason = reason;
        Service.Log.Warning($"Not writing {FileName}: {reason}");
    }

    /// <summary>
    /// Reads the tally or starts a fresh one. Unreadable files are moved aside before replacement.
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

        // Match Dalamud's metadata order to keep diffs against standalone saves readable.
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
