using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Server.Services;
using FishMMO_DB.Entities;

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
		public float UpdatePumpRate = 5.0f;
		public int UpdateFetchCount = 100;

		// clientID / guildID
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();
		private readonly Dictionary<long, HashSet<long>> guildMemberCache = new Dictionary<long, HashSet<long>>();

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server.CharacterSystem != null)
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

					List<GuildUpdateEntity> messages = FetchGuildUpdates();
					if (messages != null)
					{
						ProcessGuildUpdates(messages);
					}

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<GuildUpdateEntity> FetchGuildUpdates()
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			// fetch guild updates from the database
			List<GuildUpdateEntity> updates = GuildUpdateService.Fetch(dbContext, lastFetchTime, lastPosition, UpdateFetchCount);
			if (updates != null)
			{
				GuildUpdateEntity latest = updates.LastOrDefault();
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
			if (Server == null || Server.DbContextFactory == null || updates == null)
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

				// get the locally cached guild members
				if (!guildMemberCache.TryGetValue(update.GuildID, out HashSet<long> members))
				{
					guildMemberCache.Add(update.GuildID, members = new HashSet<long>());
				}

				// compile new id set
				HashSet<long> newIDs = new HashSet<long>(dbMembers.Select(s => s.CharacterID));

				// get our differences for removal
				var removeResults = members.Where(m => !newIDs.Contains(m)).ToList();
				if (removeResults != null && removeResults.Count > 0)
				{
					// prepare broadcasts for clients
					GuildRemoveBroadcast guildRemoveBroadcast = new GuildRemoveBroadcast()
					{
						members = removeResults,
					};

					// tell all of the local guild members to update their guild member lists
					foreach (CharacterGuildEntity entity in dbMembers)
					{
						if (Server.CharacterSystem.CharactersByID.TryGetValue(entity.CharacterID, out Character character))
						{
							character.Owner.Broadcast(guildRemoveBroadcast, true, Channel.Unreliable);
						}
					}
				}

				// get our differences for addition
				var addResults = dbMembers.Where(m => !members.Contains(m.CharacterID)).ToList();
				if (addResults != null && addResults.Count > 0)
				{
					var addBroadcasts = addResults.Select(x => new GuildNewMemberBroadcast()
					{
						memberID = x.CharacterID,
						rank = (GuildRank)x.Rank,
						location = x.Location,
					}).ToList();

					GuildAddBroadcast guildAddBroadcast = new GuildAddBroadcast()
					{
						members = addBroadcasts,
					};

					// tell all of the local guild members to update their guild member lists
					foreach (CharacterGuildEntity entity in dbMembers)
					{
						if (Server.CharacterSystem.CharactersByID.TryGetValue(entity.CharacterID, out Character character))
						{
							character.Owner.Broadcast(guildAddBroadcast, true, Channel.Unreliable);
						}
					}
				}

				// update guild member cache
				guildMemberCache[update.GuildID] = newIDs;
			}
		}

		public bool GuildNameValid(string name)
		{
			return !string.IsNullOrEmpty(name) && name.Length < MaxGuildNameLength;
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

			if (!GuildNameValid(msg.guildName))
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
					ID = newGuild.ID,
					guildName = newGuild.Name,
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
				inviter.Rank != GuildRank.Leader ||
				inviter.Rank != GuildRank.Officer ||
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
				targetCharacter.Owner.Broadcast(new GuildInviteBroadcast() { targetCharacterID = targetCharacter.ID });
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
			if (guildController == null)
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
					members.Count > 0 &&
					members.Count < MaxGuildSize)
				{
					guildController.Rank = GuildRank.Member;
					guildController.ID = pendingGuildID;
					CharacterGuildService.Save(dbContext, guildController.Character);
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, pendingGuildID);
					dbContext.SaveChanges();

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					conn.Broadcast(new GuildNewMemberBroadcast()
					{
						memberID = guildController.Character.ID,
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
				if (guildController.Rank == GuildRank.Leader)
				{
					// are there any other members in the guild? if so we transfer leadership to officers first and then members
					if (remainingCount > 0)
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
							else
							{
								remainingMembers.Add(member);
							}
						}

						CharacterGuildEntity newLeader = null;
						if (officers.Count > 0)
						{
							// pick a random officer
							newLeader = officers[UnityEngine.Random.Range(0, officers.Count)];
						}
						else
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
				}

				if (remainingCount < 1)
				{
					// delete the guild
					GuildService.Delete(dbContext, guildController.ID);
				}
				else
				{
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, guildController.ID);
				}

				// remove the guild member
				guildController.ID = 0;
				guildController.Name = "";
				guildController.Rank = GuildRank.None;
				CharacterGuildService.Delete(dbContext, guildController.ID, guildController.Character.ID);
				dbContext.SaveChanges();

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