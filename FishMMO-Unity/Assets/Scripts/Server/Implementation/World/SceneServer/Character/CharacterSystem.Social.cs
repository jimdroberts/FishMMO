using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character social and broadcast helpers: async guild/party/friend data fetch, non-DB data broadcasts (abilities, achievements, inventory, bank, hotkeys), and targeted character broadcasts.
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Fetches guild, party, and friend data asynchronously from the database
		/// and marshals broadcasts back to the main thread.
		/// All parameters must be captured on the main thread before enqueueing.
		/// </summary>
		/// <param name="owner">Network connection of the character owner (captured on main thread).</param>
		/// <param name="guildID">Guild ID (captured on main thread).</param>
		/// <param name="partyID">Party ID (captured on main thread).</param>
		/// <param name="friendIDs">Friend character IDs (captured on main thread), or null if none.</param>
		private async Task SendAllCharacterDataAsync(NetworkConnection owner, long guildID, long partyID, List<long> friendIDs)
		{
			if (owner == null || !owner.IsActive)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			try
			{
				// Guild members
				if (guildID > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var guildService))
				{
					DatabaseResult<IReadOnlyList<CharacterGuildData>> guildResult = await guildService.FetchManyAsync(guildID);
					if (guildResult.IsSuccess && guildResult.Data != null && guildResult.Data.Count > 0)
					{
						var addBroadcasts = guildResult.Data.Select(x => new GuildAddBroadcast()
						{
							GuildID = x.GuildID,
							CharacterID = x.CharacterID,
							Rank = (GuildRank)x.Rank,
							Location = x.Location,
						}).ToList();

						TryEnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new GuildAddMultipleBroadcast()
								{
									Members = addBroadcasts,
								}, true, Channel.Reliable);
							}
						});
					}
				}

				// Party members
				if (partyID > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var partyService))
				{
					DatabaseResult<IReadOnlyList<CharacterPartyData>> partyResult = await partyService.FetchManyAsync(partyID);
					if (partyResult.IsSuccess && partyResult.Data != null && partyResult.Data.Count > 0)
					{
						var addBroadcasts = partyResult.Data.Select(x => new PartyAddBroadcast()
						{
							PartyID = x.PartyID,
							CharacterID = x.CharacterID,
							Rank = (PartyRank)x.Rank,
							HealthPCT = x.HealthPCT,
						}).ToList();

						TryEnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new PartyAddMultipleBroadcast()
								{
									Members = addBroadcasts,
								}, true, Channel.Reliable);
							}
						});
					}
				}

				// Friends online status
				if (friendIDs != null && friendIDs.Count > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					List<FriendAddBroadcast> friends = new List<FriendAddBroadcast>();
					foreach (long friendID in friendIDs)
					{
						DatabaseResult<CharacterData?> friendResult = await characterService.FetchAsync(friendID);
						bool online = friendResult.IsSuccess && friendResult.Data.HasValue && friendResult.Data.Value.Online;
						friends.Add(new FriendAddBroadcast()
						{
							CharacterID = friendID,
							Online = online,
						});
					}

					if (friends.Count > 0)
					{
						TryEnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new FriendAddMultipleBroadcast()
								{
									Friends = friends,
								}, true, Channel.Reliable);
							}
						});
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SendAllCharacterDataAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Sends non-database character data (abilities, achievements, inventory, bank, hotkeys) to the owner.
		/// Must be called on the main thread.
		/// </summary>
		private void SendNonDbCharacterData(IPlayerCharacter character)
		{
			if (character == null || character.Owner == null || !character.Owner.IsActive)
			{
				return;
			}

			#region Abilities
			if (character.TryGet(out IAbilityController abilityController))
			{
				List<KnownAbilityAddBroadcast> knownAbilityBroadcasts = new List<KnownAbilityAddBroadcast>();
				List<KnownAbilityEventAddBroadcast> knownAbilityEventBroadcasts = new List<KnownAbilityEventAddBroadcast>();

				if (abilityController.KnownBaseAbilities != null)
				{
					// get base ability templates
					foreach (int templateID in abilityController.KnownBaseAbilities)
					{
						knownAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				if (abilityController.KnownAbilityEvents != null)
				{
					// and event templates
					foreach (int templateID in abilityController.KnownAbilityEvents)
					{
						knownAbilityEventBroadcasts.Add(new KnownAbilityEventAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				// tell the client they have known abilities
				if (knownAbilityBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityAddMultipleBroadcast()
					{
						Abilities = knownAbilityBroadcasts,
					}, true, Channel.Reliable);
				}

				if (knownAbilityEventBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityEventAddMultipleBroadcast()
					{
						AbilityEvents = knownAbilityEventBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Achievements
			if (character.TryGet(out IAchievementController achievementController))
			{
				List<AchievementUpdateBroadcast> achievements = new List<AchievementUpdateBroadcast>();
				foreach (Achievement achievement in achievementController.Achievements.Values)
				{
					achievements.Add(new AchievementUpdateBroadcast()
					{
						TemplateID = achievement.Template.ID,
						Value = achievement.CurrentValue,
						Tier = achievement.CurrentTier,
					});
				}
				if (achievements.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new AchievementUpdateMultipleBroadcast()
					{
						Achievements = achievements,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region InventoryItems
			if (character.TryGet(out IInventoryController inventoryController))
			{
				List<InventorySetItemBroadcast> itemBroadcasts = new List<InventorySetItemBroadcast>();

				foreach (Item item in inventoryController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new InventorySetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region BankItems
			if (character.TryGet(out IBankController bankController))
			{
				List<BankSetItemBroadcast> itemBroadcasts = new List<BankSetItemBroadcast>();

				foreach (Item item in bankController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new BankSetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Hotkeys
			if (character.Hotkeys != null)
			{
				List<HotkeySetBroadcast> hotkeyBroadcasts = new List<HotkeySetBroadcast>();

				foreach (HotkeyData hotkey in character.Hotkeys)
				{
					// just in case..
					if (hotkey == null)
					{
						continue;
					}
					// create the new hotkey broadcast
					hotkeyBroadcasts.Add(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						}
					});
				}

				// tell the client they have hotkeys
				if (hotkeyBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new HotkeySetMultipleBroadcast()
					{
						Hotkeys = hotkeyBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterName">Name of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(characterName.ToLowerInvariant(), out var character))
			{
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character ID.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterID">ID of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(long characterID, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out var character))
			{
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}
	}
}