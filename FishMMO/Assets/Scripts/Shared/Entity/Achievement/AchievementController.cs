using FishNet;
using FishNet.Object;
using UnityEngine;
using System;
using System.Collections.Generic;

public class AchievementController : NetworkBehaviour
{
	public Character Character;

	private Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();

#if !UNITY_SERVER || UNITY_EDITOR
	public bool ShowAchievementCompletion = true;
	public event Action<Vector3, Color, float, string> OnCompleteAchievement;
#endif

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}

#if !UNITY_SERVER || UNITY_EDITOR
		if (Character.LabelMaker != null)
		{
			OnCompleteAchievement += Character.LabelMaker.Display;
		}
#endif

		ClientManager.RegisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
		ClientManager.RegisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
#if !UNITY_SERVER || UNITY_EDITOR
			if (Character.LabelMaker != null)
			{
				OnCompleteAchievement -= Character.LabelMaker.Display;
			}
#endif

			ClientManager.UnregisterBroadcast<AchievementUpdateBroadcast>(OnClientAchievementUpdateBroadcastReceived);
			ClientManager.UnregisterBroadcast<AchievementUpdateMultipleBroadcast>(OnClientAchievementUpdateMultipleBroadcastReceived);
		}
	}

	public List<Achievement> GetAchievements()
	{
		return new List<Achievement>(achievements.Values);
	}

	public bool TryGetAchievement(int templateID, out Achievement achievement)
	{
		return achievements.TryGetValue(templateID, out achievement);
	}

	public void Increment(AchievementTemplate template, uint amount)
	{
		if (achievements != null)
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
#if !UNITY_SERVER || UNITY_EDITOR
					HandleRewards(tier);
#endif

#if !UNITY_SERVER || UNITY_EDITOR
					// Display a text message above the characters head showing the achievement.
					OnCompleteAchievement?.Invoke(Character.Transform.position, Color.yellow, 10.0f, "Achievement: " + achievement.Template.Name + " " + tier.TierCompleteMessage);
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

	private void HandleRewards(AchievementTier tier)
	{
		if (base.IsServer && Character.Owner != null)
		{
			BaseItemTemplate[] itemRewards = tier.ItemRewards;
			if (itemRewards != null && itemRewards.Length > 0 && Character.InventoryController.FreeSlots() >= itemRewards.Length)
			{
				InventorySetMultipleItemsBroadcast inventorySetMultipleItemsBroadcast = new InventorySetMultipleItemsBroadcast()
				{
					items = new List<InventorySetItemBroadcast>(),
				};

				for (int i = 0; i < itemRewards.Length; ++i)
				{
					Item newItem = new Item(123, itemRewards[i].ID, 1, 123456789);

					if (Character.InventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
					{
						foreach (Item item in modifiedItems)
						{
							inventorySetMultipleItemsBroadcast.items.Add(new InventorySetItemBroadcast()
							{
								instanceID = newItem.InstanceID,
								templateID = newItem.Template.ID,
								seed = newItem.Generator.Seed,
								slot = newItem.Slot,
								stackSize = newItem.Stackable.Amount,
							});
						}
					}
				}
				if (inventorySetMultipleItemsBroadcast.items.Count > 0)
				{
					Character.Owner.Broadcast(inventorySetMultipleItemsBroadcast);
				}
			}
		}
	}

	/// <summary>
	/// Server sent an achievement update broadcast.
	/// </summary>
	private void OnClientAchievementUpdateBroadcastReceived(AchievementUpdateBroadcast msg)
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
	private void OnClientAchievementUpdateMultipleBroadcastReceived(AchievementUpdateMultipleBroadcast msg)
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
}