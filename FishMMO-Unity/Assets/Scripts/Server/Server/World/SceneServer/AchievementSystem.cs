using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server
{
	public class AchievementSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				IAchievementController.OnCompleteAchievement += HandleAchievementRewards;
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				IAchievementController.OnCompleteAchievement -= HandleAchievementRewards;
			}
		}

		private void HandleAchievementRewards(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character != null &&
				character.TryGet(out InventoryController inventoryController))
			{
				BaseItemTemplate[] itemRewards = tier.ItemRewards;
				if (itemRewards != null && itemRewards.Length > 0 && inventoryController.FreeSlots() >= itemRewards.Length)
				{
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
						IPlayerCharacter playerCharacter = character as IPlayerCharacter;
						playerCharacter.Owner.Broadcast(new InventorySetMultipleItemsBroadcast()
						{
							items = modifiedItemBroadcasts,
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}
