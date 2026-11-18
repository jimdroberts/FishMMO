#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Jungle Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry JungleRuins()
		{
			// A collapsed jungle city: canopy with the odd kapok emergent, vines over the stone, broad herbs and bromeliads,
			// tree ferns and bananas in the gaps the fallen buildings left.
			return E("Jungle Ruins", L(Ground.JungleFloor, Stand(Was(1.2f, 5f), 25f, W(0.7f, 0.8f), "Jungle"), Plants("Fern", 15f), Plants("ShrubSmall", 6f), Bush("Hibiscus", 0.5f),
					With(Trees(0.004f, 28f, 20f, "Kapok"), r => { r.Spacing = 28f; r.ClusterSize = 2f; }), Plants("BroadHerb", 3f), Plants("RosetteBromeliad", 1f, 128)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Trees(0.15f, 8f, 25f, "TreeFern"), Trees(0.1f, 8f, 25f, "Banana"), Logs("Fallen logs", 0.05f, 10f, Wood("FallenLog_Oak", "Stump_Broken")))),
				C(Ground.Rock), Ground.Mud, Ground.Mud)
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.HuntingLodge, 1.5f), (POIType.LumberCamp, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f),
					(POIType.FeyRing, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f),
					(POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.AncientTree, 1f), (POIType.HerbGrove, 1.5f),
					(POIType.LumberMill, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f), (POIType.Hamlet, 0.3f),
					(POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 1f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f),
					(POIType.LostLibrary, 1.4f), (POIType.TreeVillage, 0.8f), (POIType.SpiderThicket, 1f), (POIType.DryadOak, 0.4f),
					(POIType.MossColossus, 1f), (POIType.StepPyramid, 1.4f), (POIType.Cenote, 1f), (POIType.JaguarTemple, 1.2f),
					(POIType.SerpentIdol, 1f), (POIType.LostCity, 2f), (POIType.LizardfolkVillage, 0.8f), (POIType.IdolVault, 1f),
					(POIType.LizardfolkHatchery, 0.6f), (POIType.LostThrone, 1.2f), (POIType.Keep, 1f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Longhouse", 0.4f)
				.Prefers(POIType.Town, "Palisaded", 1.2f)
				.Prefers(POIType.City, "Timber", 1.5f)
				.Prefers(POIType.Capital, "Timber", 1.5f)
				.Prefers(POIType.Keep, "Palisade", 1.5f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.Tower, "Wizard", 1.4f)
				.Prefers(POIType.Camp, "Lean-tos", 1.5f)
				.Prefers(POIType.BanditCamp, "Palisade", 1.5f)
				.Prefers(POIType.Monastery, "Timber", 1.5f)
				.Prefers(POIType.Ruins, "Burned", 1.5f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1.4f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.3f)
				.Prefers(POIType.TreeVillage, "Jungle Boughs", 1.5f)
				.Prefers(POIType.StepPyramid, "Fallen Fane", 1.6f)
				.Prefers(POIType.LostCity, "Fallen Seat", 1.5f)
				// (the fantasy wave's variants end here)
				.GrowsVines(1f, 1f, 0.6f, 0.35f, ("Liana", 1f))
				.Gathers("Bloodroot Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Rainforest");
		}
	}
}
#endif
