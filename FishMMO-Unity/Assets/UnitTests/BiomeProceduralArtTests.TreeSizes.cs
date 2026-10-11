using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The tree species' real mature sizes, which BiomeProceduralArtTests.TreeSpecies_StandAtTheirRealMatureSizes
	/// checks every catalogue species against. A file of its own so the tree package adds a species' size here
	/// without touching the spec-table tests (vegetation expansion, 2026-10-10): every species in
	/// ProceduralArtCatalogue.Trees needs an entry, and every entry a species.
	/// </summary>
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// Mature sizes, metres: what each species reaches in a stand, after the measured ranges of the
		/// real trees (Norway spruce, Scots pine, pedunculate oak, silver birch, a rainforest canopy tree,
		/// a snag, a coconut palm, a saguaro, an umbrella thorn, a clumping bamboo), and the species the vegetation
		/// expansion added, each named beside its entry.
		/// </summary>
		private static readonly Dictionary<string, (Vector2 height, Vector2 trunk, Vector2 crown)> RealSizes = new Dictionary<string, (Vector2, Vector2, Vector2)>
		{
			{ "Spruce", (new Vector2(22f, 35f), new Vector2(0.3f, 0.5f), new Vector2(2.2f, 4f)) },
			{ "Pine", (new Vector2(22f, 35f), new Vector2(0.3f, 0.5f), new Vector2(3f, 5f)) },
			{ "Oak", (new Vector2(18f, 30f), new Vector2(0.45f, 0.8f), new Vector2(5.5f, 10f)) },
			{ "Birch", (new Vector2(15f, 25f), new Vector2(0.12f, 0.22f), new Vector2(2.5f, 4.5f)) },
			{ "Jungle", (new Vector2(30f, 45f), new Vector2(0.55f, 0.95f), new Vector2(6f, 12f)) },
			{ "Dead", (new Vector2(10f, 18f), new Vector2(0.2f, 0.45f), new Vector2(2f, 5f)) },
			{ "Palm", (new Vector2(12f, 20f), new Vector2(0.14f, 0.25f), new Vector2(3.5f, 6f)) },
			{ "Saguaro", (new Vector2(8f, 12f), new Vector2(0.2f, 0.4f), new Vector2(0.6f, 1.5f)) },
			{ "Acacia", (new Vector2(6f, 12f), new Vector2(0.15f, 0.35f), new Vector2(3.5f, 7f)) },
			{ "Bamboo", (new Vector2(10f, 15f), new Vector2(0.05f, 0.08f), new Vector2(1.5f, 3.5f)) },
			// The vegetation expansion (2026-10-10). Height, trunk radius (above any buttress or swelling) and crown radius
			// of a mature tree of each real species in its habitat.
			{ "Larch", (new Vector2(20f, 40f), new Vector2(0.25f, 0.6f), new Vector2(2.5f, 5f)) },            // European larch
			{ "Beech", (new Vector2(25f, 40f), new Vector2(0.4f, 0.8f), new Vector2(6f, 11f)) },             // European beech
			{ "Alder", (new Vector2(15f, 25f), new Vector2(0.2f, 0.4f), new Vector2(3f, 5.5f)) },            // common alder
			{ "Olive", (new Vector2(6f, 12f), new Vector2(0.3f, 0.8f), new Vector2(3f, 6f)) },               // old olive
			{ "Baobab", (new Vector2(12f, 25f), new Vector2(1.5f, 3.5f), new Vector2(6f, 12f)) },            // African baobab
			{ "DatePalm", (new Vector2(15f, 25f), new Vector2(0.2f, 0.45f), new Vector2(3.5f, 6f)) },        // date palm
			{ "JoshuaTree", (new Vector2(5f, 15f), new Vector2(0.2f, 0.6f), new Vector2(2f, 5f)) },          // Yucca brevifolia
			{ "Mangrove", (new Vector2(6f, 20f), new Vector2(0.15f, 0.4f), new Vector2(2.5f, 6f)) },         // red mangrove
			{ "BaldCypress", (new Vector2(20f, 40f), new Vector2(0.4f, 1f), new Vector2(4f, 8f)) },          // Taxodium distichum
			{ "Kapok", (new Vector2(40f, 70f), new Vector2(0.8f, 1.6f), new Vector2(10f, 18f)) },           // Ceiba pentandra
			{ "TreeFern", (new Vector2(4f, 12f), new Vector2(0.1f, 0.25f), new Vector2(2f, 4f)) },          // Cyathea / Dicksonia
			{ "Pinyon", (new Vector2(5f, 15f), new Vector2(0.15f, 0.4f), new Vector2(2f, 4.5f)) },           // Pinus edulis
			{ "Aspen", (new Vector2(15f, 25f), new Vector2(0.12f, 0.3f), new Vector2(2f, 4f)) },             // quaking aspen
			{ "WeepingWillow", (new Vector2(12f, 25f), new Vector2(0.3f, 0.7f), new Vector2(4.5f, 9f)) },    // Salix babylonica
			{ "Poplar", (new Vector2(20f, 35f), new Vector2(0.4f, 0.9f), new Vector2(5f, 10f)) },            // black poplar / cottonwood
			{ "Cypress", (new Vector2(15f, 30f), new Vector2(0.2f, 0.45f), new Vector2(0.6f, 2f)) },         // Italian cypress
			{ "Banana", (new Vector2(3f, 8f), new Vector2(0.08f, 0.2f), new Vector2(1.5f, 3.5f)) },          // Musa
			{ "Bristlecone", (new Vector2(5f, 16f), new Vector2(0.4f, 1.2f), new Vector2(2f, 5f)) },         // Pinus longaeva
			{ "Mesquite", (new Vector2(4f, 10f), new Vector2(0.1f, 0.35f), new Vector2(2.5f, 6f)) },         // honey mesquite
			{ "LombardyPoplar", (new Vector2(18f, 32f), new Vector2(0.3f, 0.7f), new Vector2(1.2f, 3f)) },   // Populus nigra 'Italica'
			{ "HolmOak", (new Vector2(12f, 25f), new Vector2(0.3f, 0.8f), new Vector2(4f, 8f)) },            // Quercus ilex
			{ "DragonTree", (new Vector2(6f, 15f), new Vector2(0.3f, 0.8f), new Vector2(3f, 6f)) },          // Dracaena draco
			{ "Pandanus", (new Vector2(4f, 14f), new Vector2(0.1f, 0.3f), new Vector2(2f, 5f)) },            // Pandanus tectorius
			{ "Nipa", (new Vector2(4f, 9f), new Vector2(0.15f, 0.4f), new Vector2(1.5f, 4f)) },              // Nypa fruticans (frond height; the creeping stem's radius)
		};
	}
}
