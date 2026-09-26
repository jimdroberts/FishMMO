# AI System (NPC & Pet)

**Short description:** Server-authoritative NPC brain — a tick-driven state machine over data-defined archetypes, with a shared combat decision core, threat tracking, tick-stepped NavMesh movement with stuck and off-mesh recovery, multi-attacker spacing and separation, and distance-based level of detail.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guide](#quick-start-guide)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

Every NPC and pet is driven by one `AIController`, which runs a `BaseAIState` machine on the FishNet `TimeManager` tick.

**The whole system is server-only and lives in the `FishMMO.Server` assembly**, which no client build compiles. An NPC prefab carries no AI component and no AI data: the scene server's `AISystem` runs an `AIBrainHost` that adds an `AIController` (and its `NavMeshAgent`) the first time an NPC is spawned, gives it the archetype the `AIBrainCatalogue` names for that prefab — or a spawner's override — on every spawn, and drives every brain from one network-tick subscription. Shared code reaches a brain only through `INPCBrain` (aim, the leash evade, corpse hand-off, facing an interactor, taunt and area threat), resolved with `character.TryGet(out INPCBrain)`, which is false on every client.

| Server piece | Responsibility |
|---|---|
| `AISystem` | The scene server behaviour; owns the host and names the catalogue. |
| `AIBrainHost` | Attaches, ticks and resets brains for one `NetworkManager`. `Prepare(npc, home, archetypeOverride)` before `ServerManager.Spawn`; `NPC.OnServerSpawned` catches anything spawned another way. Also ticks every `NPCGroup` (pack) after the brains. |
| `AIBrainCatalogue` | NPC prefab → archetype and boss script, keyed by FishNet's `AssetPathHash`. Addressable in `Server_Static_Permanent`: registered when created, and re-checked by every build. |

The system is layered so that **archetypes are data, not code**:

| Layer | Responsibility |
|---|---|
| `AIArchetypeTemplate` | One asset that is a whole brain: which states, which personality, which threat tuning, which LOD profile. The only AI wiring an NPC has, through its catalogue entry. |
| `BaseAIState` machine | Execution. What the NPC is doing right now. |
| `AICombatDecision` | A pure function over plain floats that every attacking state shares. |
| `AICombatPersonality` | Ability preferences, flee threshold, targeting mode. |
| `AIAbilityRotation` | Optional condition-driven ability selection, evaluated before the default scorer. |
| `AIBehaviorTree` | Optional decision layer above the state machine, for scripted and boss encounters. |
| `AggressionController` | Threat table, vulnerability scoring and target selection. |
| `AIStateClock` | Schedules each state's updates and reports the real interval one covers. |
| `AIAbilityReach` / `AIKiteBudget` / `AISeparation` | Pure helpers the combat and movement paths lean on — how far an ability really hits, how long an NPC may kite, and how bodies push apart. |

Melee, archer, caster, healer, defender and rogue behaviour all fall out of four serialized numbers fed into the shared decision — `PreferredDistance`, `MinComfortDistance`, `EmergencyRetreatThreshold`, and the controller's personality. A designer builds a new archetype by creating an asset, not by writing a class. Only three archetypes carry code, because they need something the numbers cannot express: healers scan for injured allies, defenders body-block for one, and rogues open from a target's rear arc.

Because the decision is a pure function over plain floats, an archetype's behaviour is directly assertable in an EditMode test — a "pathetic" critter can be proven to flee where a "raging" one does not, without a scene.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | Dedicated server only; not compiled into clients |
| Linux | Yes | Dedicated server only; not compiled into clients |
| WebGL | N/A | Server-only system |

- **Unity Version:** Unity 6.3 LTS
- **Scripting Backend:** IL2CPP

## Features

### Timing

- **Tick-driven, not frame-driven.** The brain runs on `TimeManager.OnTick`, the same fixed 30 Hz clock as prediction, cooldowns and ability activation. `AiTickRate` (default 8 Hz) is rounded to a whole divisor of the network tick so the brain is phase-locked and never drifts. `EffectiveAiTickRate` reports what a requested rate resolved to.
- **Exact deltas.** Elapsed time per AI tick is computed from the fixed tick rate, not measured from a variable frame. There is no drift to correct and no spike to clamp.
- **Facing runs on the network tick**, matching the `NetworkTransform` send rate — faster is work nobody sees, slower makes an NPC's head snap between orientations while its position interpolates smoothly. Smoothing is `1 - e^(-rate * dt)`, which is identical at any step size. Once the NPC is within `FACING_TOLERANCE_DEGREES` (0.1°) of its look target the rotation is left alone rather than rewritten every tick.
- **Deterministic stagger.** Every per-NPC phase is drawn from `AIController.IdentityKey`, a bijective hash of the pooled instance's ID; raw instance IDs from one spawn wave sit a constant stride apart, and used raw they bunched a camp onto a fraction of the ticks. The brain tick takes `staggerID % ticksPerAiUpdate` and the LOD gate the quotient (`ResolveLodPhase`), so the two phases are independent and a population spreads over every network-tick slot. The gate counts a monotonic AI tick index, so the spread is identical on a server running at 200 FPS and one at 30.
- **Periodic checks start at their own phase.** The enemy sweep, LOD re-evaluation, leash check, shelter check and threat decay each start at a fraction of their period drawn from the identity key (`SeedTimerPhases`, called by `AIBrainHost.Prepare`). Started at zero, a camp spawned together ran every sweep on one brain tick in each period.
- **A brain that keeps throwing is quarantined.** `AIBrainHost` runs each brain in its own try/catch and logs through a per-brain `RepeatingFaultLog` — the first trace in full, identical repeats as periodic counts. After `MaxConsecutiveTickFaults` (30) failures with no completed pipeline run between them, the brain is taken off the tick, its body halted where it stands, and one error names the NPC, its archetype and its scene. It stays off until the NPC is next prepared (its respawn). A quarantined brain is not `IsRunning`, so it neither enters combat nor evades. A pack that throws is logged the same way but not quarantined.
- **A state is handed the interval it actually covers.** A state asks to be updated every `updateRate` seconds while the brain ticks far more often; `AIStateClock` accumulates the ticks in between and reports the real elapsed time as `AIController.StateDeltaTime`. Every timer a state advances — attack pacing, unreachable-target patience, retreat — reads `StateDeltaTime`, not `LastAiDeltaTime`. Fed the brain's tick delta instead, a 1.2 s attack cooldown took ten seconds of wall clock.

### Level of detail

- Four tiers by distance to the nearest player observer, each with its own update interval in AI ticks: **Active** (full pipeline), **Nearby** (no behaviour tree, no boss script, no proactive sweep — combat still works, entry is event-driven), **Far** (movement only, combat disengages), **Dormant** (wake-up check only; the body is not stepped either unless the agent still has a path or link to finish, `ShouldStepBody`).
- Three profiles ship: `Standard AI LOD`, `Dense Population AI LOD`, `Companion AI LOD` (pets stay responsive — they are always beside the player and always on screen).
- **Distance is measured, not inferred from the observer set.** `TryGetNearestPlayerSqrDistance` asks `ObserverStreamingRegistry` how far the nearest viewer is; `ResolveLodTier` treats an unanswered measurement as `Active`, because an unanswered question is not permission to suspend a brain.
- The out-of-combat enemy sweep gates on `AIController.HasNearbyPlayer`, **not** `Observers.Count`. The observer set is a bandwidth budget, so a monster evicted from every viewer's budget had no observers, stopped sweeping entirely and could not aggro the player standing in front of it.
- The leash reset a Far transition performs — interrupt, full heal, threat clear, boss phase reset — re-asks proximity itself (`AllowsLeashReset`) rather than trusting the tier that triggered it. A tier says how much work to do; only distance says whether a fight is really over.

### Combat

- One `BaseAttackingState` shared by every archetype, plus `HealerAttackingState`, `DefenderAttackingState` and `RogueAttackingState` where behaviour cannot be expressed as tuning.
- **Range hysteresis.** An NPC already attacking tolerates a target drifting 10% past its ability range before giving chase. Without it a strafing target flips the NPC between Attack and CloseDistance every tick, toggling `isStopped` and making it shudder in place.
- **Combat slots.** Several attackers on one target claim distinct angular slots around it rather than all pathing to the same point. Ring capacity is derived from geometry — how many agents of a given radius fit on a circle — and overflow attackers form a staggered second rank.
- **Reach, not `Ability.Range`.** `Ability.Range` is `Speed × LifeTime` — how far a projectile travels — so every ability whose object does not travel (a punch spawned in front of the caster, a held ball of flame, a self buff) reports zero, and the planner read that zero as "cannot reach": a melee orc walked up to its target and stood there, a caster held its archetype's distance and never cast. `AIAbilityReach.Resolve` falls back to geometry — caster radius + twice the ability object's half-extent + `REACH_SLACK`, floored at `MIN_REACH`, or `DEFAULT_TARGETED_REACH` (20 m) for an object spawned on the target. Half-extents come from the collider's shape via `AbilityPrefabColliderCache.ResolveShapeHalfExtents`, because a prefab **asset**'s `bounds` are empty. Issue #220.
- **Spacing is fitted to the kit the NPC really has.** `AICombatDecision.ResolveSpacing` caps `PreferredDistance` at `AIController.MaxOffensiveReach` and drops a `MinComfortDistance` at or beyond that reach: backing away to a range you cannot hit from is not kiting. The state asset's spacing is a hope, not a fact — the Caster archetype prefers 22 m and an orc mage using it may know one ability that reaches 1.25 m. An NPC that knows no abilities keeps its spacing as authored.
- **Kiting is budgeted.** `AIKiteBudget` drains while the NPC backs away; once spent, both the panic radius and the comfort band are suspended for `KiteRecoverySeconds` and the NPC stands and fights, then the budget refills (standing still refunds at `REFUND_RATE`, half rate). Backing away also runs at `KiteSpeedMultiplier` of run speed so a pursuing player gains ground. Unbounded and at player run speed, a kiting caster was a fight nobody could close.
- **A refused activation is not a pacing event.** `ActivateAbility` arms the `AttackCooldown` timer only when `IAbilityController.Activate` actually queued something, so a refusal is retried on the next brain tick rather than leaving the NPC standing next to its target waiting out a cooldown for an attack it never made.
- **Target identity is the character ID, not the Transform.** A pooled `NetworkObject` keeps its Transform and components across occupants, so when a target despawned and its object was re-issued to somebody else the cached Transform still compared equal and every null/active/alive check passed — the NPC silently continued its attack on a character that never engaged it. `AIController.Target` returns null once the ID recorded at target time stops matching.
- **The sweep sees whole bodies.** Overlap results resolve through `TargetOrdering.ResolveHitKey`, so a character whose hitbox hangs off a child transform is detectable at all (a bare `GetComponent` on the collider found no `ICharacter`, making such a character invisible to every NPC while remaining able to attack them) and a multi-collider character is counted once. Query buffers regrow through `TargetOrdering.TryGrowQueryBuffer` until a query stops coming back full — a fixed buffer let the physics broadphase pick an arbitrary, run-varying subset of a large fight. Corpses are filtered out by `AITargetSelection.IsValidTarget`; the healer's ally scan does all of the same. NPCs share the character layer with players, so most of what a camp's sweep returns is other NPCs and always the sweeper itself: its own collider is rejected by reference first, and an NPC the host drives is resolved from the host's collider-to-brain map (`AIBrainHost.TryGetBrain`) with one dictionary read; players and brainless NPCs go through `ResolveHitKey`.
- **The healer's ally scan runs on the state clock.** `HealerAttackingState` advances its scan timer by the time each state update really covers and rescans every `AllyScanInterval` (0.5 s). Between scans the cached candidate is re-validated, and a scan that found nobody injured is cached as well, so a group at full health is not rescanned on every update.
- **Death is a full stop.** `NPC.Despawn` disables the brain and calls `AIController.HaltMovement`, which clears the path, drops the target and look target, empties the threat table and releases the combat slot. Without it a corpse held its ring slot around its victim for the whole of its decay, and the pooled object came back still walking toward wherever the previous occupant's killer had stood.
- **Unreachable-target break-off.** A target standing somewhere the NPC cannot path to produces a partial path; the NPC gives up after `UnreachableTargetTimeout` and drops that target's threat so the next sweep does not immediately re-acquire it.
- **Abilities classify themselves.** An archetype never names the abilities it should use. `AIAbilityClassifier` reads the ECA actions attached to an ability and derives what it does — heal, damage, control, dispel, taunt — so a healer archetype works on any creature whose spellbook contains a heal, and picks up one added later without being edited.
- Personality styles: Balanced, Aggressive, Defensive, Cautious, Berserker, **Pathetic**, **Determined**, **Rampaging**. A Pathetic personality is guaranteed a retreat threshold even if the field is left at zero; fearless styles ignore one entirely.
- Targeting modes: Threat, Random, Weakest, Nearest. Rampaging forces Random and re-rolls onto a new victim mid-fight, so it cannot be held by threat or by a taunt.

### Movement

- **The agent simulates; the tick moves the body.** `updatePosition` and `updateRotation` are off. `StepAgent` advances the transform by `velocity × tickDelta` once per network tick, re-seats `Agent.nextPosition` on the result (which projects it back onto the mesh, so y follows the ground), and turns the heading toward the velocity at the agent's `angularSpeed` unless a `LookTarget` owns the facing. The displacement a `NetworkTransform` samples is therefore identical every tick for a given speed, whatever frame rate the server happened to run at. Off-mesh links are traversed by the agent itself and simply followed. A tick with no step, on an agent already sitting on its transform, writes neither (`NeedsAgentWrite`), so a standing NPC pays no NavMesh projection or collider move.
- **Separation replaces crowd avoidance.** `obstacleAvoidanceType` is `NoObstacleAvoidance` on every NPC agent, because Unity's crowd is one global simulation and a scene server stacks instances of the same world scene at the same coordinates — an NPC in one instance steered around NPCs in every other. `AISeparation.Resolve` takes its place, and it moves the body without turning it. Neighbours come from the host's `AIBodyGrid` for the NPC's own `PhysicsScene`: every NPC body in that scene, bucketed into 2 m cells, filled at most once per network tick on the first request and read by every separation in the scene, with the asker excluded by its identity key. The push is therefore scene-scoped by construction and costs no physics overlap. Two coincident bodies push apart along an axis drawn from both identity keys, in opposite directions (`AISeparation.CoincidentPushDirection`); a fixed axis slid a stacked pair together to the mesh edge. Attackers around a target are spaced by `AICombatSlots`; separation covers the wander, idle and approach cases the ring does not.
- **Stuck detection reads the displacement that was applied**, not `NavMeshAgent.velocity`. With avoidance off nothing ever blocks the simulated velocity, so it could not tell a walking NPC from one whose every step the NavMesh projection clamped back to the same point.
- **Off-mesh recovery.** A failed `WarpTo` (a spawn point with no mesh in reach) or the mesh going away underneath an agent (a stacked scene instance unloading its copy of the `NavMeshData`) leaves `isOnNavMesh` false for good, and every guard then reads `AgentIsUsable` as false — an NPC frozen mid-stride until the pool recycles it. `RecoverIfOffMesh` re-seats it where it stands, then at `Home`, once per `OFF_MESH_RESEAT_INTERVAL` (1 s), warning once per episode rather than once a second.
- Every warp mirrors the agent's position onto the transform (`SyncTransformToAgent`); with `updatePosition` off nothing else does, and the `NetworkTransform` would keep sending the old spot until the next tick's step.
- Destination requests report what actually happened: `Complete`, `Partial`, `Failed` or `Throttled`. Unity does not fail a path to an unreachable destination — it silently returns the closest reachable point — so callers need to tell those apart.
- `HasArrived` requires a complete path to exist. An agent whose destination never took reports zero remaining distance and no pending path, which the naive test reads as "arrived".
- NavMesh sampling widens on retry rather than silently doing nothing.
- **Stuck detection and escalating recovery**: re-sample and repath first, warp only after the NPC has visibly failed to walk out. Every combat manoeuvre is time-bounded so it can give up.
- `WarpTo` is used on spawn and pool reuse, because a recycled NPC's agent still believes it is where the previous occupant died.

### Pets

- A pet's `Home` **is its owner** — the property resolves to the owner's live position, so every leash check, wander radius and return-home destination tracks them automatically. A Stay order pins it to the held position instead.
- Pet-specific combat rules live in `BaseAttackingState`, not a subclass, so a pet healer, defender or rogue behaves like a pet without inheriting from the wrong place.
- Follow uses a hysteresis band, a distance leash, and a time-based stuck teleport — a pet jammed behind a crate five metres from its owner never trips a distance check.
- A pet does not turn on an attacker it could reach only by breaking its owner leash (`AIController.PetCanAnswerAttacker`: within `OwnerLeashRange` of the owner plus the pet's reach). With combat entry asked on every hit, a pet guarding an owner shot at from range would otherwise shuttle between the two.

### Threat

- `AggressionDispatcher` takes **one** global subscription for the process. Damage dispatches by dictionary lookup on the defender — O(1) regardless of NPC count. Previously every NPC subscribed individually, so one sword swing invoked one delegate per NPC alive.
- **Heal and kill go through a reverse index.** The dispatcher keeps, for each character ID, the NPC states whose threat tables track it, maintained by the tables' own `AggressionController.EntryAdded` / `EntryRemoved` hooks so it cannot drift. A heal asks only the NPCs tracking the healer or the healed, a kill only those tracking the victim, so both cost the NPCs involved rather than every registered NPC on the process (pooled and inactive ones included); heal-over-time ticks made the old walk the expensive one.
- **Combat entry is asked on every hit.** `AggressionState.OnHitRecorded` hands each recorded hit to `AIController.OnThreatReceived`, which enters the attacking state when the pure `CanEnterCombatFromThreat` allows: an attacking state exists, the brain is running, the NPC is not already in a combat state (`IsInCombatState` — orbit, flank and retreat included) and is not evading. A taunt's threat goes through the same rule. Entry used to be the table's empty-to-non-empty edge, and a fight that ended any way but a kill left the table full, so every later hit from beyond the sweep radius was recorded and ignored. The table is now emptied when a fight ends (`BaseAttackingState.OnCombatEnded`) and again on arriving home (`CompleteReturnHome`), as well as when a leash trips.
- **A pet and its owner share threat.** `Pet.PetOwner` declares the pair to the dispatcher (`LinkPet`/`UnlinkPet`), and a hit on either is delivered as threat to both — hit the owner and the pet engages, hit the pet and the owner engages (an NPC owner through its own aggression state). Keyed on characters, so an NPC given a pet gets the rule with no further wiring. The attacker is never a sharer, so an owner hitting its own pet generates nothing. Credit runs the other way as well: a pet's hit is recorded against its owner too, for the full amount, so an NPC struck by a summon hates the summoner and a player cannot shed threat by cycling pets.
- Threat decay and staleness share a single tick-advanced `Clock`, so expiry means "this many seconds of AI time without an event" rather than wall-clock time that disagreed with the decay whenever LOD throttled the NPC. Decay runs one pass per 0.5 s on an `AIStateClock`, and each pass is credited the time it really covers; credited a nominal 0.5 s, a Nearby NPC whose passes land every 0.8 s decayed at 62.5% speed.
- **Target choice is by threat *score*, not raw points.** `AggressionController.GetThreatScore` scales an entry's points by `VulnerabilityMultiplier` — `LowHealthThreatMultiplier` below `LowHealthThreshold`, `LowResourceThreatMultiplier` below `LowResourceThreshold`, compounding. Anything meaning to move a character up or down the order has to reason in that space.
- `ApplyTauntAction` and `ApplyThreatAction` are ECA actions that let abilities generate threat: a taunt guarantees top threat rather than adding a flat bonus a long fight has already outgrown. It gets there by clearing `highestRaw × AggressionController.MaximumVulnerabilityMultiplier` — the table stores raw points and holds no character references, so it cannot compute another entry's score, but that product bounds every actual score. Comparing raw points, as it used to, made the "guarantee" not one.
- `AggressionDispatcher.TryFindHighestThreatAgainst` finds the NPC that hates a given character most, reading only the tables the reverse index says track that character. It backs the pet attack command's highest-threat step.

### Leash and evade

- **Two leashes.** Past `MinLeashRange` the NPC walks home through its archetype's `ReturnHomeState`; past `MaxLeashRange` it is warped home. Both heal it (when the return state's `CompleteHealOnReturn` is set, for the walk), empty its threat table and, with `BossScript.ResetOnLeash`, rewind its boss phases and despawn its live adds (see *Boss adds*); the walk does the boss reset only when the leash ends a fight. A warp that ends a fight also leaves it through `CompleteReturnHome`, the same exit a completed walk takes; it used to leave the NPC in its attacking state with its target set, so it ran straight back to the player and was warped and healed again on the next check.
- **The heal belongs to the leash, not the state.** `ReturnHomeState` is also one of the calm movement states `TransitionToRandomMovementState` drifts between, so it cannot tell why it was entered. It used to heal on `Enter`, and a damaged NPC that merely strolled home between fights was topped up to full. `CheckLeash` now applies the heal after the return is in force (`AIController.LeashReturnHeals`); a calm stroll home never heals.
- **A leash that ends a fight is an evade.** `IsEvading` (`INPCBrain`) is true while such a return is the current state. The shared gate `CharacterEvade.RefusesHostileEffects` refuses, in one place for every route: damage (`CharacterDamageController.Damage`), every debuff another character tries to apply (`ApplyBuffAction` via `CharacterEvade.RefusesBuff` — stun, root, mesmerize, slow and the rest, since a slow is only a negative attribute modifier), and knockback (`KnockbackHitAction`). The brain itself refuses threat, area threat and taunts. `Immortal` is untouched by any of this.
- **A refused hit says so.** The attacker's client predicted the hit (it has no brain to ask), so the server reports the refusal to the attacker's connection alone as `CombatEventKind.Evade` — or `Immune` for an `Immortal` target — and the caster's predicted number turns into the word instead of greying out a second later. See the CharacterAttribute README, *Combat reporting*.
- **The sweep stays out of fights.** The out-of-combat enemy sweep runs only for a calm NPC (`AIController.SweepMayRun`): never going home, and never in any state that keeps the combat target (`IsInCombatState` — the attacking state and every orbit, flank or retreat sub-state). It used to test the attacking state alone, and a sweep mid-manoeuvre re-entered the attacking state with a fresh pick, cutting orbits short and ending flees the moment the pursuer came into view.

### Packs

An `NPCGroup` is a pack: NPCs that share a focus, answer each other's fights and arrange themselves by a tactic. It is a plain runtime object, never a scene component.

- **Spawners own them.** A spawner whose `Pack` is enabled founds a pack when its first NPC spawns and adds every NPC it spawns after that, with the role its entry names (`NPCSpawnableSettings.PackRole`). Authoring is in the [Spawner README](../Spawner/README.md#npc-packs). A boss founds one for its adds (below).
- **Membership is the living members.** A brain leaves its pack on death (`SuspendForCorpse`), on despawn and pool reset (`ResetForPool`), and when it is destroyed with its scene. A pack whose last member leaves is *released*: the host stops ticking it and it refuses members. A respawn therefore rejoins its spawner's pack while any member of it stands, and founds the next one after a wipe. A brain is in one pack at a time.
- **Ticked by the host, inside the AI contract.** `AIBrainHost` ticks packs after the brains on every network tick. A pack thinks only on its own AI tick — the brains' divisor of the network tick (every 4th at 30 Hz), at a phase drawn from its identity — and only while one of its running members is in a tier that fights (Active or Nearby). It evaluates every `NPCGroup.EVALUATE_INTERVAL` (0.5 s) times that member's LOD interval, so a Nearby pack evaluates a third as often. At Far or Dormant it stands down: no focus, no combat flag, no tactic slots. A pack has no `Update`.
- **Alerts.** A member entering an attacking state with a target calls `NPCGroup.AlertGroup`: the pack focuses that enemy and every member free to fight joins in. "Free" is the combat-entry rule a hit is asked, pinned as `AIController.MayAnswerPackAlert`: not already in a combat state (orbiting and fleeing included), not evading on a leash, not immortal, alive, brain running. A member strolling home as a calm movement does answer.
- **Roles.** `PickTarget` gives a Tank the threat pick and DPS/Support the pack's focus. With `FocusTargeting`, the focus follows the target of a living tank that is fighting. A healer heals through its archetype, whatever its role. The focus is held with the character's ID, so a despawned target's pooled object re-issued to someone else is never the pack's focus.
- **Most wounded member.** `LowestHealthMember` is the most wounded *living* member, and null while the whole pack is at full health, so a `DefenderAttackingState` body-blocks only for someone who is hurt.
- **Tactics shape the orbit.** Each evaluation gives every member fighting the focus a bearing around it (`AIController.PackSlotAngle`), which `OrbitState` steers to on the pack's `TacticOrbitRadius`. The bearings are fitted to where the members stand, not to the world axes: Surround is an even ring rotated to move them least; Kite is that ring turning; FocusFire is a tight arc on the side they are already on; Flank puts tanks at the front (the tank's side, or the way the enemy faces with no tank) and everyone else across the rear, a lone flanker directly behind. A member with a slot holds it rather than advancing its own orbit angle. A tactic only matters to archetypes whose attacking state has an Orbit variety state; everyone's approach is spaced by the combat-slot ring as before.
- **Behaviour-tree nodes.** `AIGroupInCombatNode` reads the pack's combat flag; `AIAdoptGroupTargetNode` adopts its focus, only when it is a living target and never for an evading NPC.

### Boss adds

`BossScriptState` calls in a phase's or a timed mechanic's adds from the pool (`GetPooledInstantiated`), moves them into the boss's own scene instance, prepares their brains there and spawns them into that scene. (They used to be `Object.Instantiate`d and spawned into whichever scene was active.) Adds join the boss's pack as DPS — the boss's spawner pack if it has one, or a new pack the boss founds with itself as a member of no role — and, arriving mid-fight, are alerted onto the boss's target at once. An add has no spawner: its corpse returns to the pool through `PersistentPool`. Adds are not pre-warmed.

- **The boss's adds are its own list, not its pack.** `BossScriptState` records every add it spawns (`AdoptAdd`), and each add holds its boss (`AIController.Summoner`). The pack is often the boss's spawner's, and its other members are not the boss's to send away. Membership is the living adds, kept from both ends as a pack's is: an add leaves on death, on despawn and pool reset, and on destruction (`LeaveSummoner`), so a pooled brain reissued as another NPC is never dismissed by a boss it never met.
- **A leash reset dismisses them.** Every leash that honours `BossScript.ResetOnLeash` (the warp, the walk home that ends a fight, and the LOD soft leash) goes through `AIController.ResetBossScriptForLeash`, which despawns the live adds through `PersistentPool.Despawn` before rewinding the phases. A dead add's corpse is left to decay with its loot. A boss that dies or despawns only lets go of its adds (`ReleaseAdds`); they fight on, as they always did.
- **A cap on live adds.** `BossScript.MaxLiveAdds` (default 12, one full `AICombatSlots` ring) holds back a timed mechanic, which fires for as long as the fight lasts, while that many of the boss's adds are alive; a mechanic naming several tops the boss up to the cap. A phase's adds always arrive, since a phase is entered at most once per pull, and count towards the cap. The rule is the pure `BossScriptState.MayCallInAdd`.

### Behaviour trees

- Optional layer above the state machine, edited visually in the Behavior Tree Editor, opened from the **Open Behavior Tree Editor** button on a tree in the FishMMO Dashboard (NPCs → Behavior Trees).
- The editor refuses connections that would make a tree cyclic, and the runtime carries a depth guard so a hand-edited or badly-merged asset degrades to a failed evaluation instead of a stack overflow that terminates the server process.

## Prerequisites

- **Unity 6.3 LTS**
- **Unity AI Navigation** — `NavMeshAgent`, `NavMesh`
- **FishNetworking** — `NetworkObject`, `TimeManager`, object pooling
- **FishMMO Shared Core** — `ICharacter`, `INPCBrain`, `IAbilityController`, `ICooldownController`, `ICharacterDamageController`, `IFactionController`
- **FishMMO Server Core** — `IAIController` and its navigation / state-machine / waypoint parts

## Installation / Build

Integrated module within the FishMMO Server assembly. No separate installation. The scene server's system list (`SceneServer.unity`) runs `AISystem`, and `AISystem.asset` names `AIBrainCatalogue.asset`; both are in the `Server_Static_Permanent` addressable group.

An NPC prefab requires `CharacterPredictionController`, `AbilityController`, `CooldownController`, `TargetController` and `EnablePrediction` on its `NetworkObject` — and must **not** carry an `AIController` or `NavMeshAgent`, which the server adds. `NPC`'s `RequireComponent` attributes add the shared components automatically; `FishMMO Dashboard → NPCs → AI Tools → Migrate NPC Brains To Server Catalogue` moves an old prefab's brain into the catalogue and strips its AI components; `FishMMO Dashboard → NPCs → AI Tools → Repair NPC Prefabs For Combat` migrates existing prefabs and enables prediction. The `TargetController` is not cosmetic: `AbilityController` resolves every cast's target through it, and a caster without one completes the cast, starts the cooldown and spawns nothing (issue #232).

## Quick Start Guide

The fastest route is the dashboard: `FishMMO > FishMMO Dashboard > NPCs > +` opens a form pre-filled from the selected NPC — name, folder, race, archetype, attribute databases, loot, abilities and interactable role — and `Create NPC` clones a working prefab, writes those fields onto it, registers it with Addressables and selects it. The steps below are what that form does, for when a piece has to be authored first.

1. Create an archetype: `FishMMO > Character > NPC > AI > Archetype`, or start from one of the 17 shipped assets under `Assets/Templates/Entity/NPCs/AI/Archetypes/` (10 enemy, 6 pet, 1 civilian).
2. Give the NPC prefab that archetype in the server's brain catalogue — the dashboard's NPC inspector (`AI Brain`) writes it, or edit `Assets/Prefabs/Server/SceneServer/AIBrainCatalogue.asset` directly. That is the whole AI setup: the brain reads every state, the personality, the rotation, the LOD profile and the threat tuning from the archetype. There is no per-prefab slot to fill or override — a creature that needs one thing different gets its own archetype, so two NPCs naming the same archetype always behave the same.
3. Populate `NPC.Abilities` with `AbilityTemplate`s — **an NPC with no abilities will chase its target and never strike**.
4. Run `FishMMO Dashboard → Core → Validate → Audit NPC Prefabs` to confirm the prefab is wired for combat.
5. Run `FishMMO Dashboard → Core → Validate → Validate Archetypes` to confirm the archetype is internally consistent.

## Configuration

### AIBrainCatalogue

| Field | Purpose |
|---|---|
| `Entries[].Prefab` | The NPC prefab |
| `Entries[].Archetype` | The brain it spawns with unless its spawner overrides it. Required |
| `Entries[].BossScript` | Optional phased encounter script. Per prefab, not per archetype, because it describes one encounter |

### AIController

A runtime component the host adds; its field defaults are the tuning every NPC runs with.

| Field | Default | Purpose |
|---|---|---|
| `Archetype` | from the catalogue | The whole brain; every state and tuning value is read from it. Assigned by the host on every spawn |
| `BossScript` | from the catalogue | Assigning one starts it from its first phase |
| `AiTickRate` | `8` | Brain updates per second; 5–10 is the useful band |
| `TurnRate` | `8` | Facing smoothing rate; higher is snappier |
| `RepathInterval` | `0.5` | Minimum seconds between throttled `SetDestination` calls |
| `StuckTimeout` | `2.5` | Seconds of no progress before the NPC counts as stuck |
| `StuckWarpTimeout` | `8.0` | Seconds stuck before it is warped free; 0 disables |
| `SeparationRadius` | `0` | Distance at which another NPC body starts pushing this one away; 0 = twice the agent radius |
| `SeparationSpeed` | `1.0` | Push speed when fully overlapped, m/s; 0 disables separation |

`Archetype` is the only serialized state slot. `InitialState`, `IdleState`, `AttackingState`, `Personality`, `AbilityRotation`, `BehaviorTree`, `LodSettings`, `EnemySweepRate` and `AvoidancePriority` are read-only properties that read straight through to it (or to a boss phase's override, where one is in force).

### AIArchetypeTemplate

| Field | Default | Purpose |
|---|---|---|
| `InitialState` / `IdleState` / `AttackingState` | `null` | Core states. Initial and idle are required; a civilian archetype leaves attacking empty |
| `WanderState` / `PatrolState` / `ReturnHomeState` / `RetreatState` / `DeadState` | `null` | Optional movement, leash, flee and death states |
| `Personality` | `null` | `AICombatPersonality`: ability weights, flee threshold, targeting mode |
| `AbilityRotation` / `BehaviorTree` | `null` | Optional decision layers above the scorer and the state machine |
| `LodSettings` | `null` | Distance throttling profile; null means always Active |
| `EnemySweepRate` | `1.5` | Seconds between out-of-combat hostile sweeps |
| `AvoidancePriority` | `Medium` | NavMeshAgent avoidance priority |
| `AggressionDamageWeight` | `1.0` | Threat per point of damage taken |
| `AggressionHealingWeight` | `0.6` | Threat per point of healing witnessed |
| `AggressionHitBonus` | `5.0` | Flat threat per hit |
| `AggressionDecayRate` | `3.0` | Threat lost per second |
| `AggressionStaleTimeout` | `30.0` | Seconds before a drained entry is forgotten |
| `AggressionVarietyChance` | `0.15` | Chance of picking the second-highest threat |

Assigning a different archetype to an initialised controller — a spawner's `ArchetypeOverride`, a harness clone — takes effect immediately: `ApplyArchetypeTuning` retunes the threat table and re-applies the avoidance priority, and every state is read live. The archetype the prefab was authored with is captured on first initialisation and restored by `ResetState`, so a spawner override does not ride a recycled instance into the next spawner.

Boss phases call `AIController.SetPhaseOverrides`, which puts the phase's attacking state, behaviour tree and ability rotation *in front of* the archetype's slots rather than writing into the shared asset; a slot the phase leaves null keeps whatever an earlier phase installed, and `ClearPhaseOverrides` drops them when the script resets or the instance is pooled. A phase attacking state takes over the fight **when the phase starts**: if the boss is in an attacking state that is no longer the one `AttackingState` resolves to (`AIController.FightNeedsHandOver`), `SetPhaseOverrides` hands the fight to the new state through `HandOverFight`, which `BaseAttackingState.Exit` recognises (`IsHandingOverFight`) and so keeps the target, the ring slot and the cast in progress. A boss in a combat sub-state picks the override up when the sub-state returns to `AttackingState`; one out of combat enters it on its next engagement. (Before, the override waited for something to re-enter an attacking state, and in practice that was the sweep misfiring mid-fight.) There is deliberately no other override layer: `AIArchetypeTemplate.ApplyTo` and its `OverrideThreatTuning` opt-in are gone, and the archetype's threat tuning always applies.

### BaseAttackingState

| Field | Default | Purpose |
|---|---|---|
| `PreferredDistance` | `0` | Working distance; 0 = close to melee reach |
| `MinComfortDistance` | `0` | Distance below which the NPC backs away; 0 = never |
| `EmergencyRetreatThreshold` | `0.5` | Fraction of comfort distance that triggers an interrupt-and-run |
| `AttackCooldown` / `AttackCooldownJitter` | `1.5` / `0.5` | Pacing between activations |
| `TargetReevaluationRate` | `3.0` | Seconds between mid-combat re-targeting |
| `AggressionSwitchThreshold` | `50` | Threat lead required to switch targets |
| `VarietyStates` / `MovementVarietyChance` | `[]` / `0` | Optional positioning manoeuvres |
| `UseCombatSlots` | `true` | Spread multiple attackers into a ring |
| `UnreachableTargetTimeout` | `6.0` | Seconds before breaking off an unreachable target |
| `OwnerLeashRange` | `30` | Pets only: distance from owner before breaking off |
| `KiteBudgetSeconds` | `2.5` | Seconds of backing away allowed per window; 0 = unlimited |
| `KiteRecoverySeconds` | `5.0` | Seconds the NPC holds its ground once the budget is spent |
| `KiteSpeedMultiplier` | `0.8` | Run speed multiplier while backing away |

### AILodSettings

Intervals are counted in **AI ticks**, not frames. At the default 8 Hz brain, the `Standard AI LOD` intervals of 1 / 3 / 10 / 40 give roughly 8 Hz, 2.7 Hz, 0.8 Hz and 0.2 Hz.

## Usage Examples

### Editor tooling

| Menu | Purpose |
|---|---|
| `FishMMO Dashboard → NPCs → AI Tools → Migrate NPC Brains To Server Catalogue` | Moves a prefab's archetype and boss script into the catalogue and removes its `AIController` and `NavMeshAgent` |
| `FishMMO Dashboard → NPCs → AI Tools → Repair NPC Prefabs For Combat` | Adds missing ability-pipeline components and enables prediction |
| `FishMMO Dashboard → Core → Validate → Audit NPC Prefabs` | Reports prefabs that cannot fight, and why |
| `FishMMO Dashboard → Core → Validate → Validate Archetypes` | Reports archetypes whose configuration cannot behave as described |
| `FishMMO Dashboard → Core → Validate → Audit Ability Intents` | Reports what the AI derives each ability template to do |
| `FishMMO Dashboard → NPCs → AI Tools → Organize AI Assets` | Files every AI asset into the canonical folder layout |
| `FishMMO Dashboard → NPCs → AI Tools → Re-serialize AI Assets` | Writes newly added serialized fields into the asset YAML |
| `FishMMO Dashboard → NPCs → Behavior Trees` → **Open Behavior Tree Editor** | Visual behaviour tree graph editor |
| `FishMMO Dashboard → Core → Validate → Validate Network Timing` | Confirms every scene agrees on tick rate |

### How an NPC chooses an ability

Ability selection has two questions, asked in order.

**What can this ability do?** `AIAbilityClassifier` walks the ability template's five ECA event
lists, follows each event's conditions-met and conditions-not-met action lists, and turns the action
types it finds into `AIAbilityIntent` flags:

| ECA action | Intent |
|---|---|
| `ApplyDamageAction` | `Damage` |
| `ApplyHealAction` | `Heal` |
| `ApplyReviveAction` | `Revive` |
| `ApplyTauntAction`, `ApplyThreatAction` | `Threat` |
| `InterruptAction`, `KnockbackHitAction` | `Control` |
| `ApplyDispelAction` | `Dispel`, plus `Buff` or `Debuff` by direction |
| `ApplyBuffAction` | depends on the buff template (below) |
| `PetAbilityTemplate` | `Summon` |

Buffs carry no "harmful" flag, so direction is inferred: a state flag `CharacterIncapacitation`
recognises is `Control`; the **sum** of the attribute modifiers gives `Buff` or `Debuff`; the sum of
the resource ticks gives `Heal` or `Damage`. The sum rather than the count, so a plate-armour buff
with a small speed penalty is still a buff. This inference is the one place classification can be
wrong, and `AbilityTemplate.IntentOverride` is the fix when it is — it replaces the derived value
outright. Run `FishMMO Dashboard → Core → Validate → Audit Ability Intents` to see what the AI makes of every ability in
the project.

An ability with no recognisable actions classifies as `None` and stays usable, so content that
predates classification keeps working rather than silently disarming the NPC that knows it.

**How much does this archetype want it?** `AICombatPersonality` carries a weight per intent —
`DamageWeight`, `HealWeight`, `ControlWeight`, `DebuffWeight`, `BuffWeight`, `ThreatWeight` — which
multiplies into the ability's score alongside the existing delivery weights (melee / ranged / AOE /
support). Delivery and purpose are orthogonal and both apply: a crowd controller wants ranged
delivery *and* controlling purpose. An ability carrying several intents takes the strongest matching
weight, not the product, so a compound ability cannot out-score a specialised one on flag count.

The two specialised archetypes use the same classification rather than a list:

- **Healer** — heals are abilities classified `Heal`. Everything else falls through to the damage
  rotation, and the damage rotation excludes anything purely supportive.
- **Defender** — taunts are abilities classified `Threat`, used ahead of anything the scoring picker
  would otherwise choose.

Both still expose their old template-ID list (`HealAbilityTemplateIDs`, `TauntAbilityTemplateIDs`)
as an **override**, for an ability that acts through some route the classifier cannot see. Leave
them empty in the normal case.

Finally, `BaseAttackingState.IsEnemyAbility` keeps the attack rotation honest: an ability that is
purely supportive and aimed at another character is excluded, so an NPC no longer heals the player
it is fighting. A self-cast shield stays in — the NPC aims it at itself — and so does a drain that
damages and heals, because the damage is the point.

### Asset layout

```
Assets/Templates/Entity/NPCs/AI/
├── Archetypes/        # AIArchetypeTemplate — the asset to assign to a prefab
├── Personalities/     # AICombatPersonality
├── States/
│   ├── Attack/        # BaseAttackingState and subclasses
│   ├── Combat/        # Orbit, flank, flee — combat positioning sub-states
│   ├── Movement/      # Idle, wander, patrol, return home
│   └── Pet/           # Pet follow states
├── Rotations/         # AIAbilityRotation
├── Conditions/        # AIAbilityCondition
├── BehaviorTrees/     # AIBehaviorTree
├── BehaviorNodes/     # AIBehaviorNode
├── Boss/              # BossScript
├── Aim/               # AIAimProfile
└── LOD/               # AILodSettings
```

### Shipped archetypes

**Enemy** — Melee, Brute, Pathetic Critter, Raging Beast, Archer, Caster, Crowd Controller, Healer, Defender, Rogue.
**Pet** — Melee, Archer, Caster, Healer, Defender, Rogue.

### Combat sub-states and `KeepsCombatTarget`

A state entered mid-fight for positioning (orbit, flank, flee) must have `KeepsCombatTarget` enabled. `BaseAttackingState.Exit` clears the combat target and interrupts the cast, which is correct on a disengage and catastrophic on a manoeuvre — the sub-state is handed a null target and bails straight to idle, silently ending the fight. `Validate` reports any variety or retreat state missing the flag.

## Operational Checks

| Check | How to Verify |
|---|---|
| Brain is ticking | `AIController.EffectiveAiTickRate` reports the resolved rate; at 30 Hz network tick and 8 Hz requested it is 7.5 |
| Archetype applied | `AIController.InitialState` and the other state properties resolve to the archetype's assets; a prefab with no archetype is reported by `Audit NPC Prefabs` and by the `EveryNPCPrefab_HasAnArchetypeInTheBrainCatalogue` EditMode test |
| NPC can fight | `FishMMO Dashboard → Core → Validate → Audit NPC Prefabs` reports no problems |
| Archetypes valid | `FishMMO Dashboard → Core → Validate → Validate Archetypes` reports all valid |
| LOD engaged | Move a player away from an NPC; its update rate should drop through Nearby, Far and Dormant |
| Threat dispatch | Damage an NPC and confirm only that NPC's threat table changes |
| Ability intents | `FishMMO Dashboard → Core → Validate → Audit Ability Intents` reads each ability the way it was authored |
| Taunt | Attach `ApplyTauntAction` to an ability's on-hit event; confirm the target switches to the taunter and stays |
| Leash evade | Drag an NPC past its leash and hit, debuff and knock it back on the way home: "Evade" over it on the attacker's screen; no health change, no debuff lands, no displacement; it arrives home healed and attackable |
| Calm stroll home | Damage an NPC, let the fight end without a leash, and watch it pick the return state as a calm movement: it walks home without healing |
| Boss phase hand-over | Push a boss through a phase that overrides its attacking state: it fights on with the phase's state at once, against the same target |
| Multi-attacker spacing | Pull three or more melee NPCs onto one target; they should form a ring, not a scrum |
| Pet follow | Run a player through doorways and around props; the pet should keep up without wedging |
| Pet stance | Passive never engages, Defensive answers an attack on the owner, Aggressive hunts |
| Stuck recovery | Wedge an NPC against geometry; it should repath, then warp free after `StuckWarpTimeout` |
| Pack alert | Pull one member of a pack spawner's pack: the rest engage the same player; a member walking home from a leash does not |
| Pack lifecycle | `NPCPackTests`: members leave on death, despawn and destroy; the last one out releases the pack; a respawn rejoins, a wipe founds a new pack; packs tick only on AI ticks and only at Active/Nearby |
| Boss adds | Push a boss through a phase that spawns adds: they appear in the boss's instance, fight its target, and their corpses pool |
| Boss adds on a leash | Pull a boss into a phase with adds, kill one, then run past its `MaxLeashRange`: the live adds despawn as it resets; the dead add's corpse and any spawner packmate stay |
| Boss add cap | Leave a boss's timed summon running with its adds alive: no more than `MaxLiveAdds` of its adds stand at once; killing one lets the next interval replace it |
| Off-mesh recovery | Spawn an NPC away from any NavMesh; it re-seats within a second, or warns once naming the position |
| Attack pacing | `AttackCooldown` should elapse in wall-clock seconds, not brain ticks — `StateDeltaTime`, not `LastAiDeltaTime` |
| Kiting terminates | Chase a caster on foot; it backs away for `KiteBudgetSeconds`, then stands and fights for `KiteRecoverySeconds` |
| Reach on static abilities | An NPC whose only ability does not travel should close and strike, not stand at its archetype's preferred distance |
| Behaviour tree cycle safety | Connect a node to its own ancestor in the editor; the connection is refused |

## Flow Diagram

### Tick pipeline

```mermaid
flowchart TD
    Tick[TimeManager.OnTick 30 Hz] --> Body{ShouldStepBody<br/>Dormant with no path?}
    Body -->|no| Off[RecoverIfOffMesh]
    Body -->|no| Step[StepAgent: velocity x tickDelta<br/>+ separation, re-seat, heading]
    Body -->|no| Face[FaceLookTarget + aim]
    Tick --> Gate{AI tick gate<br/>every Nth network tick}
    Gate -->|no| Done[return]
    Gate -->|yes| Lod{LOD tier}
    Lod -->|Dormant| Done
    Lod -->|Far| Far[Leash + movement state]
    Lod -->|Nearby| Near[Leash + state + camera + threat]
    Lod -->|Active| Act[Sweep + leash + BT + boss + state + camera + threat]
    Tick --> Packs[After the brains: each pack on its AI tick<br/>evaluates if a member is Active or Nearby]
```

### Combat decision

```
BaseAttackingState.UpdateState
│
├─ 1. Tick attack pacing timer
├─ 2. Pet leash check (Home tracks the owner)
├─ 3. Target lost or dead?  → sweep for a new one, else OnCombatEnded
├─ 4. TryAttack
│      ├─ Activation in progress? → hold and auto-release charged abilities
│      ├─ Roll for a movement-variety manoeuvre
│      ├─ PickAbility (rotation → personality-weighted scorer)
│      ├─ BuildContext (distance, reach, fitted spacing, health,
│      │                personality, was-attacking, kite exhausted)
│      ├─ AICombatDecision.Plan → intent
│      ├─ Charge the kite budget, set agent speed
│      └─ ExecutePlan
│           ├─ Flee              → RetreatState
│           ├─ EmergencyRetreat  → interrupt, break away
│           ├─ BackAway          → retreat, optionally firing on the way out
│           ├─ Attack            → stop and activate
│           ├─ CloseDistance     → move to a claimed combat slot
│           └─ HoldPosition      → stand and wait
└─ 5. ReevaluateTarget (threat lead, or rampage re-roll)
```

### Movement outcome

```
TryMoveTo(destination)
│
├─ Agent unusable      → Failed
├─ Repath throttled    → Throttled
├─ NavMesh sample fails (widening retries) → Failed
├─ Path is partial     → Partial   (destination unreachable; do NOT treat stopping as arriving)
└─ Path is complete    → Complete

GetMovementProgress(dt)
│
├─ Stopped / no path            → Idle
├─ Path pending                 → Computing
├─ Complete path within tolerance → Arrived
├─ Wants to move but is not,
│  or stranded on a partial path → Stuck (after StuckTimeout)
└─ otherwise                    → Moving
```

## Project Structure

```
AI/                                # Server/Implementation/World/SceneServer/AI
├── AISystem.cs                    # Scene server behaviour: runs the host
├── AIBrainHost.cs                 # Attaches, ticks and resets brains per NetworkManager
├── AIBrainCatalogue.cs            # NPC prefab -> archetype / boss script
├── AIController.cs                # Brain: tick pipeline, state machine, LOD, threat wiring, INPCBrain
├── AIController.Movement.cs       # Destination requests, arrival, stuck detection and recovery
├── AIArchetypeTemplate.cs         # One asset = one complete brain, plus Validate()
├── AICombatPersonality.cs         # Styles, ability weights, flee threshold, targeting mode
├── AITargetingMode.cs
├── AIAbilityRotation.cs           # Condition-driven ability selection
├── AILodSettings.cs               # Distance tiers and per-tier tick intervals
├── AIShelterSettings.cs           # Whether, and in what weather, an archetype's NPCs seek cover
├── AggressionController.cs        # Threat table, tick-advanced clock, target scoring
├── AggressionState.cs             # Per-NPC threat state; reports every hit for combat entry
├── AggressionDispatcher.cs        # One global subscription; O(1) damage routing, reverse index for heal/kill
├── AggressionEntry.cs
├── AgentAvoidancePriority.cs
├── PackTactic.cs
├── AIUtility.cs
├── Aim/                           # AIAimProfile / AIAimSolver: where on a target an NPC aims, and how well
├── Combat/
│   ├── AIAbilityClassifier.cs     # Derives what an ability does from its ECA actions
│   ├── AIAbilityReach.cs          # How far an ability really hits from; Range is 0 for anything static
│   ├── AIBodyGrid.cs              # Per-scene grid of NPC bodies that separation reads instead of an overlap
│   ├── AICombatDecision.cs        # The shared, Unity-free combat decision, plus ResolveSpacing
│   ├── AICombatIntent.cs
│   ├── AICombatSlots.cs           # Ring slotting so attackers do not converge on one point
│   ├── AIKiteBudget.cs            # Per-NPC kiting allowance and recovery hold
│   ├── AIMovementResult.cs
│   ├── AIRetreatBudget.cs / AIRetreatDecision.cs  # When a retreating NPC keeps running, turns to fight or disengages
│   ├── AISeparation.cs            # Scene-scoped body separation, in place of crowd avoidance
│   ├── AIStateClock.cs            # Per-state update scheduling and the interval each update covers
│   └── AITargetSelection.cs       # Random / weakest / nearest picking, and target validity
├── States/
│   ├── BaseAttackingState.cs      # The one attacking state; archetypes are its tuning
│   ├── MeleeAttackingState.cs     # Preset
│   ├── RangedAttackingState.cs    # Preset
│   ├── CasterAttackingState.cs    # Preset
│   ├── PetAttackingState.cs       # Preset
│   ├── HealerAttackingState.cs    # Scans for injured allies
│   ├── DefenderAttackingState.cs  # Taunts and body-blocks
│   ├── RogueAttackingState.cs     # Opens from the target's rear arc
│   ├── PetIdleState.cs            # Pet follow, stance engagement, stuck escape
│   ├── IdleState.cs / WanderState.cs / PatrolState.cs / ReturnHomeState.cs
│   ├── OrbitState.cs / GetBehindState.cs / RetreatState.cs
│   └── SeekShelterState.cs        # Walks to cover and waits out the weather
├── Conditions/                    # AIAbilityCondition subclasses
├── BehaviorTree/                  # Tree, nodes, composites, decorators
├── Boss/                          # Phases and timed mechanics
└── Group/                         # NPCGroup (runtime packs), NPCPackSettings (authored), roles, members
```

### Related

- Shared seam: `Shared/Core/Entity/NPC/INPCBrain.cs`; `NPC.OnServerSpawned` / `OnServerDespawned`; `Pet.OnPetOwnerChanged`
- Evade gate (shared, asked by damage, debuff and knockback): `Shared/Implementation/Entity/CharacterEvade.cs`
- Server interfaces: `Server/Core/World/SceneServer/AI/` (`IAIController` and parts)
- ECA actions (shared shells over `INPCBrain`): `Shared/.../ECA/Actions/Character/ApplyTauntAction.cs`, `ApplyThreatAction.cs`
- Ability intent flags (shared, ability data): `Shared/.../Prediction/Ability/Template/AIAbilityIntent.cs`
- Spawners (prepare brains before spawning): `Server/Implementation/World/SceneServer/Spawner/`
- Editor tooling: `Shared/Implementation/Tools/Extensions/Unity/Editor/AI/`
- Tests: `Assets/UnitTests/AI/`

## License

This project is subject to the FishMMO project license.
