#if UNITY_EDITOR
namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How much moss each generated material may grow: the world lit shader's moss layer (FishMoss.hlsl), set as the
	/// material's <c>_FishMoss</c> (0..1; the shader's default 0 grows none). The scene's dampness multiplies it at
	/// run time (MossClimate, from the biome), so these say only what the SURFACE allows: porous, rough, lime-rich stone
	/// holds water and spores and greens over; glassy, salty, sulphurous or metallic ground never does
	/// (2026-10-12: "a moss layer that can be applied to objects").
	/// </summary>
	/// <remarks>
	/// Deadwood and logs are drawn with FishMMO/Vegetation, which has no moss layer; they are not set here.
	/// </remarks>
	public static class MossAmounts
	{
		/// <summary>A rock type not named below (a type added later): an ordinary stone's share.</summary>
		public const float DefaultRock = 0.6f;

		/// <summary>The moss a rock type's own material (its formations, cliffs and the caves cut in it) may grow.</summary>
		public static float ForRockType(string typeName)
		{
			switch (typeName)
			{
				// Lime and porous stone: the mossiest walls and outcrops there are.
				case "Limestone": return 0.9f;
				case "Tuff": return 0.8f;
				case "Sandstone": return 0.75f;
				case "Granite": return 0.7f;
				case "Conglomerate": return 0.7f;
				case "Shale": return 0.65f;
				case "Schist": return 0.65f;
				case "Gneiss": return 0.65f;
				case "Basalt": return 0.6f;
				case "Andesite": return 0.6f;
				case "Slate": return 0.6f;
				case "Breccia": return 0.6f;
				case "Chalk": return 0.55f;
				case "Marble": return 0.5f;
				case "Anorthosite": return 0.5f;
				// Hard and smooth, or crumbling and dry-draining: a little.
				case "Quartzite": return 0.4f;
				case "Pumice": return 0.35f;
				case "Scoria": return 0.35f;
				case "Termitaria": return 0.15f;
				case "Sinter": return 0.1f;
				case "Gypsum": return 0.1f;
				case "Obsidian": return 0.1f;
				case "DarkLag": return 0.05f;
				// Salt, sulphur and ore poison it; snow and ice have nothing to hold to.
				case "Halite":
				case "Sulphur":
				case "Sulphide":
				case "Pyrite":
				case "Firn":
				case "WaterIce":
					return 0f;
				default: return DefaultRock;
			}
		}

		/// <summary>The moss a legacy small-rock material (boulders, rocks, pebbles: ProceduralArtCatalogue.AllRockMaterials) may grow.</summary>
		public static float ForLegacyRock(string legacyName)
		{
			switch (legacyName)
			{
				case "Grey": return ForRockType("Granite");
				case "Sandstone": return ForRockType("Sandstone");
				case "Basalt": return ForRockType("Basalt");
				case "Limestone": return ForRockType("Limestone");
				// The sea floor's nodules and coral live under water, and ice holds none.
				case "Nodule":
				case "Coral":
				case "Ice":
					return 0f;
				default: return DefaultRock;
			}
		}

		/// <summary>
		/// The moss a structure kit material may grow. The Mossy finish's albedo already has moss baked in
		/// (StructureSurfaces.Finish); it takes more of the layer on top, so an old graveyard's stones stay the heaviest
		/// in any scene, and a dry scene still shows its baked green while a damp one grows it further. A charred surface
		/// grows almost none; the algae finish's half-sunk sites keep some above their waterline.
		/// </summary>
		public static float ForStructure(StructureMaterial material, StructureFinish finish)
		{
			float amount;
			switch (material)
			{
				case StructureMaterial.Fieldstone: amount = 0.75f; break;
				case StructureMaterial.Monolith: amount = 0.7f; break;
				case StructureMaterial.Thatch: amount = 0.7f; break;
				case StructureMaterial.Stone: amount = 0.6f; break;
				case StructureMaterial.Timber: amount = 0.55f; break;
				case StructureMaterial.Earth: amount = 0.35f; break;
				case StructureMaterial.Plaster: amount = 0.3f; break;
				case StructureMaterial.Bone: amount = 0.2f; break;
				// Cloth, iron, ash and glass: none.
				default: amount = 0f; break;
			}
			switch (finish)
			{
				case StructureFinish.Mossy: return amount > 0f ? System.Math.Min(1f, amount + 0.35f) : 0f;
				case StructureFinish.Charred: return amount * 0.2f;
				case StructureFinish.Algae: return amount * 0.6f;
				default: return amount;
			}
		}
	}
}
#endif
