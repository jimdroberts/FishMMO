using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;
using SceneType = FishMMO.Shared.SceneType;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Arena board: validates queue requests made at a board, queues players — alone or as a
	/// pre-made party — into the shared group finder queue as arena rows, and forms matches from
	/// them on the pump.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything the dungeon group finder established carries over unchanged: the queue table,
	/// the per-server pump, the heartbeat, the leash to the board, the panel-close rule, and the
	/// single transaction that forms a match. What differs is the unit of play — a match of N
	/// teams rather than one party — and that a party may queue together. Team assignment is
	/// written into the match rows by <see cref="IGroupFinderQueueService.TryFormArenaMatchAsync"/>;
	/// running the match is the coordinator's job, in <c>InteractableSystem.ArenaMatch.cs</c>.
	/// </para>
	/// <para>
	/// <b>Pre-made parties.</b> The leader presses Queue as Party. Every member must be connected
	/// to this scene server, standing within the leash of the same board, free of instances and
	/// live matches, and the party must fit on one team of the format. Then every member gets a
	/// queue row carrying the party id, all or none, and each member's own entry on this server
	/// with the board as its leash. A member who walks off or closes their panel leaves alone; the
	/// rest stay queued as a smaller pre-made.
	/// </para>
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>Ingress guard operation code for arena queue requests.</summary>
		private const byte ArenaQueueOperation = 14;

		/// <summary>Minimum milliseconds between arena queue requests from one connection.</summary>
		private const int ArenaQueueDebounceMilliseconds = 2000;

		/// <summary>
		/// Everything the main thread knows about an arena queue request, captured before going async.
		/// </summary>
		private struct ArenaRequestContext
		{
			public IPlayerCharacter Character;
			public long CharacterID;
			public long WorldServerID;
			public long PartyID;
			public PartyRank PartyRank;
			public IArenaBoard Board;
			public ArenaTemplate Template;
			public int Format;
			public int TeamSize;
			public int MatchSize;
			public WorldSceneDetails SceneDetails;
		}

		/// <summary>Ingress guard operation code for arena board lookups (profile, history, leaderboard).</summary>
		private const byte ArenaLookupOperation = 15;

		/// <summary>Minimum milliseconds between arena board lookups from one connection.</summary>
		private const int ArenaLookupDebounceMilliseconds = 1000;

		/// <summary>Rows the leaderboard shows.</summary>
		private const int ArenaLeaderboardRows = 50;

		/// <summary>Matches the history shows.</summary>
		private const int ArenaHistoryRows = 20;

		/// <summary>Seats a single pump may backfill per arena and format.</summary>
		private const int ArenaBackfillPerPump = 4;

		/// <summary>Registers the arena board's requests. Called from the group finder's initialiser.</summary>
		private void InitializeArena()
		{
			Server.NetworkWrapper.RegisterBroadcast<ArenaQueueBroadcast>(OnServerArenaQueueBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ArenaProfileRequestBroadcast>(OnServerArenaProfileRequestReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ArenaHistoryRequestBroadcast>(OnServerArenaHistoryRequestReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ArenaLeaderboardRequestBroadcast>(OnServerArenaLeaderboardRequestReceived, true);

			/* Game masters may step into a running match to watch it. A spectator has no seat, so
			 * the team registry reports them as an ally to everyone and nobody can hit them or be
			 * hit by them; they are returned with everybody else when the match closes. */
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/spectatearena", OnSpectateArenaCommand },
				{ "/spectateteam", OnSpectateTeamCommand },
			}, FishMMO.Auth.Core.AccessLevel.GameMaster);

			InitializeArenaMatches();
		}

		/// <summary>Unregisters the arena board's requests.</summary>
		private void DeinitializeArena()
		{
			Server.NetworkWrapper.UnregisterBroadcast<ArenaQueueBroadcast>(OnServerArenaQueueBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ArenaProfileRequestBroadcast>(OnServerArenaProfileRequestReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ArenaHistoryRequestBroadcast>(OnServerArenaHistoryRequestReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ArenaLeaderboardRequestBroadcast>(OnServerArenaLeaderboardRequestReceived);
			ChatHelper.RemoveCommands(new[] { "/spectatearena", "/spectateteam" });
			DeinitializeArenaMatches();
		}

		/// <summary>
		/// <c>/spectatearena &lt;matchId&gt;</c>. Moves a game master into a live match's instance as
		/// a spectator.
		/// </summary>
		/// <remarks>
		/// Uses the ordinary instance hand-off, so the spectator is routed by the world server like
		/// any occupant and leaves through <c>/leaveinstance</c> or the match's own close. Refused
		/// for a character already in an instance, or one that may not travel.
		/// </remarks>
		private bool OnSpectateArenaCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character?.Owner == null)
			{
				return true;
			}

			NetworkConnection conn = character.Owner;
			string idText = ChatHelper.GetWordAndTrimmed(msg.Text, out _);
			if (string.IsNullOrWhiteSpace(idText) || !long.TryParse(idText, out long matchID) || matchID <= 0)
			{
				SendSystemMessage(conn, "Usage: /spectatearena <matchId>");
				return true;
			}

			if (character.IsInInstance())
			{
				SendSystemMessage(conn, "Leave your current instance first.");
				return true;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				SendSystemMessage(conn, "You cannot travel right now.");
				return true;
			}

			long characterID = character.ID;
			long worldServerID = character.WorldServerID;
			if (!TryEnqueueAsyncWork(() => SpectateArenaAsync(conn, character, characterID, worldServerID, matchID), characterID))
			{
				SendSystemMessage(conn, "The match could not be looked up right now. Please try again.");
			}
			return true;
		}

		private async Task SpectateArenaAsync(NetworkConnection conn, IPlayerCharacter character, long characterID, long worldServerID, long matchID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The match could not be looked up right now. Please try again."));
					return;
				}

				DatabaseResult<ArenaMatchData?> matchResult = await matchService.FetchAsync(matchID);
				if (!matchResult.IsSuccess || !matchResult.Data.HasValue)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "No such arena match."));
					return;
				}

				ArenaMatchData match = matchResult.Data.Value;
				if (match.WorldServerID != worldServerID || match.Status >= (int)FishMMO.Database.Data.Enums.ArenaMatchStatus.Ended)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "That match is not running on this world."));
					return;
				}

				DatabaseResult<SceneData> instanceResult = await sceneService.FetchAsync(match.InstanceID);
				if (!instanceResult.IsSuccess || !IsUsableInstance(instanceResult.Data, worldServerID))
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "That match's arena is not available."));
					return;
				}

				long instanceID = match.InstanceID;
				string sceneName = match.SceneName;
				TryEnqueueMainThread(() =>
				{
					if (worldSceneDetailsCache == null ||
						!worldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details) ||
						details.RespawnPositions == null || details.RespawnPositions.Count < 1)
					{
						SendSystemMessage(conn, "That arena's scene is not known to this server.");
						return;
					}

					CharacterRespawnPositionDetails respawn = details.RespawnPositions.Values.ToList().GetRandom();
					SendSystemMessage(conn, $"Spectating arena match {matchID}. Use /leaveinstance to return.");
					EnterInstance(conn, character, instanceID, respawn, 0);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error spectating arena match {matchID} for character {characterID}: {ex}");
				TryEnqueueMainThread(() => SendSystemMessage(conn, "The match could not be looked up right now. Please try again."));
			}
		}

		/// <summary>
		/// Validates that a connection is standing at a board that offers the arena and format it
		/// asked for. Main thread only.
		/// </summary>
		private bool TryResolveArenaBoard(NetworkConnection conn, ArenaQueueBroadcast msg, out ArenaRequestContext context, out GroupFinderRefusalReason refusal)
		{
			context = default;
			refusal = GroupFinderRefusalReason.NoEntrance;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return false;
			}

			if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return false;
			}

			IArenaBoard board = sceneObject.GameObject.GetComponent<IArenaBoard>();
			if (board == null || !board.InRange(character.Transform))
			{
				return false;
			}

			// The board decides what it offers; the message only chooses among that.
			ArenaTemplate template = msg.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(msg.ArenaTemplateID) : null;
			if (template == null || !board.Offers(msg.ArenaTemplateID))
			{
				refusal = GroupFinderRefusalReason.NotAvailable;
				return false;
			}

			if (!template.IsValidFormat(msg.Format))
			{
				refusal = GroupFinderRefusalReason.UnknownDifficulty;
				return false;
			}

			if (worldSceneDetailsCache == null ||
				!worldSceneDetailsCache.Scenes.TryGetValue(template.ArenaSceneName, out WorldSceneDetails details) ||
				details.RespawnPositions == null || details.RespawnPositions.Count < 1)
			{
				Log.Debug("InteractableSystem", $"Arena '{template.name}' names scene '{template.ArenaSceneName}', which is missing or has no respawn points.");
				refusal = GroupFinderRefusalReason.NotAvailable;
				return false;
			}

			int matchSize = ArenaRules.ResolveMatchSize(template, msg.Format, details.MaxClients);
			if (matchSize <= 0)
			{
				refusal = GroupFinderRefusalReason.NotAvailable;
				return false;
			}

			long partyID = 0;
			PartyRank partyRank = PartyRank.None;
			if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
			{
				partyID = partyController.ID;
				partyRank = partyController.Rank;
			}

			context = new ArenaRequestContext
			{
				Character = character,
				CharacterID = character.ID,
				WorldServerID = character.WorldServerID,
				PartyID = partyID,
				PartyRank = partyRank,
				Board = board,
				Template = template,
				Format = msg.Format,
				TeamSize = template.GetTeamSize(msg.Format),
				MatchSize = matchSize,
				SceneDetails = details,
			};
			refusal = GroupFinderRefusalReason.None;
			return true;
		}

		/// <summary>
		/// Handles a request to queue for an arena, alone or as a party.
		/// </summary>
		public void OnServerArenaQueueBroadcastReceived(NetworkConnection conn, ArenaQueueBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, ArenaQueueOperation, ArenaQueueDebounceMilliseconds, out long guardKey))
			{
				SendGroupFinderRefusal(conn, GroupFinderRefusalReason.OnCooldown, SceneType.PvP);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (!TryResolveArenaBoard(conn, msg, out ArenaRequestContext context, out GroupFinderRefusalReason refusal))
				{
					SendGroupFinderRefusal(conn, refusal, SceneType.PvP);
					return;
				}

				if (groupFinderEntries.TryGetValue(context.CharacterID, out GroupFinderEntry existing) &&
					existing.State == GroupFinderState.Matched)
				{
					SendGroupFinderStatus(conn, existing, GroupFinderState.Matched, GroupFinderRefusalReason.None, existing.LastSentWaitingCount);
					return;
				}

				if (context.Character.IsInInstance())
				{
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.InInstance, SceneType.PvP);
					return;
				}

				/* Who is queuing. Alone: just this character, and a party of others is a reason to
				 * queue as the party instead. As a party: the leader, for every member, and every
				 * member must be here — on this server, at this board — because each one gets a
				 * leashed entry here and is moved from here. */
				var members = new List<GroupFinderEntry>(msg.AsParty ? 6 : 1);
				long groupID = 0;

				if (msg.AsParty)
				{
					if (context.PartyID <= 0)
					{
						// No party: "as party" is just "alone".
						members.Add(BuildArenaEntry(conn, context.Character, context));
					}
					else
					{
						if (context.PartyRank != PartyRank.Leader)
						{
							SendGroupFinderRefusal(conn, GroupFinderRefusalReason.NotPartyLeader, SceneType.PvP);
							return;
						}

						if (!TryCollectPresentPartyMembers(context, out List<IPlayerCharacter> present, out refusal))
						{
							SendGroupFinderRefusal(conn, refusal, SceneType.PvP);
							return;
						}

						if (!ArenaRules.GroupFitsFormat(present.Count, context.TeamSize))
						{
							SendGroupFinderRefusal(conn, GroupFinderRefusalReason.PartyTooLarge, SceneType.PvP);
							return;
						}

						/* Ranked: a pre-made party is the whole team or nothing. A solo rated player
						 * should not be handed a team of friends on comms as their side, or theirs. */
						if (context.Template.IsRankedFormat(context.Format) && present.Count != context.TeamSize)
						{
							SendGroupFinderRefusal(conn, GroupFinderRefusalReason.PartyMustFillTeam, SceneType.PvP);
							return;
						}

						groupID = context.PartyID;
						foreach (IPlayerCharacter member in present)
						{
							members.Add(BuildArenaEntry(member.Owner, member, context));
						}
					}
				}
				else
				{
					if (context.PartyID > 0)
					{
						/* In a party. Alone in it, they may queue as themselves — the party is not
						 * touched, unlike the dungeon finder, because arenas do not form parties and
						 * nothing about being seated conflicts with it. With others, they must queue
						 * the party or leave it; the async half tells the two apart. */
					}
					members.Add(BuildArenaEntry(conn, context.Character, context));
				}

				ArenaRequestContext captured = context;
				bool asParty = msg.AsParty;
				long capturedGroup = groupID;

				if (TryEnqueueAsyncWork(
					() => ProcessArenaQueueAsync(conn, captured, asParty, capturedGroup, members, guardKey),
					conn,
					context.CharacterID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP);
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Builds the leashed entry an arena waiter gets on this server. Not yet registered.
		/// </summary>
		private GroupFinderEntry BuildArenaEntry(NetworkConnection conn, IPlayerCharacter character, ArenaRequestContext context)
		{
			return new GroupFinderEntry
			{
				Connection = conn,
				Character = character,
				CharacterID = character.ID,
				WorldServerID = context.WorldServerID,
				Kind = SceneType.PvP,
				SceneName = context.Template.ArenaSceneName,
				ArenaTemplateID = context.Template.ID,
				TeamCount = Math.Max(2, context.Template.TeamCount),
				TeamSize = context.TeamSize,
				Difficulty = context.Format,
				Capacity = context.MatchSize,
				GroupSize = context.MatchSize,
				SceneDetails = context.SceneDetails,
				AchievementTemplate = null,
				Entrance = context.Board,
				State = GroupFinderState.Waiting,
				NextBackfillAttemptUtc = DateTime.MaxValue,
				QueuedAtUtc = DateTime.UtcNow,
				Ranked = context.Template.IsRankedFormat(context.Format),
				BalanceTeams = context.Template.BalanceTeams,
				RatingBandBase = context.Template.RatingBandBase,
				RatingBandGrowth = context.Template.RatingBandGrowthPerSecond,
				RatingBandMax = context.Template.RatingBandMax,
				RankAttributeTemplateID = TryResolvePvPTemplate(PvPRankAttributeName, out CharacterAttributeTemplate rankTemplate) ? rankTemplate.ID : 0,
			};
		}

		/// <summary>
		/// Collects the leader's party members who are connected here, at the board, and free.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// Uses the party trackers this server keeps, which is exactly the set of members connected
		/// to this scene server. A member elsewhere cannot be leashed to this board or moved from
		/// here, so the party cannot queue until they arrive — the refusal says so.
		/// </remarks>
		private bool TryCollectPresentPartyMembers(ArenaRequestContext context, out List<IPlayerCharacter> present, out GroupFinderRefusalReason refusal)
		{
			present = new List<IPlayerCharacter>(6);
			refusal = GroupFinderRefusalReason.None;

			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var partyMapping) ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMapping))
			{
				refusal = GroupFinderRefusalReason.ServerError;
				return false;
			}

			if (!partyMapping.PartyCharacterTracker.TryGetValue(context.PartyID, out HashSet<long> localMembers) || localMembers.Count == 0)
			{
				refusal = GroupFinderRefusalReason.PartyNotPresent;
				return false;
			}

			float leashSqr = groupFinderLeashMeters * groupFinderLeashMeters;
			Transform door = context.Board.Transform;

			foreach (long memberID in localMembers)
			{
				if (!characterMapping.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) ||
					member?.Owner == null || !member.Owner.IsActive || member.Transform == null || door == null)
				{
					refusal = GroupFinderRefusalReason.PartyNotPresent;
					return false;
				}

				if ((door.position - member.Transform.position).sqrMagnitude > leashSqr)
				{
					refusal = GroupFinderRefusalReason.PartyNotPresent;
					return false;
				}

				if (member.IsInInstance())
				{
					refusal = GroupFinderRefusalReason.PartyMemberBusy;
					return false;
				}

				if (groupFinderEntries.TryGetValue(memberID, out GroupFinderEntry existing) && existing.State == GroupFinderState.Matched)
				{
					refusal = GroupFinderRefusalReason.PartyMemberBusy;
					return false;
				}

				present.Add(member);
			}

			return true;
		}

		/// <summary>
		/// Checks the roster against the database, queues the rows, and registers the entries.
		/// </summary>
		private async Task ProcessArenaQueueAsync(
			NetworkConnection conn,
			ArenaRequestContext context,
			bool asParty,
			long groupID,
			List<GroupFinderEntry> members,
			long guardKey)
		{
			long characterID = context.CharacterID;
			long worldServerID = context.WorldServerID;
			string sceneName = context.Template.ArenaSceneName;

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService) ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
					return;
				}

				var memberIDs = new List<long>(members.Count);
				foreach (GroupFinderEntry entry in members)
				{
					memberIDs.Add(entry.CharacterID);
				}

				/* Queuing alone while in a party with others is refused: arenas are where a party
				 * queues together, and one member slipping into a match alone would leave the party
				 * unable to open anything — the one-instance rule counts arenas — for a match they
				 * did not choose. Alone in a party of one is simply alone. */
				if (!asParty && context.PartyID > 0)
				{
					List<long> roster = await FetchPartyMemberIDsAsync(context.PartyID);
					if (roster == null)
					{
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
						return;
					}
					if (roster.Count > 1)
					{
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.InParty, SceneType.PvP));
						return;
					}
				}

				/* The whole party is checked, not only those queuing: one instance per party
				 * counts arenas, so a member anywhere with a dungeon open blocks the arena, and a
				 * member seated in a live match blocks it too. */
				var partyRoster = new List<long>(memberIDs);
				if (context.PartyID > 0)
				{
					List<long> roster = await FetchPartyMemberIDsAsync(context.PartyID);
					if (roster == null)
					{
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
						return;
					}
					foreach (long id in roster)
					{
						if (!partyRoster.Contains(id))
						{
							partyRoster.Add(id);
						}
					}

					/* Queuing as a party means the whole party. The main thread could only see the
					 * members connected to this server; a member on another server, or logged out,
					 * is on the roster but not at the board, and the party waits for them rather
					 * than queuing without them. */
					if (groupID > 0 && partyRoster.Count != memberIDs.Count)
					{
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.PartyNotPresent, SceneType.PvP));
						return;
					}
				}

				// Deserters and decliners wait out their lock before queuing again.
				if (Server.Database.ServiceRegistry.TryGet<IArenaPenaltyService>(out var penaltyService))
				{
					var lockResult = await penaltyService.FetchActiveAsync(memberIDs);
					if (lockResult.IsSuccess && lockResult.Data.Count > 0)
					{
						bool onlyMe = lockResult.Data.Count == 1 && lockResult.Data[0].CharacterID == characterID;
						GroupFinderRefusalReason why = onlyMe ? GroupFinderRefusalReason.QueueLocked : GroupFinderRefusalReason.PartyMemberBusy;
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, why, SceneType.PvP));
						return;
					}
				}

				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					partyRoster, DbSceneType(SceneType.Group), worldServerID, context.PartyID);
				if (!heldResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
					return;
				}
				foreach (SceneData held in heldResult.Data)
				{
					if (IsUsableInstance(held, worldServerID))
					{
						GroupFinderRefusalReason why = partyRoster.Count > memberIDs.Count || held.CharacterID != characterID
							? GroupFinderRefusalReason.PartyMemberBusy
							: GroupFinderRefusalReason.HoldsInstance;
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, why, SceneType.PvP));
						return;
					}
				}

				var liveResult = await matchService.FetchCharactersInLiveMatchesAsync(partyRoster);
				if (!liveResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
					return;
				}
				if (liveResult.Data.Count > 0)
				{
					GroupFinderRefusalReason why = liveResult.Data.Count == 1 && liveResult.Data[0] == characterID
						? GroupFinderRefusalReason.HoldsInstance
						: GroupFinderRefusalReason.PartyMemberBusy;
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, why, SceneType.PvP));
					return;
				}

				bool queued;
				if (groupID > 0)
				{
					DatabaseResult<int> groupResult = await queueService.EnqueueGroupAsync(
						worldServerID, DbSceneType(SceneType.PvP), sceneName, context.Format, groupID, memberIDs, GroupFinderStaleBefore);
					queued = groupResult.IsSuccess && groupResult.Data == memberIDs.Count;
					if (!queued)
					{
						await Log.Debug("InteractableSystem",
							$"Arena: party {groupID} could not be queued for '{sceneName}': {groupResult.ErrorCode} - {groupResult.ErrorMessage}");
					}
				}
				else
				{
					DatabaseResult<long> soloResult = await queueService.EnqueueAsync(
						worldServerID, characterID, DbSceneType(SceneType.PvP), sceneName, context.Format, GroupFinderStaleBefore);
					queued = soloResult.IsSuccess && soloResult.Data > 0;
				}

				if (!queued)
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.PartyMemberBusy, SceneType.PvP));
					return;
				}

				var countResult = await queueService.CountWaitingAsync(worldServerID, DbSceneType(SceneType.PvP), sceneName, context.Format, GroupFinderStaleBefore);
				int waiting = countResult.IsSuccess ? Math.Max(memberIDs.Count, countResult.Data) : memberIDs.Count;

				TryEnqueueMainThread(() =>
				{
					foreach (GroupFinderEntry entry in members)
					{
						if (entry.Connection == null || !entry.Connection.IsActive || entry.Connection.FirstObject == null)
						{
							// Gone between the insert and now; the disconnect hook or the sweep removes the row.
							continue;
						}

						entry.NextBackfillAttemptUtc = DateTime.MaxValue;
						groupFinderEntries[entry.CharacterID] = entry;
						SendGroupFinderStatus(entry.Connection, entry, GroupFinderState.Waiting, GroupFinderRefusalReason.None, waiting);
					}

					Log.Debug("InteractableSystem",
						$"Arena: {memberIDs.Count} queued for '{context.Template.name}' {context.Template.GetFormatName(context.Format)} ({waiting}/{context.MatchSize})" +
						(groupID > 0 ? $" as party {groupID}." : "."));
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error queuing character {characterID} for an arena: {ex}");
				TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError, SceneType.PvP));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// For one arena at one format: try to form a match, then tell whoever is still waiting how
		/// many are waiting. Called by the group finder pump for arena rows.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Backfill first.</b> A live match of this arena and format with a seat vacated inside
		/// its backfill window takes the longest-waiting solo before any new match is formed: a
		/// game already running is a better use of a waiter than a game that might form.
		/// </para>
		/// <para>
		/// <b>Then form.</b> Ranked formats match within a rating band anchored on the longest
		/// local waiter and widened by their wait; unranked formats have no band. Both balance
		/// teams by rating when the template asks: the season rating for ranked, the PvP Rank
		/// attribute otherwise. A match that forms ranked is stamped with the season at once, so
		/// whichever server hosts it reads the right ratings.
		/// </para>
		/// </remarks>
		private async Task ProcessWaitingArenaGroupAsync(IGroupFinderQueueService queueService, string sceneName, int format, List<GroupFinderPumpItem> waiters)
		{
			if (waiters.Count == 0)
			{
				return;
			}

			long worldServerID = waiters[0].WorldServerID;
			int matchSize = waiters[0].GroupSize;
			int teamCount = waiters[0].TeamCount;
			int teamSize = waiters[0].TeamSize;
			int templateID = waiters[0].ArenaTemplateID;
			bool ranked = waiters[0].Ranked;

			var stillWaiting = new List<GroupFinderPumpItem>(waiters);

			// Backfill: seats vacated in live matches, one waiter per seat, bounded per pump.
			for (int i = 0; i < ArenaBackfillPerPump && stillWaiting.Count > 0; ++i)
			{
				var backfill = await queueService.TryBackfillArenaSeatAsync(worldServerID, sceneName, format, GroupFinderStaleBefore);
				if (!backfill.IsSuccess || !backfill.Data.Filled)
				{
					break;
				}
				await Log.Debug("InteractableSystem",
					$"Arena: character {backfill.Data.CharacterID} backfilled team {backfill.Data.Team + 1} of match {backfill.Data.MatchID} ('{sceneName}' format {format}).");
				int index = stillWaiting.FindIndex(w => w.CharacterID == backfill.Data.CharacterID);
				if (index >= 0)
				{
					stillWaiting.RemoveAt(index);
					await DispatchMatchedAsync(backfill.Data.CharacterID, 0, backfill.Data.InstanceID);
				}
				/* A waiter from another server took the seat: their own pump sees the row matched
				 * and moves them. Try again; another seat may be open. */
			}

			var countResult = await queueService.CountWaitingAsync(worldServerID, DbSceneType(SceneType.PvP), sceneName, format, GroupFinderStaleBefore);
			if (!countResult.IsSuccess)
			{
				return;
			}
			int waiting = countResult.Data;

			if (waiting >= matchSize && stillWaiting.Count > 0)
			{
				/* Rating source and band. The band grows with the longest wait this server can see;
				 * a waiter on another server anchors their own server's band, so the two widen at
				 * about the same rate and whoever pumps first forms the match. */
				DateTime oldest = DateTime.UtcNow;
				foreach (GroupFinderPumpItem w in stillWaiting)
				{
					if (w.QueuedAtUtc < oldest)
					{
						oldest = w.QueuedAtUtc;
					}
				}
				double waited = (DateTime.UtcNow - oldest).TotalSeconds;

				ArenaRatingSource ratingSource = ArenaRatingSource.None;
				long seasonID = 0;
				int band = 0;
				if (ranked)
				{
					if (Server.Database.ServiceRegistry.TryGet<IArenaRatingService>(out var ratingService))
					{
						var seasonResult = await ratingService.GetOrCreateActiveSeasonAsync();
						if (seasonResult.IsSuccess)
						{
							seasonID = seasonResult.Data.ID;
							ratingSource = ArenaRatingSource.FromSeason(seasonID, ArenaRating.DefaultRating);
						}
					}
					band = ArenaRating.ResolveBand(waiters[0].RatingBandBase, waiters[0].RatingBandGrowth, waited, waiters[0].RatingBandMax);
				}
				else if (waiters[0].BalanceTeams && waiters[0].RankAttributeTemplateID > 0)
				{
					ratingSource = ArenaRatingSource.FromAttribute(waiters[0].RankAttributeTemplateID);
				}
				var composeOptions = new ArenaComposeOptions(band, waiters[0].BalanceTeams);

				var formResult = await queueService.TryFormArenaMatchAsync(
					worldServerID, sceneName, format, templateID, teamCount, teamSize, GroupFinderStaleBefore, 128, ratingSource, composeOptions);

				if (formResult.IsSuccess && formResult.Data.Formed && ranked && seasonID > 0 &&
					Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					await matchService.SetRankedAsync(formResult.Data.MatchID, seasonID);
				}

				if (!formResult.IsSuccess)
				{
					await Log.Debug("InteractableSystem",
						$"Arena: no match formed for '{sceneName}' format {format}: {formResult.ErrorCode} - {formResult.ErrorMessage}");
				}
				else if (formResult.Data.Formed)
				{
					ArenaMatchFormedData match = formResult.Data;
					await Log.Debug("InteractableSystem",
						$"Arena: match {match.MatchID} formed for '{sceneName}' format {format} with {match.Seats.Count} players; instance {match.InstanceID}.");

					var placedHere = new HashSet<long>();
					foreach (ArenaSeat seat in match.Seats)
					{
						foreach (GroupFinderPumpItem waiter in stillWaiting)
						{
							if (waiter.CharacterID == seat.CharacterID)
							{
								placedHere.Add(seat.CharacterID);
								await DispatchMatchedAsync(seat.CharacterID, 0, match.InstanceID);
								break;
							}
						}
					}

					stillWaiting.RemoveAll(w => placedHere.Contains(w.CharacterID));
					waiting = Math.Max(0, waiting - match.Seats.Count);
				}
			}

			int reportedWaiting = waiting;
			foreach (GroupFinderPumpItem waiter in stillWaiting)
			{
				long characterID = waiter.CharacterID;
				TryEnqueueMainThread(() =>
				{
					if (!groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry) ||
						entry.State != GroupFinderState.Waiting)
					{
						return;
					}

					if (entry.LastSentWaitingCount != reportedWaiting)
					{
						SendGroupFinderStatus(entry.Connection, entry, GroupFinderState.Waiting, GroupFinderRefusalReason.None, reportedWaiting);
					}
				});
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Board lookups: profile, history, leaderboard
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Validates that a connection is standing at the board it names. Main thread only.</summary>
		private bool TryResolveArenaLookup(NetworkConnection conn, long interactableID, out IPlayerCharacter character)
		{
			character = null;
			if (conn?.FirstObject == null)
			{
				return false;
			}
			character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null || !ValidateSceneObject(interactableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return false;
			}
			IArenaBoard board = sceneObject.GameObject.GetComponent<IArenaBoard>();
			return board != null && board.InRange(character.Transform);
		}

		/// <summary>The player's season standing and queue lock, for the board.</summary>
		public void OnServerArenaProfileRequestReceived(NetworkConnection conn, ArenaProfileRequestBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (!TryBeginIngressGuard(conn.ClientId, ArenaLookupOperation, ArenaLookupDebounceMilliseconds, out long guardKey))
			{
				return;
			}
			bool asyncOwns = false;
			try
			{
				if (!TryResolveArenaLookup(conn, msg.InteractableID, out IPlayerCharacter character))
				{
					return;
				}
				long characterID = character.ID;
				asyncOwns = TryEnqueueAsyncWork(() => SendArenaProfileAsync(conn, characterID, guardKey), conn, characterID);
			}
			finally
			{
				if (!asyncOwns)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		private async Task SendArenaProfileAsync(NetworkConnection conn, long characterID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaRatingService>(out var ratingService))
				{
					return;
				}

				var seasonResult = await ratingService.GetOrCreateActiveSeasonAsync();
				if (!seasonResult.IsSuccess)
				{
					return;
				}
				ArenaSeasonData season = seasonResult.Data;

				var profile = new ArenaProfileBroadcast
				{
					SeasonID = season.ID,
					SeasonName = season.Name ?? string.Empty,
					Rating = ArenaRating.DefaultRating,
					PeakRating = ArenaRating.DefaultRating,
					QueueLockReason = string.Empty,
				};

				var ids = new List<long>(1) { characterID };
				var ratingResult = await ratingService.FetchRatingsAsync(season.ID, ids);
				if (ratingResult.IsSuccess && ratingResult.Data.Count > 0)
				{
					ArenaRatingData r = ratingResult.Data[0];
					profile.Rating = r.Rating;
					profile.PeakRating = r.PeakRating;
					profile.Games = r.Games;
					profile.Wins = r.Wins;
					profile.Losses = r.Losses;
				}

				if (Server.Database.ServiceRegistry.TryGet<IArenaPenaltyService>(out var penaltyService))
				{
					var lockResult = await penaltyService.FetchActiveAsync(ids);
					if (lockResult.IsSuccess && lockResult.Data.Count > 0)
					{
						profile.QueueLockSeconds = Math.Max(1, (int)Math.Ceiling((lockResult.Data[0].LockedUntilUtc - DateTime.UtcNow).TotalSeconds));
						profile.QueueLockReason = lockResult.Data[0].Reason ?? string.Empty;
					}
				}

				int games = profile.Games;
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive)
					{
						return;
					}
					/* Placement games come from the template of whatever arena the board shows; the
					 * profile is per season, so the default is used unless every template agrees.
					 * Clients show the template's own number beside the rating for each arena. */
					profile.PlacementGamesRemaining = ArenaRating.PlacementGamesRemaining(games, 10);
					Server.NetworkWrapper.Broadcast(conn, profile, true, FishNet.Transporting.Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error building the arena profile of character {characterID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>The player's recent matches, for the board.</summary>
		public void OnServerArenaHistoryRequestReceived(NetworkConnection conn, ArenaHistoryRequestBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (!TryBeginIngressGuard(conn.ClientId, ArenaLookupOperation, ArenaLookupDebounceMilliseconds, out long guardKey))
			{
				return;
			}
			bool asyncOwns = false;
			try
			{
				if (!TryResolveArenaLookup(conn, msg.InteractableID, out IPlayerCharacter character))
				{
					return;
				}
				long characterID = character.ID;
				asyncOwns = TryEnqueueAsyncWork(() => SendArenaHistoryAsync(conn, characterID, guardKey), conn, characterID);
			}
			finally
			{
				if (!asyncOwns)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		private async Task SendArenaHistoryAsync(NetworkConnection conn, long characterID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					return;
				}

				var historyResult = await matchService.FetchRecentForCharacterAsync(characterID, ArenaHistoryRows);
				if (!historyResult.IsSuccess)
				{
					return;
				}

				var entries = new ArenaHistoryEntry[historyResult.Data.Count];
				for (int i = 0; i < entries.Length; ++i)
				{
					ArenaHistoryData line = historyResult.Data[i];
					DateTime ended = line.Match.TimeEnded ?? line.Match.TimeCreated;
					entries[i] = new ArenaHistoryEntry
					{
						MatchID = line.Match.ID,
						ArenaTemplateID = line.Match.TemplateID,
						Format = line.Match.Format,
						Ranked = line.Match.Ranked,
						Team = line.Seat.Team,
						WinnerTeam = line.Match.Status == (int)FishMMO.Database.Data.Enums.ArenaMatchStatus.Ended ? line.Match.WinnerTeam : -1,
						Kills = line.Seat.Kills,
						Deaths = line.Seat.Deaths,
						Score = line.Seat.Score,
						RatingDelta = line.Seat.RatingDelta,
						Deserted = line.Seat.Status == (int)ArenaSeatStatus.Vacated,
						EndedUnix = new DateTimeOffset(DateTime.SpecifyKind(ended, DateTimeKind.Utc)).ToUnixTimeSeconds(),
					};
				}

				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new ArenaHistoryBroadcast { Entries = entries }, true, FishNet.Transporting.Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error building the arena history of character {characterID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>The season leaderboard, for the board.</summary>
		public void OnServerArenaLeaderboardRequestReceived(NetworkConnection conn, ArenaLeaderboardRequestBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (!TryBeginIngressGuard(conn.ClientId, ArenaLookupOperation, ArenaLookupDebounceMilliseconds, out long guardKey))
			{
				return;
			}
			bool asyncOwns = false;
			try
			{
				if (!TryResolveArenaLookup(conn, msg.InteractableID, out IPlayerCharacter character))
				{
					return;
				}
				long characterID = character.ID;
				asyncOwns = TryEnqueueAsyncWork(() => SendArenaLeaderboardAsync(conn, characterID, guardKey), conn, characterID);
			}
			finally
			{
				if (!asyncOwns)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		private async Task SendArenaLeaderboardAsync(NetworkConnection conn, long characterID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaRatingService>(out var ratingService) ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				var seasonResult = await ratingService.GetOrCreateActiveSeasonAsync();
				if (!seasonResult.IsSuccess)
				{
					return;
				}
				ArenaSeasonData season = seasonResult.Data;

				var topResult = await ratingService.FetchTopAsync(season.ID, ArenaLeaderboardRows);
				if (!topResult.IsSuccess)
				{
					return;
				}

				var ids = new List<long>(topResult.Data.Count);
				foreach (ArenaRatingData r in topResult.Data)
				{
					ids.Add(r.CharacterID);
				}
				var names = new Dictionary<long, string>(ids.Count);
				var namesResult = await characterService.FetchNamesAsync(ids);
				if (namesResult.IsSuccess)
				{
					foreach (CharacterNameData n in namesResult.Data)
					{
						names[n.CharacterID] = n.Name;
					}
				}

				var entries = new ArenaLeaderboardEntry[topResult.Data.Count];
				int yourRank = 0;
				for (int i = 0; i < entries.Length; ++i)
				{
					ArenaRatingData r = topResult.Data[i];
					if (r.CharacterID == characterID)
					{
						yourRank = i + 1;
					}
					entries[i] = new ArenaLeaderboardEntry
					{
						CharacterID = r.CharacterID,
						CharacterName = names.TryGetValue(r.CharacterID, out string name) ? name : "Unknown",
						Rating = r.Rating,
						Wins = r.Wins,
						Losses = r.Losses,
					};
				}

				var board = new ArenaLeaderboardBroadcast
				{
					SeasonID = season.ID,
					SeasonName = season.Name ?? string.Empty,
					Entries = entries,
					YourRank = yourRank,
				};
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, board, true, FishNet.Transporting.Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error building the arena leaderboard for character {characterID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}
