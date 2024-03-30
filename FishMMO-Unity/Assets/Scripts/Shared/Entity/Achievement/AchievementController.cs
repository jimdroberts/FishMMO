using UnityEngine;
using System;
using FishNet.Transporting;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class AchievementController : CharacterBehaviour, IAchievementController
	{
		private Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();


		public Dictionary<int, Achievement> Achievements { get { return achievements; } }

#if !UNITY_SERVER
		public bool ShowAchievementCompletion = true;
		public event Func<string, Vector3, Color, float, float, bool, IReference> OnCompleteAchievement;

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
			AchievementTemplate template = AchievementTemplate.Get<AchievementTemplate>(msg.templateID);
			if (template != null &&
				achievements.TryGetValue(template.ID, out Achievement achievement))
			{
				achievement.CurrentValue = msg.newValue;
			}
		}

		/// <summary>
		/// Server sent a multiple achievement update broadcast.
		/// </summary>
		private void OnClientAchievementUpdateMultipleBroadcastReceived(AchievementUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (AchievementUpdateBroadcast subMsg in msg.achievements)
			{
				AchievementTemplate template = AchievementTemplate.Get<AchievementTemplate>(subMsg.templateID);
				if (template != null &&
					achievements.TryGetValue(template.ID, out Achievement achievement))
				{
					achievement.CurrentValue = subMsg.newValue;
				}
			}
		}
#endif

		public void SetAchievement(int templateID, byte tier, uint value)
		{
			if (achievements == null)
			{
				achievements = new Dictionary<int, Achievement>();
			}

			if (achievements.TryGetValue(templateID, out Achievement achievement))
			{
				achievement.CurrentTier = tier;
				achievement.CurrentValue = value;
			}
			else
			{
				achievements.Add(templateID, new Achievement(templateID, tier, value));
			}
		}

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
			if (achievements == null)
			{
				achievements = new Dictionary<int, Achievement>();
			}

			Achievement achievement;
			if (!achievements.TryGetValue(template.ID, out achievement))
			{
				achievements.Add(template.ID, achievement = new Achievement(template.ID));
			}

			// get the old values
			byte currentTier = achievement.CurrentTier;
			uint currentValue = achievement.CurrentValue;

			// update current value
			achievement.CurrentValue += amount;

			List<AchievementTier> tiers = template.Tiers;
			if (tiers != null)
			{
				for (byte i = currentTier; i < tiers.Count && i < byte.MaxValue; ++i)
				{
					AchievementTier tier = tiers[i];
					if (achievement.CurrentValue > tier.MaxValue)
					{
#if UNITY_SERVER
						HandleRewards(tier);
#else
						// Display a text message above the characters head showing the achievement.
						OnCompleteAchievement?.Invoke("Achievement: " + achievement.Template.Name + " " + tier.TierCompleteMessage, Character.Transform.position, Color.yellow, 12.0f, 10.0f, false);
#endif
					}
					else
					{
						achievement.CurrentTier = i;
						break;
					}

				}
			}
		}

#if UNITY_SERVER
	public void HandleRewards(AchievementTier tier)
	{
		if (base.IsServerInitialized &&
			Character.Owner != null &&
			Character.TryGet(out InventoryController inventoryController))
		{
			BaseItemTemplate[] itemRewards = tier.ItemRewards;
			if (itemRewards != null && itemRewards.Length > 0 && inventoryController.FreeSlots() >= itemRewards.Length)
			{
				InventorySetMultipleItemsBroadcast inventorySetMultipleItemsBroadcast = new InventorySetMultipleItemsBroadcast()
				{
					items = new List<InventorySetItemBroadcast>(),
				};

				List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();

				for (int i = 0; i < itemRewards.Length; ++i)
				{
					Item newItem = new Item(123, 0, itemRewards[i].ID, 1);

					if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
					{
						foreach (Item item in modifiedItems)
						{
							modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
							{
								instanceID = newItem.ID,
								templateID = newItem.Template.ID,
								slot = newItem.Slot,
								seed = newItem.IsGenerated ? newItem.Generator.Seed : 0,
								stackSize = newItem.IsStackable ? newItem.Stackable.Amount : 0,
							});
						}
					}
				}
				if (modifiedItemBroadcasts.Count > 0)
				{
					Character.Owner.Broadcast(new InventorySetMultipleItemsBroadcast()
					{
						items = modifiedItemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
		}
	}
#endif
	}
}