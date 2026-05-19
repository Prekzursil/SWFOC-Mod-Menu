using System;
using System.Collections.Generic;
using System.Linq;

namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// 2026-04-27 (iter 28) — fluent helpers for building <see cref="FakeGameState"/>
/// scenarios in tests. Replaces 5–10 lines of <c>state.Units.Add(new FakeUnit { ... })</c>
/// boilerplate with one-liners. Use via:
/// <code>
/// var state = new FakeGameStateBuilder()
///     .Tactical()
///     .WithUnit("Rebel_Trooper_Squad", slot: 0, count: 5)
///     .WithDeadHero("Han_Solo", slot: 0)
///     .WithLiveHero("Luke_Skywalker", slot: 0)
///     .Build();
/// </code>
/// </summary>
public sealed class FakeGameStateBuilder
{
    private FakeGameState _state;

    public FakeGameStateBuilder()
    {
        _state = FakeGameState.NewTacticalSkirmish();
    }

    /// <summary>Reset the builder to a fresh tactical skirmish.</summary>
    public FakeGameStateBuilder Tactical()
    {
        _state = FakeGameState.NewTacticalSkirmish();
        return this;
    }

    /// <summary>Reset the builder to a fresh galactic campaign.</summary>
    public FakeGameStateBuilder Galactic()
    {
        _state = FakeGameState.NewGalacticCampaign();
        return this;
    }

    /// <summary>Add <paramref name="count"/> alive units of the given type to the slot.</summary>
    public FakeGameStateBuilder WithUnit(string typeName, int slot, int count = 1,
        float maxHull = 100f)
    {
        for (var i = 0; i < count; i++)
        {
            _state.Units.Add(new FakeUnit
            {
                TypeName = typeName,
                OwnerSlot = slot,
                MaxHull = maxHull,
                CurrentHull = maxHull,
                IsGround = true,
            });
        }
        return this;
    }

    /// <summary>Add a single dead unit (alive=false, hull=0).</summary>
    public FakeGameStateBuilder WithDeadUnit(string typeName, int slot, float maxHull = 100f)
    {
        _state.Units.Add(new FakeUnit
        {
            TypeName = typeName,
            OwnerSlot = slot,
            MaxHull = maxHull,
            CurrentHull = 0f,
            Alive = false,
        });
        return this;
    }

    /// <summary>Add an alive hero unit.</summary>
    public FakeGameStateBuilder WithLiveHero(string typeName, int slot, float maxHull = 200f)
    {
        _state.Units.Add(new FakeUnit
        {
            TypeName = typeName,
            OwnerSlot = slot,
            IsHero = true,
            MaxHull = maxHull,
            CurrentHull = maxHull,
        });
        return this;
    }

    /// <summary>Add a dead hero unit (alive=false, hull=0).</summary>
    public FakeGameStateBuilder WithDeadHero(string typeName, int slot, float maxHull = 200f)
    {
        _state.Units.Add(new FakeUnit
        {
            TypeName = typeName,
            OwnerSlot = slot,
            IsHero = true,
            MaxHull = maxHull,
            CurrentHull = 0f,
            Alive = false,
        });
        return this;
    }

    /// <summary>Add an invulnerable unit (Invulnerable=true).</summary>
    public FakeGameStateBuilder WithInvulnerableUnit(string typeName, int slot)
    {
        var u = new FakeUnit
        {
            TypeName = typeName,
            OwnerSlot = slot,
            Invulnerable = true,
        };
        _state.Units.Add(u);
        return this;
    }

    /// <summary>Override credits for a slot.</summary>
    public FakeGameStateBuilder WithCredits(int slot, int credits)
    {
        var p = _state.GetPlayer(slot);
        if (p is not null) p.Credits = credits;
        return this;
    }

    /// <summary>Add an additional player slot beyond the seeded skirmish.</summary>
    public FakeGameStateBuilder WithPlayer(int slot, string faction, bool isHuman = false,
        int credits = 5000)
    {
        if (_state.Players.Any(p => p.Slot == slot))
        {
            throw new InvalidOperationException($"slot {slot} already exists; use WithCredits to mutate");
        }
        var p = isHuman
            ? FakePlayer.NewLocalHumanSlot(slot, faction)
            : FakePlayer.NewAiSlot(slot, faction);
        p.Credits = credits;
        _state.Players.Add(p);
        return this;
    }

    /// <summary>Register an extra type name in the catalog (so SpawnUnit / BatchTypeExists accept it).</summary>
    public FakeGameStateBuilder WithType(params string[] types)
    {
        foreach (var t in types) _state.KnownTypeNames.Add(t);
        return this;
    }

    /// <summary>Add a planet (galactic mode only).</summary>
    public FakeGameStateBuilder WithPlanet(string name, string ownerFaction = "NONE",
        bool revealed = false, int tech = 1)
    {
        var slot = _state.Players.FirstOrDefault(p =>
            string.Equals(p.Faction, ownerFaction, StringComparison.OrdinalIgnoreCase))?.Slot ?? -1;
        var p = FakePlanet.New(name, slot, revealed: revealed, ownerFaction: ownerFaction);
        p.TechLevel = tech;
        _state.Planets.Add(p);
        return this;
    }

    public FakeGameState Build() => _state;
}
