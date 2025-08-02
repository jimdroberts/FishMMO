using FishNet.Transporting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls and tracks a character's achievements, including progress, tier, and event handling.
	/// </summary>
	public class AchievementController : CharacterBehaviour, IAchievementController
	{
		/// <summary>
		/// Internal dictionary mapping achievement template IDs to achievement progress.
		/// </summary>
		private Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();

		/// <summary>
		/// Public accessor for the character's achievements.
		/// </summary>
		public Dictionary<int, Achievement> Achievements { get { return achievements; } }

		/// <summary>
		/// Resets the achievement state for this character, clearing all progress.
		/// </summary>
		/// <param name="asServer">Whether the reset is being performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			achievements.Clear();
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character is started on the client. Registers broadcast listeners for achievement updates.
		/// </summary>
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

		/// <summary>
		/// Called when the character is stopped on the client. Unregisters achievement update listeners.
		/// </summary>
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
		/// Handles a broadcast from the server to update a single achievement's progress.
		/// </summary>
		/// <param name="msg">The achievement update message.</param>
		/// <param name="channel">The network channel.</param>
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
		/// Handles a broadcast from the server to update multiple achievements at once.
		/// </summary>
		/// <param name="msg">The multiple achievement update message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAchievementUpdateMultipleBroadcastReceived(AchievementUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (AchievementUpdateBroadcast subMsg in msg.Achievements)
			{
				OnClientAchievementUpdateBroadcastReceived(subMsg, channel);
			}
		}
#endif

		/// <summary>
		/// Sets or updates the progress for a specific achievement, optionally skipping the update event.
		/// </summary>
		/// <param name="templateID">The template ID of the achievement.</param>
		/// <param name="tier">The current tier to set.</param>
		/// <param name="value">The current value to set.</param>
		/// <param name="skipEvent">If true, does not invoke the update event.</param>
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

		/// <summary>
		/// Attempts to retrieve an achievement by template ID.
		/// </summary>
		/// <param name="templateID">The template ID of the achievement.</param>
		/// <param name="achievement">The resulting achievement if found.</param>
		/// <returns>True if the achievement exists, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetAchievement(int templateID, out Achievement achievement)
		{
			return achievements.TryGetValue(templateID, out achievement);
		}

		/// <summary>
		/// Increments the progress of an achievement by a specified amount, handling tier advancement and rewards.
		/// </summary>
		/// <param name="template">The achievement template to increment.</param>
		/// <param name="amount">The amount to increment the achievement's value by.</param>
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
						// Client: Display a text message above the character's head showing the achievement.
						// Server: Provide rewards for completion.
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