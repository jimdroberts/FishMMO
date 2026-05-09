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

		/// <summary>
		/// Optional deterministic RNG carried with hit events so value providers (e.g.
		/// random damage rolls) and dispel actions stay deterministic across client and
		/// server. Null when the event has no RNG context (e.g. tick, scene-load).
		/// </summary>
		public DeterministicRNG RNG { get; set; }

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
			TargetCharacter = target != null && target.TryGetComponent(out ICharacter character)
				? character
				: null;
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
			scoped.RNG = RNG;
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
		/// Attempts to retrieve a sub-payload of type <typeparamref name="T"/>.
		/// </summary>
		/// <typeparam name="T">The type of event data to retrieve.</typeparam>
		/// <param name="data">The retrieved sub-payload, or default if not found.</param>
		/// <returns>True if found; otherwise, false.</returns>
		public bool TryGet<T>(out T data) where T : EventData
		{
			if (eventDataDictionary.TryGetValue(typeof(T), out EventData foundData))
			{
				data = foundData as T;
				return data != null;
			}
			data = default(T);
			return false;
		}

		/// <summary>
		/// Checks if a sub-payload of type <typeparamref name="T"/> exists.
		/// </summary>
		/// <typeparam name="T">The type of event data to check for.</typeparam>
		/// <returns>True if found; otherwise, false.</returns>
		public bool Contains<T>() where T : EventData
		{
			return eventDataDictionary.ContainsKey(typeof(T));
		}
	}
}