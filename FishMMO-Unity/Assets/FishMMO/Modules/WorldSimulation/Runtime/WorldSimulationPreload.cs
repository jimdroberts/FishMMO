using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Asks the server to preload every biome template at boot (<see cref="ServerPreloadLabels"/>): the server keeps all of them
	/// (its builds strip their art); the client loads only a scene's own biomes with the scene (SceneBiomeLoader).
	/// </summary>
	public static class WorldSimulationPreload
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			ServerPreloadLabels.Register(Constants.SharedBiomesLabel);
		}
	}
}
