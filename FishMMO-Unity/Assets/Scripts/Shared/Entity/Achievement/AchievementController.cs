using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public class AchievementController : CharacterBehaviour, IAchievementController
	{
		private Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();

		public Dictionary<int, Achievement> Achievements { get { return achievements; } }

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			achievements.Clear();
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an achievement update broadcast.
		/// </summary>
		private void OnClientAchievementUpdateBroadcastReceived(AchievementUpdateBroadcast msg, Channel channel)
		{
			AchievementTemplate template = AchievementTemplate.Get<AchievementTemplate>(msg.TemplateID);
			if (template != null)
			{
				SetAchievement(template.ID, msg.Tier, msg.Value);
			}
			else
			{
				//Log.Debug($"Achievement Template not found while Updating: {msg.TemplateID}");
			}
		}

		/// <summary>
		/// Server sent a multiple achievement update broadcast.
		/// </summary>
		private void OnClientAchievementUpdateMultipleBroadcastReceived(AchievementUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (AchievementUpdateBroadcast subMsg in msg.Achievements)
			{
				OnClientAchievementUpdateBroadcastReceived(subMsg, channel);
			}
		}
#endif

		public void SetAchievement(int templateID, byte tier, uint value, bool skipEvent = false)
		{
			if (achievements.TryGetValue(templateID, out Achievement achievement))
			{
				achievement.CurrentTier = tier;
				achievement.CurrentValue = value;
			}
			else
			{
				achievements.Add(templateID, achievement = new Achievement(templateID, tier, value));
			}

			if (!skipEvent)
			{
				IAchievementController.OnUpdateAchievement?.Invoke(Character, achievement);
			}
			//Log.Debug($"Achievement Template Set: {achievement.Template.ID}:{achievement.CurrentValue}");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetAchievement(int templateID, out Achievement achievement)
		{
			return achievements.TryGetValue(templateID, out achievement);
		}

		public void Increment(AchievementTemplate template, uint amount)
		{
			if (template == null)
			{
				return;
			}

			Achievement achievement;
			if (!achievements.TryGetValue(template.ID, out achievement))
			{
				achievements.Add(template.ID, achievement = new Achievement(template.ID));
			}

			byte currentTier = achievement.CurrentTier;

			achievement.CurrentValue += amount;

			List<AchievementTier> tiers = template.Tiers;
			if (tiers != null)
			{
				for (byte i = currentTier; i < tiers.Count; ++i)
				{
					AchievementTier tier = tiers[i];

					if (achievement.CurrentValue >= tier.Value)
					{
						// Client: Display a text message above the characters head showing the achievement.
						// Server: Provide rewards.
						IAchievementController.OnCompleteAchievement?.Invoke(Character, achievement.Template, tier);

						achievement.CurrentTier = (byte)(i + 1);
					}
					else break;
				}
			}

			IAchievementController.OnUpdateAchievement?.Invoke(Character, achievement);
		}
	}
}