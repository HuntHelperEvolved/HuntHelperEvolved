using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

/// <summary>
/// Announces a mark the moment it is first spotted: a line in chat, a spoken
/// message, and fly text on your character.
///
/// This is Hunt Helper's Notifications tab, reproduced rather than reinvented
/// (img02/HuntHelper, MIT licensed — HuntManager.SendChatMessage, SendFlyText
/// and NewMobFoundTTS). Someone arriving from Hunt Helper should be able to
/// type the message they already use and get the line they already know, so the
/// placeholders, the defaults and the colour indices are all its own: A is 12,
/// B is 34, S is 506 in chat, and the fly text deliberately tints the rank and
/// the name differently.
///
/// Everything here is local. Chat goes through IChatGui.Print, which cannot
/// reach another player, and fly text is drawn on your own screen.
/// </summary>
public sealed class MarkNotifier
{
    private readonly IChatGui _chatGui;
    private readonly IFlyTextGui _flyText;
    private readonly IPluginLog _log;
    private readonly Configuration _config;

    // Hunt Helper's chat palette.
    private const ushort AColour = 12;   // pinkish red
    private const ushort BColour = 34;   // blue
    private const ushort SColour = 506;  // gold
    private const ushort FlagColour = 64; // white

    // Its fly text palette, which is not the same: the rank and the name are
    // tinted apart so the pair reads as two things rather than one long string.
    private const ushort AFlyColour = 10;
    private const ushort BFlyColour = 33;
    private const ushort SFlyColour = 16;

    // Health colours, again Hunt Helper's: green at full, yellow down to 70%,
    // red below it.
    private const ushort FullHealthColour = 67;
    private const ushort HurtColour = 573;
    private const ushort BadlyHurtColour = 531;

    /// <summary>
    /// Set when the speech engine could not be started, which is the normal
    /// state anywhere that is not Windows. Chat and fly text are unaffected;
    /// only the spoken message is dropped, and the settings screen says so
    /// rather than leaving a toggle that silently does nothing.
    /// </summary>
    public bool SpeechUnavailable { get; private set; }

    public string SpeechStatus { get; private set; } = "Not tried yet.";

    public MarkNotifier(IChatGui chatGui, IFlyTextGui flyText, IPluginLog log, Configuration config)
    {
        _chatGui = chatGui;
        _flyText = flyText;
        _log = log;
        _config = config;
    }

    /// <summary>
    /// Everything that should happen when a mark is first seen. Each channel
    /// decides for itself whether it is wanted for this rank, so turning chat
    /// off for B ranks does not touch their fly text.
    /// </summary>
    public void Announce(OtherRankSighting sighting)
    {
        SendChat(sighting);
        SendFlyText(sighting);
        Speak(sighting);
    }

    private void SendChat(OtherRankSighting sighting)
    {
        if (!_config.EchoOnDetection) return;

        var wanted = sighting.Rank switch
        {
            HuntRank.B => _config.EchoBRanks,
            HuntRank.A => _config.EchoARanks,
            _ => _config.EchoSRanks,
        };
        if (!wanted) return;

        var template = sighting.Rank switch
        {
            HuntRank.B => _config.DetectionChatMessageB,
            HuntRank.A => _config.DetectionChatMessageA,
            _ => _config.DetectionChatMessageS,
        };

        if (string.IsNullOrWhiteSpace(template)) return;

        try
        {
            _chatGui.Print(BuildChatMessage(template, sighting));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not print the detection message.");
        }
    }

    /// <summary>
    /// Every placeholder Hunt Helper understands, matched case-insensitively
    /// and split out of the template so the text around them survives exactly
    /// as typed.
    /// </summary>
    private const string Placeholders =
        @"(?i)(<flag>|<rank>|<name>|<hpp>"
        + @"|<goldstar>|<silverstar>|<warning>|<nocircle>"
        + @"|<controllerbutton0>|<controllerbutton1>"
        + @"|<priorityworld>|<elementallevel>"
        + @"|<exclamationrectangle>|<notoriousmonster>"
        + @"|<alarm>|<fanfestival>)";

