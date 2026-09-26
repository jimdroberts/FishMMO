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
		/// <param name="characterID">The character the payload is for (captured on main thread).</param>
		/// <param name="guildID">Guild ID (captured on main thread).</param>
		/// <param name="partyID">Party ID (captured on main thread).</param>
		/// <param name="friendIDs">Friend character IDs (captured on main thread), or null if none.</param>
		private async Task SendAllCharacterDataAsync(NetworkConnection owner, long characterID, long guildID, long partyID, List<long> friendIDs)
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
					if (!guildResult.IsSuccess)
					{
						await Log.Warning("CharacterSystem", $"SendAllCharacterDataAsync: could not read the roster of guild {guildID} for character {characterID}: [{guildResult.ErrorCode}] {guildResult.ErrorMessage}");
					}
					else if (guildResult.Data != null && guildResult.Data.Count > 0)
					{
						IReadOnlyList<CharacterGuildData> members = guildResult.Data;

						/* The connecting character's own membership row, found in the roster that
						 * was just read rather than fetched again — it is guaranteed to be in
						 * there, since this roster is the guild they belong to. */
						byte viewerRankOrder = 0;
						for (int i = 0; i < members.Count; ++i)
						{
							if (members[i].CharacterID == characterID)
							{
								viewerRankOrder = members[i].Rank;
								break;
							}
						}

						/* The officer note is filtered HERE, where the payload is built, not in
						 * the panel. A client that may not read the note never receives it, so
						 * there is nothing in the packet for a modified client or a packet capture
						 * to recover. The viewer's permissions come from the guild's own rank
						 * rows, so a guild that has taken ViewOfficerNotes off a rank stops
						 * sending the column to that rank on the very next connect. */
						GuildPermissions viewerPermissions = GuildPermissions.None;
						byte leaderRankOrder = 0;
						GuildRankEntry[] rankEntries = Array.Empty<GuildRankEntry>();
						/* Whether the ladder was actually read. A failed read used to fall through with
						 * the defaults above and publish them: an empty ladder to the client, and None /
						 * 0 into the server's own cache of the character's standing — overwriting any
						 * mask GuildSystem had already published, so a guild leader was refused by its
						 * pre-filters until the ladder was next published, because a read failed.
						 * Unread, neither is published; the roster still goes out, and the officer notes
						 * stay withheld (None is the safe side). */
						bool ladderRead = false;
						if (Server.Database.ServiceRegistry.TryGet<IGuildRankService>(out var rankService))
						{
							DatabaseResult<IReadOnlyList<GuildRankData>> ladderResult = await rankService.FetchManyAsync(guildID);
							if (!ladderResult.IsSuccess)
							{
								await Log.Warning("CharacterSystem", $"SendAllCharacterDataAsync: could not read the rank ladder of guild {guildID} for character {characterID}: [{ladderResult.ErrorCode}] {ladderResult.ErrorMessage}");
							}
							else if (ladderResult.Data != null)
							{
								ladderRead = true;
								rankEntries = new GuildRankEntry[ladderResult.Data.Count];
								for (int i = 0; i < ladderResult.Data.Count; ++i)
								{
									GuildRankData rank = ladderResult.Data[i];
									if (rank.RankOrder > leaderRankOrder)
									{
										leaderRankOrder = rank.RankOrder;
									}
									if (rank.RankOrder == viewerRankOrder)
									{
										viewerPermissions = (GuildPermissions)rank.Permissions;
									}

									rankEntries[i] = new GuildRankEntry()
									{
										RankOrder = rank.RankOrder,
										Name = rank.Name ?? string.Empty,
										Permissions = rank.Permissions,
									};
								}
							}
						}

						bool mayReadOfficerNotes = (viewerPermissions & GuildPermissions.ViewOfficerNotes) == GuildPermissions.ViewOfficerNotes;

						var addBroadcasts = members.Select(x => new GuildAddEntry()
						{
							CharacterID = x.CharacterID,
							RankOrder = x.Rank,
							Location = x.Location ?? string.Empty,
							RaceID = x.RaceID,
							PublicNote = x.PublicNote ?? string.Empty,
							OfficerNote = mayReadOfficerNotes ? (x.OfficerNote ?? string.Empty) : string.Empty,
							LastOnlineUnixSeconds = x.LastOnlineUtc > DateTime.UnixEpoch
								? new DateTimeOffset(DateTime.SpecifyKind(x.LastOnlineUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()
								: 0L,
						}).ToList();

						GuildPermissions capturedPermissions = viewerPermissions;
						byte capturedRankOrder = viewerRankOrder;
						byte capturedLeaderRankOrder = leaderRankOrder;

						TryEnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new GuildAddMultipleBroadcast()
								{
									GuildID = guildID,
									Members = addBroadcasts.ToArray(),
								}, true, Channel.Reliable);

								/* Sent outside the guild update pump, which records what each client
								 * holds and sends deltas against that record. This roster, and the
								 * ladder below, may be older or newer than it, so the pump's next
								 * delivery to this character goes out whole. */
								if (Server.BehaviourRegistry.TryGet(out IGuildSystem<NetworkConnection> guildSystem))
								{
									guildSystem.ForgetGuildDeliveryBaselines(characterID);
								}

								if (!ladderRead)
								{
									return;
								}

								/* The ladder goes out WITH the roster, not on request. Every
								 * roster row renders its rank by NAME, and the name lives only in
								 * the ladder — a client that had the roster but not the ladder
								 * would render a column of bare numbers until something else
								 * happened to the guild. */
								Server.NetworkWrapper.Broadcast(owner, new GuildRankListBroadcast()
								{
									Ranks = rankEntries,
									ViewerRankOrder = capturedRankOrder,
									ViewerPermissions = (long)capturedPermissions,
									LeaderRankOrder = capturedLeaderRankOrder,
								}, true, Channel.Reliable);

								/* Refresh the server's own cache of this character's standing, so
								 * the cheap pre-filters in GuildSystem start out agreeing with the
								 * database rather than with whatever the character loaded with. */
								if (owner.FirstObject != null)
								{
									IGuildController guildController = owner.FirstObject.GetComponent<IGuildController>();
									if (guildController != null && guildController.ID == guildID)
									{
										guildController.RankOrder = capturedRankOrder;
										guildController.Permissions = capturedPermissions;
										guildController.LeaderRankOrder = capturedLeaderRankOrder;
									}
								}
							}
						});
					}
				}

				// Party members
				if (partyID > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var partyService))
				{
					DatabaseResult<IReadOnlyList<CharacterPartyData>> partyResult = await partyService.FetchManyAsync(partyID);
					if (!partyResult.IsSuccess)
					{
						await Log.Warning("CharacterSystem", $"SendAllCharacterDataAsync: could not read the roster of party {partyID} for character {characterID}: [{partyResult.ErrorCode}] {partyResult.ErrorMessage}");
					}
					else if (partyResult.Data != null && partyResult.Data.Count > 0)
					{
						var addBroadcasts = partyResult.Data.Select(x => new PartyAddEntry()
						{
							CharacterID = x.CharacterID,
							Rank = (PartyRank)x.Rank,
							HealthPCT = PartyVitalsQuantiser.FractionToByte(x.HealthPCT),
						}).ToList();

						TryEnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new PartyAddMultipleBroadcast()
								{
									PartyID = partyID,
									Members = addBroadcasts.ToArray(),
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
						if (!friendResult.IsSuccess)
						{
							/* Unknown, and reported as such here. Still listed — as offline — because
							 * this payload is what puts the friend in the client's list at all, and a
							 * dropped entry would hide the friend for the session; offline is the
							 * guess their own status change corrects. */
							await Log.Warning("CharacterSystem", $"SendAllCharacterDataAsync: could not read friend {friendID} of character {characterID}: [{friendResult.ErrorCode}] {friendResult.ErrorMessage}; listing them as offline.");
						}
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
									Friends = friends.ToArray(),
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
						Abilities = knownAbilityBroadcasts.ToArray(),
					}, true, Channel.Reliable);
				}

				if (knownAbilityEventBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityEventAddMultipleBroadcast()
					{
						AbilityEvents = knownAbilityEventBroadcasts.ToArray(),
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
						Achievements = achievements.ToArray(),
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Quests
			if (character.TryGet(out IQuestController questController))
			{
				List<QuestUpdateBroadcast> questBroadcasts = new List<QuestUpdateBroadcast>();
				foreach (QuestInstance quest in questController.Quests.Values)
				{
					if (quest.Template == null)
					{
						continue;
					}

					long[] objectiveValues = null;
					if (quest.Objectives != null && quest.Objectives.Count > 0)
					{
						objectiveValues = new long[quest.Objectives.Count];
						for (int i = 0; i < quest.Objectives.Count; i++)
						{
							objectiveValues[i] = quest.Objectives[i].CurrentValue;
						}
					}

					questBroadcasts.Add(new QuestUpdateBroadcast()
					{
						TemplateID = quest.Template.ID,
						Status = quest.Status,
						ObjectiveValues = objectiveValues,
					});
				}
				if (questBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new QuestUpdateMultipleBroadcast()
					{
						Quests = questBroadcasts.ToArray(),
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
						Items = itemBroadcasts.ToArray(),
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
						Items = itemBroadcasts.ToArray(),
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
						Hotkeys = hotkeyBroadcasts.ToArray(),
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
				Server.NetworkWrapper.Broadcast(character.Owner, msg);
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
				Server.NetworkWrapper.Broadcast(character.Owner, msg);
				return true;
			}
			return false;
		}
	}
}