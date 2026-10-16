#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Cave()
		{
			// A limestone show cave, floor only (no ceiling to hang stalactites from): stalagmites and columns, flowstone,
			// rimstone gours in the wet hollows, breakdown, pale fungi, rare gypsum. The pinnacles were a surface karst spire.
			return E("Cave", L(Ground.Rock, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 6f), Blocks("Limestone", 0.05f, 10f, F(RockTypes.Limestone, "Block")),
					Outcrops("Stalagmites", 0.05f, 6f, F(RockTypes.Limestone, "Stalagmite")), Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone"), 35f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.003f, 20f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Mud, 64f, 2f, -1f, -1f, Blocks("Rimstone gours", 0.015f, 14f, F(RockTypes.Limestone, "Gours"), 12f), Plants("MushroomCave", 1.5f, 128))),
				C(Ground.CliffRock, 35f), Ground.Mud, Ground.Gravel)
				.Moved("main", "Formations: Limestone pinnacles")
				.Accepts(RockTypes.Limestone, RockTypes.Chalk, RockTypes.Marble, RockTypes.Granite)
				.Look(BiomeLookClass.Cave)
				.Wildlife(BiomeHabitat.Cave);
		}
	}
}
#endif
