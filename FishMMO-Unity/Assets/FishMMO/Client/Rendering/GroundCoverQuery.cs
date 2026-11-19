using System;

namespace FishMMO.Client
{
	/// <summary>
	/// What the ground's procedural cover offers the rest of the client (Phase 4 of the core and modules plan, 2026-11-18): the
	/// grass cover at a point, for footsteps, the wind's beds and ambient life's foraging, and whether the grass or the detail
	/// scatter is still being built, for the frame series and the perf probes. The Terrain module installs the sources; with
	/// no Terrain module there is no cover, nothing is building, and every reader gets bare ground.
	/// </summary>
	public static class GroundCoverQuery
	{
		/// <summary>The grass cover at (x, z), 0..1; installed by the Terrain module.</summary>
		public static Func<float, float, float> CoverSource;

		/// <summary>A stamp that changes whenever the cover does; installed by the Terrain module.</summary>
		public static Func<int> StampSource;

		/// <summary>Whether the grass is being built; installed by the Terrain module.</summary>
		public static Func<bool> GrassBuildingSource;

		/// <summary>Whether the detail scatter is being built; installed by the Terrain module.</summary>
		public static Func<bool> ScatterBuildingSource;

		/// <summary>The grass cover at (x, z), 0..1; 0 with no Terrain module.</summary>
		public static float CoverAt(float x, float z) => CoverSource != null ? CoverSource(x, z) : 0f;

		/// <summary>A stamp that changes whenever the cover does; 0 with no Terrain module.</summary>
		public static int CoverStamp => StampSource != null ? StampSource() : 0;

		/// <summary>Whether the grass is being built.</summary>
		public static bool GrassBuilding => GrassBuildingSource != null && GrassBuildingSource();

		/// <summary>Whether the detail scatter is being built.</summary>
		public static bool ScatterBuilding => ScatterBuildingSource != null && ScatterBuildingSource();
	}
}
