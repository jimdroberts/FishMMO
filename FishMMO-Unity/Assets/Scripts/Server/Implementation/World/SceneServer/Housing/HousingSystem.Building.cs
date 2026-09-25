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
		/// Declares a house finished, moving the plot from building to occupied.
		/// </summary>
		/// <remarks>
		/// An explicit step rather than something inferred from the first structure being placed. The
		/// two states differ in who may come in — building admits only the owner, occupied admits
		/// their friends — and that is a decision the owner should make when they are ready, not one
		/// the game makes for them the moment they put down a wall.
		///
		/// <para>Closing the build session first is deliberate. The session is the tighter lock, and
		/// leaving it open would move the plot to occupied while still turning every guest away —
		/// which reads as the state change having silently failed.</para>
		///
		/// <para>Answered once the database has the new state, not when the request is accepted. The
		/// client used to be told it had succeeded before the write ran, so a write that failed left
		/// a player who had been told their house was open standing in one that was not.</para>
		/// </remarks>
		/// <returns>
		/// False when the request is refused outright, which the caller answers. True when it was
		/// accepted; the answer then follows from here once the database has replied.
		/// </returns>
		public bool TryFinishBuilding(NetworkConnection conn, IPlayerCharacter player, IPlotFoundation foundation)
		{
			if (player == null || foundation is not PlotFoundation plot || !IsHousingEnabled)
			{
				return false;
			}

			if (plot.PlotID <= 0 || plot.State != PlotState.Building)
			{
				return false;
			}

			if (plot.Owner.Type != PlotOwnerType.Character || plot.Owner.ID != player.ID)
			{
				return false;
			}

			// Ending the session is not conditional: they may have finished without one open.
			TryEndBuilding(player, foundation);

			long plotID = plot.PlotID;
			long characterID = player.ID;

			// EnqueuePersistence: the session has already ended in memory, so the state move must run.
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out IPlotService plotService))
				{
					Log.Error("HousingSystem", $"Could not finish building on plot {plotID}: IPlotService unavailable.");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				DatabaseResult<int> moved = await plotService.TrySetStateAsync(
					plotID,
					(int)PlotState.Building,
					(int)PlotState.Occupied,
					characterID,
					0);

				if (!moved.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not finish building on plot {plotID}: [{moved.ErrorCode}] {moved.ErrorMessage}");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				if (moved.Data != 1)
				{
					/* The plot moved on underneath the request — reclaimed for unpaid tax, or already
					 * finished. Either way this is no longer the transition to make. */
					SendHousingResultOnMainThread(conn, plotID, HousingResult.WrongState);
					return;
				}

				if (!TryEnqueueHousingMainThread(() =>
				{
					ApplyStateEverywhere(plotID, PlotState.Occupied);
					SendHousingResult(conn, plotID, HousingResult.Success);
				}))
				{
					Log.Warning("HousingSystem", $"Could not apply the occupied state for plot {plotID} locally; the plot sync applies it.");
				}

				MarkPlotChanged(plotID);
			}, characterID);

			return true;
		}

		/// <summary>
		/// Applies a state to every loaded copy of a plot.
		/// </summary>
		/// <remarks>
		/// Every copy, because channels are several loaded copies of one scene sharing one row. A
		/// state applied to the first match would leave the same house finished in one channel and
		/// still a building site in the next.
		/// </remarks>
		private static void ApplyStateEverywhere(long plotID, PlotState state)
		{
			foreach (PlotFoundation foundation in PlotFoundation.Registry.ForPlot(plotID))
			{
				foundation.ApplyState(state);
			}
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
		/// What is standing on each plot this server has resolved, as the database last said.
		/// </summary>
		/// <remarks>
		/// Cached because placement needs it on every attempt: a new structure has to be tested
		/// against everything already there, and asking the database for a plot's contents once per
		/// placement would put a round trip in front of every piece a player drags into position.
		///
		/// <para>Server-side and authoritative. The client has its own copy for previewing, and this
		/// is the one that decides — the client's is a courtesy that a modified client can lie
		/// about.</para>
		/// </remarks>
		private readonly Dictionary<long, List<PlotStructureData>> structuresByPlot = new Dictionary<long, List<PlotStructureData>>();

		/// <summary>
		/// The volumes claimed by placements that have been accepted and are waiting on the database.
		/// </summary>
		/// <remarks>
		/// Placement is tested against the cache on the main thread and written on the worker, and
		/// the cache only learns of a piece once its row exists. Without these, two placements sent
		/// in quick succession were both tested against the same cache, both passed, and both were
		/// written — two pieces standing in one spot, permanently. A reservation closes that window:
		/// it takes up room from the moment a placement is accepted until the database answers, and
		/// gives the room back if the answer is no.
		/// </remarks>
		private readonly Dictionary<long, List<Bounds>> reservedPlacements = new Dictionary<long, List<Bounds>>();

		/// <summary>
		/// The volumes occupied on a plot, for testing a proposed placement against.
		/// </summary>
		private List<Bounds> OccupiedBounds(PlotFoundation plot)
		{
			List<Bounds> occupied = new List<Bounds>();

			if (plot == null)
			{
				return occupied;
			}

			// Accepted placements still on their way to the database take up room too.
			if (reservedPlacements.TryGetValue(plot.PlotID, out List<Bounds> reserved))
			{
				occupied.AddRange(reserved);
			}

			if (!structuresByPlot.TryGetValue(plot.PlotID, out List<PlotStructureData> structures))
			{
				return occupied;
			}

			Vector3 origin = plot.transform.position;

			foreach (PlotStructureData structure in structures)
			{
				PlotStructureTemplate template = PlotStructureTemplate.Get<PlotStructureTemplate>(structure.TemplateID);
				if (template == null)
				{
					/* A template that no longer ships leaves a row nothing can be measured against.
					 * Skipped rather than guessed at: an invented footprint would either block
					 * placements for no visible reason or let a new piece grow through whatever is
					 * actually standing there. */
					continue;
				}

				Vector3 world = PlotPlacement.ToWorld(origin, new Vector3(structure.LocalX, structure.LocalY, structure.LocalZ));
				occupied.Add(template.GetBounds(world, structure.Yaw));
			}

			return occupied;
		}

		/// <summary>
		/// Places a structure on a plot, if the server agrees it fits.
		/// </summary>
		/// <remarks>
		/// Every check is re-made here rather than trusted, because the client's copy of the plot's
		/// bounds, its owner, and what is already built on it are all things a modified client can
		/// lie about. <see cref="EvaluatePlacement"/> is the shared arithmetic both sides use, so an
		/// honest client's preview and this verdict agree.
		/// </remarks>
		private void PlaceStructure(NetworkConnection conn, IPlayerCharacter player, PlotFoundation plot, int templateID, Vector3 localPosition, float yaw)
		{
			PlotStructureTemplate template = PlotStructureTemplate.Get<PlotStructureTemplate>(templateID);
			if (template == null)
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.Failed);
				return;
			}

			/* Placing is a permission, not an ownership test. A friend given PlaceItems may decorate,
			 * which is the whole point of the permission existing. */
			if (!plot.PermissionsFor(player.ID, GuildIDOf(player)).HasFlag(PlotPermission.PlaceItems))
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.NotPermitted);
				return;
			}

			/* A plot whose contents never loaded is refused, not measured as empty. The cache is the
			 * only thing placement is tested against, and a plot missing from it used to read as bare
			 * ground — so a read that failed let a player build straight through a house the
			 * database still held. */
			if (!structuresByPlot.ContainsKey(plot.PlotID))
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.Failed);
				return;
			}

			PlotPlacementResult verdict = EvaluatePlacement(player, plot, template, localPosition, yaw, OccupiedBounds(plot));
			if (verdict != PlotPlacementResult.Allowed)
			{
				SendHousingResult(conn, plot.PlotID, ToHousingResult(verdict));
				return;
			}

			long plotID = plot.PlotID;
			long characterID = player.ID;

			/* The room is taken now, on the main thread, before anything else can be tested against
			 * this plot — see reservedPlacements. It is only a reservation: the piece itself joins the
			 * cache once its row exists, because a piece cached ahead of a write the database then
			 * refused would be furniture nobody can see, and never goes away. */
			Bounds reserved = template.GetBounds(PlotPlacement.ToWorld(plot.transform.position, localPosition), yaw);
			ReservePlacement(plotID, reserved);

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotStructureService structureService))
				{
					Log.Error("HousingSystem", $"Could not place structure {templateID} on plot {plotID}: IPlotStructureService unavailable.");
					FailPlacementOnMainThread(conn, plotID, reserved);
					return;
				}

				DatabaseResult<long> placed = await structureService.PlaceAsync(
					plotID, templateID, localPosition.x, localPosition.y, localPosition.z, yaw);

				if (!placed.IsSuccess || placed.Data <= 0)
				{
					Log.Error("HousingSystem", $"Could not place structure {templateID} on plot {plotID}: [{placed.ErrorCode}] {placed.ErrorMessage}");
					FailPlacementOnMainThread(conn, plotID, reserved);
					return;
				}

				PlotStructureData structure = new PlotStructureData(
					placed.Data, plotID, templateID, localPosition.x, localPosition.y, localPosition.z, yaw);

				// The reservation becomes the real piece, under the identity the database gave it.
				if (!TryEnqueueHousingMainThread(() =>
				{
					ReleasePlacement(plotID, reserved);
					CacheStructure(plotID, structure);
					SendHousingResult(conn, plotID, HousingResult.Success);
				}))
				{
					/* The reservation stays, and that is correct: the piece exists, and the room it
					 * holds is the room the piece takes up. */
					Log.Warning("HousingSystem", $"Could not record the placement on plot {plotID}; it will appear on the next resolve.");
				}

				MarkPlotChanged(plotID);
			}, characterID))
			{
				ReleasePlacement(plotID, reserved);
				SendHousingResult(conn, plotID, HousingResult.Failed);
			}
		}

		/// <summary>
		/// Takes up room on a plot for a placement the database has not answered yet.
		/// </summary>
		private void ReservePlacement(long plotID, Bounds bounds)
		{
			if (!reservedPlacements.TryGetValue(plotID, out List<Bounds> reserved))
			{
				reserved = new List<Bounds>();
				reservedPlacements.Add(plotID, reserved);
			}
			reserved.Add(bounds);
		}

		/// <summary>
		/// Gives back room reserved by <see cref="ReservePlacement"/>.
		/// </summary>
		/// <remarks>
		/// Matched by value. Two reservations on one plot can never be equal, because the second
		/// would have intersected the first and been refused before it was made.
		/// </remarks>
		private void ReleasePlacement(long plotID, Bounds bounds)
		{
			if (!reservedPlacements.TryGetValue(plotID, out List<Bounds> reserved))
			{
				return;
			}

			reserved.Remove(bounds);
			if (reserved.Count < 1)
			{
				reservedPlacements.Remove(plotID);
			}
		}

		/// <summary>
		/// Gives back a refused placement's room and tells the player. Callable from the worker.
		/// </summary>
		private void FailPlacementOnMainThread(NetworkConnection conn, long plotID, Bounds reserved)
		{
			if (!TryEnqueueHousingMainThread(() =>
			{
				ReleasePlacement(plotID, reserved);
				SendHousingResult(conn, plotID, HousingResult.Failed);
			}))
			{
				Log.Warning("HousingSystem", $"Could not report the failed placement on plot {plotID}; its room stays reserved until the plot is next cleared.");
			}
		}

		/// <summary>
		/// Takes a structure back off a plot.
		/// </summary>
		/// <remarks>
		/// Gated on <see cref="PlotPermission.RemoveItems"/>, which is deliberately not implied by
		/// the permission to place. Removal is what destroys work; a friend helping decorate needs
		/// one, and only a trusted one needs both.
		/// </remarks>
		private void RemoveStructure(NetworkConnection conn, IPlayerCharacter player, PlotFoundation plot, long structureID)
		{
			if (structureID <= 0)
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.Failed);
				return;
			}

			if (!plot.PermissionsFor(player.ID, GuildIDOf(player)).HasFlag(PlotPermission.RemoveItems))
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.NotPermitted);
				return;
			}

			long plotID = plot.PlotID;
			long characterID = player.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out IPlotStructureService structureService))
				{
					Log.Error("HousingSystem", $"Could not remove structure {structureID} from plot {plotID}: IPlotStructureService unavailable.");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				/* The plot is part of the delete, not merely a label on it. Without it a client could
				 * name a structure standing on somebody else's land and have it removed, having
				 * proved only that it can reach a plot it does have rights on. */
				DatabaseResult<int> removed = await structureService.DemolishAsync(structureID, plotID);
				if (!removed.IsSuccess)
				{
					Log.Error("HousingSystem", $"Could not remove structure {structureID} from plot {plotID}: [{removed.ErrorCode}] {removed.ErrorMessage}");
					SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
					return;
				}

				if (removed.Data != 1)
				{
					/* Already gone, or never on this plot. Nothing was removed, so the request did
					 * not succeed — but a piece the database no longer holds on this plot must not
					 * keep taking up room here either, so the cache lets go of it all the same. */
					if (!TryEnqueueHousingMainThread(() =>
					{
						UncacheStructure(plotID, structureID);
						SendHousingResult(conn, plotID, HousingResult.Failed);
					}))
					{
						Log.Warning("HousingSystem", $"Could not report the refused removal on plot {plotID}.");
					}
					return;
				}

				if (!TryEnqueueHousingMainThread(() =>
				{
					UncacheStructure(plotID, structureID);
					SendHousingResult(conn, plotID, HousingResult.Success);
				}))
				{
					Log.Warning("HousingSystem", $"Could not record the removal on plot {plotID}; it will correct on the next resolve.");
				}

				MarkPlotChanged(plotID);
			}, characterID))
			{
				SendHousingResult(conn, plotID, HousingResult.Failed);
			}
		}

		/// <summary>
		/// Records a newly placed structure in the cache placement is tested against.
		/// </summary>
		private void CacheStructure(long plotID, PlotStructureData structure)
		{
			if (!structuresByPlot.TryGetValue(plotID, out List<PlotStructureData> structures))
			{
				structures = new List<PlotStructureData>();
				structuresByPlot.Add(plotID, structures);
			}
			structures.Add(structure);
		}

		/// <summary>
		/// Forgets one removed structure.
		/// </summary>
		private void UncacheStructure(long plotID, long structureID)
		{
			if (!structuresByPlot.TryGetValue(plotID, out List<PlotStructureData> structures))
			{
				return;
			}

			for (int i = structures.Count - 1; i >= 0; --i)
			{
				if (structures[i].ID == structureID)
				{
					structures.RemoveAt(i);
					break;
				}
			}
		}

		/// <summary>
		/// Forgets this server's copies of a plot's guest list and contents, once the database has
		/// emptied both. Main thread only.
		/// </summary>
		/// <remarks>
		/// The contents become an empty list rather than no entry. No entry now means "never
		/// loaded", which placement refuses; an emptied plot is loaded, and bare. A plot this server
		/// does not show is left without an entry, since there is nothing here to keep in step.
		/// </remarks>
		private void ForgetPlotContents(long plotID)
		{
			ForgetAccessGrants(plotID);

			if (structuresByPlot.ContainsKey(plotID))
			{
				structuresByPlot[plotID] = new List<PlotStructureData>();
			}
			reservedPlacements.Remove(plotID);
		}

		/// <summary>
		/// Turns a placement verdict into something the client can be told.
		/// </summary>
		/// <remarks>
		/// Two enums rather than one, because they answer different questions.
		/// <see cref="PlotPlacementResult"/> is shared arithmetic about geometry and is the same on
		/// both sides of the wire; <see cref="HousingResult"/> is the reply to any housing request at
		/// all. Collapsing them would put "the plot is not in building mode" into the vocabulary of a
		/// vault retrieval.
		/// </remarks>
		private static HousingResult ToHousingResult(PlotPlacementResult verdict)
		{
			switch (verdict)
			{
				case PlotPlacementResult.Allowed:
					return HousingResult.Success;
				case PlotPlacementResult.UnknownPlot:
					return HousingResult.UnknownPlot;
				case PlotPlacementResult.NotTheOwner:
					return HousingResult.NotTheOwner;
				case PlotPlacementResult.NotBuilding:
					return HousingResult.WrongState;
				case PlotPlacementResult.OutOfBounds:
				case PlotPlacementResult.Occupied:
					return HousingResult.DoesNotFit;
				default:
					return HousingResult.Failed;
			}
		}

		/// <summary>
		/// Reads back everything built on a scene's plots.
		/// </summary>
		/// <returns>
		/// The structures keyed by plot, or <c>null</c> when they could not be read. The two are kept
		/// apart deliberately: an empty dictionary says nothing is built, and a failed read reported
		/// as one would let the next placement go straight through whatever is standing there.
		/// </returns>
		private async Task<Dictionary<long, List<PlotStructureData>>> FetchStructuresAsync(List<long> plotIDs)
		{
			Dictionary<long, List<PlotStructureData>> byPlot = new Dictionary<long, List<PlotStructureData>>();

			if (plotIDs == null || plotIDs.Count < 1)
			{
				return byPlot;
			}

			if (!TryGetDbService(out IPlotStructureService structureService))
			{
				Log.Error("HousingSystem", "Could not read plot structures: IPlotStructureService unavailable.");
				return null;
			}

			DatabaseResult<List<PlotStructureData>> structures = await structureService.FetchByPlotsAsync(plotIDs);
			if (!structures.IsSuccess || structures.Data == null)
			{
				Log.Error("HousingSystem", $"Could not read plot structures: [{structures.ErrorCode}] {structures.ErrorMessage}");
				return null;
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
