using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Building on owned land: sessions, placement, and keeping other players out while it happens.
	/// </summary>
	public partial class HousingSystem
	{
		/// <summary>
		/// How long a build session may stay open without the builder being present, in seconds.
		/// </summary>
		/// <remarks>
		/// A session is closed by leaving it, but a player can also disconnect, crash, or walk away.
		/// None of those produce a "done" message, and a plot left in build mode is a plot nobody
		/// else can enter — including the owner, after they log back in — so the session has to be
		/// able to end without being told.
		/// </remarks>
		private const float BuildSessionTimeoutSeconds = 300f;

		/// <summary>
		/// How often abandoned build sessions are swept, in seconds.
		/// </summary>
		private const float BuildSessionSweepSeconds = 15f;

		/// <summary>
		/// A plot currently being edited.
		/// </summary>
		private struct BuildSession
		{
			/// <summary>The editing character.</summary>
			public long CharacterID;

			/// <summary>
			/// The editing character itself, so presence can be checked without a lookup.
			/// </summary>
			/// <remarks>
			/// Held for the life of the session only. A character that despawns leaves a destroyed
			/// transform behind, which is how the sweep notices they have gone even when no logout
			/// message arrived.
			/// </remarks>
			public IPlayerCharacter Builder;

			/// <summary>The foundation being edited.</summary>
			public PlotFoundation Foundation;

			/// <summary>When the builder was last seen inside the plot.</summary>
			public float LastSeen;
		}

		/// <summary>
		/// Open build sessions, keyed by plot.
		/// </summary>
		private readonly Dictionary<long, BuildSession> buildSessions = new Dictionary<long, BuildSession>();

		/// <summary>
		/// Seconds until the next abandoned-session sweep.
		/// </summary>
		private float buildSweepCountdown = BuildSessionSweepSeconds;

		/// <summary>
		/// Opens a build session on a plot, if the character may edit it.
		/// </summary>
		/// <returns>True when the session is now open and held by this character.</returns>
		public bool TryBeginBuilding(IPlayerCharacter player, IPlotFoundation foundation)
		{
			if (player == null || foundation is not PlotFoundation plot || !IsHousingEnabled)
			{
				return false;
			}

			if (plot.PlotID <= 0 || !plot.Owner.IsOwned)
			{
				return false;
			}

			/* Ownership is checked against the character, not against a permission flag, because
			 * guild-owned land has no ranks wired into housing yet. When it does, this is the one
			 * place that answer changes. */
			if (plot.Owner.Type != PlotOwnerType.Character || plot.Owner.ID != player.ID)
			{
				return false;
			}

			if (buildSessions.TryGetValue(plot.PlotID, out BuildSession existing))
			{
				// Re-entering a session you already hold is fine; taking someone else's is not.
				if (existing.CharacterID != player.ID)
				{
					return false;
				}
			}

			buildSessions[plot.PlotID] = new BuildSession
			{
				CharacterID = player.ID,
				Builder = player,
				Foundation = plot,
				LastSeen = Time.time,
			};

			plot.SetBuilder(player.ID);
			return true;
		}

		/// <summary>
		/// Closes a build session.
		/// </summary>
		/// <remarks>
		/// Closing somebody else's session is refused rather than ignored, so a stray message
		/// cannot unlock a plot mid-edit and drop other players into it.
		/// </remarks>
		public bool TryEndBuilding(IPlayerCharacter player, IPlotFoundation foundation)
		{
			if (player == null || foundation is not PlotFoundation plot)
			{
				return false;
			}

			if (!buildSessions.TryGetValue(plot.PlotID, out BuildSession session) ||
				session.CharacterID != player.ID)
			{
				return false;
			}

			CloseSession(plot.PlotID, session);
			return true;
		}

		/// <summary>
		/// Ends every session a character holds.
		/// </summary>
		/// <remarks>
		/// Called when a character leaves. Without it, logging out mid-build would leave the plot
		/// closed until the timeout, and the owner would come back to land they cannot enter.
		/// </remarks>
		public void EndBuildingFor(long characterID)
		{
			if (characterID <= 0 || buildSessions.Count < 1)
			{
				return;
			}

			List<long> closing = null;
			foreach (KeyValuePair<long, BuildSession> pair in buildSessions)
			{
				if (pair.Value.CharacterID == characterID)
				{
					(closing ??= new List<long>()).Add(pair.Key);
				}
			}

			if (closing == null)
			{
				return;
			}

			foreach (long plotID in closing)
			{
				if (buildSessions.TryGetValue(plotID, out BuildSession session))
				{
					CloseSession(plotID, session);
				}
			}
		}

		/// <summary>
		/// Drops a session and reopens the plot.
		/// </summary>
		private void CloseSession(long plotID, BuildSession session)
		{
			buildSessions.Remove(plotID);

			if (session.Foundation != null)
			{
				session.Foundation.SetBuilder(0);
			}
		}

		/// <summary>
		/// Closes sessions whose builder has stopped turning up, and keeps live ones alive.
		/// </summary>
		/// <remarks>
		/// Presence is what keeps a session open, rather than a message from the client. A client
		/// that has crashed cannot say it is gone, and one that is lying can say anything it likes —
		/// but neither can put a character back inside the plot.
		/// </remarks>
		private void SweepBuildSessions(float deltaTime)
		{
			if (buildSessions.Count < 1)
			{
				return;
			}

			buildSweepCountdown -= deltaTime;
			if (buildSweepCountdown > 0f)
			{
				return;
			}
			buildSweepCountdown = BuildSessionSweepSeconds;

			float now = Time.time;
			List<long> expired = null;
			List<long> present = null;

			/* Nothing is written to the dictionary inside this loop, including refreshing a
			 * timestamp. Assigning to an existing key during enumeration is legal on some runtimes
			 * and throws on others, and a sweep that crashes leaves every open plot locked. */
			foreach (KeyValuePair<long, BuildSession> pair in buildSessions)
			{
				BuildSession session = pair.Value;

				if (session.Foundation == null)
				{
					// The scene unloaded underneath it.
					(expired ??= new List<long>()).Add(pair.Key);
					continue;
				}

				if (IsBuilderPresent(session))
				{
					(present ??= new List<long>()).Add(pair.Key);
					continue;
				}

				if (now - session.LastSeen >= BuildSessionTimeoutSeconds)
				{
					Log.Debug("HousingSystem", $"Build session on plot {pair.Key} timed out; reopening it.");
					(expired ??= new List<long>()).Add(pair.Key);
				}
			}

			if (present != null)
			{
				foreach (long plotID in present)
				{
					if (buildSessions.TryGetValue(plotID, out BuildSession session))
					{
						session.LastSeen = now;
						buildSessions[plotID] = session;
					}
				}
			}

			if (expired == null)
			{
				return;
			}

			foreach (long plotID in expired)
			{
				if (buildSessions.TryGetValue(plotID, out BuildSession session))
				{
					CloseSession(plotID, session);
				}
			}
		}

		/// <summary>
		/// True when the builder is still standing on the plot they are editing.
		/// </summary>
		/// <remarks>
		/// Presence is read from the character's own transform rather than from anything the client
		/// sends. A crashed client cannot report that it is gone, and a modified one can report
		/// whatever it likes — but neither can put a character back inside the plot.
		/// </remarks>
		private static bool IsBuilderPresent(BuildSession session)
		{
			IPlayerCharacter builder = session.Builder;
			if (builder == null)
			{
				return false;
			}

			/* Unity's null is the point of this check: a despawned character leaves a destroyed
			 * transform, which compares equal to null while the interface reference does not. */
			Transform transform = builder.Transform;
			if (transform == null)
			{
				return false;
			}

			return session.Foundation.Contains(transform.position);
		}

		/// <summary>
		/// Decides whether a structure may be placed, without writing anything.
		/// </summary>
		/// <remarks>
		/// The server's answer, reached with the same shared code a client uses to grey out a
		/// placement before asking. Every check here is re-made server-side rather than trusted,
		/// because the client's copy of the plot's bounds, its owner, and what is already built on
		/// it are all things a modified client can lie about.
		/// </remarks>
		public PlotPlacementResult EvaluatePlacement(
			IPlayerCharacter player,
			PlotFoundation plot,
			PlotStructureTemplate template,
			Vector3 localPosition,
			float yaw,
			IReadOnlyList<Bounds> existingStructures)
		{
			if (player == null || plot == null || plot.PlotID <= 0)
			{
				return PlotPlacementResult.UnknownPlot;
			}
			if (template == null)
			{
				return PlotPlacementResult.UnknownStructure;
			}

			if (plot.Owner.Type != PlotOwnerType.Character || plot.Owner.ID != player.ID)
			{
				return PlotPlacementResult.NotTheOwner;
			}

			if (!buildSessions.TryGetValue(plot.PlotID, out BuildSession session) ||
				session.CharacterID != player.ID)
			{
				return PlotPlacementResult.NotBuilding;
			}

			Vector3 world = PlotPlacement.ToWorld(plot.transform.position, localPosition);
			Bounds proposed = template.GetBounds(world, yaw);

			if (!PlotPlacement.IsFullyInside(proposed, plot.Bounds))
			{
				return PlotPlacementResult.OutOfBounds;
			}

			if (existingStructures != null)
			{
				for (int i = 0; i < existingStructures.Count; ++i)
				{
					if (PlotPlacement.Intersects(proposed, existingStructures[i]))
					{
						return PlotPlacementResult.Occupied;
					}
				}
			}

			return PlotPlacementResult.Allowed;
		}

		/// <summary>
		/// Removes everything built on a plot, and forgets it.
		/// </summary>
		/// <remarks>
		/// Land that changes hands must not arrive with the last owner's house still on it. Called
		/// on release, and by reclamation when that lands.
		/// </remarks>
		public void ClearStructures(long plotID)
		{
			if (plotID <= 0)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotStructureService structureService))
				{
					return;
				}

				DatabaseResult<int> removed = await structureService.DemolishAllAsync(plotID);
				if (!removed.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not clear structures on plot {plotID}: {removed.ErrorMessage}");
					return;
				}

				if (removed.Data > 0)
				{
					Log.Debug("HousingSystem", $"Cleared {removed.Data} structure(s) from plot {plotID}.");
				}
			}))
			{
				Log.Warning("HousingSystem", $"Could not enqueue structure clearing for plot {plotID}.");
			}
		}

		/// <summary>
		/// Reads back everything built on a scene's plots.
		/// </summary>
		private async Task<Dictionary<long, List<PlotStructureData>>> FetchStructuresAsync(List<long> plotIDs)
		{
			Dictionary<long, List<PlotStructureData>> byPlot = new Dictionary<long, List<PlotStructureData>>();

			if (plotIDs == null || plotIDs.Count < 1 ||
				!TryGetDbService(out IPlotStructureService structureService))
			{
				return byPlot;
			}

			DatabaseResult<List<PlotStructureData>> structures = await structureService.FetchByPlotsAsync(plotIDs);
			if (!structures.IsSuccess || structures.Data == null)
			{
				Log.Error("HousingSystem", $"Could not read plot structures: {structures.ErrorMessage}");
				return byPlot;
			}

			foreach (PlotStructureData structure in structures.Data)
			{
				if (!byPlot.TryGetValue(structure.PlotID, out List<PlotStructureData> list))
				{
					list = new List<PlotStructureData>();
					byPlot.Add(structure.PlotID, list);
				}
				list.Add(structure);
			}

			return byPlot;
		}
	}
}
