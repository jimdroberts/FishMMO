using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Instance management: reporting who is in an instance and how long it has left, letting the
	/// party leader remove somebody from it, and letting them hide the run from the dungeon finder.
	/// </summary>
	/// <remarks>
	/// Lives on the character system rather than beside the dungeon finder because every operation
	/// here is about the characters standing in a scene, not about creating or resolving one — the
	/// membership walk, the removal, and the leave path a removal reuses are all already here.
	/// <para>
	/// <b>Leadership is the owning party's leader.</b> An instance belongs to a party, not to
	/// whoever happened to open it: the scene row records both, and the party is the one that
	/// survives its creator leaving, logging out, or handing leadership on. Reading leadership
	/// from the party also means it moves the moment the party's does — a promotion is reflected
	/// in this panel on its next refresh, with nothing here needing to know a promotion happened.
	/// </para>
	/// <para>
	/// An instance opened by an ungrouped character has no party, and there its owner <em>is</em>
	/// its leader. That is the only case where the two differ, and it is also the case where they
	/// cannot disagree: a run of one.
	/// </para>
	/// </remarks>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Ingress guard operation codes and debounce rates for the instance panel's requests.
		/// </summary>
		/// <remarks>
		/// The panel refreshes on a timer as well as on open, and the answer costs a walk of every
		/// character on this scene server. Short enough that a player pressing refresh feels it,
		/// long enough that a modified client cannot make the walk a denial of service.
		/// </remarks>
		private const byte InstanceDetailsOperation = 4;
		private const byte InstanceKickOperation = 5;
		private const byte InstancePrivacyOperation = 6;
		private const int InstanceDetailsDebounceMs = 1000;
		private const int InstanceKickDebounceMs = 500;
		private const int InstancePrivacyDebounceMs = 2000;

		/// <summary>
		/// Who controls one instance, resolved for one viewer. Main thread only.
		/// </summary>
		/// <remarks>
		/// Both the readout and the two authorised actions need the same answer, and it is not a
		/// trivial one — it depends on the instance's party, on the viewer's rank in that party,
		/// and on which members happen to be standing inside. Deriving it in one place is what
		/// stops the panel drawing a control the request behind it would refuse.
		/// </remarks>
		private struct InstanceAuthority
		{
			/// <summary>Character ID of the leader, or 0 when nobody can be named.</summary>
			public long LeaderCharacterID;

			/// <summary>Leader's name, or null when they are not on this scene server.</summary>
			public string LeaderName;

			/// <summary>Whether the viewer may remove others and change the listing.</summary>
			public bool ViewerIsLeader;
		}

		/// <summary>
		/// Works out who leads an instance, from the point of view of one of its occupants.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The viewer's own authority is decided by their own rank</b>, read from the party
		/// controller the party system keeps in step with the database. It never depends on
		/// finding the leader in the roster walk, because a leader who is standing outside the
		/// instance — they opened it and stepped out, or have not arrived yet — must not silently
		/// lose control of it, and a member must not silently gain control because the leader is
		/// momentarily unresolvable.
		/// </para>
		/// <para>
		/// <b>Naming the leader is a separate, best-effort question.</b> Character names live on
		/// the character objects this scene server holds, so a leader elsewhere cannot be named
		/// from here. The panel is told there is a leader it cannot name rather than being told
		/// there is none — the difference matters, because "no leader" is a state this system
		/// actively repairs and would be alarming to display for a run that has one.
		/// </para>
		/// </remarks>
		/// <param name="viewer">The character asking.</param>
		/// <param name="details">The instance they are standing in.</param>
		/// <param name="data">Character mapping for this scene server.</param>
		/// <returns>Who leads, and whether the viewer is them.</returns>
		private InstanceAuthority ResolveInstanceAuthority(IPlayerCharacter viewer, ISceneInstanceDetails details, ICharacterMappingData<NetworkConnection> data)
		{
			InstanceAuthority authority = default;

			if (viewer == null || details == null)
			{
				return authority;
			}

			long owningPartyID = details.PartyID;

			if (owningPartyID <= 0)
			{
				/* No party behind this run, so its owner is its leader. Reached by a solo player
				 * who opened a private dungeon: nobody else can be inside it to disagree. */
				authority.LeaderCharacterID = details.OwnerCharacterID;
				authority.ViewerIsLeader = details.OwnerCharacterID != 0 && viewer.ID == details.OwnerCharacterID;
			}
			else
			{
				authority.ViewerIsLeader =
					viewer.TryGet(out IPartyController viewerParty) &&
					viewerParty.ID == owningPartyID &&
					viewerParty.Rank == PartyRank.Leader;

				if (authority.ViewerIsLeader)
				{
					authority.LeaderCharacterID = viewer.ID;
				}
				else if (data != null)
				{
					// Somebody else leads. Findable only if they are on this scene server.
					foreach (var kvp in data.ConnectionCharacters)
					{
						IPlayerCharacter candidate = kvp.Value;
						if (candidate != null &&
							candidate.TryGet(out IPartyController candidateParty) &&
							candidateParty.ID == owningPartyID &&
							candidateParty.Rank == PartyRank.Leader)
						{
							authority.LeaderCharacterID = candidate.ID;
							break;
						}
					}
				}
			}

			if (authority.LeaderCharacterID != 0 &&
				data != null &&
				data.CharactersByID.TryGetValue(authority.LeaderCharacterID, out IPlayerCharacter leader))
			{
				authority.LeaderName = leader.CharacterName;
			}

			return authority;
		}

		/// <summary>
		/// Answers a client asking about the instance it is standing in.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="msg">Request payload (empty).</param>
		/// <param name="channel">Transport channel.</param>
		private void OnClientRequestInstanceDetailsBroadcastReceived(NetworkConnection conn, RequestInstanceDetailsBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			if (!TryBeginRespawnResurrectGuard(conn.ClientId, InstanceDetailsOperation, out long guardKey))
			{
				return;
			}

			try
			{
				SendInstanceDetails(conn);
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// Builds and sends the instance readout for one connection. Main thread only.
		/// </summary>
		/// <remarks>
		/// Every request is answered, including from a character that is not in an instance — the
		/// panel opens on the player's click and only fills in when a reply arrives, so silence
		/// leaves a blank window over the game with nothing to explain it. That is the same
		/// failure the scene-transfer refusals exist to eliminate.
		/// </remarks>
		/// <param name="conn">Connection to answer.</param>
		private void SendInstanceDetails(NetworkConnection conn)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter viewer) ||
				!viewer.IsInInstance())
			{
				Server.NetworkWrapper.Broadcast(conn,
					new InstanceDetailsBroadcast { InInstance = false, Members = Array.Empty<InstanceMemberData>() },
					true, FishNet.Transporting.Channel.Reliable);
				return;
			}

			long instanceSceneID = viewer.InstanceSceneHandle;
			int remainingSeconds = 0;
			InstanceAuthority authority = default;
			string difficultyName = null;
			bool isPrivate = false;

			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				if (sceneServerSystem.TryGetSceneInstanceDetails(viewer.WorldServerID, viewer.InstanceSceneName, instanceSceneID, out ISceneInstanceDetails details))
				{
					authority = ResolveInstanceAuthority(viewer, details, data);
					isPrivate = details.IsPrivate;
					difficultyName = ResolveDifficultyName(details.Name, details.Difficulty);
				}

				/* Clamped at zero rather than sent negative. An instance past its expiry is closed
				 * by the next pulse, and a countdown that has gone negative on screen in the
				 * meantime reads as a bug rather than as "any moment now". */
				if (sceneServerSystem.TryGetInstanceExpiry(instanceSceneID, out DateTime expiresUtc))
				{
					double remaining = (expiresUtc - DateTime.UtcNow).TotalSeconds;
					remainingSeconds = remaining > 0.0 ? (int)Math.Ceiling(remaining) : 0;
				}
			}

			var members = new List<InstanceMemberData>(8);

			foreach (var kvp in data.ConnectionCharacters)
			{
				IPlayerCharacter resident = kvp.Value;
				if (resident == null ||
					!resident.IsInInstance() ||
					resident.InstanceSceneHandle != instanceSceneID)
				{
					continue;
				}

				members.Add(new InstanceMemberData
				{
					CharacterID = resident.ID,
					Name = resident.CharacterName,
					IsLeader = authority.LeaderCharacterID != 0 && resident.ID == authority.LeaderCharacterID,
					// Resolved here rather than by the client comparing ids: the same judgement
					// decides whether the kick controls are drawn.
					IsSelf = resident.ID == viewer.ID,
				});
			}

			/* Ordered by character ID so the roster does not reshuffle between refreshes. The walk
			 * above is over a dictionary, whose order is not defined and changes as characters
			 * come and go — a list that reorders under the player's cursor is how a kick lands on
			 * the wrong person. */
			members.Sort((a, b) => a.CharacterID.CompareTo(b.CharacterID));

			Server.NetworkWrapper.Broadcast(conn, new InstanceDetailsBroadcast
			{
				InInstance = true,
				SceneName = viewer.InstanceSceneName,
				RemainingSeconds = remainingSeconds,
				LeaderCharacterID = authority.LeaderCharacterID,
				/* Null when the leader is not on this scene server — they lead from outside the
				 * instance, or from another one. There is still a leader, so the client is told
				 * there is one it cannot name rather than being told there is none. */
				LeaderName = authority.LeaderName,
				ViewerIsLeader = authority.ViewerIsLeader,
				Members = members.ToArray(),
				DifficultyName = difficultyName,
				IsPrivate = isPrivate,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Names the difficulty an instance is being run at, for display.
		/// </summary>
		/// <remarks>
		/// Resolved from the dungeon's own template by scene name, because difficulty indices mean
		/// nothing on their own — every dungeon declares its own list. Null when the dungeon has
		/// no template or declares no difficulties, in which case there is only one way to run it
		/// and naming it would be noise.
		/// </remarks>
		private static string ResolveDifficultyName(string sceneName, int difficulty)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return null;
			}

			DungeonTemplate template = DungeonTemplate.GetBySceneName(sceneName);
			if (template == null || template.Difficulties == null || template.Difficulties.Count < 1)
			{
				return null;
			}

			return template.GetDifficultyName(difficulty);
		}

		/// <summary>
		/// Removes another character from the instance, at the party leader's request.
		/// </summary>
		/// <remarks>
		/// Authorisation is re-derived here from the instance's owning party. The client is told
		/// whether to draw the controls, but a drawn control is not an authorisation and the
		/// broadcast can be sent without one.
		/// <para>
		/// The removal is the ordinary leave-instance transfer, not a disconnect: the target is
		/// announced out of the instance so its population is debited, put back at the open-world
		/// position it entered from, saved, released and re-routed. Disconnecting them instead
		/// would drop them to a loading screen and, worse, leave the instance flag set — so the
		/// world server would route them straight back into the instance they were just removed
		/// from.
		/// </para>
		/// </remarks>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="msg">Which character to remove.</param>
		/// <param name="channel">Transport channel.</param>
		private void OnClientInstanceKickBroadcastReceived(NetworkConnection conn, InstanceKickBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			if (!TryBeginRespawnResurrectGuard(conn.ClientId, InstanceKickOperation, out long guardKey))
			{
				return;
			}

			try
			{
				if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
					!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter leader) ||
					!leader.IsInInstance())
				{
					return;
				}

				// Removing yourself is a different request with different rules. Leaving is always
				// permitted; being removed is not, and conflating them would let the leader bypass
				// whatever gating the leave path applies.
				if (msg.CharacterID <= 0 || msg.CharacterID == leader.ID)
				{
					SendSystemMessage(conn, "Use Leave to remove yourself from the dungeon.");
					return;
				}

				long instanceSceneID = leader.InstanceSceneHandle;

				if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) ||
					!sceneServerSystem.TryGetSceneInstanceDetails(leader.WorldServerID, leader.InstanceSceneName, instanceSceneID, out ISceneInstanceDetails details))
				{
					SendSystemMessage(conn, "The dungeon could not be read. Please try again.");
					return;
				}

				InstanceAuthority authority = ResolveInstanceAuthority(leader, details, data);
				if (!authority.ViewerIsLeader)
				{
					SendSystemMessage(conn, "Only the party leader can remove players from the dungeon.");
					return;
				}

				/* Resolved from this server's own map, then checked against the instance. A
				 * character id is a global identity, so without the instance check a leader could
				 * name anyone on the scene server — including somebody in an entirely different
				 * dungeon — and have them thrown out of it. */
				if (!data.CharactersByID.TryGetValue(msg.CharacterID, out IPlayerCharacter target) ||
					!target.IsInInstance() ||
					target.InstanceSceneHandle != instanceSceneID)
				{
					SendSystemMessage(conn, "That player is no longer in the dungeon.");
					return;
				}

				string targetName = target.CharacterName;

				/* enforceState: false — the removal is immediate by design. The leader's decision
				 * is not deferred until the target happens to be out of combat, because a target
				 * who does not want to go could then stay indefinitely simply by staying in a
				 * fight. Note that this is therefore also a way out of a fight for a target who
				 * *does* want to go, with the leader's cooperation; the instance is PvE content
				 * and the pair could achieve the same by walking out together, but it is the one
				 * thing "immediately" costs. */
				if (!TryLeaveInstance(target, enforceState: false))
				{
					SendSystemMessage(conn, $"{targetName} could not be removed. Please try again.");
					return;
				}

				Log.Debug("CharacterSystem", $"{leader.CharacterName} removed {targetName} from instance {instanceSceneID}.");
				SendSystemMessage(conn, $"{targetName} has been removed from the dungeon.");

				// The roster the leader is looking at is now wrong, and they did not ask for it to
				// change out from under them — they asked for exactly this. Send the new one.
				SendInstanceDetails(conn);
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// Shows or hides the instance in the dungeon finder's public list, at the leader's request.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The lock on the front door. A run that is listed can be joined by anybody who meets its
		/// difficulty's requirements — and joining it puts them in the party — so a group that
		/// wants to be left alone needs a way to say so without having to finish and reopen the
		/// dungeon. Hiding it changes nothing for the people already inside and nothing about
		/// re-entry for the owning party; it only stops the run being offered.
		/// </para>
		/// <para>
		/// Deliberately not gated on character state. It is not a move, it takes nothing away from
		/// anybody, and a group being overrun mid-fight is exactly when they most want to close
		/// the door — refusing because they are in combat would deny the request at its most
		/// useful moment.
		/// </para>
		/// </remarks>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="msg">The visibility being asked for.</param>
		/// <param name="channel">Transport channel.</param>
		private void OnClientInstancePrivacyBroadcastReceived(NetworkConnection conn, InstancePrivacyBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			if (!TryBeginRespawnResurrectGuard(conn.ClientId, InstancePrivacyOperation, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
					!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter leader) ||
					!leader.IsInInstance())
				{
					return;
				}

				if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) ||
					!sceneServerSystem.TryGetSceneInstanceDetails(leader.WorldServerID, leader.InstanceSceneName, leader.InstanceSceneHandle, out ISceneInstanceDetails details))
				{
					SendSystemMessage(conn, "The dungeon could not be read. Please try again.");
					return;
				}

				InstanceAuthority authority = ResolveInstanceAuthority(leader, details, data);
				if (!authority.ViewerIsLeader)
				{
					SendSystemMessage(conn, "Only the party leader can change who may join the dungeon.");
					return;
				}

				if (details.IsPrivate == msg.IsPrivate)
				{
					// Already in the requested state. Answered rather than ignored, so a client
					// whose view had drifted is corrected instead of being left waiting.
					SendInstanceDetails(conn);
					return;
				}

				long instanceSceneID = details.SceneID;
				long owningPartyID = details.PartyID;
				long ownerCharacterID = details.OwnerCharacterID;
				bool requested = msg.IsPrivate;

				if (TryEnqueueAsyncWork(() => SetInstancePrivacyAsync(conn, instanceSceneID, owningPartyID, ownerCharacterID, requested, guardKey), conn, leader.ID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendSystemMessage(conn, "The dungeon could not be updated. Please try again.");
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndRespawnResurrectGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Writes the new visibility, then updates this server's copy and tells the leader.
		/// </summary>
		/// <remarks>
		/// The row is the authority and is written first. The in-memory copy is what the panel and
		/// the join path read, so it is updated only after the write succeeds — a local flag that
		/// disagreed with the database would make the panel claim a run was private while the
		/// finder went on offering it.
		/// </remarks>
		private async Task SetInstancePrivacyAsync(NetworkConnection conn, long sceneID, long owningPartyID, long ownerCharacterID, bool isPrivate, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be updated. Please try again."));
					return;
				}

				DatabaseResult<bool> result = await sceneService.SetInstancePrivacyAsync(sceneID, owningPartyID, ownerCharacterID, isPrivate);

				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem",
						$"Could not change the visibility of instance {sceneID}: {result.ErrorCode} - {result.ErrorMessage}");
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be updated. Please try again."));
					return;
				}

				if (!result.Data)
				{
					/* No row matched, so the ownership test inside the UPDATE failed. The
					 * authorisation this server made was against a roster that has since moved —
					 * the party changed, or the instance did. Reported rather than retried. */
					TryEnqueueMainThread(() => SendSystemMessage(conn, "You are no longer in control of this dungeon."));
					return;
				}

				TryEnqueueMainThread(() =>
				{
					if (Server != null &&
						Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
						conn != null && conn.IsActive &&
						Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
						data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter leader) &&
						leader.IsInInstance() &&
						sceneServerSystem.TryGetSceneInstanceDetails(leader.WorldServerID, leader.InstanceSceneName, leader.InstanceSceneHandle, out ISceneInstanceDetails details) &&
						details.SceneID == sceneID)
					{
						details.IsPrivate = isPrivate;
					}

					SendSystemMessage(conn, isPrivate
						? "The dungeon is now hidden from the dungeon finder."
						: "The dungeon is now listed in the dungeon finder.");
					SendInstanceDetails(conn);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"Error changing the visibility of instance {sceneID}: {ex}");
				TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be updated. Please try again."));
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Difficulty rules that apply to the people inside
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Deaths suffered inside the current instance, per character.
		/// </summary>
		/// <remarks>
		/// Kept here rather than on the character because it is a property of a <em>run</em>, not
		/// of a character: leaving the instance and coming back starts over, which is what makes a
		/// one-death rule a rule about this attempt rather than a permanent mark. Cleared on the
		/// way out for exactly that reason, and cleared on disconnect so the map cannot grow by one
		/// entry per character the server has ever hosted.
		/// </remarks>
		private readonly Dictionary<long, int> instanceDeathCounts = new Dictionary<long, int>();

		/// <summary>
		/// The difficulty rules in force where a character is standing, if any.
		/// </summary>
		/// <remarks>
		/// Read from the registry the scene server publishes as it loads each instance, so it
		/// answers for the scene the character is actually in rather than for whatever the client
		/// believes. Null in the open world and in dungeons that declare no difficulties.
		/// </remarks>
		private static DungeonDifficultyDefinition ResolveDifficultyFor(IPlayerCharacter character)
		{
			if (character == null || character.GameObject == null)
			{
				return null;
			}

			return DungeonDifficultyRegistry.TryGet(character.GameObject.scene.handle, out DungeonDifficultyDefinition difficulty)
				? difficulty
				: null;
		}

		/// <summary>
		/// Whether a character may be resurrected where it is standing.
		/// </summary>
		/// <remarks>
		/// A difficulty that bans resurrection is banning it inside its own dungeon and nowhere
		/// else, so this is decided by the scene rather than by anything carried on the character.
		/// Checked at the offer <em>and</em> at the accept: the offer check is what stops a
		/// pointless prompt appearing on a dead player's screen, and the accept check is the one
		/// that actually enforces the rule, because an offer recorded a moment before the
		/// character walked into the instance would otherwise still be redeemable inside it.
		/// </remarks>
		private static bool IsResurrectionAllowed(IPlayerCharacter character)
		{
			DungeonDifficultyDefinition difficulty = ResolveDifficultyFor(character);
			return difficulty == null || difficulty.AllowResurrection;
		}

		/// <summary>
		/// Counts a death against a character's allowance and removes them if it was their last.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Removes only the character who died. The instance and everybody else in it carry on:
		/// ending a group's run over one member's mistake is a much harsher rule than any dungeon
		/// here is trying to express, and it would also make a hardcore run a thing any one member
		/// could end for everybody.
		/// </para>
		/// <para>
		/// The removal is the ordinary leave-instance transfer, not a disconnect — the same path a
		/// kick uses — so the character keeps its death: it arrives back in the open world still
		/// dead, and chooses a bind point like anyone else. What it does not keep is the run.
		/// </para>
		/// <para>
		/// <c>enforceState: false</c>, because the character is dead and there is no state left to
		/// validate against. This is not player-triggered, so it is not the combat-escape the
		/// voluntary paths have to guard against.
		/// </para>
		/// </remarks>
		/// <param name="player">The character that just died.</param>
		private void ApplyInstanceDeathRules(IPlayerCharacter player)
		{
			if (player == null || !player.IsInInstance())
			{
				return;
			}

			DungeonDifficultyDefinition difficulty = ResolveDifficultyFor(player);
			if (difficulty == null || difficulty.LivesPerCharacter < 1)
			{
				// Unlimited lives, which is every dungeon that does not say otherwise.
				return;
			}

			instanceDeathCounts.TryGetValue(player.ID, out int deaths);
			deaths += 1;
			instanceDeathCounts[player.ID] = deaths;

			int remaining = difficulty.LivesPerCharacter - deaths;
			NetworkConnection conn = player.Owner;

			if (remaining > 0)
			{
				SendSystemMessage(conn, remaining == 1
					? "One life left. Your next death ends your run."
					: $"{remaining} lives left in this dungeon.");
				return;
			}

			SendSystemMessage(conn, "Your run is over. You have been returned to the world.");

			if (!TryLeaveInstance(player, enforceState: false))
			{
				/* Could not move them, so the count stands and the next death tries again. Left
				 * in place rather than cleared: clearing would silently restore a life the
				 * difficulty said they did not have. */
				Log.Warning("CharacterSystem",
					$"{player.CharacterName} used their last life but could not be removed from the instance.");
				return;
			}

			ClearInstanceDeathCount(player.ID);
		}

		/// <summary>
		/// Forgets a character's death count for the run they have just left.
		/// </summary>
		/// <remarks>
		/// Called from every exit from an instance, and from disconnect. A count that outlived its
		/// run would follow the character into their next attempt at the same dungeon and end it
		/// early — and, left uncalled entirely, the map would grow by one entry per character the
		/// process ever hosted.
		/// </remarks>
		private void ClearInstanceDeathCount(long characterID)
		{
			instanceDeathCounts.Remove(characterID);
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
	}
}
