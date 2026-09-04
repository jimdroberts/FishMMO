using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
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

		/// <summary>Registers the arena board's requests. Called from the group finder's initialiser.</summary>
		private void InitializeArena()
		{
			Server.NetworkWrapper.RegisterBroadcast<ArenaQueueBroadcast>(OnServerArenaQueueBroadcastReceived, true);

			/* Game masters may step into a running match to watch it. A spectator has no seat, so
			 * the team registry reports them as an ally to everyone and nobody can hit them or be
			 * hit by them; they are returned with everybody else when the match closes. */
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/spectatearena", OnSpectateArenaCommand },
			}, FishMMO.Auth.Core.AccessLevel.GameMaster);

			InitializeArenaMatches();
		}

		/// <summary>Unregisters the arena board's requests.</summary>
		private void DeinitializeArena()
		{
			Server.NetworkWrapper.UnregisterBroadcast<ArenaQueueBroadcast>(OnServerArenaQueueBroadcastReceived);
			ChatHelper.RemoveCommands(new[] { "/spectatearena" });
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
		/// No late-join. An arena match is complete or it is not; nobody joins one in progress.
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

			var countResult = await queueService.CountWaitingAsync(worldServerID, DbSceneType(SceneType.PvP), sceneName, format, GroupFinderStaleBefore);
			if (!countResult.IsSuccess)
			{
				return;
			}
			int waiting = countResult.Data;

			var stillWaiting = new List<GroupFinderPumpItem>(waiters);

			if (waiting >= matchSize)
			{
				var formResult = await queueService.TryFormArenaMatchAsync(
					worldServerID, sceneName, format, templateID, teamCount, teamSize, GroupFinderStaleBefore);

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
	}
}
