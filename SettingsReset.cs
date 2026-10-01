using System;
using System.Linq;

namespace HuntHelperEvolved;

public enum SettingsResetCategory
{
    Train, Counters, MapDisplay, MapColours, PlayerGuides,
    DetectionNotifications, CommunityNotifications, Travel, Sharing,
    HuntWindows, Tally, General, AllPreferences, SyncConnection, DiscordWebhooks,
}

/// <summary>Resets explicit preference groups without touching hunt records or saving either configuration.</summary>
public static class SettingsReset
{
    public static void Apply(Configuration config, HuntTally.Configuration tally, SettingsResetCategory category, bool includeTally = true)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(tally);

        if (category == SettingsResetCategory.AllPreferences)
        {
            Apply(config, tally, SettingsResetCategory.Train);
            Apply(config, tally, SettingsResetCategory.Counters);
            Apply(config, tally, SettingsResetCategory.MapDisplay);
            Apply(config, tally, SettingsResetCategory.MapColours);
            Apply(config, tally, SettingsResetCategory.PlayerGuides);
            Apply(config, tally, SettingsResetCategory.DetectionNotifications);
            Apply(config, tally, SettingsResetCategory.CommunityNotifications);
            Apply(config, tally, SettingsResetCategory.Travel);
            Apply(config, tally, SettingsResetCategory.Sharing);
            Apply(config, tally, SettingsResetCategory.HuntWindows);
            if (includeTally) Apply(config, tally, SettingsResetCategory.Tally);
            Apply(config, tally, SettingsResetCategory.General);
            return;
        }

