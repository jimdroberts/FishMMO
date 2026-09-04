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
		/// Last faction values sent to interested connections.
		/// </summary>
		private readonly Dictionary<int, int> lastSentFactionValues = new Dictionary<int, int>();
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
		/// Subscribes to network tick updates used to batch and flush dirty faction state.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			MarkAllFactionsDirty();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Unsubscribes from network tick updates.
		/// </summary>
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Called on network tick to flush dirty faction updates.
		/// </summary>
		private void TimeManager_OnTick()
		{
			FlushDirtyFactionUpdates();
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
			}
		}

		/// <summary>
		/// Flushes dirty faction updates to owner and observers.
		/// </summary>
		private void FlushDirtyFactionUpdates()
		{
			if (!base.IsServerStarted || !base.IsSpawned || dirtyFactionTemplateIDs.Count == 0)
			{
				return;
			}

			List<FactionUpdateBroadcast> updates = new List<FactionUpdateBroadcast>(dirtyFactionTemplateIDs.Count);
			foreach (int templateID in dirtyFactionTemplateIDs)
			{
				if (!factions.TryGetValue(templateID, out Faction faction) || faction?.Template == null)
				{
					continue;
				}

				int current = faction.Value;
				if (lastSentFactionValues.TryGetValue(templateID, out int last) && last == current)
				{
					continue;
				}

				updates.Add(new FactionUpdateBroadcast()
				{
					TemplateID = templateID,
					NewValue = current,
				});

				lastSentFactionValues[templateID] = current;
			}

			dirtyFactionTemplateIDs.Clear();
			SendFactionUpdates(updates);
		}

		/// <summary>
		/// Sends faction updates to owner and observers using separate payload types.
		/// </summary>
		private void SendFactionUpdates(List<FactionUpdateBroadcast> updates)
		{
			if (updates == null || updates.Count == 0)
			{
				return;
			}

			SendOwnerFactionUpdates(updates);
			SendObserverFactionUpdates(updates);
		}

		/// <summary>
		/// Sends faction updates to owner connection.
		/// </summary>
		private void SendOwnerFactionUpdates(List<FactionUpdateBroadcast> updates)
		{
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
		/// Sends faction updates to observers with character routing context.
		/// </summary>
		private void SendObserverFactionUpdates(List<FactionUpdateBroadcast> updates)
		{
			if (Character == null)
			{
				return;
			}

			CharacterObserverFactionUpdateBroadcast observerBroadcast = new CharacterObserverFactionUpdateBroadcast()
			{
				CharacterID = Character.ID,
				Factions = updates.ToArray(),
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
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

		/// <summary>
		/// Broadcasts payload to observers only (excluding owner).
		/// </summary>
		private static void BroadcastToObserversOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null || character.Observers == null)
			{
				return;
			}

			NetworkConnection owner = character.Owner;
			foreach (NetworkConnection observer in character.Observers)
			{
				if (observer == null || observer == owner || !observer.IsActive)
				{
					continue;
				}

				observer.Broadcast(broadcast, true, channel);
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
				factionController.SetFaction(subMsg.TemplateID, subMsg.NewValue, true);
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
			dirtyFactionTemplateIDs.Clear();
			lastSentFactionValues.Clear();
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

			/* The race, before anything derived from it.
			 *
			 * A prefab carries a serialized race, but a spawner may override it
			 * (NPCSpawnableSettings.FactionOverride → SetRaceTemplateOnSpawn) so one prefab can be
			 * hostile at one spawn point and neutral at another. That override lives on the server
			 * only, so a client that trusted its prefab judged those NPCs by the wrong race — the
			 * one field of "default configuration" that genuinely has to travel. */
			int payloadRaceTemplateID = reader.ReadInt32();
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
					int factionID = reader.ReadInt32();
					int value = reader.ReadInt32();

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
					 * walking this dictionary after the payload has been read. */
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
			writer.WriteInt32(raceTemplateID);

			/* Two shapes, chosen by where the roster came from rather than by what the reader is,
			 * so the reader never has to guess. A derived roster (every ordinary NPC) is rebuilt
			 * from the race template on the far side and costs nothing here; an owned roster (a
			 * player's persisted standings, a pet's copy of its owner's) is written out. */
			bool derivedRoster = FactionsAreTemplateDerived;
			writer.WriteBoolean(derivedRoster);

			if (!derivedRoster)
			{
				writer.WriteInt32(Factions.Count);
				// Keyed by the dictionary key: an entry whose template failed to resolve would NRE
				// on faction.Template.ID, and the key is the same value.
				foreach (KeyValuePair<int, Faction> faction in Factions)
				{
					writer.WriteInt32(faction.Key);
					writer.WriteInt32(faction.Value.Value);
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

			if (!skipEvent)
			{
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