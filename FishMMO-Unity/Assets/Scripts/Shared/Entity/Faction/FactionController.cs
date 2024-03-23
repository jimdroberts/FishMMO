using FishNet.Transporting;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class FactionController : CharacterBehaviour
	{
		private Dictionary<int, Faction> factions = new Dictionary<int, Faction>();

		public Dictionary<int, Faction> Factions { get { return factions; } }

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
			if (template != null &&
				factions.TryGetValue(template.ID, out Faction faction))
			{
				faction.Value = msg.newValue;
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
				if (template != null &&
					factions.TryGetValue(template.ID, out Faction faction))
				{
					faction.Value = subMsg.newValue;
				}
			}
		}
#endif

		public void SetFaction(int templateID, int value)
		{
			if (factions == null)
			{
				factions = new Dictionary<int, Faction>();
			}

			if (factions.TryGetValue(templateID, out Faction faction))
			{
				faction.Value = value;
			}
			else
			{
				factions.Add(templateID, new Faction(templateID, value));
			}
		}

		public bool TryGetFaction(int templateID, out Faction faction)
		{
			return factions.TryGetValue(templateID, out faction);
		}

		public void Add(FactionTemplate template, int amount = 1)
		{
			if (template == null)
			{
				return;
			}
			if (factions == null)
			{
				factions = new Dictionary<int, Faction>();
			}


			if (!factions.TryGetValue(template.ID, out Faction faction))
			{
				factions.Add(template.ID, faction = new Faction(template.ID, amount));
			}
			else
			{
				// update value
				faction.Value += amount;
			}
		}

		public FactionAllianceLevel GetAllianceLevel(FactionTemplate enemyFaction)
		{
			if (!factions.TryGetValue(enemyFaction.ID, out Faction faction))
			{
				return FactionAllianceLevel.Neutral;
			}
			if (faction.Value >= enemyFaction.AlliedLevel)
			{
				return FactionAllianceLevel.Ally;
			}
			else if (faction.Value <= enemyFaction.EnemyLevel)
			{
				return FactionAllianceLevel.Enemy;
			}
			return FactionAllianceLevel.Neutral;
		}
	}
}