using FishNet.Connection;
#if UNITY_SERVER
using FishNet.Broadcast;
#endif
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls faction reputation, alliance grouping, and relationship queries for a character.
	/// Handles network synchronization of faction standings via FishNet broadcasts and payload serialization.
	/// </summary>
	public class FactionController : CharacterBehaviour, IFactionController
	{
#if UNITY_SERVER
		/// <summary>
		/// Dirty faction template IDs pending a network flush.
		/// </summary>
		private readonly HashSet<int> dirtyFactionTemplateIDs = new HashSet<int>();

		/// <summary>
		/// Last exact faction values the OWNER has been given, by template ID.
		/// </summary>
		/// <remarks>
		/// Seeded by <see cref="WritePayload"/> with what the owner's spawn payload carried, so the
		/// first tick after a spawn does not re-send a roster the owner is already holding. See
		/// <see cref="FlushDirtyFactionUpdates"/>.
		/// </remarks>
		private readonly Dictionary<int, int> lastSentFactionValues = new Dictionary<int, int>();

		/// <summary>
		/// Last standing SIGN observers have been given, by template ID. See
		/// <see cref="SendObserverFactionUpdates"/> for why observers get a sign and not a value.
		/// </summary>
		private readonly Dictionary<int, int> lastSentObserverSigns = new Dictionary<int, int>();

		/// <summary>
		/// Whether this controller currently holds a <c>TimeManager.OnTick</c> subscription.
		/// </summary>
		/// <remarks>
		/// The subscription belongs to the dirty set, not to the lifetime: it is taken when
		/// something is marked dirty and dropped as soon as the set drains. A settled character —
		/// which is nearly every character, nearly all of the time — then costs no per-tick
		/// delegate at all, where before every server-side character invoked an
		/// immediately-returning flush thirty times a second.
		/// </remarks>
		private bool flushTickSubscribed;
#endif

		/// <summary>
		/// Dictionary of all factions for this character, keyed by template ID.
		/// Holds reputation/standing values for each faction.
		/// </summary>
		private Dictionary<int, Faction> factions = new Dictionary<int, Faction>();

		/// <summary>
		/// Dictionary of allied factions (positive standing), keyed by template ID.
		/// </summary>
		private Dictionary<int, Faction> allied = new Dictionary<int, Faction>();

		/// <summary>
		/// Dictionary of neutral factions (zero standing), keyed by template ID.
		/// </summary>
		private Dictionary<int, Faction> neutral = new Dictionary<int, Faction>();

		/// <summary>
		/// Dictionary of hostile factions (negative standing), keyed by template ID.
		/// </summary>
		private Dictionary<int, Faction> hostile = new Dictionary<int, Faction>();

		/// <summary>
		/// If true, this character is aggressive and will treat others as enemies regardless of faction standing.
		/// </summary>
		[SerializeField]
		private bool isAggressive = false;

		[Header("ECA - Faction")]
		[Tooltip("Triggers invoked when a faction standing changes for this character.")]
		[SerializeField]
		private List<Trigger> onFactionChangeTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnFactionChangeTriggers => onFactionChangeTriggers;

		/// <summary>
		/// Gets or sets whether the character is aggressive (treats others as enemies).
		/// </summary>
		public bool IsAggressive { get { return isAggressive; } set { isAggressive = value; } }

		/// <summary>
		/// Public accessor for all factions and their standing values.
		/// </summary>
		public Dictionary<int, Faction> Factions { get { return factions; } }

		/// <summary>
		/// Public accessor for allied factions.
		/// </summary>
		public Dictionary<int, Faction> Allied { get { return allied; } }

		/// <summary>
		/// Public accessor for neutral factions.
		/// </summary>
		public Dictionary<int, Faction> Neutral { get { return neutral; } }

		/// <summary>
		/// Public accessor for hostile factions.
		/// </summary>
		public Dictionary<int, Faction> Hostile { get { return hostile; } }

		/// <summary>
		/// The race template ID associated with this character, used for initial faction setup.
		/// </summary>
		[SerializeField, TemplateReference(typeof(RaceTemplate))]
		private int raceTemplateID;
		private RaceTemplate cachedRaceTemplate;
		/// <summary>
		/// Gets the race template for this character.
		/// </summary>
		public RaceTemplate RaceTemplate
		{
			get
			{
				if (cachedRaceTemplate == null && raceTemplateID != 0)
				{
					cachedRaceTemplate = RaceTemplate.Get<RaceTemplate>(raceTemplateID);
				}
				return cachedRaceTemplate;
			}
		}

		/// <summary>
		/// Replaces the race template this character derives its initial factions from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Spawn-time only. The race template is read lazily — nothing is derived from it during
		/// initialisation — so assigning it before <c>ServerManager.Spawn</c> takes effect cleanly.
		/// Calling it on an already-spawned character would leave the alliance tables it has
		/// already built disagreeing with its new race, which is why there is no plain setter.
		/// </para>
		/// <para>
		/// Exists so one NPC prefab can be hostile at one spawner and neutral at another without
		/// duplicating the prefab — and a duplicated prefab is a second object-pool bucket and a
		/// second fixed slice of the map's memory budget.
		/// </para>
		/// </remarks>
		/// <param name="raceTemplate">The race template to adopt. Ignored when null.</param>
		public void SetRaceTemplateOnSpawn(RaceTemplate raceTemplate)
		{
			if (raceTemplate == null)
			{
				return;
			}

			raceTemplateID = raceTemplate.ID;
			cachedRaceTemplate = raceTemplate;

			// A spawner override arrives before the spawn payload is written, so the new race
			// travels with the object and the derived roster below is rebuilt from it.
			if (FactionsAreTemplateDerived)
			{
				InitializeTemplateFactions();
			}
		}

		/// <summary>
		/// True while this character's standings are DERIVED from
		/// <see cref="RaceTemplate"/>.<c>InitialFaction</c> rather than owned as mutable state.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the difference between the two kinds of character in the faction system. A player
		/// carries standings that change during play and are persisted per character, so they are
		/// authored by the server and must travel. An NPC never changes faction —
		/// <see cref="SetFaction"/> and <see cref="Add"/> refuse to move an NPC's standing on
		/// purpose — so its roster is a pure function of immutable template data that every peer
		/// already holds, and sending it is both wasted bytes and a second source of truth.
		/// </para>
		/// <para>
		/// A pet is the exception that proves the rule: <see cref="CopyFrom"/> installs its owner's
		/// standings, which are not derivable from any template, so copying clears this flag and the
		/// roster travels like a player's.
		/// </para>
		/// </remarks>
		public bool FactionsAreTemplateDerived { get; private set; }

		/// <summary>
		/// Rebuilds the faction roster from <see cref="RaceTemplate"/>'s initial faction, exactly as
		/// character creation seeds a new player: allied at maximum, neutral at zero, hostile at
		/// minimum.
		/// </summary>
		/// <remarks>
		/// Runs on every peer, from <see cref="InitializeOnce"/> on the server and from
		/// <see cref="ReadPayload"/> on a client, so both ends compute the same table from the same
		/// immutable asset instead of one end being told. The values mirror
		/// <c>CharacterCreateSystem.BuildStartingFactionEntries</c>; if those diverge, an NPC and a
		/// freshly created player of the same race would disagree about the same faction.
		/// </remarks>
		public void InitializeTemplateFactions()
		{
			factions.Clear();
			allied.Clear();
			neutral.Clear();
			hostile.Clear();

			FactionsAreTemplateDerived = true;

			FactionTemplate initialFaction = RaceTemplate?.InitialFaction;
			if (initialFaction == null)
			{
				return;
			}

			if (initialFaction.DefaultAllied != null)
			{
				foreach (FactionTemplate faction in initialFaction.DefaultAllied)
				{
					if (faction != null)
					{
						ApplyFactionValue(faction.ID, FactionTemplate.Maximum);
					}
				}
			}
			if (initialFaction.DefaultNeutral != null)
			{
				foreach (FactionTemplate faction in initialFaction.DefaultNeutral)
				{
					if (faction != null)
					{
						ApplyFactionValue(faction.ID, 0);
					}
				}
			}
			if (initialFaction.DefaultHostile != null)
			{
				foreach (FactionTemplate faction in initialFaction.DefaultHostile)
				{
					if (faction != null)
					{
						ApplyFactionValue(faction.ID, FactionTemplate.Minimum);
					}
				}
			}
		}

		/// <summary>
		/// Seeds an NPC's derived roster as soon as the character is assembled, before any network
		/// activity, so the server holds the same table a client will build for itself.
		/// </summary>
		public override void InitializeOnce()
		{
			base.InitializeOnce();

			if (Character as NPC != null)
			{
				InitializeTemplateFactions();
			}
		}

#if UNITY_SERVER
		/// <summary>
		/// Marks the roster dirty so any genuine pre-spawn mutation is still flushed.
		/// </summary>
		/// <remarks>
		/// No tick subscription is taken here. <see cref="MarkAllFactionsDirty"/> takes one if it
		/// actually marks anything, and in the ordinary case it marks nothing that survives the
		/// first flush: the values were written into the spawn payload moments later and
		/// <see cref="WritePayload"/> records them as sent. See <see cref="flushTickSubscribed"/>.
		/// </remarks>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			MarkAllFactionsDirty();
		}

		/// <summary>
		/// Unsubscribes from network tick updates.
		/// </summary>
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			UnsubscribeFromFlushTick();
		}

		/// <summary>
		/// Takes the flush tick subscription if it is not already held.
		/// </summary>
		private void SubscribeToFlushTick()
		{
			if (flushTickSubscribed)
			{
				return;
			}

			/* Through the null-safe NetworkObject accessor: base.TimeManager dereferences
			 * _networkObjectCache, which is null on a controller that has never been spawned — a
			 * pooled instance before its first spawn, or a test. */
			if (base.NetworkObject == null || base.TimeManager == null)
			{
				return;
			}

			base.TimeManager.OnTick += TimeManager_OnTick;
			flushTickSubscribed = true;
		}

		/// <summary>
		/// Drops the flush tick subscription if it is held. Idempotent.
		/// </summary>
		private void UnsubscribeFromFlushTick()
		{
			if (!flushTickSubscribed)
			{
				return;
			}

			flushTickSubscribed = false;

			if (base.NetworkObject != null && base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Called on network tick to flush dirty faction updates, then hands the subscription back
		/// once there is nothing left to flush.
		/// </summary>
		private void TimeManager_OnTick()
		{
			FlushDirtyFactionUpdates();

			/* Only when the set is genuinely empty. FlushDirtyFactionUpdates leaves it populated
			 * when it bails out on not being spawned yet, and dropping the subscription there would
			 * strand those entries until the next mutation. */
			if (dirtyFactionTemplateIDs.Count == 0)
			{
				UnsubscribeFromFlushTick();
			}
		}

		/// <summary>
		/// Marks all known faction entries as dirty for next flush.
		/// </summary>
		private void MarkAllFactionsDirty()
		{
			/* A derived roster is never sent: every peer builds it from the same template. Marking
			 * it dirty here would push the whole table to the owner and to every observer, reliably,
			 * on the first tick after each NPC spawns. */
			if (FactionsAreTemplateDerived)
			{
				return;
			}

			foreach (Faction faction in Factions.Values)
			{
				if (faction?.Template != null)
				{
					dirtyFactionTemplateIDs.Add(faction.Template.ID);
				}
			}

			if (dirtyFactionTemplateIDs.Count > 0)
			{
				SubscribeToFlushTick();
			}
		}

		/// <summary>
		/// Marks a faction template ID as dirty for the next server flush.
		/// </summary>
		private void MarkFactionDirty(int templateID)
		{
			if (FactionsAreTemplateDerived)
			{
				return;
			}

			if (templateID > 0)
			{
				dirtyFactionTemplateIDs.Add(templateID);
				SubscribeToFlushTick();
			}
		}

		/// <summary>
		/// Flushes dirty faction state to the owner and to observers, on the two different terms the
		/// two audiences actually consume.
		/// </summary>
		/// <remarks>
		/// The owner is sent the EXACT value whenever the exact value moved; observers are sent the
		/// SIGN, and only when the sign crossed. The two therefore have their own baselines — a kill
		/// that nudges a standing from 4000 to 4100 is an owner message and no observer message at
		/// all. See <see cref="SendObserverFactionUpdates"/>.
		/// </remarks>
		private void FlushDirtyFactionUpdates()
		{
			if (!base.IsServerStarted || !base.IsSpawned || dirtyFactionTemplateIDs.Count == 0)
			{
				return;
			}

			List<FactionUpdateBroadcast> ownerUpdates = null;
			List<FactionUpdateBroadcast> observerUpdates = null;

			foreach (int templateID in dirtyFactionTemplateIDs)
			{
				if (!factions.TryGetValue(templateID, out Faction faction) || faction?.Template == null)
				{
					continue;
				}

				int current = faction.Value;

				// The owner's channel: full precision, on any movement.
				if (!lastSentFactionValues.TryGetValue(templateID, out int lastValue) || lastValue != current)
				{
					ownerUpdates ??= new List<FactionUpdateBroadcast>(dirtyFactionTemplateIDs.Count);
					ownerUpdates.Add(new FactionUpdateBroadcast()
					{
						TemplateID = templateID,
						NewValue = current,
					});

					lastSentFactionValues[templateID] = current;
				}

				// The observers' channel: the sign, on a crossing.
				int sign = StandingSign(current);
				if (ObserverNeedsSignUpdate(lastSentObserverSigns.TryGetValue(templateID, out int lastSign), lastSign, current))
				{
					observerUpdates ??= new List<FactionUpdateBroadcast>(dirtyFactionTemplateIDs.Count);
					observerUpdates.Add(new FactionUpdateBroadcast()
					{
						TemplateID = templateID,
						// A SIGN, not a standing. See SendObserverFactionUpdates.
						NewValue = sign,
					});

					lastSentObserverSigns[templateID] = sign;
				}
			}

			dirtyFactionTemplateIDs.Clear();

			SendOwnerFactionUpdates(ownerUpdates);
			SendObserverFactionUpdates(observerUpdates);
		}

		/// <summary>
		/// Sends faction updates to owner connection.
		/// </summary>
		private void SendOwnerFactionUpdates(List<FactionUpdateBroadcast> updates)
		{
			if (updates == null || updates.Count == 0)
			{
				return;
			}

			if (updates.Count == 1)
			{
				BroadcastToOwnerOnly(Character, updates[0], Channel.Reliable);
			}
			else
			{
				BroadcastToOwnerOnly(Character, new FactionUpdateMultipleBroadcast()
				{
					Factions = updates.ToArray(),
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Sends observers the sign transitions of this character's standings.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A sign, not a value.</b> <c>NewValue</c> on this channel carries -1, 0 or +1 — the
		/// three-state standing and nothing more. That is the whole of what an observer consumes: the
		/// only observer-side reader of a peer's factions is <see cref="GetAllianceLevel"/>, which
		/// walks the VIEWER's own hostile set and asks whether the peer's standing with that faction
		/// is greater than zero. <c>UITKFactions</c> renders only the local character's rows, and
		/// refuses any other character's by identity.
		/// </para>
		/// <para>
		/// <b>Why that matters.</b> Every mob kill credits standing (<c>CharacterDamageController</c>
		/// → <c>AdjustFaction</c>), so the exact integer used to go out reliably to every observer on
		/// every reputation tick to be reduced to a boolean on arrival — and it handed a
		/// packet-inspecting client a peer's precise progression. Sending the sign means a message
		/// goes out only when a standing actually crosses zero, which is when the relationship it
		/// describes changes. Full precision stays on the owner-only
		/// <c>FactionUpdate</c>/<c>FactionUpdateMultipleBroadcast</c>.
		/// </para>
		/// <para>
		/// One serialisation for the whole set: the private per-connection loop this replaced wrote
		/// the struct again for every recipient, where <c>ServerManager.Broadcast(HashSet, ...)</c>
		/// writes it once and reuses the <c>ArraySegment</c>. <see cref="ObserverBroadcastScope"/>
		/// also owns the defensive copy that makes FishNet's set-mutating <c>BroadcastExcept</c> safe
		/// against <c>NetworkObject.Observers</c>.
		/// </para>
		/// </remarks>
		private void SendObserverFactionUpdates(List<FactionUpdateBroadcast> updates)
		{
			if (Character == null || updates == null || updates.Count == 0)
			{
				return;
			}

			CharacterObserverFactionUpdateBroadcast observerBroadcast = new CharacterObserverFactionUpdateBroadcast()
			{
				CharacterID = Character.ID,
				Factions = updates.ToArray(),
			};
			ObserverBroadcastScope.BroadcastToObserversExceptOwner(base.NetworkObject, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Broadcasts payload to owner only.
		/// </summary>
		private static void BroadcastToOwnerOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null)
			{
				return;
			}

			NetworkConnection owner = character.Owner;
			if (owner != null && owner.IsActive)
			{
				owner.Broadcast(broadcast, true, channel);
			}
		}
#endif

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character is started on the client. Registers broadcast listeners for faction updates.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<FactionUpdateBroadcast>(OnClientFactionUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<FactionUpdateMultipleBroadcast>(OnClientFactionUpdateMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverFactionUpdateBroadcast>(OnClientCharacterObserverFactionUpdateBroadcastReceived);
		}

		/// <summary>
		/// Called when the character is stopped on the client. Unregisters faction update listeners.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<FactionUpdateBroadcast>(OnClientFactionUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<FactionUpdateMultipleBroadcast>(OnClientFactionUpdateMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverFactionUpdateBroadcast>(OnClientCharacterObserverFactionUpdateBroadcastReceived);
			}
		}

		/// <summary>
		/// Resolves a target faction controller from the client character cache.
		/// </summary>
		private static bool TryGetCachedFactionController(long characterID, out IFactionController factionController)
		{
			factionController = null;
			if (characterID <= 0)
			{
				return false;
			}

			if (!BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) ||
				character == null)
			{
				return false;
			}

			return character.TryGet(out factionController);
		}

		/// <summary>
		/// Server sent an faction update broadcast.
		/// </summary>
		private void OnClientFactionUpdateBroadcastReceived(FactionUpdateBroadcast msg, Channel channel)
		{
			FactionTemplate template = FactionTemplate.Get<FactionTemplate>(msg.TemplateID);
			if (template != null)
			{
				SetFaction(template.ID, msg.NewValue);
			}
			else
			{
				Log.Debug("FactionController", $"Faction Template not found while Updating: {msg.TemplateID}");
			}
		}

		/// <summary>
		/// Server sent a multiple faction update broadcast.
		/// </summary>
		private void OnClientFactionUpdateMultipleBroadcastReceived(FactionUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (FactionUpdateBroadcast subMsg in msg.Factions)
			{
				OnClientFactionUpdateBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// Server sent observer-targeted faction updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverFactionUpdateBroadcastReceived(CharacterObserverFactionUpdateBroadcast msg, Channel channel)
		{
			if (!TryGetCachedFactionController(msg.CharacterID, out IFactionController factionController) ||
				msg.Factions == null)
			{
				return;
			}

			foreach (FactionUpdateBroadcast subMsg in msg.Factions)
			{
				/* NewValue on the OBSERVER channel is a SIGN, not a standing — see
				 * SendObserverFactionUpdates. Passed through StandingSign rather than trusted: it is
				 * idempotent on a well-formed message and normalises a malformed one, so this peer
				 * can never end up holding a number that reads as somebody's real reputation. */
				factionController.SetFaction(subMsg.TemplateID, StandingSign(subMsg.NewValue), true);
			}
		}
#endif

		/// <summary>
		/// Resets the faction state for this character, clearing all standing data.
		/// </summary>
		/// <param name="asServer">Whether the reset is being performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Factions.Clear();
			Allied.Clear();
			Neutral.Clear();
			Hostile.Clear();
			FactionsAreTemplateDerived = false;

#if UNITY_SERVER
			/* The subscription first: it is keyed off the dirty set, and clearing the set without
			 * dropping it would leave a pooled instance ticking a flush that can never do anything. */
			UnsubscribeFromFlushTick();

			dirtyFactionTemplateIDs.Clear();
			lastSentFactionValues.Clear();
			lastSentObserverSigns.Clear();
#endif
		}

		/// <summary>
		/// Reads the faction state from the network payload and applies each faction standing.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		/// <summary>
		/// Width of the byte count that frames this behaviour's spawn payload.
		/// </summary>
		private const int FACTION_PAYLOAD_LENGTH_BYTES = 4;

		/// <summary>
		/// Upper bound on factions accepted from a spawn payload. Far above any realistic
		/// faction roster; exists so a corrupt count cannot drive an unbounded read loop.
		/// </summary>
		private const int MAX_PAYLOAD_FACTIONS = 4096;

		/// <summary>Non-derived roster shape: exact standings. Written for the owner only.</summary>
		private const byte FACTION_PAYLOAD_SHAPE_VALUES = 0;

		/// <summary>Non-derived roster shape: one sign byte per entry. Written for everyone else.</summary>
		private const byte FACTION_PAYLOAD_SHAPE_SIGNS = 1;

		/// <summary>
		/// The three-state standing an observer acts on: +1 allied, 0 neutral, -1 hostile.
		/// </summary>
		/// <remarks>
		/// The whole of what a non-owner consumes. <see cref="GetAllianceLevel"/> asks only whether a
		/// peer's standing with one of the viewer's enemies is greater than zero, and the alliance
		/// tables (<see cref="Allied"/>, <see cref="Neutral"/>, <see cref="Hostile"/>) are partitioned
		/// on the same three cases — so a copy holding nothing but the sign answers every
		/// observer-side question identically to one holding the real integer.
		/// </remarks>
		/// <param name="value">A standing.</param>
		/// <returns>-1, 0 or +1.</returns>
		public static int StandingSign(int value)
		{
			return value > 0 ? 1 : (value < 0 ? -1 : 0);
		}

		/// <summary>
		/// The <see cref="FactionAllianceLevel"/> a standing expresses: positive is
		/// <see cref="FactionAllianceLevel.Ally"/>, zero is <see cref="FactionAllianceLevel.Neutral"/>,
		/// negative is <see cref="FactionAllianceLevel.Enemy"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The same three-way partition <see cref="InsertToAllianceGroup"/> and
		/// <see cref="RemoveFromAllianceGroup"/> apply when filing a faction into
		/// <see cref="Allied"/>, <see cref="Neutral"/> or <see cref="Hostile"/>. Those two are
		/// inlined hot-path code read by <see cref="GetAllianceLevel"/> and keep their own
		/// comparisons; this method is their named, testable statement of the same rule, and
		/// <c>AllianceLevel_AgreesWithTheAllianceTablesItRepresents</c> pins the agreement rather
		/// than leaving it to be re-derived.
		/// </para>
		/// <para>
		/// Spelled out rather than folded through <see cref="StandingSign"/>: that returns
		/// +1 for allied, so <c>StandingSign(value) + 1</c> is the INVERSE mapping — it would name
		/// every ally an enemy, and the enum's own numeric order (<c>Ally</c>, <c>Neutral</c>,
		/// <c>Enemy</c>) is the display order a caller can index an array by.
		/// </para>
		/// <para>
		/// Pure and static so a UI panel can group by it without a live controller.
		/// </para>
		/// </remarks>
		/// <param name="standing">A standing value.</param>
		/// <returns>The alliance level that standing expresses.</returns>
		public static FactionAllianceLevel GetAllianceLevelForStanding(int standing)
		{
			if (standing > 0)
			{
				return FactionAllianceLevel.Ally;
			}
			if (standing < 0)
			{
				return FactionAllianceLevel.Enemy;
			}
			return FactionAllianceLevel.Neutral;
		}

		/// <summary>
		/// Encodes a standing's sign as the single unsigned byte the observer payload carries:
		/// 0 hostile, 1 neutral, 2 allied.
		/// </summary>
		/// <param name="value">A standing.</param>
		public static byte EncodeStandingSign(int value)
		{
			return (byte)(StandingSign(value) + 1);
		}

		/// <summary>
		/// Decodes an observer sign byte back to a stand-in standing of -1, 0 or +1.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Truth table: 0 → -1, 1 → 0, 2 → +1, anything else → 0.
		/// </para>
		/// <para>
		/// An out-of-range byte reads as NEUTRAL deliberately. The permissive-looking answer is the
		/// hostile one — <see cref="GetAllianceLevel"/> turns "allied with my enemy" into
		/// <c>Enemy</c> — and an unknown relationship must not flash a red marker on a stranger, the
		/// same rule <c>MapRelationshipTracker</c> documents for a character whose faction state has
		/// not arrived.
		/// </para>
		/// <para>
		/// The installed value is ±1 rather than ±<see cref="FactionTemplate.Maximum"/> on purpose: an
		/// observer holds a SIGN, and a number that looks like a real standing would invite somebody
		/// to read one out of it.
		/// </para>
		/// </remarks>
		/// <param name="encoded">The byte from the wire.</param>
		public static int DecodeStandingSign(byte encoded)
		{
			switch (encoded)
			{
				case 0: return -1;
				case 2: return 1;
				default: return 0;
			}
		}

		/// <summary>
		/// Whether observers have to be told about a standing that has moved to
		/// <paramref name="currentValue"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Truth table:
		/// <list type="bullet">
		/// <item><description>never sent → true (observers hold nothing for this faction)</description></item>
		/// <item><description>sent, sign unchanged → false (a reputation tick inside one band)</description></item>
		/// <item><description>sent, sign crossed → true</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// Pure so the rule can be tested without a NetworkManager. See
		/// <see cref="SendObserverFactionUpdates"/> for why the sign is the whole message.
		/// </para>
		/// </remarks>
		/// <param name="previouslySent">True when observers have already been given a sign for this faction.</param>
		/// <param name="previousSign">The sign they were given; meaningless when <paramref name="previouslySent"/> is false.</param>
		/// <param name="currentValue">The standing now.</param>
		public static bool ObserverNeedsSignUpdate(bool previouslySent, int previousSign, int currentValue)
		{
			return !previouslySent || previousSign != StandingSign(currentValue);
		}

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			Factions.Clear();
			Allied.Clear();
			Neutral.Clear();
			Hostile.Clear();

			/* Where this behaviour's data ends. FishNet packs every NetworkBehaviour's spawn
			 * payload into one buffer with no per-behaviour framing, so an abort here would leave
			 * every behaviour after this one reading from the wrong offset — on an NPC prefab this
			 * controller is read immediately before NPC.ReadPayload, which carries the scene-object
			 * ID a client later names to loot the corpse. The length is validated against what the
			 * reader holds first; Reader.Position is a plain field with no bounds check. */
			uint declaredLength = reader.ReadUInt32Unpacked();
			int remainingBytes = reader.Remaining;
			if (declaredLength > (uint)remainingBytes)
			{
				Log.Error("FactionController",
					$"ReadPayload: framed length {declaredLength} exceeds the {remainingBytes} bytes remaining in " +
					"the spawn payload. The stream cannot be resynchronised; discarding the remainder.");
				reader.Position += remainingBytes;
				return;
			}
			int factionBlockLength = (int)declaredLength;
			int factionBlockEnd = reader.Position + factionBlockLength;

			/* Race id (unpacked int, 4 bytes) and the derived flag (1 byte) are the only fields read
			 * unconditionally; a frame shorter than that would read them from the next behaviour's
			 * bytes. Same guard as CharacterAttributeController.
			 *
			 * Exactly five, not more: a DERIVED roster — every ordinary NPC in the world, which is
			 * most of the objects that carry this block — writes nothing after the flag, so its whole
			 * frame IS five bytes. The shape byte and the count are read only in the non-derived
			 * branch and are bounded there against the end of the frame. */
			if (factionBlockLength < 5)
			{
				Log.Error("FactionController",
					$"ReadPayload: framed block of {factionBlockLength} bytes is too short to hold a faction payload. Skipping the block.");
				reader.Position = factionBlockEnd;
				return;
			}

			/* The race, before anything derived from it.
			 *
			 * A prefab carries a serialized race, but a spawner may override it
			 * (NPCSpawnableSettings.FactionOverride → SetRaceTemplateOnSpawn) so one prefab can be
			 * hostile at one spawn point and neutral at another. That override lives on the server
			 * only, so a client that trusted its prefab judged those NPCs by the wrong race — the
			 * one field of "default configuration" that genuinely has to travel. */
			int payloadRaceTemplateID = reader.ReadInt32Unpacked();
			if (payloadRaceTemplateID != 0 && payloadRaceTemplateID != raceTemplateID)
			{
				raceTemplateID = payloadRaceTemplateID;
				cachedRaceTemplate = null;
			}

			bool derivedRoster = reader.ReadBoolean();
			if (derivedRoster)
			{
				/* Nothing else is on the wire: the roster is a function of the race template, which
				 * is immutable data this peer already holds. See FactionsAreTemplateDerived. */
				InitializeTemplateFactions();
			}
			else
			{
				FactionsAreTemplateDerived = false;

				/* Shape byte (1) and count (at least 1) come next. Bounded against the end of the
				 * frame rather than assumed, for the same reason the prefix above is. */
				if (reader.Position + 2 > factionBlockEnd)
				{
					Log.Error("FactionController",
						$"ReadPayload: framed block of {factionBlockLength} bytes has no room for an owned roster's " +
						"shape and count. Skipping the block.");
					reader.Position = factionBlockEnd;
					return;
				}

				/* The shape is on the wire so the reader never has to guess which one it is holding,
				 * the way BuffController's payload does it. See WritePayload for the two shapes. */
				byte rosterShape = reader.ReadUInt8Unpacked();
				if (rosterShape != FACTION_PAYLOAD_SHAPE_VALUES &&
					rosterShape != FACTION_PAYLOAD_SHAPE_SIGNS)
				{
					/* The entry width follows from the shape, so an unrecognised shape means the rest
					 * of this block cannot be walked at all. Seek to the frame instead of guessing. */
					Log.Error("FactionController",
						$"ReadPayload: unrecognised roster shape {rosterShape}. Skipping the block.");
					reader.Position = factionBlockEnd;
					return;
				}
				bool exactValues = rosterShape == FACTION_PAYLOAD_SHAPE_VALUES;

				int factionCount = reader.ReadInt32();
				if (factionCount > MAX_PAYLOAD_FACTIONS || factionCount < 0)
				{
					Log.Error("FactionController",
						$"ReadPayload: faction count {factionCount} exceeds limit {MAX_PAYLOAD_FACTIONS}. Aborting payload read.");
					reader.Position = factionBlockEnd;
					return;
				}

				for (int i = 0; i < factionCount; ++i)
				{
					int factionID = reader.ReadInt32Unpacked();

					/* The owner is handed its own standings; everyone else is handed the sign and
					 * installs ±1. See WritePayload and DecodeStandingSign. */
					int value = exactValues
						? reader.ReadInt32()
						: DecodeStandingSign(reader.ReadUInt8Unpacked());

					/* The restore path, not SetFaction.
					 *
					 * SetFaction refuses to move an NPC's standing, which is right for a gameplay
					 * adjustment and wrong here: a pet's roster is copied from its owner and sent in
					 * this payload precisely so the owner's client can see it, and routing it
					 * through that guard discarded every entry on arrival.
					 *
					 * Events are not raised either. This runs on every client for every character
					 * that comes into observer range, so the default skipEvent:false fired the
					 * STATIC OnUpdateFaction once per faction per stranger walking past, and invoked
					 * that character's onFactionChangeTriggers on the observer's machine. The owner's
					 * own panel is unaffected: UITKFactions.OnPostSetCharacter rebuilds every row by
					 * walking this dictionary after the payload has been read — and the owner is the
					 * receiver that was handed exact standings above, so those rows are real. */
					ApplyFactionValue(factionID, value);
				}
			}

			/* Belt and braces on the success path: the frame absorbs any shape disagreement here
			 * rather than corrupting the behaviour after this one. */
			if (reader.Position != factionBlockEnd)
			{
				Log.Error("FactionController",
					$"ReadPayload consumed {reader.Position - (factionBlockEnd - factionBlockLength)} of " +
					$"{factionBlockLength} framed bytes. Seeking to the end of the block; the faction " +
					"state read above may be incomplete.");
				reader.Position = factionBlockEnd;
			}
		}

		/// <summary>
		/// Writes the current faction state to the network payload for synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			/* Everything below is framed by a byte count so ReadPayload can resynchronise after
			 * rejecting an untrustworthy count. See FACTION_PAYLOAD_LENGTH_BYTES. */
			writer.Skip(FACTION_PAYLOAD_LENGTH_BYTES);
			int factionBlockStart = writer.Position;

			// The race this character's standings are judged against, which a spawner may have
			// overridden server-side. See ReadPayload.
			/* Unpacked. Template ids are a deterministic 32-bit hash
			 * (CachedScriptableObject.AddToCache), so they span the whole range and the
			 * signed-packed form spends FIVE bytes on one. Every NPC in the world carries this
			 * block, so it is the most-multiplied single id in the game. See ObservedBuffEntry. */
			writer.WriteInt32Unpacked(raceTemplateID);

			/* Two shapes, chosen by where the roster came from rather than by what the reader is,
			 * so the reader never has to guess. A derived roster (every ordinary NPC) is rebuilt
			 * from the race template on the far side and costs nothing here; an owned roster (a
			 * player's persisted standings, a pet's copy of its owner's) is written out. */
			bool derivedRoster = FactionsAreTemplateDerived;
			writer.WriteBoolean(derivedRoster);

			if (!derivedRoster)
			{
				/* Shaped per RECEIVER, and the shape is written so the reader never has to guess —
				 * the same arrangement BuffController's payload uses.
				 *
				 * The OWNER gets its exact standings: they are its own persisted progression and its
				 * faction panel renders the numbers. EVERYONE ELSE gets one sign byte per entry,
				 * because the sign is the entirety of what a non-owner consumes (see
				 * SendObserverFactionUpdates) — and a peer's precise reputation is not a fact any
				 * bystander is entitled to, which is the same leak the buff and equipment payload
				 * splits were introduced to close.
				 *
				 * Safe to vary by connection: FishNet builds the spawn message per receiving
				 * connection (ServerObjects.Observers calls WriteSpawn(nob, writer, conn) in the
				 * per-connection rebuild), so no two receivers share this buffer. */
				bool ownerShape = PayloadVisibility.IsOwner(this, conn);
				writer.WriteUInt8Unpacked(ownerShape ? FACTION_PAYLOAD_SHAPE_VALUES : FACTION_PAYLOAD_SHAPE_SIGNS);

				writer.WriteInt32(Factions.Count);
				// Keyed by the dictionary key: an entry whose template failed to resolve would NRE
				// on faction.Template.ID, and the key is the same value.
				foreach (KeyValuePair<int, Faction> faction in Factions)
				{
					// Unpacked for the same reason; the standing VALUE stays packed, it is small.
					writer.WriteInt32Unpacked(faction.Key);

					int standing = faction.Value.Value;
					if (ownerShape)
					{
						writer.WriteInt32(standing);
					}
					else
					{
						writer.WriteUInt8Unpacked(EncodeStandingSign(standing));
					}

#if UNITY_SERVER
					/* Record what this receiver now holds, per channel.
					 *
					 * Faction rows are installed on a character BEFORE ServerManager.Spawn (see
					 * CharacterSystem.Loading), so the complete roster is already in this payload by
					 * the time OnStartNetwork marks everything dirty. With no baseline the next tick
					 * flushed the whole table again — a reliable FactionUpdateMultipleBroadcast to the
					 * owner and a CharacterObserverFactionUpdateBroadcast to every observer, all of
					 * them carrying values the receiver had just been handed and applying them as
					 * no-op writes. Seeding here rather than deleting the MarkAllFactionsDirty call
					 * keeps the baseline HONEST: a genuine pre-spawn mutation that this payload does
					 * not carry would still be flushed. */
					if (ownerShape)
					{
						lastSentFactionValues[faction.Key] = standing;
					}
					else
					{
						lastSentObserverSigns[faction.Key] = StandingSign(standing);
					}
#endif
				}
			}

			writer.InsertUInt32Unpacked((uint)(writer.Position - factionBlockStart),
				factionBlockStart - FACTION_PAYLOAD_LENGTH_BYTES);
		}

		/// <summary>
		/// Copies all faction data from another faction controller, replacing current state.
		/// </summary>
		/// <param name="factionController">The source faction controller to copy from.</param>
		public void CopyFrom(IFactionController factionController)
		{
			Factions.Clear();
			Allied.Clear();
			Neutral.Clear();
			Hostile.Clear();

			/* These standings are the owner's, not this character's race's, so nothing can rederive
			 * them — they have to travel in the spawn payload like a player's. */
			FactionsAreTemplateDerived = false;

			// Keyed by the dictionary key rather than faction.Template.ID: an entry whose template
			// failed to resolve would NRE here, and the key is the same value either way.
			foreach (KeyValuePair<int, Faction> faction in factionController.Factions)
			{
				Factions.Add(faction.Key, faction.Value);
			}
			foreach (KeyValuePair<int, Faction> faction in factionController.Allied)
			{
				Allied.Add(faction.Key, faction.Value);
			}
			foreach (KeyValuePair<int, Faction> faction in factionController.Neutral)
			{
				Neutral.Add(faction.Key, faction.Value);
			}
			foreach (KeyValuePair<int, Faction> faction in factionController.Hostile)
			{
				Hostile.Add(faction.Key, faction.Value);
			}
		}

		/// <summary>
		/// Sets the faction to value.
		/// </summary>
		/// <param name="templateID">The faction template ID to modify.</param>
		/// <param name="value">The new reputation or standing value.</param>
		/// <param name="skipEvent">If true, faction change events will not be invoked.</param>
		public void SetFaction(int templateID, int value, bool skipEvent = false)
		{
			// NPCs don't get faction adjustments. This would make them eventually attack each other.
			if (Character as NPC != null)
			{
				return;
			}

			Faction faction = ApplyFactionValue(templateID, value);
			if (faction == null)
			{
				return;
			}

			//Log.Debug($"Set Faction: {templateID}:{value}");

			/* skipEvent doubles as "this is a restore": the load path installs rows the database
			 * already holds and stamps their versions afterwards, and the observer update path on
			 * a client has nothing to persist. Anything else is a change the database has not seen. */
			if (!skipEvent)
			{
				faction.MarkChanged();
				IFactionController.OnUpdateFaction?.Invoke(Character, faction);
				Character.Invoke(onFactionChangeTriggers, new FactionEventData(Character, faction.Template, value));
			}
		}

		/// <summary>
		/// Installs a standing without the NPC guard, without events, and without marking anything
		/// dirty. The restore path: the spawn payload, <see cref="CopyFrom"/> and
		/// <see cref="InitializeTemplateFactions"/>.
		/// </summary>
		/// <remarks>
		/// <see cref="SetFaction"/> refuses to move an NPC's standing, which is correct for a
		/// gameplay adjustment and wrong for a restore — reading a payload through it silently
		/// discarded every faction a pet had copied from its owner, so a summoned pet arrived on
		/// its owner's client with an empty roster no matter what the server sent.
		/// </remarks>
		/// <param name="templateID">The faction template ID.</param>
		/// <param name="value">The standing to install.</param>
		/// <returns>The faction entry, or null when the template ID is unknown.</returns>
		private Faction ApplyFactionValue(int templateID, int value)
		{
			if (factions.TryGetValue(templateID, out Faction faction))
			{
				RemoveFromAllianceGroup(faction);

				faction.Value = value.Clamp(FactionTemplate.Minimum, FactionTemplate.Maximum);
			}
			else
			{
				faction = new Faction(templateID, value);
				/* Faction resolves its template through the cache in its constructor. Templates are
				 * immutable data loaded before anything spawns, so a miss means the id itself is
				 * wrong — keeping the entry would put a null Template into the alliance tables and
				 * NRE later, in WritePayload or a colour lookup, far from the cause. */
				if (faction.Template == null)
				{
					Log.Error("FactionController",
						$"ApplyFactionValue: no FactionTemplate is registered for id {templateID}. " +
						"The standing was discarded; the character will read as neutral toward it.");
					return null;
				}
				factions.Add(templateID, faction);
			}
			InsertToAllianceGroup(faction);

#if UNITY_SERVER
			MarkFactionDirty(templateID);
#endif
			return faction;
		}

		/// <summary>
		/// Adds amount to the faction value.
		/// </summary>
		/// <param name="template">The faction template to adjust.</param>
		/// <param name="amount">The amount to add to the faction standing (can be negative).</param>
		public void Add(FactionTemplate template, int amount = 1)
		{
			// NPCs don't get faction adjustments. This would make them eventually attack each other.
			if (Character as NPC != null)
			{
				return;
			}

			if (template == null)
			{
				return;
			}

			if (factions.TryGetValue(template.ID, out Faction faction))
			{
				RemoveFromAllianceGroup(faction);

				// Update value
				faction.Value = (faction.Value + amount).Clamp(FactionTemplate.Minimum, FactionTemplate.Maximum);
			}
			else
			{
				factions.Add(template.ID, faction = new Faction(template.ID, amount));
			}
			InsertToAllianceGroup(faction);
			faction.MarkChanged();

#if UNITY_SERVER
			MarkFactionDirty(template.ID);
#endif

			//Log.Debug($"Update Faction: {template.ID}:{amount}");

			IFactionController.OnUpdateFaction?.Invoke(Character, faction);
			Character.Invoke(onFactionChangeTriggers, new FactionEventData(Character, template, faction.Value));
		}

		/// <summary>
		/// Adjusts a faction's value by a percentage of a given amount.
		/// </summary>
		/// <param name="template">The faction template to adjust.</param>
		/// <param name="value">The base value to calculate the adjustment from.</param>
		/// <param name="percentageToAdjust">The percentage of the value to apply as adjustment.</param>
		private void AdjustFactionValue(FactionTemplate template, float value, float percentageToAdjust)
		{
			if (template == null)
			{
				return;
			}
			int amountToAdjust = Mathf.RoundToInt(value * percentageToAdjust);

			Add(template, amountToAdjust);

			//Log.Debug($"{(value > 0 ? "Add" : "Subtract")} Faction: {template.ID}:{amountToAdjust}");
		}

		/// <summary>
		/// Adds a percentage of the defenders hostile faction and removes a percentage of the defenders allied faction.
		/// </summary>
		/// <param name="defenderFactionController">The defender's faction controller to reference.</param>
		/// <param name="alliedPercentToSubtract">Percentage of allied faction standing to subtract.</param>
		/// <param name="hostilePercentToAdd">Percentage of hostile faction standing to add.</param>
		public void AdjustFaction(IFactionController defenderFactionController, float alliedPercentToSubtract, float hostilePercentToAdd)
		{
			// NPCs don't get faction adjustments. This would make them eventually attack each other.
			if (Character as NPC != null)
			{
				return;
			}
			if (defenderFactionController == null)
			{
				return;
			}
			// Is the other character an NPC?
			if (defenderFactionController.Character as NPC != null)
			{
				/* An NPC with no race template, or a race with no initial faction, awards no
				 * standing. This runs from the kill-reward path, so throwing here would abort a
				 * death handler mid-way on a content mistake. */
				FactionTemplate defenderInitialFaction = defenderFactionController.RaceTemplate?.InitialFaction;
				if (defenderInitialFaction == null)
				{
					return;
				}

				if (defenderInitialFaction.DefaultAllied != null)
				{
					foreach (FactionTemplate factionTemplate in defenderInitialFaction.DefaultAllied)
					{
						AdjustFactionValue(factionTemplate, -FactionTemplate.Maximum, alliedPercentToSubtract);
					}
				}
				if (defenderInitialFaction.DefaultHostile != null)
				{
					foreach (FactionTemplate factionTemplate in defenderInitialFaction.DefaultHostile)
					{
						AdjustFactionValue(factionTemplate, FactionTemplate.Maximum, hostilePercentToAdd);
					}
				}
			}
			else
			{
				foreach (Faction faction in defenderFactionController.Allied.Values)
				{
					AdjustFactionValue(faction.Template, -faction.Value, alliedPercentToSubtract);
				}
				foreach (Faction faction in defenderFactionController.Hostile.Values)
				{
					AdjustFactionValue(faction.Template, faction.Value, hostilePercentToAdd);
				}
			}
		}

		/// <summary>
		/// Removes a faction from its current alliance group (Allied, Hostile, or Neutral).
		/// </summary>
		/// <param name="faction">The faction to remove from its alliance group.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveFromAllianceGroup(Faction faction)
		{
			if (faction == null ||
				faction.Template == null ||
				factions == null)
			{
				return;
			}
			if (faction.Value > 0)
			{
				Allied.Remove(faction.Template.ID);
			}
			else if (faction.Value < 0)
			{
				Hostile.Remove(faction.Template.ID);
			}
			else
			{
				Neutral.Remove(faction.Template.ID);
			}
		}

		/// <summary>
		/// Inserts a faction into the appropriate alliance group (Allied, Hostile, or Neutral) based on its value.
		/// </summary>
		/// <param name="faction">The faction to insert into an alliance group.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void InsertToAllianceGroup(Faction faction)
		{
			if (faction == null ||
				faction.Template == null ||
				factions == null)
			{
				return;
			}
			if (faction.Value > 0)
			{
				Allied[faction.Template.ID] = faction;
			}
			else if (faction.Value < 0)
			{
				Hostile[faction.Template.ID] = faction;
			}
			else
			{
				Neutral[faction.Template.ID] = faction;
			}
		}

		/// <summary>
		/// Gets the alliance level between this character and another faction controller.
		/// Checks party, guild, aggression, and faction standings to determine the relationship.
		/// </summary>
		/// <param name="otherFactionController">The other faction controller to evaluate against.</param>
		/// <returns>The <see cref="FactionAllianceLevel"/> between the two characters.</returns>
		public FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController)
		{
			if (otherFactionController == null)
			{
				return FactionAllianceLevel.Neutral;
			}

			/* Arenas first. Inside an arena the side a character is on is the team the match
			 * seated them on, and nothing else: two guildmates on opposite teams are enemies, two
			 * strangers on one team are allies, and nobody is anybody's enemy until the match is
			 * live. Party, guild and faction are the open world's rules and are consulted only
			 * when no arena has a say. */
			if (ArenaTeamRegistry.TryResolveAlliance(Character, otherFactionController.Character, out FactionAllianceLevel arenaLevel))
			{
				return arenaLevel;
			}

			// Same party?
			if (Character.TryGet(out IPartyController partyController) &&
				otherFactionController.Character.TryGet(out IPartyController otherPartyController) &&
				partyController.ID != 0 &&
				partyController.ID == otherPartyController.ID)
			{
				return FactionAllianceLevel.Ally;
			}

			// Same guild?
			if (Character.TryGet(out IGuildController guildController) &&
				otherFactionController.Character.TryGet(out IGuildController otherGuildController) &&
				guildController.ID != 0 &&
				guildController.ID == otherGuildController.ID)
			{
				return FactionAllianceLevel.Ally;
			}

			// Is aggression toggled on either?
			if (IsAggressive || otherFactionController.IsAggressive)
			{
				return FactionAllianceLevel.Enemy;
			}

			// Is the other character an NPC? Directly use the template data if so.
			if (otherFactionController.Character as NPC != null)
			{
				// An NPC prefab with no race template, or a race with no initial faction, reads as
				// neutral rather than throwing inside a targeting or nameplate query.
				FactionTemplate otherInitialFaction = otherFactionController.RaceTemplate?.InitialFaction;
				if (otherInitialFaction != null &&
					Hostile.ContainsKey(otherInitialFaction.ID))
				{
					//UnityEngine.Log.Debug($"{otherFactionController.Template.Name}: {otherFactionController.Character.GameObject.name} is an Enemy of {this.Character.GameObject.name}.");

					return FactionAllianceLevel.Enemy;
				}
			}
			else
			{
				foreach (Faction faction in Hostile.Values)
				{
					if (otherFactionController.Factions.TryGetValue(faction.Template.ID, out Faction enemyFaction))
					{
						//UnityEngine.Log.Debug($"{faction.Template.Name}: The target is an {(enemyFaction.Value > 0 ? "Ally" : "Enemy")} of this faction.");

						// Is the enemy allied with our enemy?
						if (enemyFaction.Value > 0)
						{
							return FactionAllianceLevel.Enemy;
						}
					}
				}
			}
			return FactionAllianceLevel.Neutral;
		}

		/// <summary>
		/// Gets the color representing the alliance level between this character and another faction controller.
		/// Green for Ally, Sky Blue for Neutral, Red for Enemy or Aggressive.
		/// </summary>
		/// <param name="otherFactionController">The other faction controller to evaluate against.</param>
		/// <returns>A <see cref="Color"/> representing the alliance level.</returns>
		public Color GetAllianceLevelColor(IFactionController otherFactionController)
		{
			/* Inside an arena a seated character is drawn in their team's colour, whichever side the
			 * viewer is on, so both teams read the same colours on nameplates, target frames and
			 * scoreboards. Whether they may be attacked is still the alliance's question, answered
			 * by GetAllianceLevel; this is only what they look like. */
			if (otherFactionController?.Character != null &&
				ArenaTeamRegistry.TryGetTeamColor(otherFactionController.Character, out Color teamColor))
			{
				return teamColor;
			}

			if (IsAggressive || otherFactionController.IsAggressive)
			{
				return TinyColor.ToUnityColor(TinyColor.red);
			}

			FactionAllianceLevel allianceLevel = GetAllianceLevel(otherFactionController);

			switch (allianceLevel)
			{
				case FactionAllianceLevel.Ally:
					return TinyColor.ToUnityColor(TinyColor.green);
				case FactionAllianceLevel.Neutral:
					return TinyColor.ToUnityColor(TinyColor.skyBlue);
				case FactionAllianceLevel.Enemy:
					return TinyColor.ToUnityColor(TinyColor.red);
				default: return Color.white;
			}
		}
	}
}