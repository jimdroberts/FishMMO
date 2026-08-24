using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Routes combat events to the one NPC that cares about them, instead of broadcasting every
	/// event to every NPC in the scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The problem this replaces.</b> Each <see cref="AggressionState"/> used to subscribe
	/// individually to the global damage, heal and kill events. That meant a single sword swing
	/// invoked one delegate per NPC in the scene — a thousand NPCs, a thousand calls, of which
	/// exactly one was for the NPC actually being hit and 999 returned immediately from a
	/// reference comparison. At a busy raid boss with a few hundred hits per second the wasted
	/// work is measured in hundreds of thousands of calls per second, all to deliver a handful of
	/// events.
	/// </para>
	/// <para>
	/// <b>What it does instead.</b> One subscription for the whole process. Damage is the hot path
	/// and is dispatched by dictionary lookup on the defender — O(1) regardless of how many NPCs
	/// exist. Heal and kill still have to consider several NPCs (a heal matters to anyone tracking
	/// either party), but they walk a plain list and skip anyone whose threat table is empty with
	/// a field read, which is what almost every NPC in a scene is at any moment.
	/// </para>
	/// <para>
	/// Server-side only. Registration happens when an NPC's aggression state is built and is
	/// released when it is destroyed.
	/// </para>
	/// </remarks>
	public static class AggressionDispatcher
	{
		/// <summary>
		/// Threat state by owning character, for O(1) damage dispatch.
		/// </summary>
		private static readonly Dictionary<ICharacter, AggressionState> statesByCharacter =
			new Dictionary<ICharacter, AggressionState>();

		/// <summary>
		/// The same states as a list, for the events that must consider more than one NPC.
		/// </summary>
		/// <remarks>
		/// Kept alongside the dictionary because iterating <c>Dictionary.Values</c> allocates an
		/// enumerator on every heal and kill, and these run inside combat.
		/// </remarks>
		private static readonly List<AggressionState> allStates = new List<AggressionState>();

		/// <summary>
		/// Scratch buffer so a handler can mutate the registry (an NPC entering combat may be
		/// despawned by the same event chain) without invalidating the iteration.
		/// </summary>
		private static readonly List<AggressionState> dispatchBuffer = new List<AggressionState>();

		/// <summary>
		/// True once the single global subscription has been taken.
		/// </summary>
		private static bool subscribed;

		/// <summary>
		/// Number of NPCs currently registered. Diagnostics.
		/// </summary>
		public static int RegisteredCount => allStates.Count;

		/// <summary>
		/// Registers an NPC's threat state to receive combat events.
		/// </summary>
		/// <param name="character">The NPC that owns the state.</param>
		/// <param name="state">The threat state.</param>
		public static void Register(ICharacter character, AggressionState state)
		{
			if (character == null || state == null || statesByCharacter.ContainsKey(character))
			{
				return;
			}

			statesByCharacter[character] = state;
			allStates.Add(state);

			EnsureSubscribed();
		}

		/// <summary>
		/// Stops routing events to an NPC's threat state.
		/// </summary>
		/// <param name="character">The NPC that owns the state.</param>
		public static void Unregister(ICharacter character)
		{
			if (character == null || !statesByCharacter.TryGetValue(character, out AggressionState state))
			{
				return;
			}

			statesByCharacter.Remove(character);
			allStates.Remove(state);

			/* The subscription is deliberately NOT released when the last NPC unregisters. Scenes
			 * empty and refill constantly, and churning a static event subscription on that cycle
			 * is both pointless and a source of ordering bugs. The handlers are no-ops with an
			 * empty registry. */
		}

		/// <summary>
		/// Drops all registrations. Call on server shutdown.
		/// </summary>
		public static void Clear()
		{
			statesByCharacter.Clear();
			allStates.Clear();
			dispatchBuffer.Clear();
		}

		/// <summary>
		/// Takes the single global subscription, once.
		/// </summary>
		private static void EnsureSubscribed()
		{
			if (subscribed)
			{
				return;
			}

			ICharacterDamageController.OnDamaged += OnCharacterDamaged;
			ICharacterDamageController.OnHealed += OnCharacterHealed;
			ICharacterDamageController.OnKilled += OnCharacterKilled;

			subscribed = true;
		}

		/// <summary>
		/// Delivers a damage event to the NPC that took it, if it is one.
		/// </summary>
		/// <param name="attacker">The character that dealt the damage.</param>
		/// <param name="defender">The character that took it.</param>
		/// <param name="amount">Damage dealt.</param>
		/// <param name="damageAttribute">Damage type.</param>
		private static void OnCharacterDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			// The hot path, and the reason this class exists: one lookup, not one call per NPC.
			if (defender == null || !statesByCharacter.TryGetValue(defender, out AggressionState state))
			{
				return;
			}

			state.HandleDamaged(attacker, amount);
		}

		/// <summary>
		/// Delivers a heal event to every NPC already tracking either party.
		/// </summary>
		/// <param name="healer">The character that healed.</param>
		/// <param name="healed">The character that was healed.</param>
		/// <param name="amount">Amount healed.</param>
		private static void OnCharacterHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healer == null)
			{
				return;
			}

			int count = BeginDispatch();
			for (int i = 0; i < count; ++i)
			{
				dispatchBuffer[i].HandleHealed(healer, healed, amount);
			}
			EndDispatch();
		}

		/// <summary>
		/// Tells every NPC tracking the victim to forget it, and clears the victim's own state.
		/// </summary>
		/// <param name="killer">The killer.</param>
		/// <param name="victim">The character that died.</param>
		private static void OnCharacterKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null)
			{
				return;
			}

			int count = BeginDispatch();
			for (int i = 0; i < count; ++i)
			{
				dispatchBuffer[i].HandleKilled(victim);
			}
			EndDispatch();
		}

		/// <summary>
		/// Snapshots the states that could care about a non-damage event.
		/// </summary>
		/// <remarks>
		/// Skips NPCs with an empty threat table, which is the overwhelming majority at any moment
		/// — a field read instead of a delegate invocation and a handler frame. Copying into a
		/// buffer also means a handler is free to register or unregister without corrupting the
		/// walk, which happens whenever an event pulls an NPC into combat or kills it.
		/// </remarks>
		/// <returns>The number of entries in <see cref="dispatchBuffer"/>.</returns>
		private static int BeginDispatch()
		{
			dispatchBuffer.Clear();

			for (int i = 0; i < allStates.Count; ++i)
			{
				AggressionState state = allStates[i];
				if (state != null && state.HasAggression)
				{
					dispatchBuffer.Add(state);
				}
			}

			return dispatchBuffer.Count;
		}

		/// <summary>
		/// Releases the dispatch buffer.
		/// </summary>
		private static void EndDispatch()
		{
			dispatchBuffer.Clear();
		}
	}
}
