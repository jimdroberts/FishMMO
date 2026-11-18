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
			// Glacier (biome round, 2026-10-26): the Himalayan birch at the tree line, snow-pressed and short.
			{ "Bhojpatra", (new Vector2(5f, 15f), new Vector2(0.12f, 0.35f), new Vector2(1.5f, 4.5f)) },    // Betula utilis
			// Forest (biome round, 2026-10-26).
			{ "BeechForest", (new Vector2(30f, 45f), new Vector2(0.5f, 1f), new Vector2(7f, 13f)) },       // primeval Fagus sylvatica
			{ "OakForest", (new Vector2(22f, 35f), new Vector2(0.7f, 1.4f), new Vector2(7f, 13f)) },       // veteran Quercus petraea
			{ "Lime", (new Vector2(20f, 35f), new Vector2(0.4f, 0.9f), new Vector2(5f, 10f)) },             // Tilia cordata
			{ "Hornbeam", (new Vector2(15f, 28f), new Vector2(0.25f, 0.6f), new Vector2(3f, 6f)) },         // Carpinus betulus
			{ "SilverFir", (new Vector2(30f, 50f), new Vector2(0.4f, 0.9f), new Vector2(3f, 6f)) },         // Abies alba
			{ "Yew", (new Vector2(8f, 20f), new Vector2(0.4f, 1.2f), new Vector2(3.5f, 7f)) },              // Taxus baccata
			{ "Ash", (new Vector2(20f, 38f), new Vector2(0.3f, 0.8f), new Vector2(4f, 9f)) },               // Fraxinus excelsior
			// Woodland (biome round, 2026-10-26).
			{ "OakWoodland", (new Vector2(15f, 28f), new Vector2(0.5f, 1.1f), new Vector2(6f, 12f)) },     // open-grown Quercus robur
			{ "BirchWoodland", (new Vector2(14f, 25f), new Vector2(0.12f, 0.25f), new Vector2(2f, 4.5f)) }, // Betula pendula
			{ "SweetChestnut", (new Vector2(18f, 32f), new Vector2(0.6f, 1.3f), new Vector2(6f, 11f)) },   // Castanea sativa
			{ "FieldMaple", (new Vector2(8f, 20f), new Vector2(0.2f, 0.5f), new Vector2(3f, 6f)) },         // Acer campestre
			{ "WildCherry", (new Vector2(15f, 28f), new Vector2(0.2f, 0.5f), new Vector2(3f, 6f)) },        // Prunus avium
			{ "CrabApple", (new Vector2(5f, 11f), new Vector2(0.15f, 0.4f), new Vector2(2.5f, 5f)) },       // Malus sylvestris
			// Alpine Meadow (biome round, 2026-10-26).
			{ "Cembra", (new Vector2(12f, 25f), new Vector2(0.3f, 0.8f), new Vector2(2.5f, 5f)) },          // Pinus cembra
			{ "FlagSpruce", (new Vector2(8f, 18f), new Vector2(0.2f, 0.5f), new Vector2(1.5f, 3.5f)) },     // treeline Picea abies
			{ "SnagCembra", (new Vector2(4f, 10f), new Vector2(0.25f, 0.6f), new Vector2(0.8f, 3f)) },      // a dead stone pine
			// Jungle + Bamboo Forest (biome rounds, 2026-10-26).
			{ "Iroko", (new Vector2(40f, 60f), new Vector2(0.7f, 1.4f), new Vector2(10f, 18f)) },           // Milicia excelsa
			{ "Obeche", (new Vector2(40f, 55f), new Vector2(0.6f, 1.2f), new Vector2(9f, 16f)) },           // Triplochiton scleroxylon
			{ "AfricanMahogany", (new Vector2(35f, 50f), new Vector2(0.6f, 1.1f), new Vector2(8f, 15f)) },  // Khaya
			{ "ForestCanopy", (new Vector2(25f, 40f), new Vector2(0.4f, 0.8f), new Vector2(6f, 11f)) },     // a mixed rainforest canopy tree
			{ "Kola", (new Vector2(12f, 22f), new Vector2(0.25f, 0.5f), new Vector2(4f, 9f)) },             // Cola nitida
			{ "UmbrellaTree", (new Vector2(14f, 25f), new Vector2(0.18f, 0.4f), new Vector2(6f, 12f)) },    // Musanga cecropioides
			{ "OilPalm", (new Vector2(12f, 25f), new Vector2(0.2f, 0.4f), new Vector2(4f, 8f)) },           // Elaeis guineensis
			{ "RaffiaPalm", (new Vector2(8f, 16f), new Vector2(0.25f, 0.5f), new Vector2(4f, 8f)) },        // Raphia hookeri
			{ "Plantain", (new Vector2(4f, 8f), new Vector2(0.08f, 0.2f), new Vector2(1.5f, 4f)) },         // Musa x paradisiaca
			{ "Moso", (new Vector2(14f, 22f), new Vector2(0.06f, 0.12f), new Vector2(1.5f, 3.5f)) },        // Phyllostachys edulis
			{ "Madake", (new Vector2(10f, 16f), new Vector2(0.04f, 0.09f), new Vector2(1.5f, 3.5f)) },      // Phyllostachys bambusoides
			{ "Sugi", (new Vector2(30f, 50f), new Vector2(0.5f, 1.0f), new Vector2(2.5f, 5f)) },            // Cryptomeria japonica
			{ "Akamatsu", (new Vector2(16f, 28f), new Vector2(0.25f, 0.5f), new Vector2(3f, 6f)) },         // Pinus densiflora
			{ "IrohaMomiji", (new Vector2(6f, 14f), new Vector2(0.12f, 0.3f), new Vector2(3f, 6f)) },       // Acer palmatum
			{ "Yamazakura", (new Vector2(12f, 22f), new Vector2(0.2f, 0.45f), new Vector2(3.5f, 7f)) },     // Prunus jamasakura
			{ "Shii", (new Vector2(16f, 28f), new Vector2(0.4f, 0.9f), new Vector2(5f, 10f)) },             // Castanopsis sieboldii
			{ "Ginkgo", (new Vector2(20f, 34f), new Vector2(0.5f, 1.1f), new Vector2(5f, 10f)) },           // Ginkgo biloba
			// Mountain Slope (biome round, 2026-10-26).
			{ "Podocarpus", (new Vector2(30f, 45f), new Vector2(0.5f, 1f), new Vector2(5f, 9f)) },          // Podocarpus latifolius
			{ "Hagenia", (new Vector2(12f, 22f), new Vector2(0.35f, 0.8f), new Vector2(5f, 9f)) },          // Hagenia abyssinica
			{ "GiantHeather", (new Vector2(5f, 12f), new Vector2(0.15f, 0.4f), new Vector2(1.2f, 3f)) },    // Erica trimera
			{ "GiantGroundsel", (new Vector2(3f, 8f), new Vector2(0.15f, 0.4f), new Vector2(1f, 3f)) },     // Dendrosenecio
			{ "PencilCedar", (new Vector2(20f, 40f), new Vector2(0.35f, 0.9f), new Vector2(3f, 6f)) },      // Juniperus procera
			{ "Candelabra", (new Vector2(6f, 15f), new Vector2(0.25f, 0.6f), new Vector2(0.8f, 2.5f)) },    // Euphorbia candelabrum
			{ "Warka", (new Vector2(18f, 35f), new Vector2(1f, 2.5f), new Vector2(10f, 20f)) },             // Ficus vasta
			{ "CloudTreeFern", (new Vector2(6f, 15f), new Vector2(0.08f, 0.25f), new Vector2(2f, 4.5f)) },  // Cyathea manniana
			{ "SnagHeather", (new Vector2(2f, 6f), new Vector2(0.08f, 0.3f), new Vector2(0.5f, 2f)) },      // a dead giant heather
			// Scree (biome round, 2026-10-26).
			{ "KoreanFir", (new Vector2(8f, 18f), new Vector2(0.2f, 0.5f), new Vector2(1.2f, 3.5f)) },      // Abies koreana
			{ "GhostFir", (new Vector2(6f, 14f), new Vector2(0.15f, 0.4f), new Vector2(0.6f, 2.5f)) },      // a standing dead fir
			{ "ThousandYew", (new Vector2(5f, 15f), new Vector2(0.5f, 1.2f), new Vector2(2.5f, 6f)) },      // old Taxus cuspidata
			// Scrubland (biome round, 2026-10-26).
			{ "PaloVerde", (new Vector2(5f, 9f), new Vector2(0.1f, 0.3f), new Vector2(2f, 4.5f)) },          // Parkinsonia microphylla
			{ "PaloVerdeBlue", (new Vector2(6f, 12f), new Vector2(0.15f, 0.4f), new Vector2(2.5f, 5f)) },    // Parkinsonia florida
			{ "Ironwood", (new Vector2(5f, 10f), new Vector2(0.2f, 0.5f), new Vector2(2.5f, 5f)) },          // Olneya tesota
			{ "MesquiteVelvet", (new Vector2(5f, 12f), new Vector2(0.15f, 0.4f), new Vector2(3f, 6.5f)) },  // Prosopis velutina
			{ "Whitethorn", (new Vector2(2.5f, 6f), new Vector2(0.05f, 0.18f), new Vector2(1.2f, 3.5f)) },  // Vachellia constricta
			{ "DesertWillow", (new Vector2(4f, 9f), new Vector2(0.08f, 0.25f), new Vector2(1.5f, 3.5f)) },  // Chilopsis linearis
			{ "GooddingWillow", (new Vector2(10f, 20f), new Vector2(0.3f, 0.7f), new Vector2(3f, 6.5f)) },  // Salix gooddingii
			{ "ElephantTree", (new Vector2(2.5f, 6f), new Vector2(0.2f, 0.5f), new Vector2(1.2f, 3f)) },     // Bursera microphylla
			// Savanna (biome round, 2026-10-26).
			{ "UmbrellaThorn", (new Vector2(6f, 14f), new Vector2(0.2f, 0.45f), new Vector2(4.5f, 9f)) },    // Vachellia tortilis
			{ "BaobabMapungubwe", (new Vector2(12f, 25f), new Vector2(1.5f, 3.5f), new Vector2(5f, 11f)) },  // Adansonia digitata
			{ "Mopane", (new Vector2(8f, 18f), new Vector2(0.2f, 0.5f), new Vector2(2f, 5f)) },              // Colophospermum mopane
			{ "Marula", (new Vector2(9f, 18f), new Vector2(0.3f, 0.7f), new Vector2(3.5f, 7.5f)) },          // Sclerocarya birrea
			{ "FeverTree", (new Vector2(12f, 25f), new Vector2(0.25f, 0.6f), new Vector2(4f, 8.5f)) },       // Vachellia xanthophloea
			{ "Jackalberry", (new Vector2(10f, 22f), new Vector2(0.35f, 0.8f), new Vector2(3.5f, 7.5f)) },   // Diospyros mespiliformis
			{ "Leadwood", (new Vector2(10f, 20f), new Vector2(0.3f, 0.7f), new Vector2(3f, 6.5f)) },         // Combretum imberbe
			{ "LeadwoodDead", (new Vector2(8f, 16f), new Vector2(0.25f, 0.6f), new Vector2(1.5f, 4.5f)) },   // a standing dead leadwood
			{ "SausageTree", (new Vector2(10f, 20f), new Vector2(0.3f, 0.7f), new Vector2(4f, 8f)) },        // Kigelia africana
			{ "TreeAloe", (new Vector2(3f, 7f), new Vector2(0.1f, 0.3f), new Vector2(0.8f, 2.5f)) },         // Aloe excelsa
			// Grassland (biome round, 2026-10-26): the lone wild pear, Turkey oak, white poplar and the ditches' white willow.
			{ "WildPear", (new Vector2(8f, 20f), new Vector2(0.2f, 0.6f), new Vector2(2f, 6f)) },            // Pyrus pyraster
			{ "TurkeyOak", (new Vector2(18f, 35f), new Vector2(0.4f, 1f), new Vector2(4f, 10f)) },           // Quercus cerris
			{ "WhitePoplar", (new Vector2(15f, 30f), new Vector2(0.35f, 1f), new Vector2(4f, 9f)) },         // Populus alba
			{ "WhiteWillow", (new Vector2(6f, 25f), new Vector2(0.25f, 0.8f), new Vector2(2.5f, 6f)) },      // Salix alba (lopped)
			// Plains (biome round, 2026-10-26): cottonwood, bur oak, Osage orange, eastern red cedar.
			{ "PlainsCottonwood", (new Vector2(20f, 35f), new Vector2(0.5f, 1.5f), new Vector2(6f, 14f)) },  // Populus deltoides
			{ "BurOak", (new Vector2(15f, 30f), new Vector2(0.4f, 1.2f), new Vector2(5f, 10f)) },            // Quercus macrocarpa
			{ "OsageOrange", (new Vector2(8f, 18f), new Vector2(0.2f, 0.6f), new Vector2(2.5f, 6f)) },       // Maclura pomifera
			{ "RedCedar", (new Vector2(6f, 18f), new Vector2(0.15f, 0.5f), new Vector2(1f, 3f)) },           // Juniperus virginiana
			// Steppe (biome round, 2026-10-26): the karagach elm and the saxaul.
			{ "SteppeElm", (new Vector2(5f, 15f), new Vector2(0.2f, 0.6f), new Vector2(2f, 5f)) },          // Ulmus pumila
			{ "Saxaul", (new Vector2(2.5f, 8f), new Vector2(0.1f, 0.4f), new Vector2(1f, 3f)) },              // Haloxylon aphyllum
			// Beach (biome round, 2026-10-26): the strand coconut and the wind-flagged dune pine.
			{ "StrandCoconut", (new Vector2(8f, 16f), new Vector2(0.12f, 0.22f), new Vector2(3f, 5f)) },     // Cocos nucifera on a strand
			{ "WindPine", (new Vector2(6f, 12f), new Vector2(0.15f, 0.3f), new Vector2(2f, 4f)) },           // Pinus sylvestris on a dune
			// Desert, Badlands and Salt Flat (biome rounds, 2026-10-26).
			{ "Balanites", (new Vector2(5f, 10f), new Vector2(0.15f, 0.35f), new Vector2(2f, 4.5f)) },        // Balanites aegyptiaca
			{ "Atil", (new Vector2(3f, 7f), new Vector2(0.1f, 0.3f), new Vector2(1.5f, 3.5f)) },              // Maerua crassifolia
			{ "Apricot", (new Vector2(5f, 10f), new Vector2(0.15f, 0.35f), new Vector2(2f, 4.5f)) },          // Prunus armeniaca
			{ "Oleaster", (new Vector2(4f, 9f), new Vector2(0.12f, 0.3f), new Vector2(1.8f, 4f)) },           // Elaeagnus angustifolia
			{ "OrientalPlane", (new Vector2(20f, 32f), new Vector2(0.7f, 1.4f), new Vector2(9f, 16f)) },      // Platanus orientalis
			{ "RockHackberry", (new Vector2(3f, 7f), new Vector2(0.1f, 0.25f), new Vector2(1.2f, 3f)) },      // Celtis tournefortii
			{ "Dobera", (new Vector2(6f, 10f), new Vector2(0.25f, 0.5f), new Vector2(3f, 6f)) },              // Dobera glabra
			// Hills (biome round, 2026-10-26): terrace olive, cork oak, downy oak, almond, stone pine and the chapel cypress.
			{ "OliveTerrace", (new Vector2(5f, 12f), new Vector2(0.3f, 0.9f), new Vector2(2.5f, 6f)) },     // an old split-boled terrace olive
			{ "CorkOak", (new Vector2(10f, 20f), new Vector2(0.4f, 1f), new Vector2(4f, 8f)) },              // Quercus suber
			{ "DownyOak", (new Vector2(10f, 22f), new Vector2(0.3f, 0.8f), new Vector2(3f, 7f)) },           // Quercus pubescens
			{ "Almond", (new Vector2(4f, 10f), new Vector2(0.12f, 0.35f), new Vector2(2f, 4f)) },            // Prunus dulcis
			{ "StonePine", (new Vector2(12f, 25f), new Vector2(0.3f, 0.8f), new Vector2(5f, 10f)) },         // Pinus pinea
			{ "CypressHills", (new Vector2(12f, 30f), new Vector2(0.2f, 0.45f), new Vector2(0.6f, 2f)) },    // Italian cypress, dark
			// Estuary, Wetlands and Mangrove (biome rounds, 2026-10-26): the terps' ash, the marsh's poplar and willow gallery, and the mangal.
			{ "TerpAsh", (new Vector2(8f, 20f), new Vector2(0.2f, 0.6f), new Vector2(2f, 6f)) },             // Fraxinus excelsior, wind-shorn
			{ "EuphratesPoplar", (new Vector2(8f, 20f), new Vector2(0.25f, 0.7f), new Vector2(2.5f, 6.5f)) },  // Populus euphratica
			{ "WillowGallery", (new Vector2(12f, 28f), new Vector2(0.3f, 0.8f), new Vector2(3f, 8f)) },       // Salix alba
			{ "Mkoko", (new Vector2(8f, 25f), new Vector2(0.15f, 0.5f), new Vector2(2f, 5f)) },               // Rhizophora mucronata
			{ "Mchu", (new Vector2(3f, 12f), new Vector2(0.15f, 0.5f), new Vector2(1.5f, 4.5f)) },            // Avicennia marina
			{ "Mpia", (new Vector2(8f, 20f), new Vector2(0.2f, 0.6f), new Vector2(3f, 7f)) },                 // Sonneratia alba
			{ "Muia", (new Vector2(10f, 30f), new Vector2(0.2f, 0.7f), new Vector2(2f, 6f)) },                // Bruguiera gymnorhiza
			{ "Mkandaa", (new Vector2(3f, 10f), new Vector2(0.06f, 0.2f), new Vector2(1f, 3f)) },             // Ceriops tagal
			{ "Mkomafi", (new Vector2(8f, 20f), new Vector2(0.2f, 0.6f), new Vector2(2.5f, 6f)) },            // Xylocarpus granatum
			{ "Msikundazi", (new Vector2(8f, 24f), new Vector2(0.2f, 0.7f), new Vector2(3f, 6.5f)) },         // Heritiera littoralis
			{ "Mkindu", (new Vector2(3f, 12f), new Vector2(0.08f, 0.25f), new Vector2(1f, 3f)) },             // Phoenix reclinata
			{ "Mkoma", (new Vector2(6f, 16f), new Vector2(0.12f, 0.35f), new Vector2(1.5f, 4f)) },            // Hyphaene compressa
			// River (biome round, 2026-10-26): the crack willow of the islets and margins.
			{ "CrackWillow", (new Vector2(6f, 18f), new Vector2(0.2f, 0.7f), new Vector2(2f, 6f)) },       // Salix x fragilis
		};
	}
}
