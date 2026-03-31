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
					Factions = updates,
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
				Factions = updates,
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
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			Factions.Clear();
			Allied.Clear();
			Neutral.Clear();
			Hostile.Clear();

			int factionCount = reader.ReadInt32();
			if (factionCount < 1)
			{
				return;
			}

			for (int i = 0; i < factionCount; ++i)
			{
				int factionID = reader.ReadInt32();
				int value = reader.ReadInt32();

				SetFaction(factionID, value);
			}
		}

		/// <summary>
		/// Writes the current faction state to the network payload for synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			// Write the factions for the clients
			writer.WriteInt32(Factions.Count);
			foreach (Faction faction in Factions.Values)
			{
				writer.WriteInt32(faction.Template.ID);
				writer.WriteInt32(faction.Value);
			}
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

			foreach (Faction faction in factionController.Factions.Values)
			{
				Factions.Add(faction.Template.ID, faction);
			}
			foreach (Faction faction in factionController.Allied.Values)
			{
				Allied.Add(faction.Template.ID, faction);
			}
			foreach (Faction faction in factionController.Neutral.Values)
			{
				Neutral.Add(faction.Template.ID, faction);
			}
			foreach (Faction faction in factionController.Hostile.Values)
			{
				Hostile.Add(faction.Template.ID, faction);
			}
		}

		/// <summary>
		/// Sets the faction to value.
		/// </summary>
		public void SetFaction(int templateID, int value, bool skipEvent = false)
		{
			// NPCs don't get faction adjustments. This would make them eventually attack each other.
			if (Character as NPC != null)
			{
				return;
			}

			if (factions.TryGetValue(templateID, out Faction faction))
			{
				RemoveFromAllianceGroup(faction);

				faction.Value = value;
			}
			else
			{
				factions.Add(templateID, faction = new Faction(templateID, value));
			}
			InsertToAllianceGroup(faction);

#if UNITY_SERVER
			MarkFactionDirty(templateID);
#endif

			//Log.Debug($"Set Faction: {templateID}:{value}");

			if (!skipEvent)
			{
				IFactionController.OnUpdateFaction?.Invoke(Character, faction);
				Character.Invoke(onFactionChangeTriggers, new FactionEventData(Character, FactionTemplate.Get<FactionTemplate>(templateID), value));
			}
		}

		/// <summary>
		/// Adds amount to the faction value.
		/// </summary>
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
				foreach (FactionTemplate factionTemplate in defenderFactionController.RaceTemplate.InitialFaction.DefaultAllied)
				{
					AdjustFactionValue(factionTemplate, -FactionTemplate.Maximum, alliedPercentToSubtract);
				}
				foreach (FactionTemplate factionTemplate in defenderFactionController.RaceTemplate.InitialFaction.DefaultHostile)
				{
					AdjustFactionValue(factionTemplate, FactionTemplate.Maximum, hostilePercentToAdd);
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
				if (Hostile.ContainsKey(otherFactionController.RaceTemplate.InitialFaction.ID))
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