    private SeString BuildChatMessage(string template, OtherRankSighting sighting)
    {
        var rankColour = sighting.Rank switch
        {
            HuntRank.B => BColour,
            HuntRank.A => AColour,
            _ => SColour,
        };

        var sb = new SeStringBuilder();

        foreach (var piece in Regex.Split(template, Placeholders))
        {
            switch (piece.ToLowerInvariant())
            {
                case "<flag>":
                    // A real link rather than typed-out numbers, so it can be
                    // clicked to set the flag.
                    if (sighting.TerritoryId != 0 && sighting.MapId != 0)
                    {
                        sb.AddUiForeground(FlagColour);
                        sb.Append(SeString.CreateMapLink(
                            sighting.TerritoryId, sighting.MapId,
                            sighting.MapPosition.X, sighting.MapPosition.Y));
                        sb.AddUiForegroundOff();
                    }
                    else
                    {
                        sb.AddText($"({sighting.MapPosition.X:F1}, {sighting.MapPosition.Y:F1})");
                    }
                    break;

                case "<rank>":
                    sb.AddUiForeground($"{sighting.Rank}-Rank", rankColour);
                    break;

                case "<name>":
                    sb.AddUiForeground(
                        $"{sighting.Name}{ExpansionData.InstanceGlyph(sighting.Instance)}", rankColour);
                    break;

                case "<hpp>":
                    var hp = sighting.HealthPercent;
                    var hpColour = hp >= 99.5f ? FullHealthColour
                        : hp >= 70f ? HurtColour
                        : BadlyHurtColour;
                    sb.AddUiForeground($"{hp:0}%", hpColour);
                    break;

                case "<goldstar>": sb.AddIcon(BitmapFontIcon.GoldStar); break;
                case "<silverstar>": sb.AddIcon(BitmapFontIcon.SilverStar); break;
                case "<warning>": sb.AddIcon(BitmapFontIcon.Warning); break;
                case "<nocircle>": sb.AddIcon(BitmapFontIcon.NoCircle); break;
                case "<controllerbutton0>": sb.AddIcon(BitmapFontIcon.ControllerButton0); break;
                case "<controllerbutton1>": sb.AddIcon(BitmapFontIcon.ControllerButton1); break;
                case "<priorityworld>": sb.AddIcon(BitmapFontIcon.PriorityWorld); break;
                case "<elementallevel>": sb.AddIcon(BitmapFontIcon.ElementalLevel); break;
                case "<exclamationrectangle>": sb.AddIcon(BitmapFontIcon.ExclamationRectangle); break;
                case "<notoriousmonster>": sb.AddIcon(BitmapFontIcon.NotoriousMonster); break;
                case "<alarm>": sb.AddIcon(BitmapFontIcon.Alarm); break;
                case "<fanfestival>": sb.AddIcon(BitmapFontIcon.FanFestival); break;

                default:
                    sb.AddText(piece);
                    break;
            }
        }

        return sb.BuiltString;
    }

    /// <summary>
    /// Fly text on your own character — the same channel a crit lands in, which
    /// is why it is impossible to miss while running.
    ///
    /// The rank and the name go in as two separately coloured strings, exactly
    /// as Hunt Helper sends them. The trailing numbers are its own and appear to
    /// change nothing; they are kept so the two produce identical output.
    /// </summary>
    private void SendFlyText(OtherRankSighting sighting)
    {
        if (!_config.DetectionFlyTextEnabled) return;

        var wanted = sighting.Rank switch
        {
            HuntRank.B => _config.FlyTextBRanks,
            HuntRank.A => _config.FlyTextARanks,
            _ => _config.FlyTextSRanks,
        };
        if (!wanted) return;

        var (label, labelColour, nameColour) = sighting.Rank switch
        {
            HuntRank.B => ("B RANK", BColour, BFlyColour),
            HuntRank.A => ("A RANK", AColour, AFlyColour),
            _ => ("S RANK", SFlyColour, SColour),
        };

        try
        {
            var rankText = new SeStringBuilder();
            rankText.AddUiForeground(label, labelColour);

            var nameText = new SeStringBuilder();
            nameText.AddUiForeground($"{sighting.Name}", nameColour);

            _flyText.AddFlyText(
                FlyTextKind.DamageCritDh, 1, 1, 1,
                rankText.BuiltString, nameText.BuiltString, 16, 2, 0);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not send the detection fly text.");
        }
    }

