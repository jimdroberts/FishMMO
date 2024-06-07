using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;


namespace FishMMO.Server
{
	/// <summary>
	/// Server party system.
	/// </summary>
	public class PartySystem : ServerBehaviour
	{
		public CharacterAttributeTemplate HealthTemplate;
		public int MaxPartySize = 6;
		[Tooltip("The server party update pump rate limit in seconds.")]
		public float UpdatePumpRate = 1.0f;
		public int UpdateFetchCount = 100;

		private LocalConnectionState serverState;
		private DateTime lastFetchTime = DateTime.UtcNow;
		private long lastPosition = 0;
		private float nextPump = 0.0f;
		// clientID / partyID
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();

		private Dictionary<string, ChatCommand> partyChatCommands;
		public bool OnPartyInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string targetName = msg.text.Trim().ToLower();
			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByLowerCaseName.TryGetValue(targetName, out IPlayerCharacter character))
			{
				OnServerPartyInviteBroadcastReceived(sender.Owner, new PartyInviteBroadcast()
				{
					inviterCharacterID = sender.ID,
					targetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server != null &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem != null)
			{
				partyChatCommands = new Dictionary<string, ChatCommand>()
				{
					{ "/pi", OnPartyInvite },
					{ "/invite", OnPartyInvite },
				};
				ChatHelper.AddDirectCommands(partyChatCommands);

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				Server.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
				Server.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
				Server.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
				Server.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
				Server.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
				Server.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);

				// remove the characters pending guild invite request on disconnect
				characterSystem.OnDisconnect += RemovePending;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived);
				Server.UnregisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived);
				Server.UnregisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived);
				Server.UnregisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived);
				Server.UnregisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived);
				Server.UnregisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived);

				// remove the characters pending guild invite request on disconnect
				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					characterSystem.OnDisconnect -= RemovePending;
				}
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPump < 0)
				{
					nextPump = UpdatePumpRate;

					List<PartyUpdateEntity> updates = FetchPartyUpdates();
					ProcessPartyUpdates(updates);

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<PartyUpdateEntity> FetchPartyUpdates()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// fetch party updates from the database
			List<PartyUpdateEntity> updates = PartyUpdateService.Fetch(dbContext, lastFetchTime, lastPosition, UpdateFetchCount);
			if (updates != null && updates.Count > 0)
			{
				PartyUpdateEntity latest = updates[updates.Count - 1];
				if (latest != null)
				{
					lastFetchTime = latest.TimeCreated;
					lastPosition = latest.ID;
				}
			}
			return updates;
		}

		// process updates from the database
		private void ProcessPartyUpdates(List<PartyUpdateEntity> updates)
		{
			if (Server == null || Server.NpgsqlDbContextFactory == null || updates == null || updates.Count < 1)
			{
				return;
			}

			// parties that have previously been updated, we do this so we aren't updating partys multiple times
			HashSet<long> updatedParties = new HashSet<long>();

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			foreach (PartyUpdateEntity update in updates)
			{
				// check if we have already updated this party
				if (updatedParties.Contains(update.PartyID))
				{
					continue;
				}
				// otherwise add the party to our list and continue with the update
				updatedParties.Add(update.PartyID);

				// get the current party members from the database
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, update.PartyID);

				var addBroadcasts = dbMembers.Select(x => new PartyAddBroadcast()
				{
					partyID = x.PartyID,
					characterID = x.CharacterID,
					rank = (PartyRank)x.Rank,
					healthPCT = x.HealthPCT,
				}).ToList();

				PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
				{
					members = addBroadcasts,
				};

				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					// tell all of the local party members to update their party member lists
					foreach (CharacterPartyEntity entity in dbMembers)
					{
						if (characterSystem.CharactersByID.TryGetValue(entity.CharacterID, out IPlayerCharacter character))
						{
							if (!character.TryGet(out IPartyController partyController) ||
								partyController.ID < 1)
							{
								continue;
							}
							partyController.Rank = (PartyRank)entity.Rank;
							Server.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
						}
					}
				}
			}
		}

		public void RemovePending(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		public void OnServerPartyCreateBroadcastReceived(NetworkConnection conn, PartyCreateBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
			if (partyController == null || partyController.ID > 0)
			{
				// already in a party
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (PartyService.TryCreate(dbContext, out PartyEntity newParty))
			{
				partyController.ID = newParty.ID;
				partyController.Rank = PartyRank.Leader;
				CharacterPartyService.Save(dbContext,
										   partyController.Character.ID,
										   partyController.ID,
										   partyController.Rank,
										   partyController.Character.TryGet(out ICharacterAttributeController attributeController) ? attributeController.GetResourceAttributeCurrentPercentage(HealthTemplate) : 0.0f);

				// tell the character we made their party successfully
				Server.Broadcast(conn, new PartyCreateBroadcast()
				{
					partyID = newParty.ID,
					location = conn.FirstObject.gameObject.scene.name,
				}, true, Channel.Reliable);
			}
		}

		public void OnServerPartyInviteBroadcastReceived(NetworkConnection conn, PartyInviteBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPartyController inviter = conn.FirstObject.GetComponent<IPartyController>();
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// validate party leader is inviting
			if (inviter == null ||
				inviter.ID < 1 ||
				inviter.Rank != PartyRank.Leader ||
				!CharacterPartyService.ExistsNotFull(dbContext, inviter.ID, MaxPartySize))
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!pendingInvitations.ContainsKey(msg.targetCharacterID) &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByID.TryGetValue(msg.targetCharacterID, out IPlayerCharacter targetCharacter) &&
				targetCharacter.TryGet(out IPartyController targetPartyController))
			{
				// validate target
				if (targetPartyController.ID > 0)
				{
					// we should tell the inviter the target is already in a party
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a party invite
				pendingInvitations.Add(targetCharacter.ID, inviter.ID);
				Server.Broadcast(targetCharacter.Owner, new PartyInviteBroadcast()
				{
					inviterCharacterID = inviter.ID,
					targetCharacterID = targetCharacter.ID
				}, true, Channel.Reliable);
			}
		}

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

			// validate party invite
			if (pendingInvitations.TryGetValue(partyController.Character.ID, out long pendingPartyID))
			{
				pendingInvitations.Remove(partyController.Character.ID);

				if (Server == null || Server.NpgsqlDbContextFactory == null)
				{
					return;
				}
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
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
											   attributesExist ? attributeController.GetResourceAttributeCurrentPercentage(HealthTemplate) : 1.0f);

					// tell the other servers to update their party lists
					PartyUpdateService.Save(dbContext, pendingPartyID);

					// tell the new member they joined immediately, other clients will catch up with the PartyUpdate pass
					Server.Broadcast(conn, new PartyAddBroadcast()
					{
						partyID = pendingPartyID,
						characterID = partyController.Character.ID,
						rank = PartyRank.Member,
						healthPCT = attributesExist ? attributeController.GetResourceAttributeCurrentPercentage(HealthTemplate) : 1.0f,
					}, true, Channel.Reliable);
				}
			}
		}

		public void OnServerPartyDeclineInviteBroadcastReceived(NetworkConnection conn, PartyDeclineInviteBroadcast msg, Channel channel)
		{
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		public void OnServerPartyLeaveBroadcastReceived(NetworkConnection conn, PartyLeaveBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
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

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

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

				// remove the party member
				CharacterPartyService.Delete(dbContext, partyController.Character.ID);

				if (remainingCount < 1)
				{
					// delete the party
					PartyService.Delete(dbContext, partyController.ID);
					PartyUpdateService.Delete(dbContext, partyController.ID);
				}
				else
				{
					// tell the other servers to update their party lists
					PartyUpdateService.Save(dbContext, partyController.ID);
				}

				partyController.ID = 0;
				partyController.Rank = PartyRank.None;

				// tell character that they left the party immediately, other clients will catch up with the PartyUpdate pass
				Server.Broadcast(conn, new PartyLeaveBroadcast(), true, Channel.Reliable);
			}
		}

		public void OnServerPartyRemoveBroadcastReceived(NetworkConnection conn, PartyRemoveBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
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

			if (msg.members == null || msg.members.Count < 1)
			{
				return;
			}

			// first index only
			long memberID = msg.members[0];

			// we can't kick ourself
			if (memberID == partyController.Character.ID)
			{
				return;
			}

			// remove the character from the party in the database
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			bool result = CharacterPartyService.Delete(dbContext, partyController.Rank, partyController.ID, memberID);
			if (result)
			{
				// tell the other servers to update their party lists
				PartyUpdateService.Save(dbContext, partyController.ID);
			}
		}
	}
}