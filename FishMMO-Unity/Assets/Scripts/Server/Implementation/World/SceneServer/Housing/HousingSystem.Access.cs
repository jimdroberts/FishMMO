using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Who an owner has let into their plot, and putting out anybody who should not be in it.
	/// </summary>
	/// <remarks>
	/// Houses are locked by default. Ownership admits its owner; everybody else needs a grant, and
	/// a grant is per plot rather than per player, so buying new land does not come with the guest
	/// list of the old.
	///
	/// <para>Grants are only half of it. An access rule enforced at the doorway is a rule anybody
	/// already inside can ignore, and the two moments that matter most — a friend being revoked, and
	/// an owner starting work — both happen to people who are standing in the house at the time. The
	/// sweep in this file is what makes "may I be here" the same question as "may I come in".</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>
		/// Seconds between checks for players standing where they should not be.
		/// </summary>
		/// <remarks>
		/// A poll rather than a trigger volume, and this is the dial on it.
		///
		/// <para>Triggers would need a collider authored on every foundation, sized to the plot, and
		/// kept in step with the dimensions a designer types into the inspector — three things to get
		/// wrong, one of which fails silently by admitting everybody. The sweep needs nothing
		/// authored and cannot be defeated by a client that declines to report a collision.</para>
		///
		/// <para>Half a second is chosen against what it is for. Eviction is not a race the player
		/// can win: there is nothing inside a locked house to grab, so a fraction of a second on the
		/// wrong side of a wall costs nothing. What it must not do is take long enough that being
		/// evicted feels arbitrary rather than caused.</para>
		/// </remarks>
		[Header("Access")]
		[Tooltip("Seconds between checks for players standing inside a plot they may not be in.")]
		[SerializeField]
		private float accessSweepIntervalSeconds = 0.5f;

		/// <summary>
		/// Seconds until the next access sweep.
		/// </summary>
		private float accessSweepCountdown;

		/// <summary>
		/// Reads the access lists for a set of plots, keyed by plot.
		/// </summary>
		/// <remarks>
		/// One query for the whole scene rather than one per plot. A housing district is dozens of
		/// foundations and most of them have no grants at all; asking per plot would be dozens of
		/// round trips to learn that almost nothing is shared.
		/// </remarks>
		/// <returns>
		/// The grants keyed by plot, or <c>null</c> when they could not be read. The difference is the
		/// one <see cref="PlotFoundation.ApplyResolvedState"/> draws: an empty list means nobody has
		/// been let in, and a failed read handed back as one used to lock every guest out of every
		/// house it touched — and, through the sync, put them out of the ones they were standing in.
		/// </returns>
		private async Task<Dictionary<long, Dictionary<long, PlotPermission>>> FetchAccessGrantsAsync(List<PlotData> plots)
		{
			Dictionary<long, Dictionary<long, PlotPermission>> byPlot = new Dictionary<long, Dictionary<long, PlotPermission>>();

			if (plots == null || plots.Count < 1)
			{
				return byPlot;
			}

			if (!TryGetDbService(out IPlotAccessService accessService))
			{
				Log.Error("HousingSystem", "Could not read plot access grants: IPlotAccessService unavailable.");
				return null;
			}

			List<long> plotIDs = new List<long>(plots.Count);
			foreach (PlotData plot in plots)
			{
				if (plot.ID > 0)
				{
					plotIDs.Add(plot.ID);
				}
			}

			if (plotIDs.Count < 1)
			{
				return byPlot;
			}

			DatabaseResult<List<PlotAccessData>> grants = await accessService.FetchByPlotsAsync(plotIDs);
			if (!grants.IsSuccess || grants.Data == null)
			{
				Log.Error("HousingSystem", $"Could not read plot access grants: [{grants.ErrorCode}] {grants.ErrorMessage}");
				return null;
			}

			foreach (PlotAccessData grant in grants.Data)
			{
				/* Masked on the way in, at the boundary, rather than wherever it is later read. A
				 * bit this build has no name for is dropped here once instead of being carried
				 * around and reinterpreted by whichever reader gets it next. */
				PlotPermission permissions = PlotAccess.Sanitize(grant.Permissions);
				if (permissions == PlotPermission.None)
				{
					continue;
				}

				if (!byPlot.TryGetValue(grant.PlotID, out Dictionary<long, PlotPermission> forPlot))
				{
					forPlot = new Dictionary<long, PlotPermission>();
					byPlot.Add(grant.PlotID, forPlot);
				}
				forPlot[grant.CharacterID] = permissions;
			}

			return byPlot;
		}

		/// <summary>
		/// Grants or narrows one character's access to a plot.
		/// </summary>
		/// <param name="conn">The granter's connection, answered once the database has replied.</param>
		/// <param name="granter">The character handing out the access.</param>
		/// <param name="foundation">The plot in question.</param>
		/// <param name="targetCharacterID">Who is being granted.</param>
		/// <param name="requested">What they are being given.</param>
		/// <remarks>
		/// Clamped to what the granter holds themselves, so a friend with
		/// <see cref="PlotPermission.InviteFriends"/> cannot mint permissions the owner never gave
		/// them. Without that the model collapses to its weakest link: whoever can invite can invite
		/// themselves into everything.
		///
		/// <para>Answered from the write, not from the request. The granter used to be told it had
		/// worked, and sent the guest list, before the grant existed anywhere — so the list they got
		/// back never had the new name on it, and a write that failed had already been reported as
		/// done.</para>
		/// </remarks>
		/// <returns>
		/// False when the granter may not grant this, which the caller answers. True when this has
		/// answered, or will once the database has.
		/// </returns>
		public bool TryGrantAccess(NetworkConnection conn, IPlayerCharacter granter, IPlotFoundation foundation, long targetCharacterID, PlotPermission requested)
		{
			if (granter == null || foundation is not PlotFoundation plot || !IsHousingEnabled)
			{
				return false;
			}
			if (plot.PlotID <= 0 || targetCharacterID <= 0)
			{
				return false;
			}

			/* Granting yourself access is refused rather than being a harmless no-op. The owner
			 * already has everything, so it can only ever be a non-owner writing a row about
			 * themselves — which, if it were ever honoured, would be the whole system defeated. */
			if (targetCharacterID == granter.ID)
			{
				return false;
			}

			/* Nobody may be let into land that is not held. There is no owner to do the letting: an
			 * empty lot belongs to nobody and an abandoned one belongs to nobody yet. */
			if (!plot.State.IsHeld())
			{
				return false;
			}

			PlotPermission granterHolds = plot.PermissionsFor(granter.ID, GuildIDOf(granter));
			PlotPermission granted = PlotAccess.ClampGrant(granterHolds, requested);
			if (granted == PlotPermission.None)
			{
				return false;
			}

			long plotID = plot.PlotID;
			long granterID = granter.ID;
			int mask = (int)granted;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotAccessService accessService))
				{
					Log.Error("HousingSystem", $"Could not grant access to plot {plotID} for CharID={targetCharacterID}: IPlotAccessService unavailable.");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				DatabaseResult<int> result = await accessService.GrantAsync(plotID, targetCharacterID, mask, granterID);
				if (!result.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not grant access to plot {plotID} for CharID={targetCharacterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				/* Applied locally as well as recorded, so the granter sees it take effect now rather
				 * than on the next cross-channel poll. The other channels learn about it the same
				 * way they learn about everything else. */
				if (!TryEnqueueHousingMainThread(() =>
				{
					ApplyGrantEverywhere(plotID, targetCharacterID, granted);
					SendHousingResult(conn, plotID, HousingResult.Success);
					SendAccessList(conn, plot);
				}))
				{
					Log.Warning("HousingSystem", $"Could not apply the access grant for plot {plotID} locally; the plot sync applies it.");
				}
				MarkPlotChanged(plotID);
			}, granterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the access grant for plot {plotID}.");
				SendHousingResult(conn, plotID, HousingResult.Failed);
			}

			return true;
		}

		/// <summary>
		/// Takes away one character's access to a plot, and puts them out if they are standing in it.
		/// </summary>
		/// <remarks>
		/// The eviction is the point. A revocation that only closed the door would leave the revoked
		/// friend exactly where they were, free to stay as long as they did not walk out — and free
		/// to log out there and come back to it later.
		/// </remarks>
		/// <returns>
		/// False when the revoker may not revoke, which the caller answers. True when this has
		/// answered, or will once the database has.
		/// </returns>
		public bool TryRevokeAccess(NetworkConnection conn, IPlayerCharacter revoker, IPlotFoundation foundation, long targetCharacterID)
		{
			if (revoker == null || foundation is not PlotFoundation plot || !IsHousingEnabled)
			{
				return false;
			}
			if (plot.PlotID <= 0 || targetCharacterID <= 0)
			{
				return false;
			}

			/* Only somebody who could have granted it may take it away. Note this admits a friend
			 * with InviteFriends revoking another friend, which is the same authority the grant path
			 * gives them — an owner who does not want that should not hand out the permission. */
			PlotPermission revokerHolds = plot.PermissionsFor(revoker.ID, GuildIDOf(revoker));
			if (!revokerHolds.HasFlag(PlotPermission.InviteFriends))
			{
				return false;
			}

			long plotID = plot.PlotID;
			PlotPermission previous = plot.GrantFor(targetCharacterID);

			/* Locally first, and before the write is confirmed. The player being revoked is standing
			 * in the house now, and the round trip is the window in which they are inside a plot the
			 * owner has already decided they may not be in — so they go now, not when the database
			 * answers. */
			ApplyGrantEverywhere(plotID, targetCharacterID, PlotPermission.None);
			EvictTrespassers(plot);

			/* EnqueuePersistence: the revocation is already applied in memory, and the next plot
			 * resolve re-reads the grants from the database. A refused enqueue used to hand the
			 * revoked player their key back on that resolve.
			 *
			 * A write that runs and fails is answered, not left for a resolve to undo. The database
			 * still holds the grant, and so does every other channel, since nothing marked the plot
			 * changed; the owner used to be told it was done all the same. So the key goes back here
			 * too — this server agreeing with the database and the other channels — and the owner is
			 * told it failed, so they can try again rather than believe it was done. */
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out IPlotAccessService accessService))
				{
					Log.Error("HousingSystem", $"Could not revoke access to plot {plotID} for CharID={targetCharacterID}: IPlotAccessService unavailable.");
					RestoreRevokedGrantOnMainThread(conn, plot, targetCharacterID, previous);
					return;
				}

				DatabaseResult<int> result = await accessService.RevokeAsync(plotID, targetCharacterID);
				if (!result.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not revoke access to plot {plotID} for CharID={targetCharacterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					RestoreRevokedGrantOnMainThread(conn, plot, targetCharacterID, previous);
					return;
				}

				if (!TryEnqueueHousingMainThread(() =>
				{
					SendHousingResult(conn, plotID, HousingResult.Success);
					SendAccessList(conn, plot);
				}))
				{
					Log.Warning("HousingSystem", $"Could not confirm the revocation on plot {plotID} to the revoker.");
				}

				MarkPlotChanged(plotID);
			}, revoker.ID);

			return true;
		}

		/// <summary>
		/// Puts back a grant whose revocation the database refused, and tells the revoker.
		/// Callable from the worker.
		/// </summary>
		private void RestoreRevokedGrantOnMainThread(NetworkConnection conn, PlotFoundation plot, long targetCharacterID, PlotPermission previous)
		{
			long plotID = plot.PlotID;

			if (!TryEnqueueHousingMainThread(() =>
			{
				if (previous != PlotPermission.None)
				{
					ApplyGrantEverywhere(plotID, targetCharacterID, previous);
				}
				SendHousingResult(conn, plotID, HousingResult.Failed);
				SendAccessList(conn, plot);
			}))
			{
				Log.Warning("HousingSystem", $"Could not report the failed revocation on plot {plotID}; this server keeps the key revoked until the plot next syncs.");
			}
		}

		/// <summary>
		/// Empties the guest list on every copy of a plot here, once the database has. Main thread only.
		/// </summary>
		/// <remarks>
		/// Memory only. The rows are removed inside the transaction that takes or reclaims the land
		/// (see <see cref="TryClaimCleanAsync"/> and <see cref="TryReleaseIntoVaultAsync"/>), not by a
		/// separate write afterwards: a separate write could fail while the change of hands stood, and
		/// a grant is honoured on any occupied plot whoever issued it, so the last owner's guests kept
		/// their keys to the next owner's house.
		/// </remarks>
		private static void ForgetAccessGrants(long plotID)
		{
			if (plotID <= 0)
			{
				return;
			}

			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForPlot(plotID))
			{
				foundation.ApplyAccessGrants(new Dictionary<long, PlotPermission>());
			}
		}

		/// <summary>
		/// Applies one grant to every loaded copy of a plot.
		/// </summary>
		/// <remarks>
		/// Every copy, because channels are several live copies of one scene sharing one row. A
		/// grant applied to the first match would leave the same house open in one channel and shut
		/// in the next, and which one a player saw would depend on which they walked into.
		/// </remarks>
		private static void ApplyGrantEverywhere(long plotID, long characterID, PlotPermission permissions)
		{
			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForPlot(plotID))
			{
				foundation.ApplyAccessGrant(characterID, permissions);
			}
		}

		/// <summary>
		/// A player standing in a scene that has plots, and where they were when the sweep looked.
		/// </summary>
		private struct Occupant
		{
			/// <summary>The character.</summary>
			public IPlayerCharacter Player;

			/// <summary>Their position when read; moved to the exit if they are put out.</summary>
			public Vector3 Position;
		}

		/// <summary>
		/// The occupants of the scene being enforced. Main thread only; empty between uses.
		/// </summary>
		/// <remarks>
		/// Reused rather than allocated per scene per sweep, and cleared after each use so it holds
		/// no character alive between sweeps. Nothing an eviction does can re-enter enforcement, so
		/// one buffer serves both the sweep and the single-plot entry points.
		/// </remarks>
		private readonly List<Occupant> occupantBuffer = new List<Occupant>();

		/// <summary>
		/// Puts out anybody standing inside a plot they may not be in.
		/// </summary>
		/// <remarks>
		/// <para>Walks scenes, and reads each scene's occupants once per sweep: who is there, from
		/// FishNet's own per-scene connection set, and where each of them is, read once and reused
		/// for every plot in the scene. The sweep used to walk every character on the server for
		/// every scene with plots, asking each which scene it was in, and then re-read every
		/// occupant's position once per plot — on a busy server, tens of thousands of engine calls
		/// a second to find, almost always, nobody out of place.</para>
		///
		/// <para>A scene whose plots are all empty lots is skipped before anybody in it is looked
		/// at: an empty lot bars nobody, and most of a housing district is unclaimed most of the
		/// time.</para>
		/// </remarks>
		private void TickAccessEnforcement(float deltaTime)
		{
			if (!IsHousingEnabled)
			{
				return;
			}

			accessSweepCountdown -= deltaTime;
			if (accessSweepCountdown > 0f)
			{
				return;
			}
			accessSweepCountdown = Mathf.Max(0.1f, accessSweepIntervalSeconds);

			foreach (int sceneHandle in resolvedScenes)
			{
				IReadOnlyList<PlotFoundation> foundations = PlotFoundation.Registry.ForScene(sceneHandle);
				PlotFoundation barring = FirstBarringFoundation(foundations);
				if (barring == null)
				{
					continue;
				}

				if (SnapshotOccupants(barring, sceneHandle, occupantBuffer))
				{
					for (int i = 0; i < foundations.Count; ++i)
					{
						EvictTrespassers(foundations[i], occupantBuffer);
					}
				}
				occupantBuffer.Clear();
			}
		}

		/// <summary>
		/// The first plot in a scene that could bar anybody, or null when every one is an empty lot.
		/// </summary>
		private static PlotFoundation FirstBarringFoundation(IReadOnlyList<PlotFoundation> foundations)
		{
			for (int i = 0; i < foundations.Count; ++i)
			{
				PlotFoundation foundation = foundations[i];
				if (foundation != null && foundation.PlotID > 0 && foundation.State != PlotState.Empty)
				{
					return foundation;
				}
			}
			return null;
		}

		/// <summary>
		/// Pushes everybody who may not be in one plot back outside it.
		/// </summary>
		/// <remarks>
		/// The single-plot entry point, for the moments that cannot wait for the sweep — a friend
		/// being revoked while standing in the house, and a lot going from public ground to a
		/// building site under the people crossing it.
		/// </remarks>
		public void EvictTrespassers(PlotFoundation foundation)
		{
			if (foundation == null || foundation.PlotID <= 0 || foundation.State == PlotState.Empty)
			{
				return;
			}

			if (SnapshotOccupants(foundation, foundation.gameObject.scene.handle, occupantBuffer))
			{
				EvictTrespassers(foundation, occupantBuffer);
			}
			occupantBuffer.Clear();
		}

		/// <summary>
		/// Pushes everybody who may not be in any of these plots back outside, reading each scene's
		/// occupants once however many of its plots are in the list.
		/// </summary>
		/// <remarks>
		/// For a batch of changes arriving together — a cross-channel poll can bring dozens at once
		/// — where the single-plot entry point would read the same scene's occupants once per plot.
		/// </remarks>
		private void EvictTrespassersFrom(List<PlotFoundation> foundations)
		{
			if (foundations == null || foundations.Count < 1)
			{
				return;
			}

			HashSet<int> doneScenes = null;
			for (int i = 0; i < foundations.Count; ++i)
			{
				PlotFoundation first = foundations[i];
				if (first == null)
				{
					continue;
				}

				int sceneHandle = first.gameObject.scene.handle;
				if (!(doneScenes ??= new HashSet<int>()).Add(sceneHandle))
				{
					continue;
				}

				if (SnapshotOccupants(first, sceneHandle, occupantBuffer))
				{
					for (int j = i; j < foundations.Count; ++j)
					{
						PlotFoundation foundation = foundations[j];
						if (foundation != null && foundation.gameObject.scene.handle == sceneHandle)
						{
							EvictTrespassers(foundation, occupantBuffer);
						}
					}
				}
				occupantBuffer.Clear();
			}
		}

		/// <summary>
		/// Pushes everybody in <paramref name="occupants"/> who may not be in this plot back outside it.
		/// </summary>
		/// <param name="foundation">The plot to clear.</param>
		/// <param name="occupants">
		/// The plot's own scene's occupants, read once by the caller so a sweep over many plots does
		/// not re-read them. An evicted occupant's position is updated to where they were put, so
		/// the next plot tested sees where they are, not where they were.
		/// </param>
		private void EvictTrespassers(PlotFoundation foundation, List<Occupant> occupants)
		{
			if (foundation == null || foundation.PlotID <= 0 || occupants == null || occupants.Count < 1)
			{
				return;
			}

			/* An empty lot bars nobody, so the common case costs one enum comparison and no
			 * containment tests at all. */
			if (foundation.State == PlotState.Empty)
			{
				return;
			}

			Bounds bounds = foundation.Bounds;

			for (int i = 0; i < occupants.Count; ++i)
			{
				Occupant occupant = occupants[i];
				if (occupant.Player == null)
				{
					continue;
				}

				/* Geometry before permissions. Almost nobody is standing in any given plot, and the
				 * containment test is two comparisons where resolving access reads a dictionary and
				 * a guild controller. */
				if (!PlotEviction.IsInsideFootprint(bounds, occupant.Position))
				{
					continue;
				}

				if (foundation.AllowsEntry(occupant.Player.ID, GuildIDOf(occupant.Player)))
				{
					continue;
				}

				if (Evict(occupant.Player, foundation, bounds, occupant.Position, out Vector3 exit))
				{
					occupant.Position = exit;
					occupants[i] = occupant;
				}
			}
		}

		/// <summary>
		/// Moves one player to the nearest point outside a plot.
		/// </summary>
		/// <returns>True when the player was moved, with <paramref name="exit"/> where to.</returns>
		/// <remarks>
		/// Velocity is zeroed along with the position. Carrying momentum through the move would walk
		/// the player straight back over the boundary they were just put outside of, and the next
		/// sweep would move them again — which is not an eviction, it is a player pinned to a wall.
		/// </remarks>
		private static bool Evict(IPlayerCharacter player, PlotFoundation foundation, Bounds bounds, Vector3 position, out Vector3 exit)
		{
			exit = position;

			if (player.Motor == null)
			{
				return false;
			}

			/* Not while a teleport is already in flight. The teleport is about to decide where this
			 * character is, and writing a position underneath it would either be discarded or land
			 * them outside a plot in a scene they are no longer in. */
			if (player.IsTeleporting)
			{
				return false;
			}

			exit = PlotEviction.NearestExit(bounds, position);
			if (exit == position)
			{
				return false;
			}

			player.Motor.SetPositionAndRotationAndVelocity(exit, player.Transform.rotation, Vector3.zero);

			Log.Debug("HousingSystem",
				$"CharID={player.ID} was evicted from plot {foundation.PlotID} ('{foundation.PlotKey}', {foundation.State}).");
			return true;
		}

		/// <summary>
		/// Reads who is standing in one loaded scene, and where. Main thread only.
		/// </summary>
		/// <param name="inScene">Any foundation in the scene, for the scene itself.</param>
		/// <param name="sceneHandle">The scene's handle.</param>
		/// <param name="into">Cleared, then filled.</param>
		/// <returns>True when anybody is there.</returns>
		/// <remarks>
		/// <para>Who is there comes from FishNet's connection set for the scene — the same live set
		/// scene-wide broadcasts use — mapped to characters through the character system's mapping
		/// container, which already keeps that current through connects, disconnects and scene
		/// changes. A second copy of that bookkeeping here would only be a second thing to get out of
		/// step, and the one that was wrong would be the one deciding whether to move a player.</para>
		///
		/// <para>Each candidate's own object is then asked which scene it is in. The connection set
		/// says which scenes a client has loaded, which is not quite where its character stands
		/// across a scene change, and a position is only meaningful against the plots of the scene
		/// it was measured in. That is one question per player in a housing scene, where the sweep
		/// used to ask it of every player on the server once per housing scene.</para>
		/// </remarks>
		private bool SnapshotOccupants(PlotFoundation inScene, int sceneHandle, List<Occupant> into)
		{
			into.Clear();

			if (inScene == null ||
				Server?.NetworkWrapper == null ||
				Server.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out ICharacterMappingData<NetworkConnection> mappingData) ||
				mappingData.ConnectionCharacters == null ||
				!Server.NetworkWrapper.TryGetSceneConnections(inScene.gameObject.scene, out HashSet<NetworkConnection> connections))
			{
				return false;
			}

			foreach (NetworkConnection conn in connections)
			{
				if (conn == null ||
					!mappingData.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player) ||
					player == null)
				{
					continue;
				}

				Transform transform = player.Transform;
				if (transform == null || transform.gameObject.scene.handle != sceneHandle)
				{
					continue;
				}

				into.Add(new Occupant
				{
					Player = player,
					Position = transform.position,
				});
			}

			return into.Count > 0;
		}

		/// <summary>
		/// A character's guild, or zero when they are in none.
		/// </summary>
		/// <remarks>
		/// Guild-owned land admits its guild's members, which is the one access question that cannot
		/// be answered from the plot row alone. Read from the character's own controller rather than
		/// from the guild system, because that is where the answer is already cached for the player
		/// standing in front of us.
		/// </remarks>
		private static long GuildIDOf(IPlayerCharacter player)
		{
			if (player == null || !player.TryGet(out IGuildController guildController))
			{
				return 0;
			}
			return guildController.ID;
		}
	}
}