    private void Speak(OtherRankSighting sighting)
    {
        if (!_config.DetectionTtsEnabled) return;
        if (SpeechUnavailable) return;

        var wanted = sighting.Rank switch
        {
            HuntRank.B => _config.TtsBRanks,
            HuntRank.A => _config.TtsARanks,
            _ => _config.TtsSRanks,
        };
        if (!wanted) return;

        var template = sighting.Rank switch
        {
            HuntRank.B => _config.DetectionTtsMessageB,
            HuntRank.A => _config.DetectionTtsMessageA,
            _ => _config.DetectionTtsMessageS,
        };

        if (string.IsNullOrWhiteSpace(template)) return;

        Speak(SpokenForm(template, sighting));
    }

    /// <summary>
    /// The spoken version of a template. Hunt Helper's own substitutions, plus
    /// one it does not do: a stray &lt;flag&gt; is dropped rather than read out
    /// as its own angle brackets. It is not in any of the default messages, but
    /// nothing stops it being typed into the box, and a synthesiser saying
    /// "less than flag greater than" is a bug however faithfully it copies.
    /// </summary>
    public static string SpokenForm(string template, OtherRankSighting sighting)
    {
        var message = template
            .Replace("<rank>", $"{sighting.Rank}-Rank", true, CultureInfo.InvariantCulture)
            .Replace("<name>", sighting.Name, true, CultureInfo.InvariantCulture)
            .Replace("<hpp>", $"{sighting.HealthPercent:0}", true, CultureInfo.InvariantCulture)
            .Replace("<flag>", string.Empty, true, CultureInfo.InvariantCulture);

        // The icon placeholders have nothing to say out loud either.
        return Regex.Replace(message, Placeholders, string.Empty).Trim();
    }

    /// <summary>
    /// Says a line, on a synthesiser built for it and thrown away afterwards.
    ///
    /// One synthesiser per message rather than one kept around, which is Hunt
    /// Helper's own fix: SpeakAsync queues, so a shared instance makes four
    /// marks found together read out one after another long after you have
    /// walked past them.
    /// </summary>
    public void Speak(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            var tts = new System.Speech.Synthesis.SpeechSynthesizer();

            if (!string.IsNullOrWhiteSpace(_config.TtsVoiceName))
            {
                // A voice that has since been uninstalled must not take the
                // message down with it; the default voice still says it.
                try { tts.SelectVoice(_config.TtsVoiceName); }
                catch (Exception ex) { _log.Warning(ex, $"TTS voice '{_config.TtsVoiceName}' is unavailable; using the default."); }
            }

            tts.Volume = Math.Clamp(_config.TtsVolume, 0, 100);
            tts.SpeakAsync(message);
            tts.SpeakCompleted += (_, _) => tts.Dispose();

            SpeechUnavailable = false;
            SpeechStatus = "Working.";
        }
        catch (Exception ex)
        {
            // Almost always "not Windows". Stand down rather than throwing once
            // per mark for the rest of the session.
            SpeechUnavailable = true;
            SpeechStatus = $"Speech is unavailable here ({ex.GetType().Name}). Chat and fly text still work.";
            _log.Warning(ex, "Speech synthesis is unavailable; the spoken announcement is off for this session.");
        }
    }

    /// <summary>
    /// The installed voices, for the settings screen. Empty when there is no
    /// speech engine, which the screen reports rather than showing an empty
    /// dropdown with no explanation.
    /// </summary>
    public string[] InstalledVoices()
    {
        try
        {
            using var tts = new System.Speech.Synthesis.SpeechSynthesizer();
            var voices = new System.Collections.Generic.List<string>();
            foreach (var voice in tts.GetInstalledVoices())
            {
                if (voice.Enabled) voices.Add(voice.VoiceInfo.Name);
            }
            return voices.ToArray();
        }
        catch (Exception ex)
        {
            SpeechUnavailable = true;
            SpeechStatus = $"Speech is unavailable here ({ex.GetType().Name}). Chat and fly text still work.";
            _log.Warning(ex, "Could not list the installed speech voices.");
            return Array.Empty<string>();
        }
    }
}
