﻿using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Entities;

namespace FishMMO.Server
{
	/// <summary>
	/// Server guild system.
	/// </summary>
	public class GuildSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;
		private DateTime lastFetchTime = DateTime.UtcNow;
		private long lastPosition = 0;
		private float nextPump = 0.0f;

		public int MaxGuildSize = 100;
		public int MaxGuildNameLength = 64;
		[Tooltip("The server guild update pump rate limit in seconds.")]
		public float UpdatePumpRate = 1.0f;
		public int UpdateFetchCount = 100;

		// clientID / guildID
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();

		private Dictionary<string, ChatCommand> guildChatCommands;
		public bool OnGuildInvite(Character sender, ChatBroadcast msg)
		{
			if (Server.CharacterSystem.CharactersByLowerCaseName.TryGetValue(msg.text.Trim().ToLower(), out Character character))
			{
				OnServerGuildInviteBroadcastReceived(sender.Owner, new GuildInviteBroadcast()
				{
					targetCharacterID = character.ID
				});
				return true;
			}
			return false;
		}

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server.CharacterSystem != null)
			{
				guildChatCommands = new Dictionary<string, ChatCommand>()
				{
					{ "/gi", OnGuildInvite },
					{ "/ginvite", OnGuildInvite },
				};
				ChatHelper.AddDirectCommands(guildChatCommands);

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
			if (serverState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
			}
			else if (serverState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPump < 0)
				{
					nextPump = UpdatePumpRate;

					List<GuildUpdateEntity> updates = FetchGuildUpdates();
					ProcessGuildUpdates(updates);

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<GuildUpdateEntity> FetchGuildUpdates()
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			// fetch guild updates from the database
			List<GuildUpdateEntity> updates = GuildUpdateService.Fetch(dbContext, lastFetchTime, lastPosition, UpdateFetchCount);
			if (updates != null && updates.Count > 0)
			{
				GuildUpdateEntity latest = updates[updates.Count - 1];
				if (latest != null)
				{
					lastFetchTime = latest.TimeCreated;
					lastPosition = latest.ID;
				}
			}
			return updates;
		}

		// process updates from the database
		private void ProcessGuildUpdates(List<GuildUpdateEntity> updates)
		{
			if (Server == null || Server.DbContextFactory == null || updates == null || updates.Count < 1)
			{
				return;
			}

			// guilds that have previously been updated, we do this so we aren't updating guilds multiple times
			HashSet<long> updatedGuilds = new HashSet<long>();

			using var dbContext = Server.DbContextFactory.CreateDbContext();
			foreach (GuildUpdateEntity update in updates)
			{
				// check if we have already updated this guild
				if (updatedGuilds.Contains(update.GuildID))
				{
					continue;
				}
				// otherwise add the guild to our list and continue with the update
				updatedGuilds.Add(update.GuildID);

				// get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, update.GuildID);

				var addBroadcasts = dbMembers.Select(x => new GuildAddBroadcast()
				{
					guildID = x.GuildID,
					characterID = x.CharacterID,
					rank = (GuildRank)x.Rank,
					location = x.Location,
				}).ToList();

				GuildAddMultipleBroadcast guildAddBroadcast = new GuildAddMultipleBroadcast()
				{
					members = addBroadcasts,
				};

				// tell all of the local guild members to update their guild member lists
				foreach (CharacterGuildEntity entity in dbMembers)
				{
					if (Server.CharacterSystem.CharactersByID.TryGetValue(entity.CharacterID, out Character character))
					{
						character.Owner.Broadcast(guildAddBroadcast);
					}
				}
			}
		}

		public virtual bool IsAllowedGuildName(string guildName)
		{
			return !string.IsNullOrWhiteSpace(guildName) &&
				   guildName.Length <= MaxGuildNameLength &&
				   Regex.IsMatch(guildName, @"^[A-Za-z]+(?: [A-Za-z]+){0,2}$");
		}

		public void RemovePending(long id)
		{
			pendingInvitations.Remove(id);
		}

		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.DbContextFactory == null)
			{
				return;
			}

			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();
			if (guildController == null || guildController.ID > 0)
			{
				// already in a guild
				return;
			}

			// remove white space
			msg.guildName = msg.guildName.Trim();

			if (!IsAllowedGuildName(msg.guildName))
			{
				// we should tell the player the guild name is not valid
				return;
			}

			using var dbContext = Server.DbContextFactory.CreateDbContext();
			if (GuildService.Exists(dbContext, msg.guildName))
			{
				// guild name is taken
				return;
			}
			if (GuildService.TryCreate(dbContext, msg.guildName, out GuildEntity newGuild))
			{
				guildController.ID = newGuild.ID;
				guildController.Name = newGuild.Name;
				guildController.Rank = GuildRank.Leader;
				CharacterGuildService.Save(dbContext, guildController.Character);
				dbContext.SaveChanges();

				// tell the character we made their guild successfully
				conn.Broadcast(new GuildCreateBroadcast()
				{
					guildID = newGuild.ID,
					location = guildController.gameObject.scene.name,
				});
			}
		}

		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg)
		{
			if (Server.DbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController inviter = conn.FirstObject.GetComponent<GuildController>();
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			// validate guild leader or officer is inviting
			if (inviter == null ||
				inviter.ID < 1 ||
				!(inviter.Rank != GuildRank.Leader || inviter.Rank != GuildRank.Officer) ||
				!CharacterGuildService.ExistsNotFull(dbContext, inviter.ID, MaxGuildSize))
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!pendingInvitations.ContainsKey(msg.targetCharacterID) &&
				Server.CharacterSystem.CharactersByID.TryGetValue(msg.targetCharacterID, out Character targetCharacter))
			{
				GuildController targetGuildController = targetCharacter.GetComponent<GuildController>();

				// validate target
				if (targetGuildController == null || targetGuildController.ID > 0)
				{
					// we should tell the inviter the target is already in a guild
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.ID, inviter.ID);
				targetCharacter.Owner.Broadcast(new GuildInviteBroadcast()
				{
					inviterCharacterID = inviter.ID,
					targetCharacterID = targetCharacter.ID
				});
			}
		}

		public void OnServerGuildAcceptInviteBroadcastReceived(NetworkConnection conn, GuildAcceptInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null || guildController.ID > 0)
			{
				return;
			}

			// validate guild invite
			if (pendingInvitations.TryGetValue(guildController.Character.ID, out long pendingGuildID))
			{
				pendingInvitations.Remove(guildController.Character.ID);

				if (Server == null || Server.DbContextFactory == null)
				{
					return;
				}
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				List<CharacterGuildEntity> members = CharacterGuildService.Members(dbContext, pendingGuildID);
				if (members != null &&
					members.Count < MaxGuildSize)
				{
					guildController.ID = pendingGuildID;
					guildController.Rank = GuildRank.Member;
					
					CharacterGuildService.Save(dbContext, guildController.Character);
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, pendingGuildID);
					dbContext.SaveChanges();

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					conn.Broadcast(new GuildAddBroadcast()
					{
						guildID = guildController.ID,
						characterID = guildController.Character.ID,
						rank = GuildRank.Member,
						location = guildController.gameObject.scene.name,
					});
				}
			}
		}

		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg)
		{
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg)
		{
			if (Server.DbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null || guildController.ID < 1)
			{
				// not in a guild..
				return;
			}

			using var dbContext = Server.DbContextFactory.CreateDbContext();

			// validate guild
			List<CharacterGuildEntity> members = CharacterGuildService.Members(dbContext, guildController.ID);
			if (members != null &&
				members.Count > 0)
			{
				int remainingCount = members.Count - 1;

				List<CharacterGuildEntity> remainingMembers = new List<CharacterGuildEntity>();

				// are there any other members in the guild? if so we transfer leadership to officers first and then members
				if (guildController.Rank == GuildRank.Leader && remainingCount > 0)
				{
					List<CharacterGuildEntity> officers = new List<CharacterGuildEntity>();

					foreach (CharacterGuildEntity member in members)
					{
						if (member.CharacterID == guildController.Character.ID)
						{
							continue;
						}

						if (member.Rank == (byte)GuildRank.Officer)
						{
							officers.Add(member);
						}
						remainingMembers.Add(member);
					}

					CharacterGuildEntity newLeader = null;
					if (officers.Count > 0)
					{
						// pick a random officer
						newLeader = officers[UnityEngine.Random.Range(0, officers.Count)];
					}
					else if (remainingMembers.Count > 0)
					{
						// pick a random member
						newLeader = remainingMembers[UnityEngine.Random.Range(0, remainingMembers.Count)];
					}

					// update the guild leader status in the database
					if (newLeader != null)
					{
						CharacterGuildService.Save(dbContext, newLeader.CharacterID, newLeader.GuildID, GuildRank.Leader, newLeader.Location);
					}
				}

				// remove the guild member
				CharacterGuildService.Delete(dbContext, guildController.Character.ID);
				dbContext.SaveChanges();

				if (remainingCount < 1)
				{
					// delete the guild
					GuildService.Delete(dbContext, guildController.ID);
					GuildUpdateService.Delete(dbContext, guildController.ID);
					dbContext.SaveChanges();
				}
				else
				{
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, guildController.ID);
					dbContext.SaveChanges();
				}
				
				guildController.ID = 0;
				guildController.Name = "";
				guildController.Rank = GuildRank.None;

				// tell character that they left the guild immediately, other clients will catch up with the GuildUpdate pass
				conn.Broadcast(new GuildLeaveBroadcast());
			}
		}

		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg)
		{
			if (Server.DbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null ||
				guildController.ID < 1 ||
				guildController.Rank != GuildRank.Leader ||
				guildController.Rank != GuildRank.Officer) 
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
			if (memberID == guildController.Character.ID)
			{
				return;
			}

			// remove the character from the guild in the database
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			bool result = CharacterGuildService.Delete(dbContext, guildController.Rank, guildController.ID, memberID);
			if (result)
			{
				// tell the other servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
				dbContext.SaveChanges();
			}
		}
	}
}