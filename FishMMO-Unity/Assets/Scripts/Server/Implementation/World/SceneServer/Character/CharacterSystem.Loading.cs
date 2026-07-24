using FishNet.Connection;
using FishNet.Object;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character loading and scene-entry logic: authentication, async DB fetch, prefab instantiation, scene validation, and client broadcast handshakes.
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Minimum seconds between auth-callback character load requests per connection.
		/// Prevents a misbehaving or compromised client from spamming expensive DB load operations.
		/// </summary>
		private const float AuthCallbackCooldownSeconds = 2.0f;

		/// <summary>
		/// Tracks the last auth-callback time per account name for rate limiting.
		/// Keying by account name prevents a single user from bypassing the limit by
		/// opening multiple connections.
		/// Entries are removed when the connection disconnects via OnRemoteConnectionStopped.
		/// </summary>
		private readonly ConcurrentDictionary<string, DateTime> authCallbackLastTimeByAccount =
			new ConcurrentDictionary<string, DateTime>();

		/// <summary>
		/// Tracks the last scene-unload broadcast time per connection ClientId for rate limiting.
		/// Scene unload is per-connection, not per-account; a separate dictionary from
		/// authCallbackLastTimeByAccount.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> sceneUnloadLastTimeByClientId =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Tracks the last validated-scene broadcast time per connection ClientId for rate limiting.
		/// Prevents a malicious client from spamming <see cref="OnClientValidatedSceneBroadcastReceived"/>,
		/// which triggers expensive character-spawn and mapping operations.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> validatedSceneLastTimeByClientId =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Minimum seconds between validated-scene broadcasts per connection.
		/// </summary>
		private const float ValidatedSceneBroadcastCooldownSeconds = 2.0f;

		/// <summary>
		/// Handles client authentication results, loads character data and initiates scene loading.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			DateTime nowUtc = DateTime.UtcNow;

			// Resolve account name first so the rate limit can be applied per-account.
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// Per-account rate limit: prevent repeated auth callbacks from triggering
			// expensive DB load operations in rapid succession. Keying by account name
			// prevents a single user from bypassing the limit by opening multiple connections.
			bool wasCoolingDown = false;
			authCallbackLastTimeByAccount.AddOrUpdate(
				accountName,
				nowUtc,
				(_, lastTime) =>
				{
					if ((nowUtc - lastTime).TotalSeconds < AuthCallbackCooldownSeconds)
					{
						wasCoolingDown = true;
						return lastTime; // Don't update timestamp if cooling down.
					}
					return nowUtc;
				});
			if (wasCoolingDown)
			{
				Log.Warning("CharacterSystem", $"Auth callback rate-limited for account {accountName}");
				return;
			}

			// Is the character already loading?
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (mappingData.WaitingSceneLoadCharacters.ContainsKey(conn))
			{
				return;
			}
			if (!authenticated ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// Resolve server ID on the main thread — required for TryClaimAsync.
			long serverID = 0;
			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				serverID = runtimeData.ID;
			}

			if (serverID <= 0)
			{
				Log.Error("CharacterSystem", "Authenticator_OnClientAuthenticationResult: Server ID is invalid, cannot claim session.");
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (!EnqueueAsyncWork(() => LoadCharacterAsync(conn, accountName, characterService, serverID)))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
			}
		}

		/// <summary>
		/// Asynchronously fetches the selected character and ALL related sub-entity data
		/// from the database using a Unit of Work for a consistent snapshot, then queues
		/// the main-thread instantiation and scene loading.
		/// </summary>
		private async Task LoadCharacterAsync(NetworkConnection conn, string accountName, ICharacterService characterService, long serverID)
		{
			CharacterData charData = default;
			Guid sessionToken = Guid.Empty;

			try
			{
				var serviceRegistry = Server.Database.ServiceRegistry;

				if (!serviceRegistry.TryGet<IUnitOfWorkService>(out var unitOfWorkService))
				{
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to resolve IUnitOfWorkService.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				// --- Fetch all sub-entity data within a single Unit of Work (consistent snapshot) ---
				IReadOnlyList<CharacterInventoryData> inventoryData = null;
				IReadOnlyList<CharacterBankData> bankData = null;
				IReadOnlyList<CharacterEquipmentData> equipmentData = null;
				IReadOnlyList<CharacterAttributeData> attributeData = null;
				IReadOnlyList<CharacterAbilityData> abilityData = null;
				IReadOnlyList<CharacterKnownAbilityData> knownAbilityData = null;
				IReadOnlyList<CharacterAchievementData> achievementData = null;
				IReadOnlyList<CharacterFriendData> friendData = null;
				CharacterGuildData? guildData = null;
				CharacterPartyData? partyData = null;
				IReadOnlyList<CharacterHotkeyData> hotkeyData = null;
				IReadOnlyList<CharacterBuffData> buffData = null;
				IReadOnlyList<CharacterFactionData> factionData = null;
				IReadOnlyList<CharacterQuestData> questData = null;

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to begin UnitOfWork for character load.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// Fetch only the selected character for the account (single row, no full list)
					DatabaseResult<CharacterData?> fetchResult = await characterService.FetchByAccountAsync(accountName, selected: true);
					if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
					{
						TryEnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								Log.Debug("CharacterSystem", "No selected character found for account.");
								conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
							}
						});
						return;
					}

					charData = fetchResult.Data.Value;
					long characterID = charData.ID;

					// --- Claim the character session BEFORE loading sub-entity data ---
					// This is the gate: if another server already owns this character,
					// we fail fast before spending resources on 13 sub-entity fetches,
					// prefab instantiation, and scene loading.
					DatabaseResult<Guid> claimResult = await characterService.TryClaimAsync(characterID, serverID);
					if (!claimResult.IsSuccess)
					{
						await Log.Error("CharacterSystem", $"TryClaimAsync failed for character {characterID}: {claimResult.ErrorCode} - {claimResult.ErrorMessage}");
						TryEnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							}
						});
						return;
					}

					sessionToken = claimResult.Data;

					// All fetches share the same ambient DbContext/transaction (sequential, consistent snapshot)
					if (serviceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService))
					{
						var result = await inventoryService.FetchAsync(characterID);
						if (result.IsSuccess) inventoryData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBankService>(out var bankService))
					{
						var result = await bankService.FetchAsync(characterID);
						if (result.IsSuccess) bankData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterEquipmentService>(out var equipmentService))
					{
						var result = await equipmentService.FetchAsync(characterID);
						if (result.IsSuccess) equipmentData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAttributeService>(out var attributeService))
					{
						var result = await attributeService.FetchAsync(characterID);
						if (result.IsSuccess) attributeData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
					{
						var result = await abilityService.FetchAsync(characterID);
						if (result.IsSuccess) abilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
					{
						var result = await knownAbilityService.FetchAsync(characterID);
						if (result.IsSuccess) knownAbilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAchievementService>(out var achievementService))
					{
						var result = await achievementService.FetchAsync(characterID);
						if (result.IsSuccess) achievementData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFriendService>(out var friendService))
					{
						var result = await friendService.FetchAsync(characterID);
						if (result.IsSuccess) friendData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterGuildService>(out var guildService))
					{
						var result = await guildService.FetchAsync(characterID);
						if (result.IsSuccess) guildData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterPartyService>(out var partyService))
					{
						var result = await partyService.FetchAsync(characterID);
						if (result.IsSuccess) partyData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterHotkeyService>(out var hotkeyService))
					{
						var result = await hotkeyService.FetchAsync(characterID);
						if (result.IsSuccess) hotkeyData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBuffService>(out var buffService))
					{
						var result = await buffService.FetchAsync(characterID);
						if (result.IsSuccess) buffData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFactionService>(out var factionService))
					{
						var result = await factionService.FetchAsync(characterID);
						if (result.IsSuccess) factionData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterQuestService>(out var questService))
					{
						var result = await questService.FetchAsync(characterID);
						if (result.IsSuccess) questData = result.Data;
					}

					// Read-only: commit to cleanly close the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Warning("CharacterSystem", $"LoadCharacterAsync: CommitAsync DB error for character {characterID}: {commitResult.ErrorCode} - {commitResult.ErrorMessage}");
					}
				}

				// Marshal back to main thread for Unity object instantiation
				var loadContext = new CharacterLoadContext(
					conn, charData, sessionToken, serverID,
					inventoryData, bankData, equipmentData,
					attributeData, abilityData, knownAbilityData,
					achievementData, friendData,
					guildData, partyData,
					hotkeyData, buffData, factionData, questData);
				TryEnqueueMainThread(() => InstantiateAndLoadCharacter(loadContext));
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"LoadCharacterAsync failed: {ex}");

				// If we successfully claimed the session before the failure, release it
				if (sessionToken != Guid.Empty && charData.ID > 0)
				{
					try
					{
						await ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken);
					}
					catch (Exception releaseEx)
					{
						await Log.Error("CharacterSystem", $"LoadCharacterAsync: Failed to release session after error for character {charData.ID}: {releaseEx.Message}");
					}
				}

				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					}
				});
			}
		}

		/// <summary>
		/// Instantiates the player character from CharacterData, populates all controllers
		/// with pre-fetched sub-entity data, and initiates scene loading.
		/// Must be called on the main Unity thread.
		/// </summary>
		/// <param name="ctx">Bundled character data, session info, and all sub-entity data for loading.</param>
		private void InstantiateAndLoadCharacter(CharacterLoadContext ctx)
		{
			NetworkConnection conn = ctx.Connection;
			CharacterData charData = ctx.CharacterData;
			Guid sessionToken = ctx.SessionToken;
			long serverID = ctx.ServerID;
			IReadOnlyList<CharacterInventoryData> inventoryData = ctx.InventoryData;
			IReadOnlyList<CharacterBankData> bankData = ctx.BankData;
			IReadOnlyList<CharacterEquipmentData> equipmentData = ctx.EquipmentData;
			IReadOnlyList<CharacterAttributeData> attributeData = ctx.AttributeData;
			IReadOnlyList<CharacterAbilityData> abilityData = ctx.AbilityData;
			IReadOnlyList<CharacterKnownAbilityData> knownAbilityData = ctx.KnownAbilityData;
			IReadOnlyList<CharacterAchievementData> achievementData = ctx.AchievementData;
			IReadOnlyList<CharacterFriendData> friendData = ctx.FriendData;
			CharacterGuildData? guildData = ctx.GuildData;
			CharacterPartyData? partyData = ctx.PartyData;
			IReadOnlyList<CharacterHotkeyData> hotkeyData = ctx.HotkeyData;
			IReadOnlyList<CharacterBuffData> buffData = ctx.BuffData;
			IReadOnlyList<CharacterFactionData> factionData = ctx.FactionData;
			IReadOnlyList<CharacterQuestData> questData = ctx.QuestData;

			if (conn == null || !conn.IsActive)
			{
				// Connection died between async load and main-thread marshal — release the claimed session
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (mappingData.CharactersByID.ContainsKey(charData.ID))
			{
				Log.Debug("CharacterSystem", $"{charData.ID} is already loaded or loading.");
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			OnBeforeLoadCharacter?.Invoke(conn, charData.ID);

			// Look up the race template and instantiate the character prefab
			RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(charData.RaceID);
			if (raceTemplate == null || raceTemplate.Prefab == null)
			{
				Log.Debug("CharacterSystem", "Failed to fetch character: invalid race template.");
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Vector3 position = new Vector3(charData.X, charData.Y, charData.Z);
			Quaternion rotation = new Quaternion(charData.RotX, charData.RotY, charData.RotZ, charData.RotW);

			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(
				raceTemplate.Prefab, position, rotation, true);
			if (nob == null)
			{
				Log.Debug("CharacterSystem", "Failed to instantiate character prefab.");
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			IPlayerCharacter character = nob.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				Log.Debug("CharacterSystem", "Failed to get IPlayerCharacter from instantiated prefab.");
				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Initialize motor at the spawn position to match the teleport path.
			// Without this, the motor's internal velocity/tick/grounding state is uninitialized
			// and the first tick may miss ground detection with the 0.005f minimum probe.
			character.Motor.SetPositionAndRotationAndVelocity(position, rotation, Vector3.zero);

			// Populate character fields from CharacterData
			character.ID = charData.ID;
			character.CharacterName = charData.Name;
			character.CharacterNameLower = charData.NameLowercase;
			character.Account = charData.Account;
			character.Version = charData.Version;
			character.WorldServerID = charData.WorldServerID;
			character.AccessLevel = (AccessLevel)(int)charData.AccessLevel;
			character.TimeCreated = charData.TimeCreated;
			character.RaceID = charData.RaceID;
			character.ModelIndex = charData.ModelIndex;
			character.SceneName = charData.SceneName;
			character.SceneHandle = charData.SceneHandle;
			character.BindScene = charData.BindScene;
			character.BindPosition = new Vector3(charData.BindX, charData.BindY, charData.BindZ);
			character.InstanceID = charData.InstanceID;
			character.Flags = charData.Flags;

			// Combat is transient state — never persist or restore across sessions.
			// The save path also strips this flag, but sanitize on load for defense-in-depth.
			character.DisableFlags(CharacterFlags.IsInCombat);

			if (character.IsInInstance())
			{
				character.InstancePosition = new Vector3(charData.InstanceX, charData.InstanceY, charData.InstanceZ);
				character.InstanceRotation = new Quaternion(charData.InstanceRotX, charData.InstanceRotY, charData.InstanceRotZ, charData.InstanceRotW);
			}

			// --- Populate all controllers from pre-fetched DB data ---

			// Attributes
			if (attributeData != null && attributeData.Count > 0 &&
				character.TryGet(out ICharacterAttributeController attrController))
			{
				foreach (CharacterAttributeData attr in attributeData)
				{
					// Use the template to determine if this is a resource attribute,
					// NOT CurrentValue. A resource with CurrentValue == 0 (dead character,
					// empty mana) is still a resource and must go into the ResourceAttributes
					// dictionary. Using CurrentValue > 0 puts dead-player health into the
					// base-attribute dictionary, leaving the real resource at Version=0,
					// which causes STALE_STATE on every save.
					CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(attr.TemplateID);
					if (template != null && template.IsResourceAttribute)
					{
						attrController.SetResourceAttribute(attr.TemplateID, attr.Value, attr.CurrentValue, null);
						if (attrController.ResourceAttributes.TryGetValue(attr.TemplateID, out var resAttr))
						{
							resAttr.Version = attr.Version;
						}
					}
					else
					{
						attrController.SetAttribute(attr.TemplateID, attr.Value);
						if (attrController.Attributes.TryGetValue(attr.TemplateID, out var charAttr))
						{
							charAttr.Version = attr.Version;
						}
					}
				}
			}

			// Inventory
			if (inventoryData != null && inventoryData.Count > 0 &&
				character.TryGet(out IInventoryController inventoryController))
			{
				foreach (CharacterInventoryData inv in inventoryData)
				{
					Item item = new Item(inv.ID, inv.Seed, inv.TemplateID, inv.Amount);
					item.Version = inv.Version;
					inventoryController.SetItemSlot(item, inv.Slot);
				}
			}

			// Bank
			if (bankData != null && bankData.Count > 0 &&
				character.TryGet(out IBankController bankController))
			{
				foreach (CharacterBankData bank in bankData)
				{
					Item item = new Item(bank.ID, bank.Seed, bank.TemplateID, bank.Amount);
					item.Version = bank.Version;
					bankController.SetItemSlot(item, bank.Slot);
				}
			}

			// Equipment
			if (equipmentData != null && equipmentData.Count > 0 &&
				character.TryGet(out IEquipmentController equipmentController))
			{
				foreach (CharacterEquipmentData equip in equipmentData)
				{
					Item item = new Item(equip.ID, equip.Seed, equip.TemplateID, equip.Amount);
					item.Version = equip.Version;
					equipmentController.SetItemSlot(item, equip.Slot);

					// Apply equipment attribute modifiers via externalModifier
					if (item.IsEquippable)
					{
						item.Equippable.Equip(character);
					}
				}
			}

			// Abilities (crafted ability instances)
			if (abilityData != null && abilityData.Count > 0 &&
				character.TryGet(out IAbilityController abilityController))
			{
				foreach (CharacterAbilityData ability in abilityData)
				{
					Ability newAbility = new Ability(ability.ID, ability.TemplateID, ability.AbilityEvents);
					newAbility.Version = ability.Version;
					abilityController.LearnAbility(newAbility, ability.Cooldown);
				}

				// Known base abilities
				if (knownAbilityData != null && knownAbilityData.Count > 0)
				{
					List<BaseAbilityTemplate> knownTemplates = new List<BaseAbilityTemplate>();
					foreach (CharacterKnownAbilityData known in knownAbilityData)
					{
						BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(known.TemplateID);
						if (template != null)
						{
							knownTemplates.Add(template);
						}
					}
					if (knownTemplates.Count > 0)
					{
						abilityController.LearnBaseAbilities(knownTemplates);
					}
				}
			}

			// Achievements
			if (achievementData != null && achievementData.Count > 0 &&
				character.TryGet(out IAchievementController achievementController))
			{
				foreach (CharacterAchievementData achievement in achievementData)
				{
					achievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value, true);
					if (achievementController.Achievements.TryGetValue(achievement.TemplateID, out Achievement ach))
					{
						ach.Version = achievement.Version;
					}
				}
			}

			// Friends
			if (friendData != null && friendData.Count > 0 &&
				character.TryGet(out IFriendController friendController))
			{
				foreach (CharacterFriendData friend in friendData)
				{
					friendController.AddFriend(friend.FriendCharacterID);
				}
			}

			// Guild
			if (guildData.HasValue &&
				character.TryGet(out IGuildController guildController))
			{
				guildController.ID = guildData.Value.GuildID;
				guildController.Rank = (GuildRank)guildData.Value.Rank;
			}

			// Party
			if (partyData.HasValue &&
				character.TryGet(out IPartyController partyController))
			{
				partyController.ID = partyData.Value.PartyID;
				partyController.Rank = (PartyRank)partyData.Value.Rank;
			}

			// Hotkeys
			if (hotkeyData != null && hotkeyData.Count > 0)
			{
				foreach (CharacterHotkeyData hotkey in hotkeyData)
				{
					if (hotkey.Slot >= 0 && hotkey.Slot < character.Hotkeys.Count)
					{
						character.Hotkeys[hotkey.Slot] = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						};
					}
				}
			}

			// Buffs
			if (buffData != null && buffData.Count > 0 &&
				character.TryGet(out IBuffController buffController))
			{
				float loadTickDelta = (float)Server.NetworkWrapper.NetworkManager.TimeManager.TickDelta;
				uint loadCurrentTick = Server.NetworkWrapper.NetworkManager.TimeManager.LocalTick;
				foreach (CharacterBuffData buff in buffData)
				{
					uint expiryTick = loadCurrentTick + (uint)Math.Max(1.0, Math.Ceiling(buff.RemainingTime / loadTickDelta));
					uint nextTickTick = loadCurrentTick + (uint)Math.Max(1.0, Math.Ceiling(buff.TickTime / loadTickDelta));
					Buff newBuff = new Buff(buff.TemplateID, expiryTick, nextTickTick, loadTickDelta, buff.Stacks, buff.TickCount);
					newBuff.Version = buff.Version;
					buffController.Apply(newBuff);
				}
			}

			// Factions
			if (factionData != null && factionData.Count > 0 &&
				character.TryGet(out IFactionController factionController))
			{
				foreach (CharacterFactionData faction in factionData)
				{
					factionController.SetFaction(faction.TemplateID, faction.Value, true);
					if (factionController.Factions.TryGetValue(faction.TemplateID, out Faction fac))
					{
						fac.Version = faction.Version;
					}
				}
			}

			// Quests
			if (questData != null && questData.Count > 0 &&
				character.TryGet(out IQuestController questController))
			{
				foreach (CharacterQuestData quest in questData)
				{
					QuestTemplate questTemplate = QuestTemplate.Get<QuestTemplate>(quest.TemplateID);
					if (questTemplate == null)
					{
						continue;
					}

					long[] objectiveValues = ParseObjectiveValues(quest.ObjectiveValues);
					questController.SetQuest(questTemplate, (QuestStatus)quest.Status, objectiveValues);
				}
			}

			string sceneName = character.SceneName;
			int sceneHandle = character.SceneHandle;

			// Check if the character is in an instance or not.
			if (character.IsInInstance())
			{
				// Have the player enter the instance.
				sceneName = character.InstanceSceneName;
				sceneHandle = character.InstanceSceneHandle;
			}

			// Check if the scene is valid, loaded, and cached properly
			if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneHandle, out ISceneInstanceDetails instance) &&
				sceneServerSystem.TryLoadSceneForConnection(conn, instance))
			{
				OnAfterLoadCharacter?.Invoke(conn, character);

				// Store the session token now that we've reached the success path.
				// From here, RemoveCharacterConnectionMapping / SceneManager_OnClientLoadedStartScenes
				// will handle cleanup via the SessionTokens dictionary.
				mappingData.SessionTokens[charData.ID] = new CharacterSessionInfo(sessionToken, serverID);
				mappingData.WaitingSceneLoadCharacters.Add(conn, character);
			}
			else
			{
				Log.Debug("CharacterSystem", "Failed to load scene for connection.");

				EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(charData.ID, serverID, sessionToken));

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				conn.Disconnect(false);
			}
		}

		/// <summary>
		/// Called when a client loads world scenes after connecting. Validates character and scene, then notifies client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="asServer">True if loaded as server.</param>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			// Validate the connection has a character ready to play.
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) ||
				!mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Get the characters scene
			Scene scene;
			if (character.IsInInstance())
			{
				scene = SceneManager.GetScene(character.InstanceSceneHandle);
			}
			else
			{
				scene = SceneManager.GetScene(character.SceneHandle);
			}

			// Validate the characters scene.
			if (scene == null ||
				!scene.IsValid() ||
				!scene.isLoaded)
			{
				Log.Debug("CharacterSystem", "Scene is not valid.");

				mappingData.WaitingSceneLoadCharacters.Remove(conn);

				TryExtractAndReleaseSession(mappingData, character.ID);

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ClientValidatedSceneBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Called when a client completely finishes loading into a world scene. Spawns character and sets online status.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientValidatedSceneBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientValidatedSceneBroadcastReceived(NetworkConnection conn, ClientValidatedSceneBroadcast msg, Channel channel)
		{
			// Rate-limit validated-scene broadcasts to prevent spam.
			DateTime now2 = DateTime.UtcNow;
			if (validatedSceneLastTimeByClientId.TryGetValue(conn.ClientId, out DateTime lastValidated) &&
				(now2 - lastValidated).TotalSeconds < ValidatedSceneBroadcastCooldownSeconds)
				return;
			validatedSceneLastTimeByClientId[conn.ClientId] = now2;

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			if (mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				if (character == null)
				{
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Remove the waiting scene load character
				mappingData.WaitingSceneLoadCharacters.Remove(conn);

				// Add a connection->character map for ease of use
				mappingData.ConnectionCharacters[conn] = character;
				// Add a characterName->character map for ease of use
				mappingData.CharactersByID[character.ID] = character;
				mappingData.CharactersByLowerCaseName[character.CharacterNameLower] = character;
				// Add a worldID<characterID->character> map for ease of use
				if (!mappingData.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
				{
					mappingData.CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, IPlayerCharacter>());
				}
				characters[character.ID] = character;

				// Get the characters scene
				Scene scene;
				if (character.IsInInstance())
				{
					scene = SceneManager.GetScene(character.InstanceSceneHandle);
				}
				else
				{
					scene = SceneManager.GetScene(character.SceneHandle);
				}

				// Validate the scene
				if (scene == null ||
					!scene.IsValid() ||
					!scene.isLoaded)
				{
					Log.Debug("CharacterSystem", "Scene is not valid.");

					// Clean up all mappings that were just added
					mappingData.ConnectionCharacters.Remove(conn);
					mappingData.CharactersByID.Remove(character.ID);
					mappingData.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
					if (mappingData.CharactersByWorld.TryGetValue(character.WorldServerID, out var worldChars))
					{
						worldChars.Remove(character.ID);
					}

					// Release the session for this character
					TryExtractAndReleaseSession(mappingData, character.ID);

					Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor?.SetPhysicsScene(scene.GetPhysicsScene());

				// Character becomes mortal when loaded into the scene
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// Ensure the game object is active, pooled objects are disabled
				character.GameObject.SetActive(true);

				// Ensure the character is marked as loaded so that controllers can check this flag to prevent actions before the character is fully in the world
				character.EnableFlags(CharacterFlags.IsLoaded);

				// If the player was dead when they loaded into the scene,
				// re-send the death broadcast so the death dialog reappears.
				// Handles reconnect-while-dead and scene transitions.
				if (character.IsFlagged(CharacterFlags.IsDead))
				{
					Server.NetworkWrapper.Broadcast(conn,
						new DeathBroadcast(), true, FishNet.Transporting.Channel.Reliable);
				}

				// Spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				OnSpawnCharacter?.Invoke(conn, character, scene);

				OnConnect?.Invoke(conn, character);

				// Send non-DB data immediately on the main thread
				SendNonDbCharacterData(character);

				// Capture social IDs on the main thread for async DB fetch
				long guildID = 0;
				if (character.TryGet(out IGuildController gc))
				{
					guildID = gc.ID;
				}
				long partyID = 0;
				if (character.TryGet(out IPartyController pc))
				{
					partyID = pc.ID;
				}
				List<long> friendIDs = null;
				if (character.TryGet(out IFriendController fc) &&
					fc.Friends != null &&
					fc.Friends.Count > 0)
				{
					friendIDs = new List<long>(fc.Friends);
				}
				NetworkConnection owner = character.Owner;

				// Enqueue only the DB-dependent social data fetch
				if (!EnqueueAsyncWork(() => SendAllCharacterDataAsync(owner, guildID, partyID, friendIDs)))
				{
					Log.Warning("CharacterSystem", $"Failed to enqueue social data broadcast fetch for character {character.ID}.");
				}

				//Log.Debug("CharacterSystem", character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
		}

		/// <summary>
		/// The client notified the server it unloaded scenes. Disconnects connection if character is not loaded.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientScenesUnloadedBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientScenesUnloadedBroadcastReceived(NetworkConnection conn, ClientScenesUnloadedBroadcast msg, Channel channel)
		{
			if (msg.UnloadedScenes == null || msg.UnloadedScenes.Length == 0)
			{
				Log.Debug("CharacterSystem", "No unloaded scenes received.");
				return;
			}

			// Rate-limit scene unload broadcasts to prevent spam disconnects.
			DateTime now2 = DateTime.UtcNow;
			if (sceneUnloadLastTimeByClientId.TryGetValue(conn.ClientId, out DateTime lastUnload) &&
				(now2 - lastUnload).TotalSeconds < 5.0)
				return;
			sceneUnloadLastTimeByClientId[conn.ClientId] = now2;

			// Check if the connection has a character loaded.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.ConnectionCharacters.TryGetValue(conn, out var character))
			{
				return;
			}

			//Log.Debug($"Connection unloaded scene: {msg.UnloadedScenes[0].Name}|{msg.UnloadedScenes[0].Handle}");

			// Otherwise disconnect the connection.
			conn.Disconnect(false);
		}

		/// <summary>
		/// Parses a comma-separated objective values string into a long array.
		/// </summary>
		private static long[] ParseObjectiveValues(string objectiveValues)
		{
			if (string.IsNullOrEmpty(objectiveValues))
			{
				return Array.Empty<long>();
			}

			string[] parts = objectiveValues.Split(',');
			long[] values = new long[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				long.TryParse(parts[i], out values[i]);
			}
			return values;
		}

		/// <summary>
		/// Bundles all pre-fetched character data from the async DB load for main-thread instantiation.
		/// </summary>
		private sealed class CharacterLoadContext
		{
			/// <summary>Network connection of the owning client.</summary>
			public readonly NetworkConnection Connection;
			/// <summary>Core character data (name, position, scene, etc.).</summary>
			public readonly CharacterData CharacterData;
			/// <summary>Session ownership token for the claimed character.</summary>
			public readonly Guid SessionToken;
			/// <summary>ID of the server claiming this character session.</summary>
			public readonly long ServerID;
			/// <summary>Pre-fetched inventory item data.</summary>
			public readonly IReadOnlyList<CharacterInventoryData> InventoryData;
			/// <summary>Pre-fetched bank item data.</summary>
			public readonly IReadOnlyList<CharacterBankData> BankData;
			/// <summary>Pre-fetched equipment item data.</summary>
			public readonly IReadOnlyList<CharacterEquipmentData> EquipmentData;
			/// <summary>Pre-fetched attribute and resource data.</summary>
			public readonly IReadOnlyList<CharacterAttributeData> AttributeData;
			/// <summary>Pre-fetched crafted ability data.</summary>
			public readonly IReadOnlyList<CharacterAbilityData> AbilityData;
			/// <summary>Pre-fetched known base ability data.</summary>
			public readonly IReadOnlyList<CharacterKnownAbilityData> KnownAbilityData;
			/// <summary>Pre-fetched achievement data.</summary>
			public readonly IReadOnlyList<CharacterAchievementData> AchievementData;
			/// <summary>Pre-fetched friend data.</summary>
			public readonly IReadOnlyList<CharacterFriendData> FriendData;
			/// <summary>Pre-fetched guild membership data, if any.</summary>
			public readonly CharacterGuildData? GuildData;
			/// <summary>Pre-fetched party membership data, if any.</summary>
			public readonly CharacterPartyData? PartyData;
			/// <summary>Pre-fetched hotkey bar data.</summary>
			public readonly IReadOnlyList<CharacterHotkeyData> HotkeyData;
			/// <summary>Pre-fetched active buff data.</summary>
			public readonly IReadOnlyList<CharacterBuffData> BuffData;
			/// <summary>Pre-fetched faction standing data.</summary>
			public readonly IReadOnlyList<CharacterFactionData> FactionData;
			/// <summary>Pre-fetched quest state data.</summary>
			public readonly IReadOnlyList<CharacterQuestData> QuestData;

			/// <summary>
			/// Initializes a new CharacterLoadContext with all pre-fetched character data.
			/// </summary>
			/// <param name="connection">Network connection of the owning client.</param>
			/// <param name="characterData">Core character data.</param>
			/// <param name="sessionToken">Session ownership token.</param>
			/// <param name="serverID">Server ID that claimed the session.</param>
			/// <param name="inventoryData">Pre-fetched inventory items.</param>
			/// <param name="bankData">Pre-fetched bank items.</param>
			/// <param name="equipmentData">Pre-fetched equipment items.</param>
			/// <param name="attributeData">Pre-fetched attribute data.</param>
			/// <param name="abilityData">Pre-fetched crafted ability data.</param>
			/// <param name="knownAbilityData">Pre-fetched known base ability data.</param>
			/// <param name="achievementData">Pre-fetched achievement data.</param>
			/// <param name="friendData">Pre-fetched friend data.</param>
			/// <param name="guildData">Pre-fetched guild membership data.</param>
			/// <param name="partyData">Pre-fetched party membership data.</param>
			/// <param name="hotkeyData">Pre-fetched hotkey bar data.</param>
			/// <param name="buffData">Pre-fetched active buff data.</param>
			/// <param name="factionData">Pre-fetched faction standing data.</param>
			/// <param name="questData">Pre-fetched quest state data.</param>
			public CharacterLoadContext(
				NetworkConnection connection, CharacterData characterData,
				Guid sessionToken, long serverID,
				IReadOnlyList<CharacterInventoryData> inventoryData,
				IReadOnlyList<CharacterBankData> bankData,
				IReadOnlyList<CharacterEquipmentData> equipmentData,
				IReadOnlyList<CharacterAttributeData> attributeData,
				IReadOnlyList<CharacterAbilityData> abilityData,
				IReadOnlyList<CharacterKnownAbilityData> knownAbilityData,
				IReadOnlyList<CharacterAchievementData> achievementData,
				IReadOnlyList<CharacterFriendData> friendData,
				CharacterGuildData? guildData,
				CharacterPartyData? partyData,
				IReadOnlyList<CharacterHotkeyData> hotkeyData,
				IReadOnlyList<CharacterBuffData> buffData,
				IReadOnlyList<CharacterFactionData> factionData,
				IReadOnlyList<CharacterQuestData> questData)
			{
				Connection = connection;
				CharacterData = characterData;
				SessionToken = sessionToken;
				ServerID = serverID;
				InventoryData = inventoryData;
				BankData = bankData;
				EquipmentData = equipmentData;
				AttributeData = attributeData;
				AbilityData = abilityData;
				KnownAbilityData = knownAbilityData;
				AchievementData = achievementData;
				FriendData = friendData;
				GuildData = guildData;
				PartyData = partyData;
				HotkeyData = hotkeyData;
				BuffData = buffData;
				FactionData = factionData;
				QuestData = questData;
			}
		}
	}
}