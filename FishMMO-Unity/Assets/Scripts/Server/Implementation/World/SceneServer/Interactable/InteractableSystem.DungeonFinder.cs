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
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Dungeon finder: validates dungeon entrance interactions and asynchronously assigns or creates dungeon instances.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Ingress guard operation codes for the dungeon finder's three requests.
		/// </summary>
		/// <remarks>
		/// Separate codes rather than the shared interaction key, so browsing the list cannot
		/// debounce the attempt to enter that follows it. They are debounced independently and at
		/// very different rates: listing is cheap and repeated deliberately, entering is expensive
		/// and happens once.
		/// </remarks>
		private const byte DungeonListOperation = 10;
		private const byte DungeonEnterOperation = 11;

		/// <summary>
		/// Minimum milliseconds between list requests from one connection.
		/// </summary>
		/// <remarks>
		/// The list is a database query whose timing the client chooses, which is the shape of
		/// request that has to be rate limited whether or not the client cooperates. Two seconds
		/// is short enough that a player pressing Refresh sees it answer and long enough that
		/// holding the button down is not a query per frame. The panel disables its own Refresh
		/// for the same interval so the ordinary case never meets this at all.
		/// </remarks>
		private const int DungeonListDebounceMilliseconds = 2000;

		/// <summary>
		/// Ceiling on instances returned in one list.
		/// </summary>
		/// <remarks>
		/// The reply is serialised into a single broadcast, so this bounds a message size as well
		/// as a query. Far more rows than a player will read; a shard with more open instances of
		/// one dungeon than this has a matchmaking problem rather than a listing problem.
		/// </remarks>
		private const int MaxListedInstances = 24;

		/// <summary>
		/// Everything the main thread knows about a dungeon request, captured before going async.
		/// </summary>
		/// <remarks>
		/// A request begins on the main thread — where the character, the entrance and the scene
		/// details can be touched — and finishes on an async worker, where none of them can. This
		/// carries across what the worker needs as plain values, which is also what makes the
		/// three request paths able to share their validation: they differ in what they do with
		/// this, not in how they build it.
		/// </remarks>
		private struct DungeonRequestContext
		{
			public IPlayerCharacter Character;
			public long CharacterID;
			public long WorldServerID;
			public long PartyID;
			public PartyRank PartyRank;
			public string DungeonName;
			public int DungeonTemplateID;
			public WorldSceneDetails SceneDetails;
			public AchievementTemplate AchievementTemplate;
		}

		/// <summary>
		/// Validates that a connection is standing at a usable dungeon entrance, and captures what
		/// the async half of the request will need. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Shared by all three dungeon finder requests. Listing does not check character state —
		/// reading a list is not a move and refusing to show it to somebody in combat only hides
		/// information they can see by walking away — but everything else about the entrance is
		/// validated identically, including the range check, so a client cannot list or enter a
		/// dungeon it is not standing in front of.
		/// </para>
		/// <para>
		/// The scene object handle is validated against the character's own scene. A handle is
		/// only meaningful inside the process that allocated it, so this is what stops an ID
		/// harvested from one scene server naming something else on another.
		/// </para>
		/// </remarks>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="interactableID">Scene object ID the request named.</param>
		/// <param name="context">Receives the captured request state.</param>
		/// <returns>True when the entrance resolved; false when the request should be refused.</returns>
		private bool TryResolveDungeonEntrance(NetworkConnection conn, long interactableID, out DungeonRequestContext context)
		{
			context = default;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return false;
			}

			if (!ValidateSceneObject(interactableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return false;
			}

			IDungeonEntrance dungeonEntrance = sceneObject.GameObject.GetComponent<IDungeonEntrance>();
			if (dungeonEntrance == null ||
				!dungeonEntrance.InRange(character.Transform))
			{
				return false;
			}

			if (worldSceneDetailsCache == null ||
				!worldSceneDetailsCache.Scenes.TryGetValue(dungeonEntrance.DungeonName, out WorldSceneDetails details))
			{
				Log.Debug("InteractableSystem", "Missing Scene:" + dungeonEntrance.DungeonName);
				return false;
			}

			long partyID = 0;
			PartyRank partyRank = PartyRank.None;
			if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
			{
				partyID = partyController.ID;
				partyRank = partyController.Rank;
			}

			context = new DungeonRequestContext
			{
				Character = character,
				CharacterID = character.ID,
				WorldServerID = character.WorldServerID,
				PartyID = partyID,
				PartyRank = partyRank,
				DungeonName = dungeonEntrance.DungeonName,
				DungeonTemplateID = dungeonEntrance.DungeonTemplateID,
				SceneDetails = details,
				AchievementTemplate = dungeonEntrance.AchievementTemplate,
			};
			return true;
		}

		/// <summary>
		/// Resolves the difficulty a request named against the dungeon's own list.
		/// </summary>
		/// <remarks>
		/// A dungeon with no template, or an empty difficulty list, offers exactly one difficulty
		/// at index 0 with default rules — which is how every dungeon authored before difficulties
		/// existed behaves, so none of them needed changing.
		/// <para>
		/// An index the dungeon does not offer is <em>refused</em>, never clamped. Clamping would
		/// quietly enter a player into a ruleset they did not choose, and on a dungeon whose top
		/// difficulty ends a character's run on their first death that is not a rounding error.
		/// </para>
		/// </remarks>
		/// <param name="templateID">Dungeon template ID from the entrance.</param>
		/// <param name="difficulty">Difficulty index the request named.</param>
		/// <param name="definition">Receives the ruleset for that index.</param>
		/// <returns>True when the dungeon offers that difficulty.</returns>
		private static bool TryResolveDifficulty(int templateID, int difficulty, out DungeonDifficultyDefinition definition)
		{
			definition = null;

			if (difficulty < 0)
			{
				return false;
			}

			DungeonTemplate template = templateID != 0
				? DungeonTemplate.Get<DungeonTemplate>(templateID)
				: null;

			if (template == null)
			{
				// No template: one unnamed difficulty at index 0, default rules.
				if (difficulty != 0)
				{
					return false;
				}

				definition = FallbackDifficulty;
				return true;
			}

			if (!template.IsValidDifficulty(difficulty))
			{
				return false;
			}

			definition = template.GetDifficulty(difficulty);
			return true;
		}

		/// <summary>
		/// The ruleset a dungeon with no template runs at.
		/// </summary>
		/// <remarks>
		/// Shared and never mutated — nothing writes to a difficulty definition at runtime — so
		/// one instance is enough and a fresh allocation per request would be waste on a path a
		/// client can repeat.
		/// </remarks>
		private static readonly DungeonDifficultyDefinition FallbackDifficulty = new DungeonDifficultyDefinition();

		// ──────────────────────────────────────────────────────────────────
		//  Listing
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Answers a client browsing the instances joinable at one difficulty.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every exit sends a <see cref="DungeonFinderListResultBroadcast"/>, including the
		/// refusals and the empty lists. The panel disables its list and its Refresh button while
		/// a request is outstanding — the guard that stops the button being held down — so a
		/// handler that returned silently would leave the panel inert for the rest of its life.
		/// </para>
		/// <para>
		/// Not gated on character state. Reading a list is not a move, and a player in combat who
		/// cannot see the list can see it by walking ten metres away; refusing would hide
		/// information rather than prevent an action.
		/// </para>
		/// </remarks>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Which entrance and which difficulty.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerDungeonFinderListBroadcastReceived(NetworkConnection conn, DungeonFinderListBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, DungeonListOperation, DungeonListDebounceMilliseconds, out long guardKey))
			{
				/* Debounced. Answered rather than dropped, and answered with the difficulty the
				 * client asked for, so the panel can re-enable its controls and say why the list
				 * did not change instead of waiting forever for a reply that is not coming. */
				SendInstanceList(conn, msg.InteractableID, msg.Difficulty, null, DungeonListFailureReason.OnCooldown);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (!TryResolveDungeonEntrance(conn, msg.InteractableID, out DungeonRequestContext context))
				{
					SendInstanceList(conn, msg.InteractableID, msg.Difficulty, null, DungeonListFailureReason.NoEntrance);
					return;
				}

				if (!TryResolveDifficulty(context.DungeonTemplateID, msg.Difficulty, out DungeonDifficultyDefinition difficulty))
				{
					SendInstanceList(conn, msg.InteractableID, msg.Difficulty, null, DungeonListFailureReason.UnknownDifficulty);
					return;
				}

				int capacity = difficulty.ResolveCapacity(context.SceneDetails.MaxClients);
				long ownPartyID = context.PartyID;
				long interactableID = msg.InteractableID;
				int requestedDifficulty = msg.Difficulty;
				string dungeonName = context.DungeonName;
				long worldServerID = context.WorldServerID;

				if (TryEnqueueAsyncWork(
					() => FetchInstanceListAsync(conn, interactableID, dungeonName, requestedDifficulty, worldServerID, ownPartyID, capacity, guardKey),
					conn,
					context.CharacterID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendInstanceList(conn, interactableID, requestedDifficulty, null, DungeonListFailureReason.ServerError);
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
		/// Reads the joinable instances and their leaders' names, then answers the client.
		/// </summary>
		/// <param name="conn">Connection to answer.</param>
		/// <param name="interactableID">Entrance the request named, echoed back.</param>
		/// <param name="dungeonName">Dungeon scene to list.</param>
		/// <param name="difficulty">Difficulty index to list, echoed back.</param>
		/// <param name="worldServerID">World server to search.</param>
		/// <param name="ownPartyID">Requester's party, so its own instance can be marked.</param>
		/// <param name="capacity">Capacity at this difficulty; fuller rows are omitted by the query.</param>
		/// <param name="guardKey">Ingress guard key released when this task completes.</param>
		private async Task FetchInstanceListAsync(
			NetworkConnection conn,
			long interactableID,
			string dungeonName,
			int difficulty,
			long worldServerID,
			long ownPartyID,
			int capacity,
			long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendInstanceList(conn, interactableID, difficulty, null, DungeonListFailureReason.ServerError));
					return;
				}

				var listResult = await sceneService.FetchJoinableInstancesAsync(
					worldServerID,
					dungeonName,
					difficulty,
					(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
					capacity,
					MaxListedInstances);

				if (!listResult.IsSuccess || listResult.Data == null)
				{
					await Log.Warning("InteractableSystem",
						$"Could not list instances of '{dungeonName}' at difficulty {difficulty}: {listResult.ErrorCode} - {listResult.ErrorMessage}");
					TryEnqueueMainThread(() => SendInstanceList(conn, interactableID, difficulty, null, DungeonListFailureReason.ServerError));
					return;
				}

				/* Names resolved in one batched query rather than one per row.
				 *
				 * The row records the character who opened the instance, and that is what the list
				 * labels each run with. It is not necessarily the party's leader by the time
				 * anybody browses — leadership moves when a leader leaves — but it is stable, it
				 * is one query for the whole list, and it answers the question a player browsing
				 * actually has, which is "whose run is this". Who currently *leads* it is shown
				 * inside the instance panel, where it is authoritative and where it matters. */
				var openerIDs = new List<long>(listResult.Data.Count);
				foreach (SceneData scene in listResult.Data)
				{
					if (scene.CharacterID > 0)
					{
						openerIDs.Add(scene.CharacterID);
					}
				}

				var names = new Dictionary<long, string>(openerIDs.Count);
				if (openerIDs.Count > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					var nameResult = await characterService.FetchNamesAsync(openerIDs);
					if (nameResult.IsSuccess && nameResult.Data != null)
					{
						foreach (CharacterNameData name in nameResult.Data)
						{
							names[name.CharacterID] = name.Name;
						}
					}
					else
					{
						/* A name lookup that fails does not fail the list. The rows are still
						 * joinable and still describe themselves by size and by clock; an unnamed
						 * row is a worse row, not a broken one, and refusing the whole list over
						 * it would take away the player's only way into the dungeon. */
						await Log.Warning("InteractableSystem",
							$"Could not resolve leader names for the instance list of '{dungeonName}'.");
					}
				}

				var entries = new List<DungeonInstanceEntry>(listResult.Data.Count);
				foreach (SceneData scene in listResult.Data)
				{
					SceneStatus status = (SceneStatus)scene.SceneStatus;
					names.TryGetValue(scene.CharacterID, out string leaderName);

					entries.Add(new DungeonInstanceEntry
					{
						InstanceID = scene.ID,
						LeaderName = leaderName ?? string.Empty,
						MemberCount = scene.CharacterCount,
						MaxMembers = capacity,
						/* Not sent from here. The expiry clock belongs to the scene server hosting
						 * the instance and this is not necessarily that server; a number invented
						 * here would be wrong for every instance hosted elsewhere. The panel shows
						 * a dash, and the real clock appears in the instance panel on arrival. */
						RemainingSeconds = 0,
						IsLoading = status != SceneStatus.Ready,
						IsOwnParty = ownPartyID != 0 && scene.PartyID == ownPartyID,
					});
				}

				DungeonInstanceEntry[] payload = entries.ToArray();
				TryEnqueueMainThread(() => SendInstanceList(conn, interactableID, difficulty, payload, DungeonListFailureReason.None));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error listing dungeon instances: {ex}");
				TryEnqueueMainThread(() => SendInstanceList(conn, interactableID, difficulty, null, DungeonListFailureReason.ServerError));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Sends one instance list to a connection, if it is still there. Main thread only.
		/// </summary>
		private void SendInstanceList(NetworkConnection conn, long interactableID, int difficulty, DungeonInstanceEntry[] entries, DungeonListFailureReason reason)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new DungeonFinderListResultBroadcast()
			{
				InteractableID = interactableID,
				Difficulty = difficulty,
				Instances = entries ?? Array.Empty<DungeonInstanceEntry>(),
				Reason = reason,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Entering
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Validates the half of a dungeon entry request that is common to opening one and joining
		/// one, and refuses with a reason. Main thread only.
		/// </summary>
		/// <remarks>
		/// <b>CanActOrMove, not CanAct.</b> Entering a dungeon is a voluntary move to another
		/// scene instance, and it is implemented as a disconnect — so in combat it is both a
		/// cleaner escape than any teleporter (instant, and it lands the player somewhere their
		/// attacker cannot follow) and actively corrupting. The disconnect lands in
		/// <c>CharacterSystem.OnRemoteConnectionStopped</c>, which for an unannounced drop starts a
		/// combat-logout linger: the body stays on THIS scene server holding the character's
		/// session claim, while the row now says the character is in an instance. The world server
		/// routes the reconnect to the instance's scene server, which has no body to reattach,
		/// loses the claim race, and kicks the player — on every retry, until the linger runs out.
		/// The channel switch is gated the same way for the same reasons.
		/// </remarks>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="interactableID">Entrance the request named.</param>
		/// <param name="context">Receives the captured request state.</param>
		/// <returns>True when the request may proceed.</returns>
		private bool TryBeginDungeonEntry(NetworkConnection conn, long interactableID, out DungeonRequestContext context)
		{
			context = default;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return false;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return false;
			}

			// Already inside an instance: the entrance is not a way to hop between them.
			if (character.IsInInstance())
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return false;
			}

			if (!TryResolveDungeonEntrance(conn, interactableID, out context))
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
				return false;
			}

			if (context.SceneDetails.RespawnPositions == null || context.SceneDetails.RespawnPositions.Count < 1)
			{
				Log.Debug("InteractableSystem", $"Missing Scene: {context.DungeonName} respawn points.");
				SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
				return false;
			}

			return true;
		}

		/// <summary>
		/// Handles a request to open a new instance of a dungeon.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Which entrance, which difficulty, and whether to list it.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerDungeonFinderCreateBroadcastReceived(NetworkConnection conn, DungeonFinderCreateBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, DungeonEnterOperation, interactionDebounceMilliseconds, out long guardKey))
			{
				// Debounced or already in flight; say so rather than appearing to ignore the click.
				SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (!TryBeginDungeonEntry(conn, msg.InteractableID, out DungeonRequestContext context))
				{
					return;
				}

				if (!TryResolveDifficulty(context.DungeonTemplateID, msg.Difficulty, out DungeonDifficultyDefinition difficulty))
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.RequirementsNotMet);
					return;
				}

				/* The cap on an instance is the difficulty's if it declares one, and the scene's
				 * otherwise.
				 *
				 * Nothing capped instanced scenes at all before: the world server's instance
				 * routing sends a character to whichever scene server hosts its instance without
				 * consulting a limit, and joining a party member's instance did not either — so a
				 * Group scene could be filled without bound. The open world has always respected
				 * MaxClients; this applies a number to instanced content at the point where a
				 * refusal can still be reported to the player. */
				int capacity = difficulty.ResolveCapacity(context.SceneDetails.MaxClients);

				CharacterRespawnPositionDetails respawnDetails = context.SceneDetails.RespawnPositions.Values.ToList().GetRandom();

				// Increment achievement for entering a dungeon
				if (context.AchievementTemplate != null &&
					context.Character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(context.AchievementTemplate, 1);
				}

				float healthPCT = context.Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;

				DungeonRequestContext captured = context;
				int requestedDifficulty = msg.Difficulty;
				int minimumPartySize = difficulty.MinimumPartySize;
				bool isPrivate = msg.IsPrivate;
				string sceneName = conn.FirstObject.gameObject.scene.name;

				if (TryEnqueueAsyncWork(
					() => ProcessDungeonCreateAsync(conn, captured, requestedDifficulty, minimumPartySize, isPrivate, capacity, respawnDetails, healthPCT, sceneName, guardKey),
					conn,
					context.CharacterID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.ServerError);
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
		/// Resolves what the party already holds, then joins it or opens a new instance.
		/// </summary>
		/// <param name="conn">Owning connection, dropped after assignment so the world server re-routes it.</param>
		/// <param name="context">The request captured on the main thread.</param>
		/// <param name="difficultyIndex">Difficulty to open at.</param>
		/// <param name="minimumPartySize">Party size the difficulty demands.</param>
		/// <param name="isPrivate">Whether to hide the new instance from the finder.</param>
		/// <param name="capacity">Capacity at this difficulty.</param>
		/// <param name="respawnDetails">Where to place the character on arrival.</param>
		/// <param name="healthPCT">Health fraction, if a party has to be formed.</param>
		/// <param name="sceneName">Current scene name, for a party create broadcast.</param>
		/// <param name="guardKey">Ingress guard key released when this task completes.</param>
		private async Task ProcessDungeonCreateAsync(
			NetworkConnection conn,
			DungeonRequestContext context,
			int difficultyIndex,
			int minimumPartySize,
			bool isPrivate,
			int capacity,
			CharacterRespawnPositionDetails respawnDetails,
			float healthPCT,
			string sceneName,
			long guardKey)
		{
			long characterID = context.CharacterID;
			long worldServerID = context.WorldServerID;
			long partyID = context.PartyID;
			string dungeonName = context.DungeonName;

			/* The row this request created, if it created one.
			 *
			 * A party may hold one instance at a time, so an instance nobody ends up entering is
			 * not merely litter — it locks the whole party out of every dungeon until the world
			 * server's stale-row sweep reaps it, which is minutes away. Every path below that
			 * gives up after creating the row releases it again. */
			long createdInstanceID = 0;

			/* The party this request formed, if it formed one.
			 *
			 * Opening a dungeon publicly forms the party the listing implies, and that only makes
			 * sense if the listing happens. Every path below that gives up after forming one has
			 * to give it back, or the player is left in a group of one they never asked for,
			 * created as a side effect of a dungeon that was never opened — and, being its leader,
			 * they then get the party frame and the leave prompt for it. */
			long formedPartyID = 0;

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				/* Resolve who counts as this party.
				 *
				 * One batched query then answers all three questions this path decides between:
				 * join the instance they already hold, refuse because they hold a different one,
				 * or create. It asks about every dungeon rather than only the one being requested,
				 * which is what makes the one-instance-per-party rule enforceable here as well as
				 * in the insert guard. */
				var partyMemberIDs = new List<long>(8) { characterID };
				if (partyID > 0)
				{
					List<long> members = await FetchPartyMemberIDsAsync(partyID);

					/* A roster this request cannot read is not an empty roster.
					 *
					 * Carrying on with just the requester would look the party up as if it were a
					 * solo character: it would miss an instance a member already holds and create
					 * a second one, splitting the group — which is the failure this whole path
					 * exists to prevent, produced by a transient database error. Refusing costs
					 * the player a retry; guessing costs them the run. */
					if (members == null)
					{
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					for (int i = 0; i < members.Count; ++i)
					{
						if (members[i] != characterID)
						{
							partyMemberIDs.Add(members[i]);
						}
					}
				}

				/* The party size requirement, checked against the roster rather than against who
				 * is currently inside. A dungeon that demands a group of four cannot be started by
				 * one member of a group of four and then finished alone. */
				if (minimumPartySize > 1 && partyMemberIDs.Count < minimumPartySize)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.RequirementsNotMet));
					return;
				}

				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					partyMemberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID, partyID);

				if (!heldResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Could not read held instances for character {characterID}: {heldResult.ErrorCode} - {heldResult.ErrorMessage}");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				long instanceID = 0;

				/* A full instance is refused, never worked around.
				 *
				 * Asking for a NEW instance whenever nothing joinable was found is right for
				 * "there is no instance" and badly wrong for "the instance is full": a party
				 * member arriving at a full party instance would silently get a second, empty copy
				 * of the dungeon and be separated from the group they were trying to join. */
				bool destinationFull = false;

				/* Holding a different dungeon is refused rather than worked around, which is the
				 * whole one-instance-per-party rule. Without it a party could hold a live copy of
				 * every dungeon on the shard at once, each one an idle physics scene. */
				bool holdsOtherInstance = false;

				foreach (SceneData held in heldResult.Data)
				{
					if (!IsUsableInstance(held, worldServerID))
					{
						continue;
					}

					if (!string.Equals(held.SceneName, dungeonName, StringComparison.Ordinal))
					{
						holdsOtherInstance = true;
						continue;
					}

					/* The instance being asked for, whatever difficulty it was opened at.
					 *
					 * Deliberately not matched on difficulty. A party holds one instance; asking
					 * for the same dungeon on Hard when the party already has it open on Normal is
					 * a request to go where the group is, not a request for a second copy — and
					 * creating one would split them. The difficulty the run is actually being
					 * played at is the one it was opened at, and the instance panel says so. */
					if (HasInstanceCapacity(held, capacity))
					{
						instanceID = held.ID;
					}
					else
					{
						destinationFull = true;
					}
					holdsOtherInstance = false;
					break;
				}

				if (instanceID <= 0 && !destinationFull && holdsOtherInstance)
				{
					Log.Debug("InteractableSystem",
						$"Dungeon entry refused for character {characterID}: it or its party already has a different instance open.");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.PartyInstanceExists));
					return;
				}

				if (destinationFull)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
					return;
				}

				// Nothing to join: ask for a new instance to be loaded.
				if (instanceID <= 0)
				{
					/* An instance others may join needs a party for them to join.
					 *
					 * Ownership, leadership and kick authority are all the owning party's, so an
					 * instance opened by an ungrouped character has no group for a joiner to be
					 * added to and could only ever be a solo run. Rather than refusing to let
					 * ungrouped players advertise at all, choosing to open one publicly forms the
					 * party that listing implies. A private run forms nothing — it needs no party,
					 * and creating one silently would be a side effect nobody asked for. */
					if (!isPrivate && partyID <= 0 &&
						Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
					{
						formedPartyID = await partySystem.TryCreatePartyForInstanceAsync(conn, characterID, worldServerID, sceneName, healthPCT);
						if (formedPartyID > 0)
						{
							partyID = formedPartyID;
						}
						else
						{
							/* Could not form one. Opened private rather than refused: the player
							 * asked for a dungeon and a listing, and giving them the dungeon
							 * without the listing is much closer to what they wanted than giving
							 * them neither. */
							isPrivate = true;
							await Log.Warning("InteractableSystem",
								$"Could not form a party for character {characterID}'s public instance of '{dungeonName}'; opening it private instead.");
						}
					}

					/* The search above and this insert are separate statements, and every member
					 * of a party clicking the entrance together runs both at the same time — on
					 * per-character async workers, and potentially on different scene servers.
					 * Each one found no instance, each one created its own, and the party was
					 * split across separate copies of the dungeon: the exact failure the party
					 * search exists to prevent, in the one situation where it matters most.
					 *
					 * EnqueueForPartyAsync folds the existence check into the insert, so the
					 * losers of that race insert nothing and are told to join the winner instead.
					 *
					 * Used for a solo character too, with a list of just themselves. Nobody can
					 * race them — a character has one session, and the ingress guard already
					 * refuses a second request from the same connection — but going through the
					 * guarded insert means the one-instance rule is enforced by the database for
					 * everyone, rather than by the in-memory check above for some and the database
					 * for others. */
					DatabaseResult<long> enqueueResult = await sceneService.EnqueueForPartyAsync(
						worldServerID,
						dungeonName,
						(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
						characterID,
						partyID,
						difficultyIndex,
						isPrivate,
						partyMemberIDs);

					if (!enqueueResult.IsSuccess)
					{
						await Log.Debug("InteractableSystem", "Failed to enqueue new pending scene load request: " + worldServerID + ":" + dungeonName);
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					instanceID = enqueueResult.Data;
					createdInstanceID = instanceID;

					if (instanceID <= 0)
					{
						/* Lost the race: a party member created an instance between our search and
						 * our insert. Look again and join theirs — which is the whole point of
						 * refusing to insert. */
						var raceResult = await sceneService.FetchCharacterInstancesAsync(
							partyMemberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID, partyID);

						bool raceHoldsOther = false;
						if (raceResult.IsSuccess)
						{
							foreach (SceneData held in raceResult.Data)
							{
								if (!IsUsableInstance(held, worldServerID))
								{
									continue;
								}
								if (!string.Equals(held.SceneName, dungeonName, StringComparison.Ordinal))
								{
									raceHoldsOther = true;
									continue;
								}
								if (HasInstanceCapacity(held, capacity))
								{
									instanceID = held.ID;
								}
								else
								{
									destinationFull = true;
								}
								raceHoldsOther = false;
								break;
							}
						}

						if (destinationFull)
						{
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
							return;
						}

						if (instanceID <= 0 && raceHoldsOther)
						{
							// A member opened a different dungeon first. One instance per party.
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.PartyInstanceExists));
							return;
						}

						if (instanceID <= 0)
						{
							/* Blocked by an instance the re-search could not find. The insert guard
							 * and the search now agree on which rows count, so this needs the two
							 * to have been looking at different rosters: a member who left the
							 * party between the two calls, or a party fetch that failed outright.
							 * Both are transient, and both make "try again" the honest answer —
							 * creating a second instance to work around the block would produce
							 * exactly the split party this guard exists to prevent. */
							await Log.Warning("InteractableSystem",
								$"Dungeon entry for character {characterID} was blocked by an existing but unusable instance of '{dungeonName}'; refusing.");
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable));
							return;
						}
					}
				}

				await DispatchInstanceEntryAsync(conn, context.Character, characterID, instanceID, respawnDetails, createdInstanceID);
				createdInstanceID = 0;

				// The listing happened, so the party it implied is real. Nothing to give back.
				formedPartyID = 0;
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error processing dungeon finder: {ex}");
				ReleaseCreatedInstance(createdInstanceID, "the request failed");
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
			}
			finally
			{
				/* In the finally rather than beside each refusal: a party formed for a dungeon
				 * that did not open has to be given back on every route out, and there are six of
				 * them. Zeroed on the success path above, so this only ever fires for a request
				 * that gave up. */
				await ReleaseFormedPartyAsync(characterID, formedPartyID);

				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Removes a character from a party this request formed for a dungeon that never opened.
		/// </summary>
		/// <param name="characterID">The character the party was formed around.</param>
		/// <param name="formedPartyID">The party, or 0 when none was formed.</param>
		/// <returns>Asynchronous release task.</returns>
		/// <remarks>
		/// The party has exactly one member by construction — it was created a moment ago for this
		/// request — so removing them retires it, which is what the party system does when the
		/// last member leaves. Nobody else can have joined in between: joining one requires it to
		/// have a published instance, and the instance is the thing that failed.
        /// <para>
		/// Their client is not told directly. The removal marks the party updated, and the pump
		/// notices the membership row is gone and clears the controller — the same route every
		/// other server-side removal takes.
		/// </para>
		/// </remarks>
		private async Task ReleaseFormedPartyAsync(long characterID, long formedPartyID)
		{
			if (formedPartyID <= 0)
			{
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				await Log.Warning("InteractableSystem",
					$"Character {characterID} was left in party {formedPartyID}, formed for a dungeon that did not open, because the party system is unavailable.");
				return;
			}

			await Log.Debug("InteractableSystem", $"Releasing party {formedPartyID}, formed for a dungeon that did not open.");

			await partySystem.RemoveCharacterFromPartyAsync(characterID, formedPartyID, "the dungeon it was formed for did not open");
		}

		/// <summary>
		/// Handles a request to join an instance somebody else has opened.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Which entrance and which instance.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerDungeonFinderJoinBroadcastReceived(NetworkConnection conn, DungeonFinderJoinBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			/* The same guard key as opening one, deliberately.
			 *
			 * Joining and creating are two ways to do the same thing, and both end in a
			 * disconnect-and-reroute. Debouncing them independently would let a client submit one
			 * of each and race two transfers of the same character against one another. */
			if (!TryBeginIngressGuard(conn.ClientId, DungeonEnterOperation, interactionDebounceMilliseconds, out long guardKey))
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (msg.InstanceID <= 0)
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable);
					return;
				}

				if (!TryBeginDungeonEntry(conn, msg.InteractableID, out DungeonRequestContext context))
				{
					return;
				}

				CharacterRespawnPositionDetails respawnDetails = context.SceneDetails.RespawnPositions.Values.ToList().GetRandom();

				if (context.AchievementTemplate != null &&
					context.Character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(context.AchievementTemplate, 1);
				}

				float healthPCT = context.Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;

				DungeonRequestContext captured = context;
				long targetInstanceID = msg.InstanceID;

				if (TryEnqueueAsyncWork(
					() => ProcessDungeonJoinAsync(conn, captured, targetInstanceID, respawnDetails, healthPCT, guardKey),
					conn,
					context.CharacterID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.ServerError);
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
		/// Validates a named instance, joins its party, and enters it.
		/// </summary>
		/// <remarks>
		/// The instance ID is checked against what the finder would actually have offered rather
		/// than trusted — the right dungeon, on this world server, public, enterable, and not full
		/// — because a row ID is a small integer and the panel is not the only thing that can send
		/// this message. Every one of those checks refuses with the same
		/// <see cref="SceneTransferRefusalReason.InstanceUnavailable"/>, so an ID cannot be probed
		/// to learn whether a particular instance exists.
		/// </remarks>
		/// <param name="conn">Owning connection, dropped after assignment.</param>
		/// <param name="context">The request captured on the main thread.</param>
		/// <param name="instanceID">Instance the client asked to join.</param>
		/// <param name="respawnDetails">Where to place the character on arrival.</param>
		/// <param name="healthPCT">Health fraction, for the party roster.</param>
		/// <param name="guardKey">Ingress guard key released when this task completes.</param>
		private async Task ProcessDungeonJoinAsync(
			NetworkConnection conn,
			DungeonRequestContext context,
			long instanceID,
			CharacterRespawnPositionDetails respawnDetails,
			float healthPCT,
			long guardKey)
		{
			long characterID = context.CharacterID;
			long worldServerID = context.WorldServerID;
			long partyID = context.PartyID;

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				DatabaseResult<SceneData> instanceResult = await sceneService.FetchAsync(instanceID);
				if (!instanceResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable));
					return;
				}

				SceneData instance = instanceResult.Data;

				bool ownPartyInstance = partyID > 0 && instance.PartyID == partyID;

				if (!IsUsableInstance(instance, worldServerID) ||
					instance.SceneType != (int)SceneType.Group ||
					!string.Equals(instance.SceneName, context.DungeonName, StringComparison.Ordinal))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable));
					return;
				}

				/* Privacy is a lock on the front door, not on the instance. A member of the owning
				 * party still gets in — that is the re-entry path, and it has to keep working
				 * whether or not the run has been hidden from strangers. */
				if (instance.IsPrivate && !ownPartyInstance)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable));
					return;
				}

				/* The difficulty the instance was actually opened at decides its rules, not
				 * anything the joiner asked for. A dungeon's difficulty list can be edited between
				 * an instance being opened and somebody joining it, so an index that no longer
				 * resolves is treated as a closed instance rather than as default rules. */
				if (!TryResolveDifficulty(context.DungeonTemplateID, instance.Difficulty, out DungeonDifficultyDefinition difficulty))
				{
					await Log.Warning("InteractableSystem",
						$"Instance {instanceID} of '{instance.SceneName}' was opened at difficulty {instance.Difficulty}, which the dungeon no longer offers; refusing the join.");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable));
					return;
				}

				int capacity = difficulty.ResolveCapacity(context.SceneDetails.MaxClients);
				if (!HasInstanceCapacity(instance, capacity))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
					return;
				}

				/* Deliberately no party-size requirement on the join path.
				 *
				 * MinimumPartySize gates who may *open* a run at a difficulty, and it is checked
				 * against the opener's roster. Re-checking it here would refuse the very thing it
				 * exists to encourage: a group of four assembling one member at a time, each of
				 * whom is alone at the moment they click. The run already has the group it
				 * demanded; the joiner is joining it. */

				if (!ownPartyInstance)
				{
					if (instance.PartyID <= 0)
					{
						/* A run with no party behind it. Not joinable by anybody but its opener:
						 * there is no group to be added to, so a joiner would end up inside an
						 * instance that could never resolve them as a member and that nobody could
						 * manage them in. */
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.InstanceUnavailable));
						return;
					}

					if (!await TryLeaveOwnPartyForJoinAsync(conn, characterID, partyID))
					{
						return;
					}

					if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
					{
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					/* The party is joined BEFORE the transfer, not after.
					 *
					 * The transfer is a disconnect and a reroute, so there is no "after" on this
					 * server to do it in — and a character that arrived inside the instance
					 * without having joined the party would be a stranger in somebody's run, with
					 * no leader able to remove them and no way for the finder to resolve the
					 * instance as theirs on a later visit. Joining first also means a refusal here
					 * costs nothing: the character has not moved. */
					if (!await partySystem.TryAddCharacterToPartyAsync(conn, characterID, instance.PartyID, healthPCT))
					{
						/* Almost always a full party. Reported as a full destination because that
						 * is what it means to the player: the run they clicked has no room. */
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
						return;
					}
				}

				/* No created instance to release on this path — a join never creates one, so a
				 * failure below leaves nothing behind that could block the party. */
				await DispatchInstanceEntryAsync(conn, context.Character, characterID, instanceID, respawnDetails, 0);
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error joining dungeon instance {instanceID}: {ex}");
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Clears the joiner's own party, when they have one they are allowed to leave.
		/// </summary>
		/// <remarks>
		/// Joining another group's run joins their party, and a character can only be in one. The
		/// rule is deliberately narrow: a character alone in a party of their own is simply
		/// released from it, and a character in a party with anybody else is refused outright.
		/// <para>
		/// Silently dissolving a real group would be a much larger act than the click that caused
		/// it — and if the joiner led that group it would hand it to somebody else without asking.
		/// So the player is told to leave first, and leaving stays their own deliberate decision.
		/// </para>
		/// </remarks>
		/// <returns>True when the character is free to join another party.</returns>
		private async Task<bool> TryLeaveOwnPartyForJoinAsync(NetworkConnection conn, long characterID, long partyID)
		{
			if (partyID <= 0)
			{
				return true;
			}

			List<long> members = await FetchPartyMemberIDsAsync(partyID);
			if (members == null)
			{
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
				return false;
			}

			if (members.Count > 1)
			{
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.AlreadyInParty));
				return false;
			}

			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
				return false;
			}

			/* Alone in it, so leaving retires the party rather than breaking up a group.
			 *
			 * The result is honoured: a refusal means the membership row still stands, and the
			 * join that follows would then persist over it — moving the character out of a party
			 * that is being changed underneath us, without telling anyone still in it. Reported as
			 * AlreadyInParty because that is exactly what is still true. */
			if (!await partySystem.RemoveCharacterFromPartyAsync(characterID, partyID, "joining another group's dungeon instance"))
			{
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.AlreadyInParty));
				return false;
			}

			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive || conn.FirstObject == null)
				{
					return;
				}

				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
				if (partyController == null || partyController.ID != partyID)
				{
					return;
				}

				partyController.ID = 0;
				partyController.Rank = PartyRank.None;
				Server?.NetworkWrapper?.Broadcast(conn, new PartyLeaveBroadcast(), true, FishNet.Transporting.Channel.Reliable);
			});

			return true;
		}

		/// <summary>
		/// Hands an entry to the main thread, releasing the created instance if the queue refuses.
		/// </summary>
		/// <remarks>
		/// The last step of both entry paths, shared because the failure handling is the part that
		/// matters and is easy to get wrong: a queue that will not take the work leaves a scene row
		/// the party is now blocked by, so the row has to be released on exactly this path and no
		/// other.
		/// </remarks>
		private async Task DispatchInstanceEntryAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			long instanceID,
			CharacterRespawnPositionDetails respawnDetails,
			long createdInstanceID)
		{
			long targetInstanceID = instanceID;
			long createdForRelease = createdInstanceID;

			if (!TryEnqueueMainThread(() => EnterInstance(conn, character, targetInstanceID, respawnDetails, createdForRelease)))
			{
				await Log.Warning("InteractableSystem",
					$"Main-thread queue rejected dungeon entry for character {characterID}; refusing the request.");
				ReleaseCreatedInstance(createdForRelease, "dungeon entry could not be dispatched");
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
			}
		}


		/// <summary>
		/// Binds a character to a dungeon instance and drops its connection so the world server
		/// routes it to the scene server hosting that instance. Main thread only.
		/// </summary>
		/// <remarks>
		/// The state is re-checked here rather than trusted from the request. Everything between
		/// the broadcast and this call was asynchronous database work, and a character can be
		/// pulled into combat or killed while it runs — at which point the disconnect below is no
		/// longer a transfer but a combat logout, with the consequences described in
		/// <see cref="OnServerDungeonFinderBroadcastReceived"/>.
		/// </remarks>
		private void EnterInstance(
			NetworkConnection conn,
			IPlayerCharacter character,
			long instanceID,
			CharacterRespawnPositionDetails respawnDetails,
			long createdInstanceID)
		{
			// Guard against character/connection being destroyed between async DB return and main-thread execution
			if (Server == null || conn == null || !conn.IsActive ||
				character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
			{
				ReleaseCreatedInstance(createdInstanceID, "the requesting connection went away before entry");
				return;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				Log.Debug("InteractableSystem", $"Dungeon entry aborted for {character.CharacterName}: state changed during validation.");
				ReleaseCreatedInstance(createdInstanceID, "the character's state changed before entry");
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return;
			}

			character.InstanceID = instanceID;
			character.InstancePosition = respawnDetails.Position;
			character.InstanceRotation = respawnDetails.Rotation;
			character.EnableFlags(CharacterFlags.IsInInstance);

			// Prevent gameplay actions during the transition.
			character.DisableFlags(CharacterFlags.IsLoaded);

			/* Announce the hand-off before dropping the connection.
			 *
			 * Without this the character system cannot tell this disconnect from a player
			 * quitting, and a character that entered combat in the last instant would have its
			 * body — and its session claim — held on this scene server while the row says it
			 * belongs to the instance. The arriving client would then be kicked for claim
			 * contention on every retry until the linger expired. */
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.SuppressCombatLingerOnDisconnect(conn);
			}

			conn.Disconnect(false);
		}

		/// <summary>
		/// <c>/closedungeon</c> chat command. Ends the instance the caller's party currently holds.
		/// </summary>
		/// <remarks>
		/// A party may hold one instance at a time, so a run they are finished with blocks them
		/// from starting anything else until it empties and ages out — several minutes of being
		/// told they already have a dungeon open, about a dungeon they have already left. This is
		/// how they reclaim it deliberately.
		/// <para>
		/// Restricted to the party leader, because it removes everybody: without that, one member
		/// could end a run for the whole group. A character with no party is its own leader.
		/// </para>
		/// <para>
		/// Two cases, and both matter. Inside the instance, the scene server hosting it is this one,
		/// so it can evict and unload directly. Outside it, the instance may be hosted anywhere and
		/// this server cannot touch its scene — but it can retire the row, which is what frees the
		/// party, and the hosting server's idle sweep reclaims the scene on its own. That case is
		/// allowed only while the instance is empty; closing one out from under people who are
		/// still in it, from a server that cannot even tell them why, is not something a chat
		/// command should be able to do.
		/// </para>
		/// </remarks>
		/// <param name="character">Character issuing the command.</param>
		/// <param name="msg">The chat message that carried the command.</param>
		/// <returns>Always <c>true</c>: the command is consumed either way, never echoed to chat.</returns>
		private bool OnCloseDungeonCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			NetworkConnection conn = character.Owner;

			long partyID = 0;
			if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
			{
				partyID = partyController.ID;

				/* Rank is read from the controller the party system keeps in step. A stale value
				 * here at worst lets somebody who was leader a moment ago close a run they were
				 * entitled to close. */
				if (partyController.Rank != PartyRank.Leader)
				{
					SendSystemMessage(conn, "Only the party leader can close the dungeon.");
					return true;
				}
			}

			if (character.IsInInstance())
			{
				/* Gated exactly like every other voluntary way out of an instance.
				 *
				 * Closing from inside evicts everybody, and the eviction deliberately skips state
				 * validation because a lifetime cap expiring leaves nothing to validate against.
				 * Reached from a chat command instead, that is a combat-escape: a leader losing a
				 * fight could end the instance and be returned to the open world instantly, which
				 * is a cleaner escape than any teleporter. The cap remains ungated — it is not
				 * player-triggered, and it announces itself first. */
				if (!CharacterStateValidation.CanActOrMove(character))
				{
					SendSystemMessage(conn, "You cannot close the dungeon right now.");
					return true;
				}

				long instanceSceneID = character.InstanceSceneHandle;

				/* Nobody else may be pulled out of a fight either. A leader standing clear while a
				 * member is being attacked could otherwise extract them on demand — the same escape,
				 * one step removed. */
				if (IsAnyoneInCombatInInstance(instanceSceneID))
				{
					SendSystemMessage(conn, "The dungeon cannot be closed while anyone inside is in combat.");
					return true;
				}

				if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
				{
					SendSystemMessage(conn, "The dungeon could not be closed. Please try again.");
					return true;
				}

				Log.Debug("InteractableSystem", $"{character.CharacterName} closed instance {instanceSceneID} from inside.");

				// Sent before the close, which disconnects this player along with everyone else.
				// Disconnect(false) flushes the tick, so the line still reaches them.
				SendSystemMessage(conn, "Closing the dungeon...");
				sceneServerSystem.CloseInstance(instanceSceneID, "closed by the party leader");
				return true;
			}

			long characterID = character.ID;
			long worldServerID = character.WorldServerID;

			if (!TryEnqueueAsyncWork(() => CloseHeldInstanceAsync(conn, characterID, worldServerID, partyID), characterID))
			{
				SendSystemMessage(conn, "The dungeon could not be closed right now. Please try again.");
			}
			return true;
		}

		/// <summary>
		/// Whether anyone standing in an instance is currently in combat.
		/// </summary>
		/// <remarks>
		/// Only meaningful on the scene server hosting the instance, which is where the sole caller
		/// runs — a character can only close from inside an instance this process hosts.
		/// </remarks>
		/// <param name="instanceSceneID">Scene row of the instance to inspect.</param>
		private bool IsAnyoneInCombatInInstance(long instanceSceneID)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				// Cannot tell. Refusing is the safe direction for a combat gate.
				return true;
			}

			foreach (var kvp in charMapping.ConnectionCharacters)
			{
				IPlayerCharacter resident = kvp.Value;
				if (resident != null &&
					resident.IsInInstance() &&
					resident.InstanceSceneHandle == instanceSceneID &&
					resident.IsFlagged(CharacterFlags.IsInCombat))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Retires the party's instance from outside it, when nobody is in it.
		/// </summary>
		/// <remarks>
		/// Marks the row Failed rather than deleting it, for the same reasons as
		/// <see cref="ReleaseCreatedInstance"/>: Failed is not a state the one-instance guard blocks
		/// on, so the party is free immediately, and a scene server that is mid-load finds a row
		/// that is no longer Loading and declines to bring it into service.
		/// <para>
		/// <c>CharacterCount</c> is refreshed by the hosting scene server's pulse, so it lags by up
		/// to one pulse interval. A member who zoned in within that window could have their
		/// instance retired underneath them — they are not evicted, but their next reconnect routes
		/// them to the open world. The alternative, asking the hosting scene server synchronously,
		/// would put a cross-server round trip inside a chat command; the same soft-count trade the
		/// entry path already makes.
		/// </para>
		/// </remarks>
		private async Task CloseHeldInstanceAsync(NetworkConnection conn, long characterID, long worldServerID, long partyID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				var memberIDs = new List<long>(8) { characterID };
				if (partyID > 0)
				{
					List<long> members = await FetchPartyMemberIDsAsync(partyID);
					if (members == null)
					{
						TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
						return;
					}
					for (int i = 0; i < members.Count; ++i)
					{
						if (members[i] != characterID)
						{
							memberIDs.Add(members[i]);
						}
					}
				}

				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					memberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID);

				if (!heldResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				SceneData held = default;
				bool found = false;
				foreach (SceneData candidate in heldResult.Data)
				{
					if (IsUsableInstance(candidate, worldServerID))
					{
						held = candidate;
						found = true;
						break;
					}
				}

				if (!found)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "You do not have a dungeon open."));
					return;
				}

				if (held.CharacterCount > 0)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn,
						$"Someone is still inside {held.SceneName}. They must leave before it can be closed."));
					return;
				}

				DatabaseResult result = await sceneService.UpdateStatusAsync(
					held.ID, FishMMO.Database.Data.Enums.SceneStatus.Failed);

				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Failed to close instance {held.ID} for character {characterID}: {result.ErrorCode} - {result.ErrorMessage}");
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				await Log.Debug("InteractableSystem", $"Character {characterID} closed held instance {held.ID} ({held.SceneName}) from outside.");
				TryEnqueueMainThread(() => SendSystemMessage(conn, $"{held.SceneName} has been closed."));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error closing a held instance for character {characterID}: {ex}");
				TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
			}
		}

		/// <summary>
		/// Sends one system-channel line to a connection, if it is still there. Main thread only.
		/// </summary>
		private void SendSystemMessage(NetworkConnection conn, string text)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Marks an instance this request created, but nobody entered, as failed.
		/// </summary>
		/// <remarks>
		/// A party may hold one instance at a time, so a row created for an entry that then fell
		/// through does not merely sit there: it is the party's instance as far as the one-instance
		/// guard is concerned, and it locks every member out of every dungeon until the world
		/// server's stale-row sweep removes it — up to several minutes later, for a dungeon nobody
		/// ever set foot in. The ordinary way to reach that is entirely benign: the player is
		/// pulled into combat while the database work runs.
		/// <para>
		/// Failed rather than deleted. Failed is not one of the states the guard blocks on, so the
		/// party is free immediately, and the row survives as a record until the sweep reaps it. If
		/// a scene server has already dequeued it, that load's <c>SetReadyAsync</c> finds a row that
		/// is no longer Loading and declines, and the scene it produced is unloaded by the idle
		/// sweep like any other empty instance.
		/// </para>
		/// <para>
		/// Only ever called with a row this request created. Releasing an instance the caller merely
		/// joined would close a dungeon other people are in.
		/// </para>
		/// </remarks>
		/// <param name="createdInstanceID">Row to release, or 0 when this request created none.</param>
		/// <param name="reason">Why it is being released, for diagnostics.</param>
		private void ReleaseCreatedInstance(long createdInstanceID, string reason)
		{
			if (createdInstanceID <= 0)
			{
				return;
			}

			Log.Debug("InteractableSystem", $"Releasing unused instance {createdInstanceID}: {reason}.");

			if (!TryEnqueueAsyncWork(() => FailInstanceAsync(createdInstanceID), createdInstanceID))
			{
				Log.Warning("InteractableSystem",
					$"Could not enqueue the release of unused instance {createdInstanceID}; the party is blocked from opening another until the stale-row sweep removes it.");
			}
		}

		/// <summary>
		/// Performs the release. See <see cref="ReleaseCreatedInstance"/>.
		/// </summary>
		private async Task FailInstanceAsync(long instanceID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				DatabaseResult result = await sceneService.UpdateStatusAsync(
					instanceID, FishMMO.Database.Data.Enums.SceneStatus.Failed);

				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Failed to release unused instance {instanceID}: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error releasing unused instance {instanceID}: {ex}");
			}
		}

		/// <summary>
		/// Whether a scene row can still be entered as a dungeon instance.
		/// </summary>
		/// <remarks>
		/// Pending and Loading count as usable: the instance has been requested and the world
		/// server holds the client in its instance queue until the scene becomes ready, which is
		/// the designed wait. Failed — and any state the enum does not cover — does not, because
		/// nothing will ever move that row forward.
		/// </remarks>
		/// <param name="sceneData">Candidate scene row.</param>
		/// <param name="worldServerID">World server the requesting character belongs to.</param>
		private static bool IsUsableInstance(SceneData sceneData, long worldServerID)
		{
			if (sceneData.ID <= 0 || sceneData.WorldServerID != worldServerID)
			{
				return false;
			}

			/* Deliberately says nothing about which dungeon this is. Callers compare the name
			 * themselves, because holding an instance of a DIFFERENT dungeon is not "no instance" —
			 * it is the one-instance-per-party rule refusing the request, and folding the name in
			 * here is what previously made that case indistinguishable from having none. */
			SceneStatus status = (SceneStatus)sceneData.SceneStatus;
			return status == SceneStatus.Ready ||
				   status == SceneStatus.Pending ||
				   status == SceneStatus.Loading;
		}

		/// <summary>
		/// Whether an instance still has room for one more character.
		/// </summary>
		/// <remarks>
		/// Deliberately separate from <see cref="IsUsableInstance"/>: "not joinable because it is
		/// full" and "not there" lead to opposite actions — the first must refuse, the second
		/// must create.
		/// <para>
		/// <c>CharacterCount</c> is refreshed by the hosting scene server's pulse, so it lags by
		/// up to one pulse interval and simultaneous entries can overshoot slightly. That is the
		/// same soft-cap behaviour the open-world routing path has always had, and the alternative
		/// — asking the hosting scene server synchronously — would put a cross-server round trip
		/// in front of every dungeon entry.
		/// </para>
		/// <para>
		/// A Pending or Loading instance has no occupants yet, so it always has room.
		/// </para>
		/// </remarks>
		/// <param name="sceneData">Candidate scene row.</param>
		/// <param name="maxClients">Maximum characters allowed in one instance of this dungeon.</param>
		private static bool HasInstanceCapacity(SceneData sceneData, int maxClients)
		{
			if ((SceneStatus)sceneData.SceneStatus != SceneStatus.Ready)
			{
				return true;
			}

			return sceneData.CharacterCount < maxClients;
		}

		/// <summary>
		/// Reads a party's roster.
		/// </summary>
		/// <remarks>
		/// The finder needs the membership for two things at once: to ask, in one query, whether
		/// anyone in the party already holds an instance, and to block a racing insert against the
		/// same set of characters. It used to walk the roster itself, issuing a scene lookup per
		/// member — so a six-player party cost seven round trips to answer a question one query
		/// answers, and it could only ever ask about the dungeon being requested.
		/// </remarks>
		/// <param name="partyID">Party to read.</param>
		/// <returns>
		/// The member character IDs, or <c>null</c> when the roster could not be read. Null is
		/// meaningful: the caller must not then treat an empty roster as "this party holds
		/// nothing", because that is exactly when creating a second instance would split it.
		/// </returns>
		private async Task<List<long>> FetchPartyMemberIDsAsync(long partyID)
		{
			if (partyID <= 0 ||
				Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
			{
				return null;
			}

			var membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null)
			{
				await Log.Warning("InteractableSystem", $"Could not read the roster of party {partyID} for a dungeon request.");
				return null;
			}

			var memberIDs = new List<long>(membersResult.Data.Count);
			foreach (var member in membersResult.Data)
			{
				memberIDs.Add(member.CharacterID);
			}
			return memberIDs;
		}

		/// <summary>
		/// Tells a client its dungeon entry was declined, and why.
		/// </summary>
		/// <remarks>
		/// Every rejection on this path used to be a bare <c>return</c>. The player clicked the
		/// entrance and nothing happened, with no way to tell a refusal from a dropped request —
		/// so the natural response was to click again, which the ingress guard then swallowed
		/// too. Reliable channel: this is a one-shot transition that unblocks the client's UI.
		/// </remarks>
		/// <param name="conn">Connection to notify. Main thread only.</param>
		/// <param name="reason">Why the request was refused.</param>
		private void SendTransferRefused(NetworkConnection conn, SceneTransferRefusalReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn,
				new SceneTransferRefusedBroadcast { Reason = reason },
				true,
				FishNet.Transporting.Channel.Reliable);
		}
	}
}
