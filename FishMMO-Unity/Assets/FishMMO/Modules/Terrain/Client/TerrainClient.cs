using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Plugs the Terrain module into the client. Its systems start themselves (each hooks the render pipeline after the first
	/// scene loads); this adds what the core offers a seam for: the grass cover and the builders' state
	/// (<see cref="GroundCoverQuery"/>: footsteps, the wind's beds, ambient life, the frame series) and the moss drawn with the
	/// weather (<see cref="WeatherOverlays"/>).
	/// </summary>
	public static class TerrainClient
	{
		/// <summary>The moss overlay's id.</summary>
		public const string MossOverlayId = "terrain.moss";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			GroundCoverQuery.CoverSource = GrassBladeSystem.CoverAt;
			GroundCoverQuery.StampSource = () => GrassBladeSystem.CoverStamp;
			GroundCoverQuery.GrassBuildingSource = () => GrassBladeSystem.Building;
			GroundCoverQuery.ScatterBuildingSource = () => DetailScatterSystem.Building;
			WeatherOverlays.Register(MossOverlayId, () => new MossOverlay());
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void InstallInEditor() => Install();
#endif
	}
}
