using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace HuntHelperEvolved;

/// <summary>Local currency snapshots, read on the framework thread for use by the UI.</summary>
internal sealed class TrainCurrencyWallet
{
    private double _secondsSinceRead = 1;
    private uint _playerEntityId;
    private bool _readFailureLogged;
    private HashSet<uint>? _activeTomestones;

    public IReadOnlyDictionary<string, TrainCurrencyBalance>? Balances { get; private set; }

    public void Clear()
    {
        Balances = null;
        _playerEntityId = 0;
        _secondsSinceRead = 1;
    }

    public unsafe void Update(double elapsedSeconds)
    {
        if (!GameReadiness.CanReadCharacters) { Clear(); return; }
        var player = HuntTally.Service.Objects.LocalPlayer;
        if (player is null || player.EntityId is 0 or 0xE0000000) { Clear(); return; }
        if (_playerEntityId != player.EntityId)
        {
            Clear();
            _playerEntityId = player.EntityId;
        }

        if (double.IsFinite(elapsedSeconds)) _secondsSinceRead += Math.Max(0, elapsedSeconds);
        if (_secondsSinceRead < 1) return;
        _secondsSinceRead = 0;

        Exception? failure = null;
        var balances = new Dictionary<string, TrainCurrencyBalance>(StringComparer.Ordinal);
        try
        {
            var inventory = InventoryManager.Instance();
            var currency = CurrencyManager.Instance();
            var container = inventory is null ? null : inventory->GetInventoryContainer(InventoryType.Currency);
            if (container is null || !container->IsLoaded) { Balances = null; return; }

            Read("Allied Seals", () => new(inventory->GetAlliedSeals()));
            if (currency is not null)
            {
                // Item IDs verified against the installed game data and CurrencyManager buckets.
                Read("Centurio Seals", () => currency->HasItem(10307) ? new(currency->GetItemCount(10307)) : null);
                Read("Nuts", () => currency->HasItem(26533) ? new(currency->GetItemCount(26533)) : null);
            }

            // These names and rewards match TrainCurrency's patch 7.4 rules. Confirm
            // their active roles so a future tome rotation cannot warn for old currency.
            if (_activeTomestones is null)
            {
                var active = new HashSet<uint>();
                foreach (var row in HuntTally.Service.Data.GetExcelSheet<TomestonesItem>())
                {
                    if ((row.Tomestones.RowId, row.Item.RowId) is (1, 28) or (2, 48) or (3, 49))
                        active.Add(row.Item.RowId);
                }
                _activeTomestones = active;
            }

            if (_activeTomestones.Contains(28))
                Read("Poetics", () => new(inventory->GetTomestoneCount(28)));
            if (_activeTomestones.Contains(48))
                Read("Mathematics", () => new(inventory->GetTomestoneCount(48)));
            if (_activeTomestones.Contains(49))
                Read("Mnemonics", () =>
                {
                    var limit = InventoryManager.GetLimitedTomestoneWeeklyLimit();
                    var acquired = inventory->GetWeeklyAcquiredTomestoneCount();
                    if (limit <= 0 || acquired < 0 || acquired > limit) return null;
                    return new(inventory->GetTomestoneCount(49), limit - acquired);
                });
        }
        catch (Exception ex) { failure ??= ex; }

        Balances = balances.Count == 0 ? null : balances;
        if (failure is not null)
        {
            if (!_readFailureLogged)
                HuntTally.Service.Log.Warning(failure, "Could not read some currency balances; their cap warnings are unavailable.");
            _readFailureLogged = true;
        }
        else _readFailureLogged = false;

        void Read(string name, Func<TrainCurrencyBalance?> read)
        {
            try
            {
                if (read() is { } balance) balances[name] = balance;
            }
            catch (Exception ex) { failure ??= ex; }
        }
    }
}
