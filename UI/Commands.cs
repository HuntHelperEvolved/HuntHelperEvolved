using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private readonly Dictionary<string, CommandInfo> _ownedCommands = new();
    private readonly Dictionary<string, (IReadOnlyCommandInfo.HandlerDelegate Handler, string Help)> _commandDefinitions = new();
    private readonly HashSet<string> _shortHelpCommands = new();
    private readonly HashSet<string> _longHelpCommands = new();
    private volatile bool _commandHelpDirty;

    private void RegisterCommands()
    {
        Pair("/hh", ConfigCommand, OnCommand, "Open the main window.");
        Pair("/hht", TrainCommand, OnTrainCommand, "Open the train list popout.");
        Pair("/hhn", "/htrn", OnNextMarkCommand, "Move to the next live mark and flag it.");
        Pair("/hhna", NextAetheryteCommand, OnNextAetheryteCommand, "Name the closest aetheryte to the next mark.");
        Pair("/hhc", CounterCommand, OnCounterCommand, "Open the S-rank counter popout.");
        Pair("/hhm", MapCommand, OnMapCommand, "Show or hide the map control bar.");
        Pair("/hhs", SRankCommand, OnSRankCommand, "Open S-rank timers and mapping.");
        // /htra already means next aetheryte; retain it for existing macros.
        Pair("/hha", "/htraw", (_, _) => _arankWindow.Toggle(), "Open A-rank timers.");
        Pair("/hhv", "/htrv", (_, _) => _activeMarksWindow.Toggle(), "Open Active Marks, with health and combat status.");
        Pair("/hhtally", "/htrtally", OnTallyCommand, "Open the lifetime tally; add config for settings or ipc to test the feed.");
        RegisterOwnedCommand("/hhsa", (_, _) => _activeMarksWindow.Toggle(), "Open Active Marks.");
        RegisterOwnedCommand(TallyCommand, OnTallyCommand, "Open the lifetime tally.");
        RefreshCommandHelp();
        _pluginInterface.ActivePluginsChanged += OnCommandPluginsChanged;
    }

    private void Pair(string shortCommand, string longCommand, IReadOnlyCommandInfo.HandlerDelegate handler, string help)
    {
        _shortHelpCommands.Add(shortCommand);
        _longHelpCommands.Add(longCommand);
        RegisterOwnedCommand(shortCommand, handler, help);
        RegisterOwnedCommand(longCommand, handler, help);
    }

    private void RegisterOwnedCommand(string command, IReadOnlyCommandInfo.HandlerDelegate handler, string help)
    {
        _commandDefinitions[command] = (handler, help);
        var info = new CommandInfo(handler) { HelpMessage = help, ShowInHelp = false };
        if (_commandManager.AddHandler(command, info)) _ownedCommands.Add(command, info);
        else _log.Warning("Could not register {Command}; another plugin holds it.", command);
    }

    private void OnCommandPluginsChanged(IActivePluginsChangedEventArgs _) => _commandHelpDirty = true;

    private void RefreshCommandHelp()
    {
        var retryMissing = _commandHelpDirty;
        _commandHelpDirty = false;
        if (retryMissing)
            foreach (var (command, definition) in _commandDefinitions.Where(p => !_ownedCommands.ContainsKey(p.Key)).ToArray())
                RegisterOwnedCommand(command, definition.Handler, definition.Help);
        var installed = _pluginInterface.InstalledPlugins.Any(p =>
            p.InternalName.Equals("HuntHelper", StringComparison.OrdinalIgnoreCase));
        var visible = installed ? _longHelpCommands : _shortHelpCommands;
        foreach (var (command, info) in _ownedCommands) info.ShowInHelp = visible.Contains(command);
    }

    private void UnregisterCommands()
    {
        _pluginInterface.ActivePluginsChanged -= OnCommandPluginsChanged;
        foreach (var command in _ownedCommands.Keys) _commandManager.RemoveHandler(command);
        _ownedCommands.Clear();
    }
}
