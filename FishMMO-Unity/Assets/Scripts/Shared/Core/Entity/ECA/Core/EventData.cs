using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Container for event-related data in the ECA trigger system.
	/// <para>
	/// Carries three first-class fields so triggers, conditions and actions never have to
	/// hunt through typed sub-payloads to find the basic identities involved in an event:
	/// </para>
	/// <list type="bullet">
	///   <item><description><see cref="Initiator"/> — the single character that started the trigger (immutable).</description></item>
	///   <item><description><see cref="Target"/> — the GameObject the trigger is acting on (mutable; set by selectors / collision dispatch).</description></item>
	///   <item><description><see cref="TargetCharacter"/> — the <see cref="ICharacter"/> on <see cref="Target"/>, when one exists.</description></item>
	///   <item><description><see cref="RNG"/> — optional deterministic RNG carried alongside hit events.</description></item>
	/// </list>
	/// <para>
	/// Phase-specific data (collision, tick, spawn, damage amount, …) is still stored as
	/// typed sub-payloads accessible via <see cref="TryGet{T}"/>.
	/// </para>
	/// </summary>
	public class EventData
	{
		/// <summary>
		/// The character that initiated the event. Immutable for the lifetime of the
		/// <see cref="EventData"/> — a single trigger has exactly one initiator. To rebind
		/// initiator (e.g. reflected damage, summon procs) construct a new <see cref="EventData"/>.
		/// </summary>
		public ICharacter Initiator { get; }

		/// <summary>
		/// The GameObject the trigger is currently targeting. Mutable so target selectors can
		/// reuse a single <see cref="EventData"/> while iterating selected candidates.
		/// May be null for world-level events.
		/// </summary>
		public GameObject Target { get; set; }

		/// <summary>
		/// The character on <see cref="Target"/>, when the target GameObject implements
		/// <see cref="ICharacter"/>. May be null for non-character targets or world events.
		/// </summary>
		public ICharacter TargetCharacter { get; set; }

		/// <summary>Explicitly supplied generator, or the lazily derived one once it has been asked for.</summary>
		private DeterministicRNG rng;

		/// <summary>
		/// The event that owns this chain's independent streams — <c>this</c> for a root event, the
		/// original for anything <see cref="Fork"/> produced. Never null after construction.
		/// </summary>
		/// <remarks>
		/// A fan-out is one event, so the streams handed out by <see cref="IndependentRNG(int)"/>
		/// must be one set shared by the root and every fork of it. Routing to a root is what makes
		/// that work with LAZY allocation: sharing the dictionary by reference the way
		/// <see cref="Fork"/> shares <see cref="rng"/> would only share it when the parent happened
		/// to have allocated one before the fork was taken, and two forks of a parent that had not
		/// would each get their own — which is exactly the correlation this type exists to avoid.
		/// </remarks>
		private EventData streamRoot;

		/// <summary>
		/// Independent streams held by <see cref="streamRoot"/>, keyed by salt. Null until the first
		/// one is asked for, because most events never ask.
		/// </summary>
		private Dictionary<int, DeterministicRNG> independentStreams;

		/// <summary>
		/// The deterministic RNG for this event. An explicitly threaded generator (the ability
		/// object's, for a hit) is returned as-is; otherwise one is derived on first use from the
		/// event's own identity and cached.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Never null, and never the shared instance.</b> It used to be null for every event type
		/// that was not an ability collision — <c>AbilityEventData</c>, <c>RegionEventData</c>,
		/// <c>BuffEventData</c> — and every consumer coped by falling back to
		/// <see cref="DeterministicRNG.Shared"/>, a process-wide stream seeded from
		/// <c>Environment.TickCount</c>. That is not a fallback, it is a different answer on every
		/// peer and on every run: a random target selector picking out of the same five candidates
		/// picked a different one on the client than on the server, and a different one again the
		/// next time the same cast happened.
		/// </para>
		/// <para>
		/// The derived stream is a function of the initiator's network identity and the event's tick,
		/// both of which every peer agrees on, so the same event yields the same rolls everywhere.
		/// Two events in the same tick from the same initiator share a stream deliberately: they are
		/// the same event as far as reproduction is concerned. Where a caller needs an independent
		/// reproducible stream — a selector that must not consume the rolls a later action expects —
		/// it takes one from <see cref="IndependentRNG(int)"/> with its own salt.
		/// </para>
		/// </remarks>
		public DeterministicRNG RNG
		{
			get
			{
				if (rng == null)
				{
					rng = DeriveRNG(0);
				}
				return rng;
			}
			set => rng = value;
		}

		/// <summary>
		/// True when a generator was explicitly threaded onto this event rather than derived from it.
		/// </summary>
		/// <remarks>
		/// For diagnostics and for the few call sites that genuinely want to know whether an upstream
		/// system owns the stream. Reading it does not derive one.
		/// </remarks>
		public bool HasExplicitRNG => rng != null;

		/// <summary>
		/// The one independent stream this event chain holds for <paramref name="salt"/>, created on
		/// first use and shared by the root event and every <see cref="Fork"/> of it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is what a server-only consumer draws from, not <see cref="RNG"/>.</b> The shared
		/// generator is threaded onto an ability object's OnSpawn/OnTick/OnHit/OnDestroy payloads
		/// and is advanced by side effect, so a draw taken behind a peer gate advances it only on
		/// the peers that pass — and an ungated action later in the same chain
		/// (<c>AbilityForkHitAction</c>) then reads a different number. See <c>AbilityObject.RNG</c>
		/// for the rule and what it cost. An action can satisfy it by evaluating its providers
		/// above the gate; a server-only SELECTOR cannot, because the gate is the selector, so it
		/// takes a stream of its own instead.
		/// </para>
		/// <para>
		/// <b>Memoised, which is the whole difference from <see cref="DeriveRNG(int)"/>.</b> That
		/// method is a pure factory and hands back a FRESH generator every call, so two consumers
		/// sharing a salt — or one consumer called twice — would draw identical numbers rather than
		/// independent ones, and the sequence would not advance at all. Holding one generator per
		/// (event chain, salt) gives a stream that advances across draws exactly as
		/// <see cref="RNG"/> does, while touching nothing <see cref="RNG"/>'s consumers rely on.
		/// </para>
		/// <para>
		/// <b>Reproducible, not seedable.</b> The sequence is a function of the initiator's network
		/// identity, the event's tick and <paramref name="salt"/> — values every peer agrees on —
		/// so it is identical on every peer and on every run of the same cast. It deliberately does
		/// NOT depend on any generator a caller assigned to <see cref="RNG"/>; that independence is
		/// the point. Use a distinct constant salt per call site.
		/// </para>
		/// </remarks>
		/// <param name="salt">Distinguishes one consumer's stream from another's within one event.</param>
		/// <returns>The shared-per-chain generator for <paramref name="salt"/>.</returns>
		public DeterministicRNG IndependentRNG(int salt)
		{
			/* Always the root's map, never this instance's. A fan-out is one event: a selector that
			 * draws on the parent and an action that draws on a per-candidate fork must be walking
			 * the same sequence, or the fork's first draw repeats the parent's. */
			EventData root = streamRoot ?? this;
			if (!ReferenceEquals(root, this))
			{
				return root.IndependentRNG(salt);
			}

			independentStreams ??= new Dictionary<int, DeterministicRNG>(1);
			if (!independentStreams.TryGetValue(salt, out DeterministicRNG stream))
			{
				stream = DeriveRNG(salt);
				independentStreams[salt] = stream;
			}
			return stream;
		}

		/// <summary>
		/// Builds a reproducible generator for this event, independent of any other stream.
		/// </summary>
		/// <remarks>
		/// A pure factory: every call returns a NEW generator positioned at the start of the
		/// sequence for these inputs. That is what makes it testable, and what makes it the wrong
		/// thing for a consumer that draws more than once — see <see cref="IndependentRNG(int)"/>,
		/// which memoises one per (event chain, salt) so the sequence actually advances.
		/// </remarks>
		/// <param name="salt">
		/// Distinguishes one consumer from another within the same event. Use a constant per call
		/// site; anything derived from local state would defeat the point.
		/// </param>
		/// <returns>A generator whose sequence is identical on every peer for the same event.</returns>
		public DeterministicRNG DeriveRNG(int salt)
		{
			int identity = 0;
			if (Initiator != null)
			{
				/* ObjectId first: the server assigns it and replicates it, so every peer holds the
				 * same value for the same character. The persistent character ID is the fallback for
				 * an initiator that is not (or not yet) a spawned network object — a scene trigger,
				 * an unspawned character — and 0 for one that is neither, which still yields a
				 * reproducible stream, just one shared by all such initiators. */
				identity = Initiator.NetworkObject != null
					? Initiator.NetworkObject.ObjectId
					: (int)Initiator.ID;
			}

			uint tick = 0u;
			if (TryGet(out TickEventData tickData))
			{
				tick = tickData.Tick.Value;
			}

			return new DeterministicRNG(DeriveSeed(identity, tick, salt));
		}

		/// <summary>
		/// The seed mix behind <see cref="DeriveRNG(int)"/>, as a pure function.
		/// </summary>
		/// <remarks>
		/// FNV-1a over the three inputs. Separated out so the properties that matter — same inputs
		/// give the same seed, a different tick gives a different one — are testable without an
		/// event, a character or a network.
		/// </remarks>
		/// <param name="identity">The initiator's network object id, or its character id.</param>
		/// <param name="tick">The event's tick, or 0 when it carries none.</param>
		/// <param name="salt">Per-call-site constant.</param>
		public static int DeriveSeed(int identity, uint tick, int salt)
		{
			unchecked
			{
				uint hash = 2166136261u;
				hash = (hash ^ (uint)identity) * 16777619u;
				hash = (hash ^ tick) * 16777619u;
				hash = (hash ^ (uint)salt) * 16777619u;
				return (int)hash;
			}
		}

		/// <summary>
		/// Optional ambient filter applied to every condition evaluation occurring within
		/// this trigger fire. When non-null, returning <c>false</c> from the filter causes
		/// the condition to be skipped (treated as if not present) at every level —
		/// top-level <see cref="Core.Trigger.Conditions"/>, nested
		/// <see cref="CompositeCondition"/> children, and selector-scoped
		/// <see cref="TargetSelector.AreConditionsMet"/> calls. Carried across
		/// <see cref="Fork(GameObject, ICharacter)"/> so target fan-outs preserve the
		/// filter context.
		/// <para>
		/// Set by <see cref="Core.Trigger.Execute(EventData)"/> from
		/// <see cref="Core.Trigger.ShouldEvaluateCondition(BaseCondition)"/>. Designers
		/// and authors of new triggers/selectors generally do not need to touch this.
		/// </para>
		/// </summary>
		public System.Func<BaseCondition, bool> ConditionFilter { get; set; }

		/// <summary>
		/// Returns the type name of this event data instance.
		/// </summary>
		public override string ToString() => GetType().Name;

		/// <summary>
		/// Dictionary storing event data sub-payloads by their type.
		/// </summary>
		private readonly Dictionary<Type, EventData> eventDataDictionary = new Dictionary<Type, EventData>();

		/// <summary>
		/// Constructs a new EventData with no target, optionally seeded with extra payloads.
		/// </summary>
		/// <param name="initiator">The character that initiated the event.</param>
		/// <param name="initialData">Optional initial sub-payloads to add.</param>
		public EventData(ICharacter initiator, params EventData[] initialData)
		{
			Initiator = initiator;
			streamRoot = this;
			eventDataDictionary[GetType()] = this;
			AddRange(initialData);
		}

		/// <summary>
		/// Constructs a new EventData targeting an <see cref="ICharacter"/>. The character's
		/// <see cref="ICharacter.GameObject"/> is also assigned to <see cref="Target"/> for
		/// consumers that want a GameObject reference.
		/// </summary>
		/// <param name="initiator">The character that initiated the event.</param>
		/// <param name="targetCharacter">The character being targeted.</param>
		/// <param name="rng">Optional deterministic RNG.</param>
		public EventData(ICharacter initiator, ICharacter targetCharacter, DeterministicRNG rng = null)
			: this(initiator)
		{
			TargetCharacter = targetCharacter;
			Target = targetCharacter?.GameObject;
			RNG = rng;
		}

		/// <summary>
		/// Constructs a new EventData targeting a <see cref="GameObject"/>. When the GameObject
		/// has an <see cref="ICharacter"/> component it is auto-assigned to <see cref="TargetCharacter"/>.
		/// </summary>
		/// <param name="initiator">The character that initiated the event.</param>
		/// <param name="target">The GameObject being targeted.</param>
		/// <param name="rng">Optional deterministic RNG.</param>
		public EventData(ICharacter initiator, GameObject target, DeterministicRNG rng = null)
			: this(initiator)
		{
			SetTarget(target);
			RNG = rng;
		}

		/// <summary>
		/// Sets <see cref="Target"/> and infers <see cref="TargetCharacter"/> from the
		/// GameObject's <see cref="ICharacter"/> component (if any).
		/// </summary>
		/// <param name="target">The GameObject being targeted, or null to clear.</param>
		public void SetTarget(GameObject target)
		{
			Target = target;
			/* GetComponentInParent, not TryGetComponent.
			 *
			 * A selector yields the GameObject a physics query hit, which is a COLLIDER — and
			 * TargetOrdering.Rank already resolves a hit to its owning NetworkObject through the
			 * parents for exactly that reason. Resolving the character only on the hit object itself
			 * meant the two disagreed about what a candidate is: a character whose collider sits on a
			 * child (a hit zone, a ragdoll bone) ranked as the character and then arrived here with a
			 * null TargetCharacter, so every action that resolves through it silently did nothing.
			 * Benign on today's prefabs, where the capsule is on the root, and a trap for the first
			 * one that is not. */
			TargetCharacter = target != null && target.TryGetComponent(out ICharacter character)
				? character
				: target != null ? target.GetComponentInParent<ICharacter>() : null;
		}

		/// <summary>
		/// Sets both <see cref="Target"/> and <see cref="TargetCharacter"/> at once when both
		/// are already resolved by the caller (avoids a redundant <c>GetComponent</c>).
		/// </summary>
		/// <param name="target">The GameObject being targeted.</param>
		/// <param name="targetCharacter">The character on <paramref name="target"/>.</param>
		public void SetTarget(GameObject target, ICharacter targetCharacter)
		{
			Target = target;
			TargetCharacter = targetCharacter;
		}

		/// <summary>
		/// Creates a new <see cref="EventData"/> sharing this instance's <see cref="Initiator"/>
		/// and <see cref="RNG"/>, with all sub-payloads merged in, but with a new <see cref="Target"/>
		/// (and inferred <see cref="TargetCharacter"/>). Used by selectors to scope an event to one
		/// of several selected targets without losing the original phase-specific payloads.
		/// </summary>
		/// <param name="target">The new target GameObject. May be null.</param>
		/// <param name="targetCharacter">Optional pre-resolved character on <paramref name="target"/>.</param>
		/// <returns>A new event data scoped to the supplied target.</returns>
		public EventData Fork(GameObject target, ICharacter targetCharacter = null)
		{
			EventData scoped = new EventData(Initiator);
			if (targetCharacter != null)
			{
				scoped.SetTarget(target, targetCharacter);
			}
			else
			{
				scoped.SetTarget(target);
			}
			/* The raw field, not the property: a fork must not be the thing that decides the parent
			 * needs a generator. When the parent has one the fork shares it (a fan-out is one event);
			 * when it does not, the fork derives its own from the identity they both carry, which is
			 * the same seed the parent would have derived. */
			scoped.rng = rng;
			/* The chain's independent streams follow the fork, so a server-only consumer drawing on
			 * a per-candidate fork continues the sequence the parent started rather than restarting
			 * it. Routed rather than copied because the map is allocated lazily and may not exist
			 * yet — see the remarks on streamRoot. */
			scoped.streamRoot = streamRoot ?? this;
			scoped.ConditionFilter = ConditionFilter;
			scoped.Merge(this);
			return scoped;
		}

		/// <summary>
		/// Adds an event data sub-payload, keyed by its concrete type.
		/// Refuses to overwrite the self-registration that wires <c>this</c> into the dictionary.
		/// To copy sub-payloads from another <see cref="EventData"/>, use <see cref="Merge"/>.
		/// </summary>
		/// <param name="data">The event data sub-payload to add.</param>
		public void Add(EventData data)
		{
			if (data == null || data == this)
			{
				return;
			}
			eventDataDictionary[data.GetType()] = data;
		}

		/// <summary>
		/// Adds multiple sub-payloads, ignoring nulls.
		/// </summary>
		/// <param name="payloads">Sub-payloads to add.</param>
		public void AddRange(EventData[] payloads)
		{
			if (payloads == null) return;

			for (int i = 0; i < payloads.Length; ++i)
			{
				Add(payloads[i]);
			}
		}

		/// <summary>
		/// Copies every sub-payload from <paramref name="source"/> into this event data.
		/// Use when wrapping or re-targeting an existing event so downstream conditions/actions
		/// continue to see the original phase-specific payloads (e.g. <c>RegionEventData</c>,
		/// <c>QuestEventData</c>) carried on the source.
		/// </summary>
		/// <param name="source">The event data whose sub-payloads should be merged into this one.</param>
		public void Merge(EventData source)
		{
			if (source == null || source == this)
			{
				return;
			}

			foreach (var kv in source.eventDataDictionary)
			{
				// Skip the source's self-registration; only copy foreign payloads.
				if (kv.Value == source)
				{
					continue;
				}
				eventDataDictionary[kv.Key] = kv.Value;
			}

			// Source itself is the carrier of its own concrete type — register it so
			// downstream code can TryGet<SourceConcreteType> and find it.
			eventDataDictionary[source.GetType()] = source;
		}

		/// <summary>
		/// Attempts to retrieve a sub-payload of type <typeparamref name="T"/>. Performs an
		/// exact-type lookup first (the fast path); if no payload is registered under that
		/// exact key, falls back to a linear scan returning the first stored payload
		/// assignable to <typeparamref name="T"/>. This lets callers request a base type
		/// (e.g. <c>TryGet&lt;AbilityCollisionEventData&gt;()</c>) and still receive a
		/// designer-authored subclass payload.
		/// </summary>
		/// <typeparam name="T">The type of event data to retrieve.</typeparam>
		/// <param name="data">The retrieved sub-payload, or default if not found.</param>
		/// <returns>True if found; otherwise, false.</returns>
		public bool TryGet<T>(out T data) where T : EventData
		{
			// Fast path: exact concrete type registered under typeof(T).
			if (eventDataDictionary.TryGetValue(typeof(T), out EventData foundData) && foundData is T exact)
			{
				data = exact;
				return true;
			}

			// Fallback: scan for the first payload assignable to T so requests for a base
			// type still match subclass payloads keyed by their concrete type.
			foreach (EventData candidate in eventDataDictionary.Values)
			{
				if (candidate is T match)
				{
					data = match;
					return true;
				}
			}

			data = default(T);
			return false;
		}

		/// <summary>
		/// Checks if a sub-payload of type <typeparamref name="T"/> exists. Honors the same
		/// inheritance fallback as <see cref="TryGet{T}"/>.
		/// </summary>
		/// <typeparam name="T">The type of event data to check for.</typeparam>
		/// <returns>True if found; otherwise, false.</returns>
		public bool Contains<T>() where T : EventData
		{
			if (eventDataDictionary.ContainsKey(typeof(T)))
			{
				return true;
			}
			foreach (EventData candidate in eventDataDictionary.Values)
			{
				if (candidate is T)
				{
					return true;
				}
			}
			return false;
		}
	}
}