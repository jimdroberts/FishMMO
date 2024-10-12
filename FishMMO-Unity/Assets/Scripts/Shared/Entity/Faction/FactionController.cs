using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public class FactionController : CharacterBehaviour, IFactionController
	{
		private Dictionary<int, Faction> factions = new Dictionary<int, Faction>();
		private Dictionary<int, Faction> allied = new Dictionary<int, Faction>();
		private Dictionary<int, Faction> neutral = new Dictionary<int, Faction>();
		private Dictionary<int, Faction> hostile = new Dictionary<int, Faction>();

		public bool IsAggressive { get; set; }
		public Dictionary<int, Faction> Factions { get { return factions; } }

		public Dictionary<int, Faction> Allied { get { return allied; } }
		public Dictionary<int, Faction> Neutral { get { return neutral; } }
		public Dictionary<int, Faction> Hostile { get { return hostile; } }

		[SerializeField]
		private FactionTemplate template;
		public FactionTemplate Template { get { return this.template; } set { this.template = value; } }

#if !UNITY_SERVER
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
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<FactionUpdateBroadcast>(OnClientFactionUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<FactionUpdateMultipleBroadcast>(OnClientFactionUpdateMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an faction update broadcast.
		/// </summary>
		private void OnClientFactionUpdateBroadcastReceived(FactionUpdateBroadcast msg, Channel channel)
		{
			FactionTemplate template = FactionTemplate.Get<FactionTemplate>(msg.templateID);
				if (template != null)
				{
					SetFaction(template.ID, msg.newValue);
				}
		}

		/// <summary>
		/// Server sent a multiple faction update broadcast.
		/// </summary>
		private void OnClientFactionUpdateMultipleBroadcastReceived(FactionUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (FactionUpdateBroadcast subMsg in msg.factions)
			{
				FactionTemplate template = FactionTemplate.Get<FactionTemplate>(subMsg.templateID);
				if (template != null)
				{
					SetFaction(template.ID, subMsg.newValue);
				}
			}
		}
#endif

		public void SetFaction(int templateID, int value)
		{
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
		}

		public void Add(FactionTemplate template, int amount = 1)
		{
			if (template == null)
			{
				return;
			}
			if (factions.TryGetValue(template.ID, out Faction faction))
			{
				RemoveFromAllianceGroup(faction);

				// update value
				faction.Value = (faction.Value + amount).Clamp(FactionTemplate.Minimum, FactionTemplate.Maximum);
			}
			else
			{
				factions.Add(template.ID, faction = new Faction(template.ID, amount));
			}
			InsertToAllianceGroup(faction);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveFromAllianceGroup(Faction faction)
		{
			if (factions == null)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void InsertToAllianceGroup(Faction faction)
		{
			if (factions == null)
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

		public FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController)
		{
			if (otherFactionController == null)
			{
				return FactionAllianceLevel.Neutral;
			}

			// same party?
			if (Character.TryGet(out IPartyController partyController) &&
				otherFactionController.Character.TryGet(out IPartyController otherPartyController) &&
				partyController.ID != 0 &&
				partyController.ID == otherPartyController.ID)
			{
				return FactionAllianceLevel.Ally;
			}

			// same guild?
			if (Character.TryGet(out IGuildController guildController) &&
				otherFactionController.Character.TryGet(out IGuildController otherGuildController) &&
				guildController.ID != 0 &&
				guildController.ID == otherGuildController.ID)
			{
				return FactionAllianceLevel.Ally;
			}

			// is aggression toggled on either?
			if (IsAggressive || otherFactionController.IsAggressive)
			{
				return FactionAllianceLevel.Enemy;
			}

			if (otherFactionController.Character as NPC != null)
			{
				if (Hostile.ContainsKey(otherFactionController.Template.ID))
				{
					//UnityEngine.Debug.Log($"{otherFactionController.Template.Name}: {otherFactionController.Character.GameObject.name} is an Enemy of {this.Character.GameObject.name}.");

					return FactionAllianceLevel.Enemy;
				}
			}
			else
			{
				foreach (Faction faction in Hostile.Values)
				{
					if (otherFactionController.Factions.TryGetValue(faction.Template.ID, out Faction enemyFaction))
					{
						//UnityEngine.Debug.Log($"{faction.Template.Name}: The target is an {(enemyFaction.Value > 0 ? "Ally" : "Enemy")} of this faction.");

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
	}
}