        // Each invocation owns its defaults and collections. Nothing here is shared
        // between configurations, and new runtime fields are never reset implicitly.
        var defaults = new Configuration();
        switch (category)
        {
            case SettingsResetCategory.Train:
                config.PollIntervalSeconds = defaults.PollIntervalSeconds;
                config.AutoMarkDeadEnabled = defaults.AutoMarkDeadEnabled;
                config.MarkDeadOnObservedDefeat = defaults.MarkDeadOnObservedDefeat;
                config.EchoOnMarkClick = defaults.EchoOnMarkClick;
                config.TrainPopoutControlsExpanded = defaults.TrainPopoutControlsExpanded;
                config.HideZonesInPopout = defaults.HideZonesInPopout;
                config.SwapMarkAndZoneInPopout = defaults.SwapMarkAndZoneInPopout;
                config.ShowMarkAge = defaults.ShowMarkAge;
                config.AutoAdvance = defaults.AutoAdvance;
                config.EchoOnAdvance = defaults.EchoOnAdvance;
                config.HideDeadMarks = defaults.HideDeadMarks;
                config.ShowSpicing = defaults.ShowSpicing;
                config.GroupTrainByExpansion = defaults.GroupTrainByExpansion;
                config.ExpansionOrder = defaults.ExpansionOrder;
                config.WorldExpansionOrder = defaults.WorldExpansionOrder;
                config.CollapsedExpansions = defaults.CollapsedExpansions;
                config.AutoExpandNextExpansion = defaults.AutoExpandNextExpansion;
                config.TrainRowHeight = defaults.TrainRowHeight;
                config.AutoTrainWatches = defaults.AutoTrainWatches;
                config.ShowSRankWatchesInTrainList = defaults.ShowSRankWatchesInTrainList;
                break;
            case SettingsResetCategory.Counters:
                config.CountOnlyMyKills = defaults.CountOnlyMyKills;
                config.CounterConfig = config.CounterConfig.Keys.ToDictionary(name => name, _ => new CounterSettings());
                break;
            case SettingsResetCategory.MapDisplay:
                config.ShowSpawnPointsOnMap = defaults.ShowSpawnPointsOnMap;
                config.HideOccupiedSpawnPoints = defaults.HideOccupiedSpawnPoints;
                config.DimFoundARankSpawnPoints = defaults.DimFoundARankSpawnPoints;
                config.ShowARankPoints = defaults.ShowARankPoints;
                config.ShowBRankPoints = defaults.ShowBRankPoints;
                config.ShowSRankPoints = defaults.ShowSRankPoints;
                config.ShowARankMarks = defaults.ShowARankMarks;
                config.ShowBRankMarks = defaults.ShowBRankMarks;
                config.ShowSRankMarks = defaults.ShowSRankMarks;
                config.ShowMarksOnMap = defaults.ShowMarksOnMap;
                config.SpawnDotSize = defaults.SpawnDotSize;
                config.OutlineMapDots = defaults.OutlineMapDots;
                config.ClickSpawnPointToFlag = defaults.ClickSpawnPointToFlag;
                config.ShowMarkLabelsOnMap = defaults.ShowMarkLabelsOnMap;
                config.MarkLabelFontSize = defaults.MarkLabelFontSize;
                config.ShowSsEventOnMap = defaults.ShowSsEventOnMap;
                config.ShowMapControlBar = defaults.ShowMapControlBar;
                config.SyncShowRemoteMarksOnMap = defaults.SyncShowRemoteMarksOnMap;
                config.ShowSRankCandidatesOnMap = defaults.ShowSRankCandidatesOnMap;
                config.SpawnCandidateOutlineWidth = defaults.SpawnCandidateOutlineWidth;
                break;
            case SettingsResetCategory.MapColours:
                config.SpawnDotColourEmpty = defaults.SpawnDotColourEmpty;
                config.SpawnDotColourInTrain = defaults.SpawnDotColourInTrain;
                config.SpawnDotColourB = defaults.SpawnDotColourB;
                config.SpawnDotColourA = defaults.SpawnDotColourA;
                config.SpawnDotColourS = defaults.SpawnDotColourS;
                config.MarkLabelColour = defaults.MarkLabelColour;
                config.MarkLabelOutlineColour = defaults.MarkLabelOutlineColour;
                config.SsMinionColour = defaults.SsMinionColour;
                config.SpawnDotColourSCandidate = defaults.SpawnDotColourSCandidate;
                config.SpawnDotColourSRuledOut = defaults.SpawnDotColourSRuledOut;
                break;
            case SettingsResetCategory.PlayerGuides:
                config.ShowPlayerGuides = defaults.ShowPlayerGuides;
                config.ShowPlayerCircleOnMap = defaults.ShowPlayerCircleOnMap;
                config.PlayerCircleColour = defaults.PlayerCircleColour;
                config.PlayerCircleRadiusScale = defaults.PlayerCircleRadiusScale;
                config.PlayerCircleThickness = defaults.PlayerCircleThickness;
                config.ShowPlayerDirectionLine = defaults.ShowPlayerDirectionLine;
                config.PlayerDirectionLineColour = defaults.PlayerDirectionLineColour;
                config.PlayerDirectionLineThickness = defaults.PlayerDirectionLineThickness;
                config.ShowPlayerPositionDot = defaults.ShowPlayerPositionDot;
                config.PlayerPositionDotColour = defaults.PlayerPositionDotColour;
                config.PlayerPositionDotSize = defaults.PlayerPositionDotSize;
                config.ShowPlayerFacingOnMap = defaults.ShowPlayerFacingOnMap;
                config.PlayerFacingColour = defaults.PlayerFacingColour;
                break;
            case SettingsResetCategory.DetectionNotifications:
                config.EchoOnDetection = defaults.EchoOnDetection;
                config.EchoBRanks = defaults.EchoBRanks;
                config.EchoARanks = defaults.EchoARanks;
                config.EchoSRanks = defaults.EchoSRanks;
                config.DetectionChatMessageA = defaults.DetectionChatMessageA;
                config.DetectionChatMessageB = defaults.DetectionChatMessageB;
                config.DetectionChatMessageS = defaults.DetectionChatMessageS;
                config.DetectionTtsEnabled = defaults.DetectionTtsEnabled;
                config.TtsBRanks = defaults.TtsBRanks;
                config.TtsARanks = defaults.TtsARanks;
                config.TtsSRanks = defaults.TtsSRanks;
                config.DetectionTtsMessageA = defaults.DetectionTtsMessageA;
                config.DetectionTtsMessageB = defaults.DetectionTtsMessageB;
                config.DetectionTtsMessageS = defaults.DetectionTtsMessageS;
                config.TtsVoiceName = defaults.TtsVoiceName;
                config.TtsVolume = defaults.TtsVolume;
                config.DetectionFlyTextEnabled = defaults.DetectionFlyTextEnabled;
                config.FlyTextBRanks = defaults.FlyTextBRanks;
                config.FlyTextARanks = defaults.FlyTextARanks;
                config.FlyTextSRanks = defaults.FlyTextSRanks;
                break;
            case SettingsResetCategory.CommunityNotifications:
                config.SRankZoneReminderEnabled = defaults.SRankZoneReminderEnabled;
                config.SRankZoneReminderSound = defaults.SRankZoneReminderSound;
                config.SyncSpawnAlerts = defaults.SyncSpawnAlerts;
                config.SyncSpawnSound = defaults.SyncSpawnSound;
                config.SyncSpawnCurrentDc = defaults.SyncSpawnCurrentDc;
                config.SyncSpawnDataCenters = defaults.SyncSpawnDataCenters;
                config.SyncRelayRules = defaults.SyncRelayRules;
                config.RelayPresetId = defaults.RelayPresetId;
                break;
            case SettingsResetCategory.Travel:
                config.BlacklistedAetherytes = Configuration.CreateDefaultAetheryteBlacklist();
                config.BlacklistSeeded = true;
                config.TeleportAlsoFlags = defaults.TeleportAlsoFlags;
                break;
            case SettingsResetCategory.Sharing:
                config.SyncEnabled = defaults.SyncEnabled;
                config.SyncAllowPlaintext = defaults.SyncAllowPlaintext;
                config.SyncShareTrain = defaults.SyncShareTrain;
                config.SyncShareSightings = defaults.SyncShareSightings;
                config.SyncReceiveBearFeed = defaults.SyncReceiveBearFeed;
                config.SyncReportSRankKills = defaults.SyncReportSRankKills;
                break;
            case SettingsResetCategory.HuntWindows:
                config.VisibleMarkFilters = defaults.VisibleMarkFilters;
                config.ActiveMarkPresetId = defaults.ActiveMarkPresetId;
                config.ARankWindowCurrentWorld = defaults.ARankWindowCurrentWorld;
                config.ARankWindowWorlds = defaults.ARankWindowWorlds;
                config.ARankWindowExpansions = defaults.ARankWindowExpansions;
                config.ARankWindowAvailableOnly = defaults.ARankWindowAvailableOnly;
                config.ARankWindowSearch = defaults.ARankWindowSearch;
                config.SRankWindowCurrentWorld = defaults.SRankWindowCurrentWorld;
                config.SRankWindowWorlds = defaults.SRankWindowWorlds;
                config.SRankWindowExpansion = defaults.SRankWindowExpansion;
                // The window constructor expands a fresh null filter once. The
                // already-running window needs the effective selection immediately.
                config.SRankWindowExpansions = Sync.SRankTimerData.Expansions.ToList();
                config.SRankWindowAvailableOnly = defaults.SRankWindowAvailableOnly;
                config.SRankWindowHideUnmetConditions = defaults.SRankWindowHideUnmetConditions;
                config.SRankWindowSearch = defaults.SRankWindowSearch;
                break;
            case SettingsResetCategory.Tally:
                var tallyDefaults = new HuntTally.Configuration();
                tally.HistoryLimit = tallyDefaults.HistoryLimit;
                tally.RequireCombat = tallyDefaults.RequireCombat;
                tally.UseDamageDetection = tallyDefaults.UseDamageDetection;
                tally.StrictCredit = tallyDefaults.StrictCredit;
                tally.RequireRewardMessage = tallyDefaults.RequireRewardMessage;
                tally.MaxDistance = tallyDefaults.MaxDistance;
                tally.ChatOnKill = tallyDefaults.ChatOnKill;
                tally.PublishAllMarkDeaths = tallyDefaults.PublishAllMarkDeaths;
                tally.TrackB = tallyDefaults.TrackB;
                tally.TrackA = tallyDefaults.TrackA;
                tally.TrackS = tallyDefaults.TrackS;
                tally.AutoSeedOnLogin = tallyDefaults.AutoSeedOnLogin;
                break;
            case SettingsResetCategory.General:
                config.ShowReleaseNotesOnUpdate = defaults.ShowReleaseNotesOnUpdate;
                config.Theme = defaults.Theme;
                config.WindowOpacity = defaults.WindowOpacity;
                break;
            case SettingsResetCategory.SyncConnection:
                config.SyncEnabled = defaults.SyncEnabled;
                config.SyncServerUrl = defaults.SyncServerUrl;
                config.SyncPassword = defaults.SyncPassword;
                config.SyncDisplayName = defaults.SyncDisplayName;
                break;
            case SettingsResetCategory.DiscordWebhooks:
                config.Webhooks = defaults.Webhooks;
#pragma warning disable CS0618 // Explicit reset also prevents legacy webhook migration.
                config.WebhookUrls = null;
#pragma warning restore CS0618
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown settings category.");
        }
    }
}
