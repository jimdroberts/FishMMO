# ECA Target Selector System

The Target Selector system is the **targeting layer** of FishMMO's ECA (Event-Condition-Action) framework. Selectors decide *which* `GameObject`s a `Trigger`, `BaseCondition`, or `BaseAction` operates on. They are serialized inline on assets via `[SerializeReference] [SubclassSelector]`, so designers pick a concrete type from a dropdown in the Inspector.

## Table of Contents

- [Description](#eca-target-selector-system)
- [Supported Platforms](#supported-platforms)
- [Architecture](#architecture)
- [Key Components](#key-components)
- [Configuration](#configuration)
- [Flow Diagram](#flow-diagram)
- [1. Where selectors live](#1-where-selectors-live)
- [2. The execution model](#2-the-execution-model)
- [3. Anatomy of `TargetSelector`](#3-anatomy-of-targetselector)
- [4. Selector catalogue](#4-selector-catalogue)
- [5. Designer cookbook](#5-designer-cookbook)
- [6. Resolution in actions and conditions](#6-resolution-in-actions-and-conditions)
- [7. Determinism notes](#7-determinism-notes)
- [8. Writing a new selector](#8-writing-a-new-selector)
- [9. Quick reference: lifecycle invariants](#9-quick-reference-lifecycle-invariants)
- [10. Condition & action framework hooks](#10-condition--action-framework-hooks)

## Supported Platforms

| Platform | Status | Notes |
| --- | --- | --- |
| Windows / Linux / macOS (Editor) | Supported | Primary authoring environment. |
| Standalone Players (Win / Linux / macOS) | Supported | Builds compile everywhere, but see the authority note below — most selectors yield nothing on a client. |
| Headless Linux Server | Supported | Selectors run on the SceneServer for authoritative target resolution. |
| Android / iOS / WebGL | Supported | As above: only the five peer-agnostic selectors evaluate on a client. |

**Authority.** Nine of the fourteen selectors — every one that runs a physics query, plus
`AllCharactersTargetSelector` and `TargetedEntitySelector` — open with `IsAuthoritativePeer` (`EcaAuthority.IsServer`) and `yield break` on a
client. They exist to resolve hits authoritatively, and a client asking the same question against
interpolated peer positions would answer differently. The five that DO run on both peers are
`Initiator`, `Event`, `Children`, `NamedSceneObject` and `TaggedSceneObject`; only those need to agree
across peers, and `TargetOrdering` is what makes them agree.

Requirements: Unity 6.3 LTS with FishNet. Selectors depend only on the shared FishMMO entity layer; no platform-specific APIs.

## Architecture

Selectors are pure data: a `TargetSelector` subclass holds parameters in serialized fields and implements `IEnumerable<GameObject> SelectTargets(EventData eventData)`. Triggers, conditions, and actions call `SelectTargets` to obtain candidates and then operate on them. There is no runtime registration step — types are discovered by Unity's `SerializeReference` system and picked in the Inspector through `[SubclassSelector]`.

```
Entity/ECA/Target/
├── TargetSelector.cs                     # Abstract base (SelectTargets + Conditions + helpers)
├── InitiatorTargetSelector.cs            # The entity that fired the trigger
├── EventTargetSelector.cs                # Whatever the event already carries as Target
├── NearestTargetSelector.cs              # Closest candidate within a radius
├── FurthestTargetSelector.cs             # Furthest candidate within a radius
├── RandomTargetSelector.cs               # Random pick within a radius (uses EventData.RNG)
├── AreaTargetSelector.cs                 # Sphere overlap around the context point
├── ConeTargetSelector.cs                 # Cone-shaped AoE
├── LineTargetSelector.cs                 # Ray / line cast from the context point
├── ChainTargetSelector.cs                # Bounces target-to-target up to a hop limit
├── ChildrenTargetSelector.cs             # Direct children of the context object
├── AllCharactersTargetSelector.cs        # Every ICharacter in the active scene
├── NamedSceneObjectTargetSelector.cs     # Scene object resolved by name at fire time
└── TaggedSceneObjectTargetSelector.cs    # Scene objects resolved by Unity tag at fire time
```

## Key Components

| Component | Purpose |
| --- | --- |
| `TargetSelector` | Abstract base; declares `SelectTargets(eventData)`, carries the per-candidate `Conditions` list, and provides the `GetContext` / `AreConditionsMet` helpers. |
| `InitiatorTargetSelector` | Returns the initiator's `GameObject`. |
| `EventTargetSelector` | Returns the target already present on the `EventData`. |
| `NearestTargetSelector` / `FurthestTargetSelector` | Single closest / furthest valid candidate within a radius, excluding the context object. |
| `RandomTargetSelector` | One random valid candidate within a radius, drawn from `EventData.RNG`. Server-only (see the authority note), so the RNG buys run-to-run reproducibility, not client/server agreement. |
| `AreaTargetSelector` / `ConeTargetSelector` / `LineTargetSelector` | Geometric queries around the context point. |
| `ChainTargetSelector` | Walks target to target, accumulating a chain up to a configured hop count. |
| `NamedSceneObjectTargetSelector` / `TaggedSceneObjectTargetSelector` | Asset-safe scene lookup by name or tag, resolved at fire time. |

Filtering is not a selector. Rather than layering "tag" or "faction" selectors over source selectors, every selector carries its own `Conditions` list and yields only candidates that satisfy it — see [Anatomy of `TargetSelector`](#3-anatomy-of-targetselector).

## Configuration

Selectors are configured in-place on `ScriptableObject` assets that reference triggers / conditions / actions. The configurable surface is per-selector and exposed via Unity's Inspector. There is no separate `appsettings` file — designers tune values directly on the assets.

---

## 1. Where selectors live

| Layer | File | Purpose |
|---|---|---|
| Core base class | [TargetSelector.cs](TargetSelector.cs) | Abstract base; `SelectTargets`, per-target `Conditions`, and fork helpers |
| Identity / event | [InitiatorTargetSelector.cs](InitiatorTargetSelector.cs), [EventTargetSelector.cs](EventTargetSelector.cs) | Use the event's own Initiator / Target |
| Spatial | [AreaTargetSelector.cs](AreaTargetSelector.cs), [ConeTargetSelector.cs](ConeTargetSelector.cs), [LineTargetSelector.cs](LineTargetSelector.cs), [ChainTargetSelector.cs](ChainTargetSelector.cs) | Geometric queries around a context point |
| Distance | [NearestTargetSelector.cs](NearestTargetSelector.cs), [FurthestTargetSelector.cs](FurthestTargetSelector.cs) | Closest / furthest within a radius |
| Random | [RandomTargetSelector.cs](RandomTargetSelector.cs) | Random pick within a radius (uses `EventData.RNG` for determinism) |
| Scene-wide | [AllCharactersTargetSelector.cs](AllCharactersTargetSelector.cs) | Every `ICharacter` in the active scene |
| Hierarchy | [ChildrenTargetSelector.cs](ChildrenTargetSelector.cs) | Direct children of the context object |
| Scene lookup | [NamedSceneObjectTargetSelector.cs](NamedSceneObjectTargetSelector.cs), [TaggedSceneObjectTargetSelector.cs](TaggedSceneObjectTargetSelector.cs) | Resolve a scene object by name / Unity tag at runtime (asset-safe) |

---

## 2. The execution model

A trigger flows through three target-selector touch points. **The initiator never changes** during the lifetime of a single `Trigger.Execute`.

```
caller builds EventData
        │
        ▼
   Trigger.Execute(eventData)
        │
        ├── Trigger.TargetSelector          ── fan-out: one branch per selected target
        │       │
        │       ▼  (eventData.Fork(target))
        │   per-target eventData (Initiator unchanged, Target reassigned)
        │       │
        │       ├── For each Condition:
        │       │       Condition.TargetSelector? ── optional fan-out + Combine (All/Any)
        │       │
        │       └── For each Action in the met/not-met branch:
        │               Action.TargetSelector?    ── optional fan-out (no combine)
```

### 2.1 Top-level `Trigger.TargetSelector`
- Set → trigger fans out once per yielded target, running condition + action evaluation per target.
- **Null → trigger fires once against the eventData as-is** (whatever `Target` the caller put in). This is the intended fallback for OnHit, region, dialogue, and item-use triggers.

### 2.2 Per-condition `BaseCondition.TargetSelector`
- Optional. When set, the condition fans out across selected targets, combining results per `ConditionTargetCombine`:
  - `All` → all must pass (vacuous-true on empty selection).
  - `Any` → at least one must pass (false on empty selection).

### 2.3 Per-action `BaseAction.TargetSelector`
- Optional. When set, the action executes once per yielded target (no combine — actions are imperative).

All fan-out is implemented in `TriggerExecution` (in [Trigger.cs](../../../../Core/Entity/ECA/Core/Trigger.cs)) and uses `EventData.Fork(target)` to create per-target scopes that preserve `Initiator`, `RNG`, and all sub-payloads while reassigning `Target` / `TargetCharacter`.

---

## 3. Anatomy of `TargetSelector`

Every selector inherits these slots from the abstract base ([TargetSelector.cs](TargetSelector.cs)):

| Slot | Type | Purpose |
|---|---|---|
| `Conditions` | `List<BaseCondition>` | Per-candidate filter. A candidate is only yielded if every condition passes for it. |

> **Why no `TargetOverride` / `InitiatorOverride`?** Selectors live inside `[SerializeReference]` lists on ScriptableObject Trigger assets. Unity cannot serialize direct scene `GameObject` references from an asset, so design-time "pick this scene object" slots silently became `null` at runtime. The clean replacement: use [NamedSceneObjectTargetSelector](NamedSceneObjectTargetSelector.cs) or [TaggedSceneObjectTargetSelector](TaggedSceneObjectTargetSelector.cs) for scene-object resolution at fire time, or set `EventData.Target` at the invocation site for inline (MonoBehaviour-hosted) triggers.

### Helper methods (for selector authors)
- `GetContext(eventData)` → returns the spatial reference point: `eventData.Target ?? eventData.Initiator.GameObject`.
- `AreConditionsMet(candidate, eventData)` → evaluates per-candidate `Conditions` against a forked event data scoped to the candidate. Delegates to `TriggerExecution.AreConditionsMet`, so a condition placed inside a selector's `Conditions` list honors its own `TargetSelector` / `Combine` settings exactly the same way a top-level trigger condition does.

### Standard selector skeleton

```csharp
public override IEnumerable<GameObject> SelectTargets(EventData eventData)
{
    // 1. Server only. A physics query is not reproducible across peers.
    if (!IsAuthoritativePeer(eventData)) yield break;

    GameObject context = GetContext(eventData);
    if (context == null) yield break;

    // 2. Gather EAGERLY inside one rewind scope, then yield after it has closed.
    List<GameObject> results = new List<GameObject>();
    GatherRewound(eventData, context, results, Gather);

    for (int i = 0; i < results.Count; ++i) yield return results[i];
}

private void Gather(EventData eventData, GameObject context, List<GameObject> results)
{
    Collider[] hits = new Collider[QueryBufferSize(MaxHits)];
    List<GameObject> candidates = new List<GameObject>();
    List<GameObject> keys = new List<GameObject>();   // one BODY key per candidate
    List<TargetRank> ranks = new List<TargetRank>();

    Vector3 center = context.transform.position;
    PhysicsScene physicsScene = context.scene.GetPhysicsScene();

    // 3. Re-query until the buffer stops coming back full.
    int hitCount;
    while (true)
    {
        hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
        if (!TargetOrdering.TryGrowQueryBuffer(ref hits, hitCount)) break;
    }

    for (int i = 0; i < hitCount; i++)
    {
        Collider hit = hits[i];
        if (hit == null || !AreConditionsMet(hit.gameObject, eventData)) continue;

        candidates.Add(hit.gameObject);
        keys.Add(TargetOrdering.ResolveHitKey(hit, out ICharacter _));
        ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject,
                                      Vector3.Distance(center, hit.transform.position)));
    }

    // 4. rank -> dedupe -> cap, in that order and never another.
    TargetOrdering.SortByDistance(ranks);
    TargetOrdering.DedupeByBody(ranks, keys);
    TargetOrdering.ApplyMaxHits(ranks, MaxHits);

    for (int i = 0; i < ranks.Count; ++i) results.Add(candidates[ranks[i].Index]);
}
```

**Why the gather is a separate eager method.** Selectors are iterators, and the consumer between
two `yield return`s is the damage/ECA pipeline. Running that while the world is displaced by a
rewind would apply effects against a world hundreds of milliseconds stale, so everything is
materialised first and the scope closes before anything downstream runs. `GatherRewound` also
means one scope for the whole gather rather than one per query — the registry refuses nested
rewinds, so a selector invoked from inside somebody else's scope runs against that outer rewind
instead of corrupting it.

---

## 4. Selector catalogue

### Identity / event-resolved
| Selector | Yields |
|---|---|
| `InitiatorTargetSelector` | `eventData.Initiator.GameObject`. Use for **self-only** effects (heals, self-buffs). Ignores Target. |
| `EventTargetSelector` | `eventData.Target` as-is (optional `FallbackToInitiator`). Use for **OnHit / region / interaction** triggers where the caller already resolved the target. |

### Spatial (use `GetContext` as origin)
| Selector | Shape | Yields | What caps it |
|---|---|---|---|
| `AreaTargetSelector` | Sphere | Bodies within `Radius`, nearest first | `MaxHits`, applied after ranking and per-body dedupe |
| `ConeTargetSelector` | Cone | Bodies within `Radius` and `Angle` of context's forward, nearest first | `MaxHits`, same order |
| `LineTargetSelector` | Ray | Bodies along context's forward for `Length`, in ray order | `MaxHits` bodies, streamed as the ray is walked. **`MaxHits <= 0` means pierce everything.** |
| `ChainTargetSelector` | Chained spheres | Up to `ChainLength` bodies, each the nearest unvisited one within `ChainRadius` of the previous | `ChainLength`. Its `QueryBufferHint` field only sizes the per-jump buffer. |

### Distance-ranked
| Selector | Yields |
|---|---|
| `NearestTargetSelector` | The single closest candidate within `Radius` (excluding the context's own body). Its `QueryBufferHint` field only sizes the buffer. |
| `FurthestTargetSelector` | The single furthest candidate within `Radius` (same exclusion, same `QueryBufferHint` caveat). |

### Random
| Selector | Yields |
|---|---|
| `RandomTargetSelector` | One random candidate within `Radius`. Uses `eventData.RNG` when present, else a stream derived from the event's identity, for reproducible selection. Server-only. |

### Scene-wide / hierarchy
| Selector | Yields |
|---|---|
| `AllCharactersTargetSelector` | Every `ICharacter` in the **context's** scene — not the active scene (toggles for inactive / unspawned). Server-only. |
| `ChildrenTargetSelector` | Direct children of the context's transform. |

### Scene lookup (asset-safe scene references)
| Selector | Yields |
|---|---|
| `NamedSceneObjectTargetSelector` | The first scene `GameObject` whose name matches `ObjectName`. Scoped to the context's scene; falls back to the active scene when no context. |
| `TaggedSceneObjectTargetSelector` | All scene `GameObject`s carrying the given Unity tag in the context's scene. `FirstOnly = true` yields just the first match. |

These exist specifically because `[SerializeReference]` selectors on asset Triggers cannot hold direct scene references — they let designers point a Trigger at a named/tagged scene object without breaking serialization. For frequent triggers, prefer wiring the reference through `EventData.Target` at the invocation site (e.g., from a scene-hosted MonoBehaviour) rather than paying the name/tag scan on every fire.

---

## 5. Designer cookbook

### "Hit whatever the ability collided with"
Either of these is equivalent:
- **Implicit:** leave `Trigger.TargetSelector` null on the OnHit trigger.
- **Explicit:** set `Trigger.TargetSelector = EventTargetSelector`. Use this when you need per-candidate `Conditions`.

### "Buff myself when conditions pass"
Set `Trigger.TargetSelector = InitiatorTargetSelector`. The caster is the only target regardless of any `Target` on the event.

### "Damage the hit character AND its 4 nearest allies"
Two ways:
1. Two actions on the same trigger:
   - Action A: `ApplyDamageAction` with no `TargetSelector` (uses event Target).
   - Action B: `ApplyDamageAction` with `AreaTargetSelector` and `MaxHits=4`. The cap is applied after a
     nearest-first sort, so it really does mean "the 4 nearest". (Before the 2026-08-28 audit it sorted by
     network identity and the cap kept the four lowest ObjectIds regardless of distance.)
2. One action with `ChainTargetSelector` on the trigger.

### "AoE around the impact point"
- Trigger `TargetSelector = AreaTargetSelector` (radius, layer). Conditions can filter to enemies of the initiator via `HasFactionCondition` or similar.
- All downstream actions fan out per AoE target automatically.

### "Apply different effects to allies vs enemies in one trigger"
- Use the `Conditions` list on **two separate Triggers** (or duplicate the action set with selector-level `Conditions`).
- The OnConditionsMet / OnConditionsNotMet branching on a single Trigger is per-target, so you can also model "if target is ally → heal, else → damage" by putting a faction check in `Conditions` and a heal in the met branch / damage in the not-met branch.

### "Per-condition target check that differs from the action's target"
- Set `BaseCondition.TargetSelector` (e.g. `AllCharactersTargetSelector` with `Combine=Any`) to ask "is there *any* enemy in the scene that satisfies X?" without changing the action's target.

### "Pre-pick a specific scene object regardless of context"
Asset-based Triggers cannot store direct scene references, so use one of:
- **`NamedSceneObjectTargetSelector`** — set `ObjectName` to the scene object's name. Resolves at fire time within the event's scene.
- **`TaggedSceneObjectTargetSelector`** — set `Tag` to a Unity tag pre-assigned to the scene object (Inspector > Tag dropdown). Cheaper than a name walk and works for multi-object selection.
- **Inline trigger (preferred for one-offs):** host the Trigger on a MonoBehaviour in the scene and have the MonoBehaviour pass the picked GameObject through `EventData.Target` when firing — no string lookup, no scan.

---

## 6. Resolution in actions and conditions

Actions and conditions read targets via the convention:

```csharp
ICharacter target = (eventData?.TargetCharacter ?? initiator);
```

This works because:
- The trigger / per-action / per-condition `TargetSelector` fan-out re-forks the `EventData`, reassigning `Target` (and inferring `TargetCharacter`) per yielded candidate.
- Callers that resolve their own target up front (collision, region enter, dialogue) build `EventData` with `Target` already set, so the fallback simply uses it.
- When nothing is resolvable, `initiator` is the last-resort fallback (acceptable for self-effects; avoid for damage by always configuring a selector or ensuring the event carries a Target).

See [ApplyDamageAction.cs](../Actions/Character/ApplyDamageAction.cs) for the canonical pattern.

### 6.1 Fork semantics (important)

`EventData.Fork(target)` does **not** mutate the parent event. It constructs a brand-new `EventData`, calls `SetTarget` on that new instance, copies `Initiator` + `RNG`, and finally calls `scoped.Merge(this)` to copy sub-payload references in. The original event's `Target` is never touched.

This means a single top-level `EventData` may be the parent of many independent forks during a Trigger's fan-out, each with its own `Target` / `TargetCharacter`, all sharing the same `Initiator` and `RNG`. None of them can clobber the parent or each other.

**However**, `Merge` keeps the parent payload in the fork's dictionary under its concrete type so phase-specific data (collision, ability object, region, etc.) is still reachable. That gives every fork two routes to a "target":

| Access path | Returns |
|---|---|
| `eventData.Target` / `eventData.TargetCharacter` | The **per-fork** target (use this in actions/conditions). |
| `eventData.TryGet<AbilityCollisionEventData>(out var c); c.Target` | The **original** parent's target (stale for this fork — kept for the parent's typed context). |

The convention `eventData?.TargetCharacter ?? initiator` reads the base-class field, so it always sees the current fork. Do **not** dig through `TryGet<T>()` for `Target`/`TargetCharacter` — only do that for typed extras like `c.Collision`, `c.AbilityObject`, `c.Region`, etc. Mutating `Target` on a flowing event is also discouraged for the same reason: any forks already in flight that retained the parent in their `Merge` will see the new value through the typed payload.

---

## 7. Determinism notes

- `RandomTargetSelector` and any value provider that rolls (`RandomRangeValue`, `RandomRangeFloatValue`, `ApplyDispelAction`) read `EventData.RNG`. Always seed `RNG` on `EventData` for events that originate from a deterministic context (ability casts, collisions). Selectors do **not** propagate or fork RNG state — `EventData.Fork` shares the same RNG reference so all downstream targets draw from one deterministic stream.
- Avoid `UnityEngine.Random` inside custom selectors. Use `eventData.RNG` instead.
- **Ordering is part of determinism.** Any selector that caps, takes "the first", or rolls an index must
  impose a total order first — `TargetOrdering.SortByDistance` when the cap should mean "nearest",
  `SortStable` when it should mean "a stable set". Ties fall through to network ObjectId, then a hash of
  the name, then a hash of the authored world position, then buffer position. Never break a tie on
  `GetInstanceID()`: it is a per-process number and puts two same-named scene objects in a different
  order on each peer.

- **Query wide, cap late.** Size the physics buffer with `TargetSelector.QueryBufferSize(MaxHits)`, never
  at `MaxHits` — a buffer the size of the cap lets the broadphase choose which candidates the sort ever
  sees, in its own order. Then **re-query while the buffer comes back full**
  (`TargetOrdering.TryGrowQueryBuffer`): a non-allocating query returns at most `buffer.Length` results
  and says nothing about how many it discarded. `TryGrowQueryBuffer` is grow-only — a
  reallocate-on-mismatch would silently undo the previous cast's growth every time.

- **The pipeline is fixed: query → grow while full → resolve hit root → rank → dedupe by body → cap.**
  Each step is where it is for a reason:
  - *Resolve* through `TargetOrdering.ResolveHitKey`, never a bare `GetComponent` on the collider. A
    character is free to hang its hitbox off a child transform, and a bare lookup finds no `ICharacter`
    there — such a character is invisible to the selector while remaining perfectly able to fight back.
  - *Dedupe after the sort, before the cap.* The entry kept for a body is the first one met, which on a
    distance-ordered list is that body's nearest collider. Deduping earlier keeps an arbitrary one;
    capping earlier counts colliders instead of bodies, so a target rigged with two hitboxes eats two
    slots of `MaxHits`.
  - *Cap last.* Capping the raw overlap orders an arbitrary subset chosen by the broadphase.

- **`MaxHits <= 0` means uncapped, everywhere.** `TargetOrdering.CappedCount` defines it and every
  selector and action matches. A beam authored with `MaxHits = 0` pierces everything on the line.

- **A capped selection must never be computed by a peer that cannot agree on the answer.** A
  distance-ordered cap cannot be made peer-agreed by arithmetic, because peers hold different
  positions — identity order is the only perfectly agreed key and it is gameplay nonsense. So every
  capped selection must be either **(a)** server-only, or **(b)** run inside a rewind to the caster's
  view, where both peers see the same world. Every selector here is (a) via `IsAuthoritativePeer`, and
  additionally (b) via `GatherRewound`. A selector that is neither is a bug, and a different bug from
  cap ordering.

- **Rank inside the rewind scope.** `GatherRewound` holds one scope open across the query, the ranking
  and the conditions. Measuring distances after the scope closes selects out of the caster's world and
  then decides between the candidates using the server's — at 300 ms, a different answer.

---

## 8. Writing a new selector

1. Create `MySelector.cs` in this folder.
2. Inherit `TargetSelector`, mark `[Serializable]`.
3. Implement `SelectTargets(EventData)` using the skeleton above.
4. Surface tunable fields with `[Tooltip]` so they render usefully in the Inspector.
5. Gate on `IsAuthoritativePeer(eventData)` first. A physics query is not reproducible across peers,
   so it runs where hits are authoritative and nowhere else.
6. Gather **eagerly** through `GatherRewound`, then yield from the materialised list. Never `yield`
   while the world is displaced.
7. Allocate the hit buffer **locally**, not as a shared field. A candidate's conditions can themselves
   carry selectors and fire nested triggers, so a shared scratch buffer is one authored composition
   away from being cleared out from under the gather that owns it.
8. Size it with `QueryBufferSize(MaxHits)` and re-query through `TargetOrdering.TryGrowQueryBuffer`
   until it stops coming back full.
9. If your selector references scene physics, query `context.scene.GetPhysicsScene()` rather than the
   global physics scene — this keeps multi-scene servers correct. (`Physics.OverlapSphere` searches the
   default scene, which on a scene server holds none of these colliders.)
10. For every yielded candidate, call `AreConditionsMet(candidate, eventData)` so designers'
    per-candidate condition filters work uniformly.
11. If you cap, follow the fixed pipeline in [7. Determinism notes](#7-determinism-notes) exactly.

---

## 9. Quick reference: lifecycle invariants

- `EventData.Initiator` is **immutable** for the life of `Trigger.Execute`.
- `EventData.Target` / `TargetCharacter` are mutable instance fields, but **`Fork` does not mutate the parent** — each fork is a new instance with its own values. Avoid manually reassigning these on a flowing event during action execution.
- `EventData.RNG` is **shared** across forks — one deterministic stream per top-level event.
- Trigger `OnConditionsMetActions` / `OnConditionsNotMetActions` branching is **per-target**, not per-trigger.
- `IResourceCost` conditions are skipped during `AbilityEvent` execution (they're already aggregated and consumed at activation). Other triggers evaluate them normally.

---

## 10. Condition & action framework hooks

These are not selector features, but they directly affect how Triggers behave around the targets a selector yields.

### 10.1 `BaseCondition.Invert` (centralized)

Every concrete condition inherits an `Invert` toggle from [BaseCondition](../../../../Core/Entity/ECA/Core/Condition/BaseCondition.cs). The framework evaluates conditions through the non-virtual `Check(initiator, eventData)` wrapper, which calls the derived `Evaluate(...)` and flips the result when `Invert` is set. Implementers write **plain positive logic** in `Evaluate` and never re-derive `Invert ? !x : x` themselves.

Callers that previously called `condition.Evaluate(...)` directly have been migrated to `condition.Check(...)` (see [Trigger.cs](../../../../Core/Entity/ECA/Core/Trigger.cs), [CompositeCondition.cs](../../../../Core/Entity/ECA/Core/Condition/CompositeCondition.cs), and [Ability.cs](../../Prediction/Ability/Ability.cs)).

### 10.2 `BaseAction.StopChainOnFailure` + `IAbortableAction` (opt-in)

[BaseAction](../../../../Core/Entity/ECA/Core/Action/BaseAction.cs) exposes a `StopChainOnFailure` bool. By itself it is inert \u2014 the action must also implement `IAbortableAction` to be cancellable:

```csharp
public interface IAbortableAction { bool TryExecute(ICharacter initiator, EventData eventData); }
```

When `TriggerExecution` walks an action list:
- If an action implements `IAbortableAction`, its `TryExecute` runs. Returning `false` while `StopChainOnFailure == true` aborts the rest of the list.
- Plain `IAction` implementations continue to execute via `Execute(...)` with no abort semantics, so existing actions are unaffected.

Canonical example: [ConsumeResourceAction.cs](../Actions/Character/ConsumeResourceAction.cs) returns `false` when the resource attribute is missing or under cost, letting designers gate follow-up actions on a successful spend.

### 10.3 `BaseAction.TryResolveTarget` vs `TryResolveTargetOrInitiator`

Two helpers for action authors with deliberately different fallback semantics:

| Helper | Returns | Use for |
|---|---|---|
| `TryResolveTarget(eventData, out target)` | `eventData.TargetCharacter` only, false when null | **Outward-effecting** actions (damage, dispel, interrupt, knockback). A missing target is a no-op, never a self-hit. |
| `TryResolveTargetOrInitiator(initiator, eventData, out target)` | `TargetCharacter ?? initiator` | **Self-or-target** actions (resource consume, self-buffs, self-heals) where "no target" naturally means "act on self". |

The split exists because the old "fall back to initiator" convention silently turned a misconfigured selector into a caster self-hit. The strict variant makes that impossible; pick the forgiving variant only when self-action is the documented intent.

Migrated examples: [ApplyDamageAction](../Actions/Character/ApplyDamageAction.cs), [ApplyDispelAction](../Actions/Character/ApplyDispelAction.cs), [InterruptAction](../Actions/Character/InterruptAction.cs), [KnockbackHitAction](../Actions/Character/KnockbackHitAction.cs) use the **strict** helper. [ApplyHealAction](../Actions/Character/ApplyHealAction.cs), [ApplyBuffAction](../Actions/Character/ApplyBuffAction.cs), [ConsumeResourceAction](../Actions/Character/ConsumeResourceAction.cs) use the **forgiving** variant.

### 10.4 Fault isolation

`TriggerExecution` wraps both condition evaluation and action invocation in try/catch:
- A throwing **condition** is treated as a failed condition (the trigger takes the not-met branch) and the exception is logged at error level — it does not crash the whole trigger.
- A throwing **action** is logged at error level; sibling actions and fan-out targets continue to execute. Throwing actions do **not** trigger `StopChainOnFailure` semantics — that flag is reserved for *intentional* failure via `IAbortableAction.TryExecute` returning false. Designer-authored content cannot poison the chain by raising an exception.

### 10.5 Condition filter propagation

A `Trigger` subclass may override `ShouldEvaluateCondition` to skip categories of conditions during execution (e.g. `AbilityEvent` skips `IResourceCost` conditions because the cost was already paid at activation). The filter is published on `EventData.ConditionFilter` at the start of `ExecuteForTarget` and carried across `Fork`, so it applies uniformly at three levels:

1. Top-level `Trigger.Conditions` (via `TriggerExecution.AreConditionsMet`).
2. Children of any `CompositeCondition` nested in those lists.
3. Per-candidate `TargetSelector.Conditions` evaluated during selector fan-out.

This means a designer can nest a `HasResourceCondition` (or any other `IResourceCost`) inside a composite or behind a selector filter without it being silently double-charged at execution time. An OR composite that has all of its children filtered out evaluates to **true** (matching the empty-list behavior of AND), so an "OR of resource-paid conditions" doesn't collapse into a falsy gate after activation.

### 10.6 Tooltip contribution

`BaseCondition`, `BaseAction`, and `TargetSelector` each expose a `virtual string GetTooltipContribution() => null;` hook. Override on concrete types that should appear in ability/quest/dialogue tooltips; returning `null` or whitespace omits the line. `BaseAbilityTemplate.BuildTooltip` aggregates contributions into four ordered sections per `AbilityEvent`:

| Section | Source | Sort order |
|---|---|---|
| Resource Cost | `IResourceCost`-implementing conditions | 50 |
| Targeting | `AbilityEvent.TargetSelector.GetTooltipContribution()` | 55 |
| Requirements | Other conditions' `GetTooltipContribution()` | 60 |
| Effects | `OnConditionsMetActions[*].GetTooltipContribution()` | 70 |

Designers no longer need to author tooltip text twice: a `Deal 50 fire damage` ApplyDamageAction implementation can simply override the hook and the tooltip line will appear automatically.

### 10.7 Instrumentation hook

`Trigger.OnExecuted` is a static `event Action<Trigger, EventData>` invoked once per `Execute()` call (after fan-out completes). Subscribers receive the firing trigger asset and the *original* event data passed to `Execute` (not the per-target fork). Exceptions thrown by subscribers are caught and logged so a misbehaving recorder/audit subscriber can't poison gameplay. Use for replay, server-side audit, editor instrumentation, or to wire a breakpoint into any trigger fire without modifying it.

### 10.8 Editor validation

`Trigger.OnValidate` (editor-only) strips null entries that Unity may leave after `SubclassSelector` type renames and warns on common authoring mistakes:
- Both action lists empty (trigger has no observable effect).
- `OnConditionsNotMetActions` populated while `Conditions` is empty (not-met branch is unreachable).

Warnings are non-blocking — they appear in the Console attached to the offending asset. Designers can re-run the checks on demand via the `Validate Now` context-menu entry on any `Trigger` asset.

### 10.9 Polymorphic payload lookup

`EventData.TryGet<T>()` first does an exact-type dictionary lookup; if that misses it falls back to a linear scan returning the first stored payload assignable to `T`. This lets a designer-authored subclass of e.g. `AbilityCollisionEventData` still be retrieved by code that asks for the base type, removing a sharp edge that previously required callers to know the concrete subclass key.

### 10.10 Per-trigger verbose logging

`Trigger` exposes a `Verbose` toggle (Inspector → *Debug* group). When checked, the trigger's own lifecycle logs (conditions met / not met / no targets produced) are promoted from `Log.Debug` to `Log.Info` so a single misbehaving asset can be diagnosed without flipping the global log level for the whole project.

### 10.11 Designer-facing action ordering

`OnConditionsMetActions` and `OnConditionsNotMetActions` are executed top-to-bottom. When an action implements `IAbortableAction` and has `StopChainOnFailure` set, a failed `TryExecute` aborts every action below it in the same list (and any remaining targets in the action's own fan-out). Put resource-cost / can-afford gates at the top of the list so they short-circuit cleanly. The list `[Tooltip]` text reminds designers of this contract.

### 10.12 Inline triggers share the asset-trigger code path

Some host MonoBehaviours (e.g. `WorldDayNightCycle`) author triggers inline via `[Serializable]` types like `WorldSceneTrigger` rather than referencing a `Trigger` asset. These inline types must not reimplement the execution loop; instead they delegate to `TriggerExecution.RunInline(selector, conditions, onConditionsMetActions, onConditionsNotMetActions, eventData)`. The helper centralises selector fan-out, per-target `EventData.Fork`, condition evaluation, and the met/not-met branch dispatch — the same logic `Trigger.Execute` uses. Any future fix made to the asset path (fault isolation, ambient `ConditionFilter`, fan-out rules, …) is therefore inherited automatically by inline triggers. Inline triggers also mirror the `OnExecuted` instrumentation event and expose an editor-only `Sanitize()` so their host's `OnValidate` can null-strip `SubclassSelector` remnants exactly like `Trigger.OnValidate` does on the asset.

## Flow Diagram

```mermaid
flowchart LR
    Event[Event] --> ECA[ECA pipeline]
    ECA --> Cond[Condition match]
    Cond --> Target[Target resolver]
    Target --> Action[Action handler]
    Action --> Effect[Effect on entity]
```
