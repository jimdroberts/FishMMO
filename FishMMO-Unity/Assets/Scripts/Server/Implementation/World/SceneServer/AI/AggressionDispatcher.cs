using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
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
	/// exist. Heal and kill have to consider several NPCs (a heal matters to anyone tracking
	/// either party), and they are dispatched through a reverse index — character ID to the NPCs
	/// whose tables track it — so they cost the number of NPCs actually involved, not the number
	/// registered.
	/// </para>
	/// <para>
	/// The index replaced a walk over every registered state. That list spans every scene instance
	/// on the process and every pooled, inactive NPC, and heal events are frequent — every
	/// heal-over-time tick, heal action and consumable raises one — so two thousand registered NPCs
	/// and a hundred HoT ticks a second was two hundred thousand iterations a second to find the
	/// handful of NPCs that cared. The tables themselves keep the index exact through
	/// <see cref="AggressionController.EntryAdded"/> and <see cref="AggressionController.EntryRemoved"/>.
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

		/* There is deliberately no list of every registered state any more. It existed for the
		 * heal and kill walks, which now go through the reverse index below, and its removal was a
		 * linear search: unloading a 500-NPC scene cost about a million comparisons. */

		/// <summary>
		/// For each character ID, the NPC states whose threat tables currently hold an entry for it.
		/// </summary>
		/// <remarks>
		/// The reverse of every table at once, maintained by the tables' own add and remove hooks.
		/// Lists are small — the number of NPCs one character is fighting — so membership tests and
		/// removal are linear scans of a handful of entries. Emptied lists go back to
		/// <see cref="trackerListPool"/> rather than to the garbage collector, because entries come
		/// and go with every engagement.
		/// </remarks>
		private static readonly Dictionary<long, List<AggressionState>> trackersByCharacter =
			new Dictionary<long, List<AggressionState>>();

		/// <summary>Recycled lists for <see cref="trackersByCharacter"/>.</summary>
		private static readonly Stack<List<AggressionState>> trackerListPool = new Stack<List<AggressionState>>();

		/// <summary>
		/// Scratch buffer so a handler can mutate the registry or the index (an NPC entering combat
		/// may be despawned by the same event chain, and forgetting a victim edits the very list
		/// being dispatched from) without invalidating the iteration.
		/// </summary>
		private static readonly List<AggressionState> dispatchBuffer = new List<AggressionState>();

		/// <summary>Each linked pet's owner.</summary>
		private static readonly Dictionary<ICharacter, ICharacter> ownerByPet =
			new Dictionary<ICharacter, ICharacter>();

		/// <summary>Each owner's linked pets.</summary>
		private static readonly Dictionary<ICharacter, List<ICharacter>> petsByOwner =
			new Dictionary<ICharacter, List<ICharacter>>();

		/// <summary>
		/// True once the single global subscription has been taken.
		/// </summary>
		private static bool subscribed;

		/// <summary>
		/// True once <see cref="Pet.OnPetOwnerChanged"/> is being followed.
		/// </summary>
		private static bool petLinksTracked;

		/// <summary>
		/// Number of NPCs currently registered. Diagnostics.
		/// </summary>
		public static int RegisteredCount => statesByCharacter.Count;

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

			EnsureSubscribed();
		}

		/// <summary>
		/// Stops routing events to an NPC's threat state.
		/// </summary>
		/// <remarks>
		/// The caller empties the state's table first (<see cref="AggressionState.Destroy"/> does),
		/// which takes it out of the reverse index through the table's own remove hook. Anything
		/// still indexed is swept out here as well, so an unregistered state can never be handed a
		/// heal or a kill.
		/// </remarks>
		/// <param name="character">The NPC that owns the state.</param>
		public static void Unregister(ICharacter character)
		{
			if (character == null || !statesByCharacter.TryGetValue(character, out AggressionState state))
			{
				return;
			}

			statesByCharacter.Remove(character);

			if (state.Controller != null && state.Controller.HasAggression)
			{
				foreach (KeyValuePair<long, AggressionEntry> pair in state.Controller.Table)
				{
					UntrackCharacter(pair.Key, state);
				}
			}

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
			trackersByCharacter.Clear();
			dispatchBuffer.Clear();
			ownerByPet.Clear();
			petsByOwner.Clear();
		}

		#region Reverse index

		/// <summary>
		/// Records that <paramref name="state"/>'s table now tracks <paramref name="characterId"/>.
		/// Called by the table's <see cref="AggressionController.EntryAdded"/> hook.
		/// </summary>
		/// <param name="characterId">The character the table started tracking.</param>
		/// <param name="state">The NPC state that owns the table.</param>
		internal static void TrackCharacter(long characterId, AggressionState state)
		{
			if (!IsRegistered(state))
			{
				// An unregistered state receives no events, so it has nothing to be found for.
				return;
			}

			if (!trackersByCharacter.TryGetValue(characterId, out List<AggressionState> trackers))
			{
				trackers = trackerListPool.Count > 0 ? trackerListPool.Pop() : new List<AggressionState>(2);
				trackersByCharacter[characterId] = trackers;
			}
			if (!trackers.Contains(state))
			{
				trackers.Add(state);
			}
		}

		/// <summary>
		/// True when <paramref name="state"/> is the state registered for its character.
		/// </summary>
		private static bool IsRegistered(AggressionState state)
		{
			return state != null &&
				state.Character != null &&
				statesByCharacter.TryGetValue(state.Character, out AggressionState registered) &&
				ReferenceEquals(registered, state);
		}

		/// <summary>
		/// Records that <paramref name="state"/>'s table no longer tracks
		/// <paramref name="characterId"/>. Called by the table's
		/// <see cref="AggressionController.EntryRemoved"/> hook.
		/// </summary>
		/// <param name="characterId">The character the table stopped tracking.</param>
		/// <param name="state">The NPC state that owns the table.</param>
		internal static void UntrackCharacter(long characterId, AggressionState state)
		{
			if (!trackersByCharacter.TryGetValue(characterId, out List<AggressionState> trackers))
			{
				return;
			}

			int index = trackers.IndexOf(state);
			if (index < 0)
			{
				return;
			}

			int last = trackers.Count - 1;
			trackers[index] = trackers[last];
			trackers.RemoveAt(last);

			if (trackers.Count == 0)
			{
				trackersByCharacter.Remove(characterId);
				trackerListPool.Push(trackers);
			}
		}

		/// <summary>
		/// Appends every registered NPC state whose table tracks <paramref name="characterId"/> to
		/// <paramref name="into"/>, skipping any already present.
		/// </summary>
		/// <remarks>
		/// Pure over the index, so it can be pinned without a scene. Appends rather than clears so
		/// a heal can gather the trackers of the healer and of the healed into one list with each
		/// NPC in it once.
		/// </remarks>
		/// <param name="characterId">The tracked character.</param>
		/// <param name="into">Receives the states.</param>
		/// <returns>How many states were appended.</returns>
		internal static int CollectTrackers(long characterId, List<AggressionState> into)
		{
			if (!trackersByCharacter.TryGetValue(characterId, out List<AggressionState> trackers))
			{
				return 0;
			}

			int start = into.Count;
			for (int i = 0; i < trackers.Count; ++i)
			{
				AggressionState state = trackers[i];
				bool present = false;
				for (int j = 0; j < start; ++j)
				{
					if (ReferenceEquals(into[j], state))
					{
						present = true;
						break;
					}
				}
				if (!present)
				{
					into.Add(state);
				}
			}
			return into.Count - start;
		}

		#endregion

		#region Shared threat

		/// <summary>
		/// Starts following <see cref="Pet.OnPetOwnerChanged"/>, so every pet/owner pair the game
		/// forms is linked here without the shared <see cref="Pet"/> type naming this class.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="AIBrainHost"/> when it starts, which is before any pet can be
		/// summoned. Idempotent, and never released, for the same reason the damage subscription
		/// is not.
		/// </remarks>
		public static void EnsurePetLinksTracked()
		{
			if (petLinksTracked)
			{
				return;
			}

			Pet.OnPetOwnerChanged += OnPetOwnerChanged;
			petLinksTracked = true;
		}

		/// <summary>
		/// Mirrors a pet's owner change into the link tables.
		/// </summary>
		/// <param name="pet">The pet whose owner changed.</param>
		/// <param name="owner">The new owner, or null when the link was cleared.</param>
		private static void OnPetOwnerChanged(Pet pet, ICharacter owner)
		{
			if (owner != null)
			{
				LinkPet(pet, owner);
			}
			else
			{
				UnlinkPet(pet);
			}
		}

		/// <summary>
		/// Declares <paramref name="pet"/> to be <paramref name="owner"/>'s pet for the purpose of
		/// threat: from now on damage to either is threat against both.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A pet and its owner are one target as far as hate is concerned. Hit the owner and the
		/// pet answers; hit the pet and the owner answers — an NPC with a brain by engaging the
		/// aggressor through its own aggression state, a player through whatever it chooses to
		/// do. Without this a pet's attackers were invisible to its owner and an owner's attackers
		/// invisible to the pet, so an NPC with a summon stood and watched it being killed.
		/// </para>
		/// <para>
		/// Any owner, not only a player: the link is keyed on characters, so an NPC that is ever
		/// given a pet gets the same behaviour with no further wiring. Kept here rather than on
		/// <c>Pet</c> because the dispatcher is where a damage event is turned into threat, and the
		/// lookup has to be one dictionary read on the hot path.
		/// </para>
		/// </remarks>
		/// <param name="pet">The pet.</param>
		/// <param name="owner">Its owner.</param>
		public static void LinkPet(ICharacter pet, ICharacter owner)
		{
			UnlinkPet(pet);

			if (pet == null || owner == null || ReferenceEquals(pet, owner))
			{
				return;
			}

			ownerByPet[pet] = owner;
			if (!petsByOwner.TryGetValue(owner, out List<ICharacter> pets))
			{
				pets = new List<ICharacter>(1);
				petsByOwner[owner] = pets;
			}
			if (!pets.Contains(pet))
			{
				pets.Add(pet);
			}
		}

		/// <summary>
		/// Removes <paramref name="pet"/>'s threat link, if it has one.
		/// </summary>
		/// <param name="pet">The pet.</param>
		public static void UnlinkPet(ICharacter pet)
		{
			if (pet == null || !ownerByPet.TryGetValue(pet, out ICharacter owner))
			{
				return;
			}

			ownerByPet.Remove(pet);
			if (petsByOwner.TryGetValue(owner, out List<ICharacter> pets))
			{
				pets.Remove(pet);
				if (pets.Count == 0)
				{
					petsByOwner.Remove(owner);
				}
			}
		}

		/// <summary>
		/// The owner a pet is linked to, if any.
		/// </summary>
		public static bool TryGetPetOwner(ICharacter pet, out ICharacter owner)
		{
			owner = null;
			return pet != null && ownerByPet.TryGetValue(pet, out owner);
		}

		/// <summary>
		/// Collects every character that shares <paramref name="defender"/>'s threat: its owner
		/// when it is a pet, and its pets when it is an owner. The attacker is never one of them,
		/// so an owner hitting its own pet, or a pet its owner, generates nothing.
		/// </summary>
		/// <remarks>Pure over the link tables, so the rule can be pinned without a scene.</remarks>
		/// <param name="defender">The character that was damaged.</param>
		/// <param name="attacker">The character that did the damage.</param>
		/// <param name="into">Receives the sharers; cleared first.</param>
		/// <returns>How many sharers were collected.</returns>
		internal static int CollectThreatSharers(ICharacter defender, ICharacter attacker, List<ICharacter> into)
		{
			into.Clear();
			if (defender == null)
			{
				return 0;
			}

			if (ownerByPet.TryGetValue(defender, out ICharacter owner) &&
				!ReferenceEquals(owner, attacker))
			{
				into.Add(owner);
			}

			if (petsByOwner.TryGetValue(defender, out List<ICharacter> pets))
			{
				for (int i = 0; i < pets.Count; ++i)
				{
					ICharacter pet = pets[i];
					if (pet != null && !ReferenceEquals(pet, attacker))
					{
						into.Add(pet);
					}
				}
			}

			return into.Count;
		}

		/// <summary>
		/// Collects every character a hit by <paramref name="attacker"/> is credited to: the
		/// attacker itself, and its owner when the attacker is a pet.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The other half of the rule. A pet's hits are its owner's hits: an NPC struck by a
		/// summon hates the summoner as much as the summon, so a player cannot stand innocent
		/// behind a pet, and cannot cycle pets to shed the threat each one earned. The owner is
		/// credited the full amount, not a share — from the NPC's side there is one enemy here.
		/// </para>
		/// <para>Pure over the link tables, so the rule can be pinned without a scene.</para>
		/// </remarks>
		/// <param name="attacker">The character that did the damage.</param>
		/// <param name="into">Receives the sources; cleared first.</param>
		/// <returns>How many sources were collected.</returns>
		internal static int CollectThreatSources(ICharacter attacker, List<ICharacter> into)
		{
			into.Clear();
			if (attacker == null)
			{
				return 0;
			}

			into.Add(attacker);
			if (ownerByPet.TryGetValue(attacker, out ICharacter owner) &&
				owner != null &&
				!ReferenceEquals(owner, attacker))
			{
				into.Add(owner);
			}

			return into.Count;
		}

		/// <summary>
		/// Delivers a hit's threat with pets resolved on both sides: every source of the hit
		/// (the attacker and, for a pet, its owner) is recorded against every recipient (the
		/// defender and, for a pet or an owner, the characters linked to it).
		/// </summary>
		/// <remarks>
		/// Reached only when a link exists, which is only ever the case around a pet. Fresh
		/// lists rather than shared buffers, because a recipient's first threat can start a
		/// fight synchronously and nothing here should assume that cannot deal damage in turn.
		/// A source is never recorded against itself.
		/// </remarks>
		private static void ShareThreat(ICharacter attacker, ICharacter defender, int amount)
		{
			List<ICharacter> sources = new List<ICharacter>(2);
			int sourceCount = CollectThreatSources(attacker, sources);

			List<ICharacter> recipients = new List<ICharacter>(3) { defender };
			List<ICharacter> sharers = new List<ICharacter>(2);
			int sharerCount = CollectThreatSharers(defender, attacker, sharers);
			for (int i = 0; i < sharerCount; ++i)
			{
				recipients.Add(sharers[i]);
			}

			for (int r = 0; r < recipients.Count; ++r)
			{
				if (!statesByCharacter.TryGetValue(recipients[r], out AggressionState state))
				{
					continue;
				}
				for (int s = 0; s < sourceCount; ++s)
				{
					if (!ReferenceEquals(sources[s], recipients[r]))
					{
						state.HandleDamaged(sources[s], amount);
					}
				}
			}
		}

		/// <summary>
		/// Finds the registered NPC holding the most threat against <paramref name="subject"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// "Whatever is attacking me and hates me most." An NPC's threat toward a character
		/// accumulates from that character's hits on it — and, since a pet's hits are credited to
		/// its owner, from the pet's hits too — so the highest entry is the thing the subject and
		/// its pet have attacked the most. That is the pet attack command's third choice, after
		/// the owner's pinned and hovered targets.
		/// </para>
		/// <para>
		/// Reads the reverse index, so it considers only the NPCs actually tracking the subject.
		/// The caller's <paramref name="accept"/> applies its own rules — alive, in range, hostile —
		/// before an entry can win, so a distant grudge does not send the pet across the map.
		/// </para>
		/// </remarks>
		/// <param name="subject">The character the threat is measured against.</param>
		/// <param name="accept">Optional rule an NPC must pass to be considered.</param>
		/// <param name="best">The NPC with the most threat against the subject.</param>
		/// <returns>True when one was found.</returns>
		public static bool TryFindHighestThreatAgainst(ICharacter subject, System.Predicate<ICharacter> accept, out ICharacter best)
		{
			best = null;
			if (subject == null ||
				!trackersByCharacter.TryGetValue(subject.ID, out List<AggressionState> trackers))
			{
				return false;
			}

			float bestPoints = 0f;
			for (int i = 0; i < trackers.Count; ++i)
			{
				AggressionState state = trackers[i];
				if (state == null || !state.HasAggression)
				{
					continue;
				}

				ICharacter candidate = state.Character;
				if (candidate == null || ReferenceEquals(candidate, subject))
				{
					continue;
				}

				float points = state.Controller.GetPoints(subject.ID);
				if (points <= 0f || points <= bestPoints)
				{
					continue;
				}
				if (accept != null && !accept(candidate))
				{
					continue;
				}

				bestPoints = points;
				best = candidate;
			}

			return best != null;
		}

		#endregion

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
			if (defender == null)
			{
				return;
			}

			/* Pets on either side of the hit. When nothing is linked — the ordinary NPC-versus-
			 * player hit — this is two dictionary misses and the single delivery below. */
			if (ownerByPet.Count > 0 &&
				(ownerByPet.ContainsKey(attacker ?? defender) || ownerByPet.ContainsKey(defender) || petsByOwner.ContainsKey(defender)))
			{
				ShareThreat(attacker, defender, amount);
				return;
			}

			if (statesByCharacter.TryGetValue(defender, out AggressionState state))
			{
				state.HandleDamaged(attacker, amount);
			}
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

			/* Only an NPC tracking the healed or the healer can turn this heal into threat —
			 * HandleHealed applies exactly that rule — so those are the only ones asked. */
			dispatchBuffer.Clear();
			if (healed != null)
			{
				CollectTrackers(healed.ID, dispatchBuffer);
			}
			CollectTrackers(healer.ID, dispatchBuffer);

			int count = dispatchBuffer.Count;
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

			/* Copied out of the index before dispatch: forgetting the victim removes each state
			 * from the very list this came from. */
			dispatchBuffer.Clear();
			int count = CollectTrackers(victim.ID, dispatchBuffer);
			for (int i = 0; i < count; ++i)
			{
				dispatchBuffer[i].HandleKilled(victim);
			}
			EndDispatch();
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
