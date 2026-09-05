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
		private async Task<Dictionary<long, Dictionary<long, PlotPermission>>> FetchAccessGrantsAsync(List<PlotData> plots)
		{
			Dictionary<long, Dictionary<long, PlotPermission>> byPlot = new Dictionary<long, Dictionary<long, PlotPermission>>();

			if (plots == null || plots.Count < 1 ||
				!TryGetDbService(out IPlotAccessService accessService))
			{
				return byPlot;
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
				Log.Error("HousingSystem", $"Could not read plot access grants: {grants.ErrorMessage}");
				return byPlot;
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
		/// <param name="granter">The character handing out the access.</param>
		/// <param name="foundation">The plot in question.</param>
		/// <param name="targetCharacterID">Who is being granted.</param>
		/// <param name="requested">What they are being given.</param>
		/// <remarks>
		/// Clamped to what the granter holds themselves, so a friend with
		/// <see cref="PlotPermission.InviteFriends"/> cannot mint permissions the owner never gave
		/// them. Without that the model collapses to its weakest link: whoever can invite can invite
		/// themselves into everything.
		/// </remarks>
		public bool TryGrantAccess(IPlayerCharacter granter, IPlotFoundation foundation, long targetCharacterID, PlotPermission requested)
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
					return;
				}

				DatabaseResult<int> result = await accessService.GrantAsync(plotID, targetCharacterID, mask, granterID);
				if (!result.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not grant access to plot {plotID} for CharID={targetCharacterID}: {result.ErrorMessage}");
					return;
				}

				/* Applied locally as well as recorded, so the granter sees it take effect now rather
				 * than on the next cross-channel poll. The other channels learn about it the same
				 * way they learn about everything else. */
				if (!TryEnqueueHousingMainThread(() => ApplyGrantEverywhere(plotID, targetCharacterID, granted)))
				{
					Log.Warning("HousingSystem", $"Could not apply the access grant for plot {plotID} locally.");
				}
				MarkPlotChanged(plotID);
			}, granterID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the access grant for plot {plotID}.");
				return false;
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
		public bool TryRevokeAccess(IPlayerCharacter revoker, IPlotFoundation foundation, long targetCharacterID)
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

			/* Locally first, and before the write is confirmed. The player being revoked is standing
			 * in the house now, and the round trip is the window in which they are inside a plot the
			 * owner has already decided they may not be in. If the write then fails the next resolve
			 * puts the grant back, which is the harmless direction to be wrong in. */
			ApplyGrantEverywhere(plotID, targetCharacterID, PlotPermission.None);

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotAccessService accessService))
				{
					return;
				}

				DatabaseResult<int> result = await accessService.RevokeAsync(plotID, targetCharacterID);
				if (!result.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not revoke access to plot {plotID} for CharID={targetCharacterID}: {result.ErrorMessage}");
					return;
				}

				MarkPlotChanged(plotID);
			}, revoker.ID))
			{
				Log.Warning("HousingSystem", $"Could not enqueue the access revocation for plot {plotID}.");
			}

			return true;
		}

		/// <summary>
		/// Clears every grant on a plot, in the database and on every copy of it here.
		/// </summary>
		/// <remarks>
		/// Run when a plot changes hands. A new owner must not inherit the last one's guest list, and
		/// an owner who reclaims land later must not find the people they evicted still holding keys.
		/// </remarks>
		public void ClearAccessGrants(long plotID)
		{
			if (plotID <= 0)
			{
				return;
			}

			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForPlot(plotID))
			{
				foundation.ApplyAccessGrants(new Dictionary<long, PlotPermission>());
			}

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotAccessService accessService))
				{
					return;
				}

				DatabaseResult<int> result = await accessService.RevokeAllAsync(plotID);
				if (!result.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not clear access grants on plot {plotID}: {result.ErrorMessage}");
				}
			}))
			{
				Log.Warning("HousingSystem", $"Could not enqueue clearing the access grants on plot {plotID}.");
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
		/// Puts out anybody standing inside a plot they may not be in.
		/// </summary>
		/// <remarks>
		/// Walks scenes, not plots and not players, because the player list is the expensive half.
		/// Building it means enumerating every character on the server and filtering by scene, so it
		/// is built once per scene and reused across that scene's foundations — doing it per
		/// foundation would make a district of fifty plots fifty passes over the whole server's
		/// characters, twice a second.
		///
		/// <para>Scenes with nobody in them cost one pass and stop, and the plots inside a scene are
		/// filtered on state first: an empty lot bars nobody, and most of a housing district is
		/// unclaimed most of the time.</para>
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
				if (foundations.Count < 1)
				{
					continue;
				}

				List<IPlayerCharacter> players = PlayersInScene(sceneHandle);
				if (players == null || players.Count < 1)
				{
					continue;
				}

				for (int i = 0; i < foundations.Count; ++i)
				{
					EvictTrespassers(foundations[i], players);
				}
			}
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

			EvictTrespassers(foundation, PlayersInScene(foundation.gameObject.scene.handle));
		}

		/// <summary>
		/// Pushes everybody in <paramref name="players"/> who may not be in this plot back outside it.
		/// </summary>
		/// <param name="foundation">The plot to clear.</param>
		/// <param name="players">
		/// The characters in the plot's own scene. Supplied by the caller so a sweep over many plots
		/// builds it once.
		/// </param>
		private void EvictTrespassers(PlotFoundation foundation, List<IPlayerCharacter> players)
		{
			if (foundation == null || foundation.PlotID <= 0 || players == null || players.Count < 1)
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

			for (int i = 0; i < players.Count; ++i)
			{
				IPlayerCharacter player = players[i];
				if (player == null)
				{
					continue;
				}

				Transform transform = player.Transform;
				if (transform == null)
				{
					continue;
				}

				/* Geometry before permissions. Almost nobody is standing in any given plot, and the
				 * containment test is two comparisons where resolving access reads a dictionary and
				 * a guild controller. */
				Vector3 position = transform.position;
				if (!PlotEviction.IsInsideFootprint(bounds, position))
				{
					continue;
				}

				if (foundation.AllowsEntry(player.ID, GuildIDOf(player)))
				{
					continue;
				}

				Evict(player, foundation, bounds, position);
			}
		}

		/// <summary>
		/// Moves one player to the nearest point outside a plot.
		/// </summary>
		/// <remarks>
		/// Velocity is zeroed along with the position. Carrying momentum through the move would walk
		/// the player straight back over the boundary they were just put outside of, and the next
		/// sweep would move them again — which is not an eviction, it is a player pinned to a wall.
		/// </remarks>
		private static void Evict(IPlayerCharacter player, PlotFoundation foundation, Bounds bounds, Vector3 position)
		{
			if (player.Motor == null)
			{
				return;
			}

			/* Not while a teleport is already in flight. The teleport is about to decide where this
			 * character is, and writing a position underneath it would either be discarded or land
			 * them outside a plot in a scene they are no longer in. */
			if (player.IsTeleporting)
			{
				return;
			}

			Vector3 exit = PlotEviction.NearestExit(bounds, position);
			if (exit == position)
			{
				return;
			}

			player.Motor.SetPositionAndRotationAndVelocity(exit, player.Transform.rotation, Vector3.zero);

			Log.Debug("HousingSystem",
				$"CharID={player.ID} was evicted from plot {foundation.PlotID} ('{foundation.PlotKey}', {foundation.State}).");
		}

		/// <summary>
		/// The players currently in one loaded scene.
		/// </summary>
		/// <remarks>
		/// Read from the character system's mapping container rather than tracked here. It already
		/// knows who is where and keeps that current through connects, disconnects and scene
		/// changes; a second copy of that bookkeeping would only be a second thing to get out of
		/// step, and the one that was wrong would be the one deciding whether to move a player.
		/// </remarks>
		private List<IPlayerCharacter> PlayersInScene(int sceneHandle)
		{
			if (Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out ICharacterMappingData<NetworkConnection> mappingData) ||
				mappingData.CharactersByID == null)
			{
				return null;
			}

			List<IPlayerCharacter> players = null;

			foreach (IPlayerCharacter player in mappingData.CharactersByID.Values)
			{
				if (player == null)
				{
					continue;
				}

				Transform transform = player.Transform;
				if (transform == null || transform.gameObject.scene.handle != sceneHandle)
				{
					continue;
				}

				(players ??= new List<IPlayerCharacter>()).Add(player);
			}

			return players;
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
