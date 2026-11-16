using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for housing configuration.
	/// </summary>
	/// <remarks>
	/// Everything the housing feature set builds on — purchase, building permissions, tax and
	/// reclamation — resolves to "who may own this plot", so that question is answered here once
	/// rather than restated by each system.
	/// </remarks>
	public interface IHousingSystem : IServerBehaviour
	{
		/// <summary>
		/// Who may own land and housing on this server.
		/// </summary>
		HousingOwnershipMode OwnershipMode { get; }

		/// <summary>
		/// True when housing is enabled in any form.
		/// </summary>
		bool IsHousingEnabled { get; }

		/// <summary>
		/// True when an individual character may own land.
		/// </summary>
		bool AllowsPlayerOwnership { get; }

		/// <summary>
		/// True when a guild may own land.
		/// </summary>
		bool AllowsGuildOwnership { get; }

		// ── Staff tools (the /gm plot and /admin plot commands) ──────────────────

		/// <summary>
		/// Finds the plot a staff command names. Main thread. Empty: the plot the actor stands on, or
		/// the nearest deed post within 30 m. A number: that plot ID (it need not be in this scene, so an
		/// orphaned row can be named). Anything else: a plot key in the actor's scene.
		/// </summary>
		bool TryFindPlotForStaff(IPlayerCharacter actor, string target, out long plotID, out string error);

		/// <summary>Describes a plot for staff: row, owner, rent dates, guests, structures. Worker thread.</summary>
		Task<IReadOnlyList<string>> DescribePlotForStaffAsync(long plotID);

		/// <summary>
		/// Takes a plot from its owner. With <paramref name="keepContents"/> whatever stands on it goes to the
		/// owner's vault (as a reclamation does); without, it is demolished and the lot is bare. Worker thread.
		/// </summary>
		Task<string> ReleasePlotForStaffAsync(long plotID, bool keepContents);

		/// <summary>Moves a plot's rent due date: <c>now</c>, or a change such as <c>+1h</c> / <c>-2d</c>. Worker thread.</summary>
		Task<string> SetRentDueForStaffAsync(long plotID, string when);

		/// <summary>
		/// Prepares the list of owned plots in the actor's scene whose key no foundation carries any more
		/// (a re-cut dropped them). Main thread; the returned work runs on the worker.
		/// </summary>
		Func<Task<IReadOnlyList<string>>> PrepareOrphanListForStaff(IPlayerCharacter actor);

		/// <summary>Runs this server's rent sweep on the next frame instead of at its usual interval.</summary>
		void SweepRentNow();
	}
}
