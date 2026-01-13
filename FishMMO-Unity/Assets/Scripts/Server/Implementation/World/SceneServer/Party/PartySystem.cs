using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages party creation, invitations, membership, and updates for the MMO server.
	/// Handles party broadcasts, chat commands, and synchronizes party state with the database.
	/// </summary>
	[CreateAssetMenu(fileName = "PartySystem", menuName = "FishMMO/Server/SceneServer/Party System", order = 1)]
	[RequiresDataContainer(typeof(PartyRuntimeData))]
	public class PartySystem : ServerBehaviour, IPartySystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of members allowed in a party.
		/// </summary>
		public int MaxPartySize = 6;
		/// <summary>
		/// The server party update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server party update pump rate limit in seconds.")]
		[SerializeField]
		public float UpdatePumpRate = 1.0f;

		/// <summary>
		/// Registered chat commands for party actions.
		/// </summary>
		private Dictionary<string, ChatCommand> partyChatCommands;

		/// <summary>
		/// Handles party invite chat commands.
		/// </summary>
		public bool OnPartyInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string targetName = msg.Text.Trim().ToLower();
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(targetName, out IPlayerCharacter character))
			{
				OnServerPartyInviteBroadcastReceived(sender.Owner, new PartyInviteBroadcast()
				{
					InviterCharacterID = sender.ID,
					TargetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Called once to initialize the party system. Registers chat commands, broadcast handlers, and character events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("PartySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) ||
				characterSystem == null)
			{
				Log.Error("PartySystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Chat commands
			partyChatCommands = new Dictionary<string, ChatCommand>()
			{
				{ "/pi", OnPartyInvite },
				{ "/invite", OnPartyInvite },
			};
			ChatHelper.AddCommands(partyChatCommands);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived, true);

			// Character system events
			characterSystem.OnConnect += CharacterSystem_OnConnect;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
			}

			Log.Debug("PartySystem", $"Initialized (MaxPartySize={MaxPartySize}, UpdatePumpRate={UpdatePumpRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unregisters broadcast handlers and character events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("PartySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived);

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnConnect -= CharacterSystem_OnConnect;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicUpdate);
			}
		}

		/// <summary>
		/// Periodic callback that fetches and processes party updates from the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (Server.ServerState == ConnectionState.Started)
			{
				List<PartyUpdateEntity> updates = FetchPartyUpdates();
				ProcessPartyUpdates(updates);
			}
		}

		/// <summary>
		/// Fetches new party updates from the database since the last fetch.
		/// </summary>
		/// <returns>List of new party update entities.</returns>
		private List<PartyUpdateEntity> FetchPartyUpdates()
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// Fetch party updates from the database
			if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				return new List<PartyUpdateEntity>();
			}

			List<PartyUpdateEntity> updates = PartyUpdateService.Fetch(dbContext, runtimeData.PartyCharacterTracker.Keys.ToList(), runtimeData.LastFetchTime);
			if (updates != null && updates.Count > 0)
			{
				runtimeData.LastFetchTime = DateTime.UtcNow;
			}
			return updates;
		}

		/// <summary>
		/// Processes a list of party updates, synchronizing party membership and broadcasting changes to clients.
		/// </summary>
		/// <param name="updates">List of party update entities to process.</param>
		private void ProcessPartyUpdates(List<PartyUpdateEntity> updates)
		{
			if (Server == null || Server.CoreServer.NpgsqlDbContextFactory == null || updates == null || updates.Count < 1)
			{
				return;
			}

			// Parties that have previously been updated, to avoid duplicate updates
			HashSet<long> updatedParties = new HashSet<long>();

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			foreach (PartyUpdateEntity update in updates)
			{
				// Check if we have already updated this party
				if (updatedParties.Contains(update.PartyID))
				{
					continue;
				}
				// Otherwise add the party to our list and continue with the update
				updatedParties.Add(update.PartyID);

				// Get the current party members from the database
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, update.PartyID);

				// Get the current member IDs
				var currentMemberIDs = dbMembers.Select(x => x.CharacterID).ToHashSet();

				if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
				{
					continue;
				}

				// Check if we have previously cached the party member list
				if (runtimeData.PartyMemberTracker.TryGetValue(update.PartyID, out var previousMembers))
				{
					// Compute the difference: members that are in previousMembers but not in currentMemberIDs
					List<long> difference = previousMembers.Except(currentMemberIDs).ToList();

					foreach (long memberID in difference)
					{
						// Tell the member connection to leave their party immediately
						if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var partyCharacterMappingData) &&
							partyCharacterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
								character != null &&
								character.TryGet(out IPartyController targetPartyController))
						{
							targetPartyController.ID = 0;
							Server.NetworkWrapper.Broadcast(character.Owner, new PartyLeaveBroadcast(), true, Channel.Reliable);
						}
					}
				}
				// Cache the party member IDs
				runtimeData.PartyMemberTracker[update.PartyID] = currentMemberIDs;

				var addBroadcasts = dbMembers.Select(x => new PartyAddBroadcast()
				{
					PartyID = x.PartyID,
					CharacterID = x.CharacterID,
					Rank = (PartyRank)x.Rank,
					HealthPCT = x.HealthPCT,
				}).ToList();

				PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
				{
					Members = addBroadcasts,
				};

				if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
				{
					// Tell all of the local party members to update their party member lists
					foreach (CharacterPartyEntity entity in dbMembers)
					{
						if (characterMappingData.CharactersByID.TryGetValue(entity.CharacterID, out IPlayerCharacter character))
						{
							if (!character.TryGet(out IPartyController partyController) ||
								partyController.ID < 1)
							{
								continue;
							}
							partyController.Rank = (PartyRank)entity.Rank;
							Server.NetworkWrapper.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
						}
					}
				}
			}
		}

		/// <summary>
		/// Adds a mapping for the Party to Party Members connected to this Scene Server.
		/// </summary>
		public void AddPartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				return;
			}
			var tracker = runtimeData.PartyCharacterTracker;
			if (!tracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				tracker.Add(partyID, characterIDs = new HashSet<long>());
			}
			if (!characterIDs.Contains(characterID))
			{
				characterIDs.Add(characterID);
			}
		}

		/// <summary>
		/// Removes the mapping of Party to Party Members connected to this Scene Server.
		/// </summary>
		public void RemovePartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				return;
			}
			if (runtimeData.PartyCharacterTracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active party members we can remove the character and member trackers for the party.
				if (characterIDs.Count < 1)
				{
					runtimeData.PartyCharacterTracker.Remove(partyID);
					runtimeData.PartyMemberTracker.Remove(partyID);
				}
			}
		}

		/// <summary>
		/// Handles character connect event, adding the character to the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			AddPartyCharacterTracker(partyController.ID, character.ID);

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			PartyUpdateService.Save(dbContext, partyController.ID);
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null && Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				runtimeData.PendingInvitations.Remove(character.ID);
			}

			if (character == null)
			{
				return;
			}

			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			RemovePartyCharacterTracker(partyController.ID, character.ID);

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			PartyUpdateService.Save(dbContext, partyController.ID);
		}

		/// <summary>
		/// Handles party creation broadcast, validates and creates a new party for the requesting character.
		/// </summary>
		public void OnServerPartyCreateBroadcastReceived(NetworkConnection conn, PartyCreateBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
			if (partyController == null || partyController.ID > 0)
			{
				// already in a party
				return;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (PartyService.TryCreate(dbContext, out PartyEntity newParty))
			{
				partyController.ID = newParty.ID;
				partyController.Rank = PartyRank.Leader;
				CharacterPartyService.Save(dbContext,
										   partyController.Character.ID,
										   partyController.ID,
										   partyController.Rank,
										   partyController.Character.TryGet(out ICharacterAttributeController attributeController) ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 0.0f);

				AddPartyCharacterTracker(partyController.ID, partyController.Character.ID);

				// tell the character we made their party successfully
				Server.NetworkWrapper.Broadcast(conn, new PartyCreateBroadcast()
				{
					PartyID = newParty.ID,
					Location = conn.FirstObject.gameObject.scene.name,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles party invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only party leaders can invite, and invitations are tracked to prevent duplicates.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">PartyInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyInviteBroadcastReceived(NetworkConnection conn, PartyInviteBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController inviter = conn.FirstObject.GetComponent<IPartyController>();
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// validate party leader is inviting
			if (inviter == null ||
				inviter.ID < 1 ||
				inviter.Rank != PartyRank.Leader ||
				!CharacterPartyService.ExistsNotFull(dbContext, inviter.ID, MaxPartySize))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!runtimeData.PendingInvitations.ContainsKey(msg.TargetCharacterID) &&
				Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
				characterMappingData.CharactersByID.TryGetValue(msg.TargetCharacterID, out IPlayerCharacter targetCharacter) &&
				targetCharacter.TryGet(out IPartyController targetPartyController))
			{
				// validate target
				if (targetPartyController.ID > 0)
				{
					// we should tell the inviter the target is already in a party
					Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
					{
						Channel = ChatChannel.Party,
						SenderID = msg.TargetCharacterID,
						Text = ChatHelper.PARTY_ERROR_TARGET_IN_PARTY + " ",
					}, true, Channel.Reliable);
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a party invite
				runtimeData.PendingInvitations.Add(targetCharacter.ID, inviter.ID);
				Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new PartyInviteBroadcast()
				{
					InviterCharacterID = inviter.Character.ID,
					TargetCharacterID = targetCharacter.ID
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles acceptance of a party invitation, validates the invite, adds the character to the party, and broadcasts the update.
		/// </summary>
		/// <param name="conn">Network connection of the accepting character.</param>
		/// <param name="msg">PartyAcceptInviteBroadcast message containing acceptance details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyAcceptInviteBroadcastReceived(NetworkConnection conn, PartyAcceptInviteBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

			// validate character
			if (partyController == null || partyController.ID > 0)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				return;
			}

			// validate party invite
			if (runtimeData.PendingInvitations.TryGetValue(partyController.Character.ID, out long pendingPartyID))
			{
				runtimeData.PendingInvitations.Remove(partyController.Character.ID);

				if (Server == null || Server.CoreServer.NpgsqlDbContextFactory == null)
				{
					return;
				}
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				List<CharacterPartyEntity> members = CharacterPartyService.Members(dbContext, pendingPartyID);
				if (members != null &&
					members.Count < MaxPartySize)
				{
					bool attributesExist = partyController.Character.TryGet(out ICharacterAttributeController attributeController);

					partyController.ID = pendingPartyID;
					partyController.Rank = PartyRank.Member;

					CharacterPartyService.Save(dbContext,
											   partyController.Character.ID,
											   partyController.ID,
											   partyController.Rank,
											   attributesExist ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 1.0f);

					AddPartyCharacterTracker(partyController.ID, partyController.Character.ID);

					// tell the other servers to update their party lists
					PartyUpdateService.Save(dbContext, partyController.ID);

					// tell the new member they joined immediately, other clients will catch up with the PartyUpdate pass
					Server.NetworkWrapper.Broadcast(conn, new PartyAddBroadcast()
					{
						PartyID = pendingPartyID,
						CharacterID = partyController.Character.ID,
						Rank = PartyRank.Member,
						HealthPCT = attributesExist ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 1.0f,
					}, true, Channel.Reliable);
				}
			}
		}

		/// <summary>
		/// Handles decline of a party invitation, removes pending invitation for the character.
		/// </summary>
		/// <param name="conn">Network connection of the declining character.</param>
		/// <param name="msg">PartyDeclineInviteBroadcast message containing decline details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyDeclineInviteBroadcastReceived(NetworkConnection conn, PartyDeclineInviteBroadcast msg, Channel channel)
		{
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null && Server.DataContainerRegistry.TryGet(out IPartyRuntimeData runtimeData))
			{
				runtimeData.PendingInvitations.Remove(character.ID);
			}
		}

		/// <summary>
		/// Handles party leave broadcast, validates character, transfers leadership if needed, removes member from party, and updates or deletes party as appropriate.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">PartyLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyLeaveBroadcastReceived(NetworkConnection conn, PartyLeaveBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

			// validate character
			if (partyController == null || partyController.ID < 1)
			{
				// not in a party..
				return;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// validate party
			List<CharacterPartyEntity> members = CharacterPartyService.Members(dbContext, partyController.ID);
			if (members != null &&
				members.Count > 0)
			{
				int remainingCount = members.Count - 1;

				List<CharacterPartyEntity> remainingMembers = new List<CharacterPartyEntity>();

				// are there any other members in the party? if so we transfer leadership
				if (partyController.Rank == PartyRank.Leader && remainingCount > 0)
				{
					foreach (CharacterPartyEntity member in members)
					{
						if (member.CharacterID == partyController.Character.ID)
						{
							continue;
						}
						remainingMembers.Add(member);
					}

					CharacterPartyEntity newLeader = null;
					if (remainingMembers.Count > 0)
					{
						// pick a random member
						newLeader = remainingMembers[UnityEngine.Random.Range(0, remainingMembers.Count)];
					}

					// update the party leader status in the database
					if (newLeader != null)
					{
						CharacterPartyService.Save(dbContext, newLeader.CharacterID, newLeader.PartyID, PartyRank.Leader, newLeader.HealthPCT);
					}
				}

				long partyID = partyController.ID;

				partyController.ID = 0;
				partyController.Rank = PartyRank.None;

				RemovePartyCharacterTracker(partyController.ID, partyController.Character.ID);

				// tell character that they left the party immediately, other clients will catch up with the PartyUpdate pass
				Server.NetworkWrapper.Broadcast(conn, new PartyLeaveBroadcast(), true, Channel.Reliable);

				// remove the party member
				CharacterPartyService.Delete(dbContext, partyController.Character.ID);

				if (remainingCount < 1)
				{
					// delete the party
					PartyService.Delete(dbContext, partyID);
					PartyUpdateService.Delete(dbContext, partyID);
				}
				else
				{
					// tell the other servers to update their party lists
					PartyUpdateService.Save(dbContext, partyID);
				}
			}
		}

		/// <summary>
		/// Handles party member removal broadcast, validates and removes a member from the party in the database.
		/// Only party leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyRemoveBroadcastReceived(NetworkConnection conn, PartyRemoveBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

			// Validate that the requester is a party leader and not trying to remove themselves.
			if (partyController == null ||
				partyController.ID < 1 ||
				partyController.Rank != PartyRank.Leader)
			{
				return;
			}

			if (msg.MemberID < 1)
			{
				return;
			}

			// Prevent party leaders from kicking themselves.
			if (msg.MemberID == partyController.Character.ID)
			{
				return;
			}

			// Remove the character from the party in the database.
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			bool result = CharacterPartyService.Delete(dbContext, partyController.Rank, partyController.ID, msg.MemberID);
			if (result)
			{
				RemovePartyCharacterTracker(partyController.ID, partyController.Character.ID);

				// Tell the other servers to update their party lists.
				PartyUpdateService.Save(dbContext, partyController.ID);
			}
		}

		/// <summary>
		/// Handles party rank change broadcast, validates leader and target, and updates ranks in the database.
		/// Only party leaders can promote another member to leader.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyChangeRankBroadcast message containing target member ID.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyChangeRankBroadcastReceived(NetworkConnection conn, PartyChangeRankBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

			// validate character
			if (partyController == null ||
				partyController.ID < 1 ||
				partyController.Rank != PartyRank.Leader)
			{
				return;
			}

			if (msg.MemberID < 1)
			{
				return;
			}

			// we can't promote ourself
			if (msg.MemberID == partyController.Character.ID)
			{
				return;
			}

			// update the leader and target party ranks in the party in the database
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (CharacterPartyService.TrySaveRank(dbContext, partyController.Character.ID, partyController.ID, PartyRank.Member) &&
				CharacterPartyService.TrySaveRank(dbContext, msg.MemberID, partyController.ID, PartyRank.Leader))
			{
				// tell the other servers to update their party lists
				PartyUpdateService.Save(dbContext, partyController.ID);
			}
		}
	}
}