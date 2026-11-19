using System.Linq;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The World Simulation module asks the server for its biome templates at boot (Phase 4 follow-up, 2026-11-18).</summary>
	public class WorldSimulationPreloadTests
	{
		[Test]
		public void TheModule_AsksForTheBiomeTemplates()
		{
			WorldSimulationPreload.Install();
			LogAssert.IsTrue(ServerPreloadLabels.All.Contains(Constants.SharedBiomesLabel), "the biome label is preloaded by the server");
		}
	}
}
