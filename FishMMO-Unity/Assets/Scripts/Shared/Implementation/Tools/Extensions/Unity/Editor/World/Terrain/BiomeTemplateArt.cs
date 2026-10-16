#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a scene cut reads off a biome template for its ground, rock, ways and river bars, for the growth a point of
	/// interest takes from its biome, and for its vines and gathering resources: the template's own values, and the old code
	/// mapping only where a template has never been authored with them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The template decides (2026-10-15).</b> The cut once read these from the spec table (BiomeArtSpec: the biome's own
	/// rock, the rocks it accepts, each cliff's rock, a herb grove's flowers, a cave mouth's growth), from the main ground's
	/// generated path ground (SurfaceCatalogue.PathGroundFor / RoadGroundFor, the same flagstones for every biome's cobbles),
	/// and from two biomes by name (the desert's and the forest's riverbeds were every biome's river bars). Each is now a
	/// field on the template, which the biome art authoring fills with exactly what that code decided, so an authored
	/// template cuts the same scene, and a template edited by hand cuts what it says.
	/// </para>
	/// <para>
	/// <b>Fallbacks.</b> A template the authoring has not yet reached (an empty slot, an empty rock, a list it never wrote)
	/// still cuts as before: the value comes from the old mapping, and the cut warns once a session for each field, naming
	/// the first biome that fell back, so the gap is seen and closed by Author biome art rather than lived with. This
	/// class is the one place at cut time that still reads the spec table and the biome names, and only for those.
	/// </para>
	/// </remarks>
	public static class BiomeTemplateArt
	{
		// ── Fallback warnings ─────────────────────────────────────────

		private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
		private static readonly object gate = new object();

		/// <summary>
		/// Notes that a cut took a value from the old mapping because the biome's template lacks it: one warning a session
		/// for each field (the first biome to fall back is named; the rest are silent), and the field's name for a test.
		/// </summary>
		/// <returns>True when this call logged (the field's first fallback this session).</returns>
		public static bool Fallback(BiomeTemplate biome, string field, string instead)
		{
			lock (gate)
			{
				if (!warned.Add(field))
				{
					return false;
				}
			}
			Debug.LogWarning($"[Biome templates] '{(biome != null ? biome.name : "(no biome)")}' has no {field}, so the scene cut used {instead}. "
				+ "Run Biomes → Authoring → Author biome art to write it onto every template, then repaint or re-cut. "
				+ "Logged once a session for this field: other biomes may be falling back too.");
			return true;
		}

		/// <summary>Forgets which fields have warned, so the next fallback of each warns again (tests; a new session).</summary>
		public static void ForgetWarnings()
		{
			lock (gate)
			{
				warned.Clear();
			}
		}

		// ── Rock ──────────────────────────────────────────────────────

		/// <summary>
		/// A biome's own rock (BiomeTemplate.OwnRock): what its river boulders, fall ledges and point-of-interest stone are
		/// made of where the planet's geology is not one it accepts. Granite with no biome at all, as before.
		/// </summary>
		public static string OwnRock(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return CliffRocks.OwnRock((BiomeArtSpec.Entry)null);
			}
			if (!string.IsNullOrEmpty(biome.OwnRock))
			{
				return biome.OwnRock;
			}
			string own = CliffRocks.OwnRock(BiomeArtSpec.For(biome.name));
			Fallback(biome, "Own Rock", $"the spec table's ({own})");
			return own;
		}

		/// <summary>The planet-geology rocks a biome accepts besides its own (BiomeTemplate.AcceptedRocks). None with no biome.</summary>
		public static IReadOnlyList<string> AcceptedRocks(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return Array.Empty<string>();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.AcceptedRocks) || biome.AcceptedRocks != null && biome.AcceptedRocks.Count > 0)
			{
				return biome.AcceptedRocks ?? (IReadOnlyList<string>)Array.Empty<string>();
			}
			string[] rocks = BiomeArtSpec.For(biome.name)?.Rocks ?? Array.Empty<string>();
			Fallback(biome, "Accepted Rocks", $"the spec table's ({(rocks.Length > 0 ? string.Join(", ", rocks) : "none")})");
			return rocks;
		}

		/// <summary>
		/// A biome's rock rule, resolved once: the rock it stands in where the planet's geology is the given rock (a
		/// <see cref="RockTypes"/> name, or null where unknown): the geology's where the biome accepts it, else its own; ice
		/// stays ice (<see cref="CliffRocks.RockFor(string, IReadOnlyList{string}, string)"/>). Safe from several threads.
		/// </summary>
		public static Func<string, string> RockRule(BiomeTemplate biome)
		{
			string own = OwnRock(biome);
			IReadOnlyList<string> accepted = AcceptedRocks(biome);
			return geology => CliffRocks.RockFor(own, accepted, geology);
		}

		/// <summary>The rock a biome stands in where the geology is <paramref name="geology"/> (<see cref="RockRule"/>).</summary>
		public static string RockFor(BiomeTemplate biome, string geology) => RockRule(biome)(geology);

		/// <summary>
		/// The rock a palette cliff entry's faces stand in (its layer's <see cref="CliffTextureLayer.rockType"/>), or null
		/// where they stay painted terrain (<see cref="CliffTextureLayer.NoRock"/>, soil banks and sand).
		/// </summary>
		/// <param name="family">The ground the face is painted with, for a report: the layer's generated ground, else its name.</param>
		public static string CliffRock(SceneTerrainPalette.Entry entry, out string family)
		{
			family = GroundOf(entry?.Source) ?? $"{entry?.Biome?.ResolvedDisplayName} {entry?.Slot} (unknown family)";
			if (entry?.Source is CliffTextureLayer cliff && !string.IsNullOrEmpty(cliff.rockType))
			{
				return cliff.rockType == CliffTextureLayer.NoRock ? null : cliff.rockType;
			}
			// Never authored: the family the spec table gives the slot, else the layer's generated ground, as the rock it stands for.
			BiomeArtSpec.Entry spec = entry?.Biome != null ? BiomeArtSpec.For(entry.Biome.name) : null;
			string specFamily = GroundOf(entry?.Source);
			if (spec != null && entry.Slot != null && entry.Slot.StartsWith(SceneTerrainPalette.SlotCliff, StringComparison.Ordinal)
				&& int.TryParse(entry.Slot.Substring(SceneTerrainPalette.SlotCliff.Length), out int slot) && slot >= 0 && slot < spec.Cliffs.Length)
			{
				specFamily = spec.Cliffs[slot].Family;
			}
			string type = CliffRocks.RockTypeFor(specFamily, spec);
			family = specFamily ?? family;
			Fallback(entry?.Biome, "Rock Type on a cliff layer", $"the spec table's ({type ?? "plain terrain"} for {entry?.Slot})");
			return type;
		}

		/// <summary>
		/// The rock a cave of a form carved in a biome is cut in (BiomeTemplate.CaveShells), its formations included: a rock
		/// type name, <see cref="CliffRocks.Ice"/>, or <see cref="BiomeCaveShell.Ground"/> for the ground's own rock by the
		/// biome's rock rule (the caller resolves that where the cave stands). A template that has never been authored with
		/// its cave shells takes the spec table's (the form's old shell, unless the biome's spec names its own) and warns; one
		/// authored but holding no entry for the form, and a cave with no biome, take the form's old shell
		/// (<see cref="BiomeArtSpec.DefaultCaveShell"/>) silently.
		/// </summary>
		public static string CaveShell(BiomeTemplate biome, CaveForm form)
		{
			string own = biome != null ? biome.CaveShellFor((BiomeCaveForm)form) : null;
			if (own != null)
			{
				return own;
			}
			if (biome == null || biome.IsAuthored(BiomeTemplate.Authored.CaveShells))
			{
				return BiomeArtSpec.DefaultCaveShell(form);
			}
			string shell = BiomeArtSpec.For(biome.name)?.CaveShellFor(form) ?? BiomeArtSpec.DefaultCaveShell(form);
			Fallback(biome, "Cave Shells", $"the spec table's ({shell} for a {form})");
			return shell;
		}

		// ── Water and ice roles ───────────────────────────────────────

		/// <summary>
		/// The names the cut found its water and ice biomes by until the templates carried the role (2026-10-15): the fallback
		/// for a role no registered template holds yet.
		/// </summary>
		public static string LegacyWaterBiomeName(BiomeWaterRole role)
		{
			switch (role)
			{
				case BiomeWaterRole.Lake: return "Lake";
				case BiomeWaterRole.MethaneLake: return "Methane Lake";
				case BiomeWaterRole.River: return "River";
				case BiomeWaterRole.Estuary: return "Estuary";
				case BiomeWaterRole.SaltFlat: return "Salt Flat";
				case BiomeWaterRole.IceSheet: return "Ice Sheet";
				default: return null;
			}
		}

		/// <summary>
		/// The biome that plays a part for the scene's water or ice (BiomeTemplate.WaterRole,
		/// <see cref="BiomeRegistry.WithWaterRole"/>), or null where none does. While no registered template holds the role,
		/// the biome the cut once found by name (<see cref="LegacyWaterBiomeName"/>) stands in, with a warning, unless the
		/// authoring has written that template's role already (then its None is a decision: nothing plays the part).
		/// </summary>
		public static BiomeTemplate WaterBiome(BiomeWaterRole role)
		{
			BiomeTemplate holder = BiomeRegistry.WithWaterRole(role);
			if (holder != null)
			{
				return holder;
			}
			string name = LegacyWaterBiomeName(role);
			BiomeTemplate named = name != null ? BiomeRegistry.Get(name) : null;
			if (named == null || named.IsAuthored(BiomeTemplate.Authored.WaterRole))
			{
				return null;
			}
			Fallback(named, $"Water Role {role} (no registered template holds it)", $"the biome named '{name}'");
			return named;
		}

		/// <summary>The generated ground a texture layer holds (its <c>Ground_&lt;Family&gt;</c> terrain layer's family), or null.</summary>
		public static string GroundOf(TerrainTextureLayer layer)
		{
			string name = layer?.terrainLayer != null ? layer.terrainLayer.name : null;
			const string prefix = "Ground_";
			return name != null && name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : null;
		}

		// ── Ways and river bars ───────────────────────────────────────

		/// <summary>
		/// A biome's way and river-bar grounds as the cut paints them: its template's slots (Small Path, Road, Cobbles, Bar
		/// Sand, Bar Gravel), and for a slot it has never been given, what the cut used before the template carried them. One
		/// per cut: it keeps the generated layers it loads for those fallbacks.
		/// </summary>
		public sealed class Grounds
		{
			private readonly Dictionary<string, TerrainTextureLayer> generated = new Dictionary<string, TerrainTextureLayer>(StringComparer.Ordinal);
			private TerrainTextureLayer legacySand, legacyGravel;
			private bool legacyBarsRead;

			/// <summary>The ground a footpath or trail wears to.</summary>
			public TerrainTextureLayer Path(BiomeTemplate biome) => Slot(biome, biome?.SmallPathTextureLayer, "Small Path layer",
				() => Generated(SurfaceCatalogue.PathGroundFor(PaintedGround(biome))) ?? Generated(Ground.Soil), "its painted ground's path ground");

			/// <summary>The ground a road or cart track wears to.</summary>
			public TerrainTextureLayer Road(BiomeTemplate biome) => Slot(biome, biome?.RoadTextureLayer, "Road layer",
				() => Generated(SurfaceCatalogue.RoadGroundFor(PaintedGround(biome))) ?? Generated(Ground.Gravel), "its painted ground's road ground");

			/// <summary>The paving of a way's cobbled stretches.</summary>
			public TerrainTextureLayer Cobbles(BiomeTemplate biome) => Slot(biome, biome?.CobbleTextureLayer, "Cobbles layer",
				() => Generated(Ground.Flagstone), "the generated flagstones");

			/// <summary>The sand on a river's slow bars.</summary>
			public TerrainTextureLayer BarSand(BiomeTemplate biome) => Slot(biome, biome?.BarSandTextureLayer, "Bar Sand layer",
				() => { ReadLegacyBars(); return legacySand; }, "the Desert biome's riverbed");

			/// <summary>The gravel on a river's quick bars.</summary>
			public TerrainTextureLayer BarGravel(BiomeTemplate biome) => Slot(biome, biome?.BarGravelTextureLayer, "Bar Gravel layer",
				() => { ReadLegacyBars(); return legacyGravel; }, "the Forest biome's riverbed");

			private static TerrainTextureLayer Slot(BiomeTemplate biome, TerrainTextureLayer slot, string field, Func<TerrainTextureLayer> fallback, string instead)
			{
				if (slot != null && slot.HasAlbedo)
				{
					return slot;
				}
				TerrainTextureLayer old = fallback();
				Fallback(biome, field, instead);
				return old;
			}

			// The sand and gravel every biome's bars were painted with until the template carried its own.
			private void ReadLegacyBars()
			{
				if (legacyBarsRead)
				{
					return;
				}
				legacyBarsRead = true;
				legacySand = BiomeRegistry.Get("Desert")?.RiverbedTextureLayer;
				legacyGravel = BiomeRegistry.Get("Forest")?.RiverbedTextureLayer;
			}

			private TerrainTextureLayer Generated(string ground)
			{
				if (!generated.TryGetValue(ground, out TerrainTextureLayer layer))
				{
					var terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ProceduralArtCatalogue.GroundLayerPath(ground));
					layer = terrainLayer != null && terrainLayer.diffuseTexture != null
						? new TerrainTextureLayer
						{
							terrainLayer = terrainLayer,
							albedoTexture = terrainLayer.diffuseTexture,
							normalTexture = terrainLayer.normalMapTexture,
							maskTexture = terrainLayer.maskMapTexture,
							tileSize = terrainLayer.tileSize,
						}
						: null;
					generated[ground] = layer;
				}
				return layer;
			}

			// The ground family a biome is painted with: its main layer's, else its first detail's, else grass.
			private static string PaintedGround(BiomeTemplate biome)
			{
				if (biome == null)
				{
					return Ground.Grass;
				}
				string ground = GroundOf(biome.MainTextureLayer);
				if (ground == null && biome.DetailTextureLayers != null)
				{
					foreach (TerrainTextureLayer detail in biome.DetailTextureLayers)
					{
						ground = GroundOf(detail);
						if (ground != null)
						{
							break;
						}
					}
				}
				return ground ?? Ground.Grass;
			}
		}

		// ── Point-of-interest growth ──────────────────────────────────

		/// <summary>The flowers a herb grove grows between its beds in a biome (BiomeTemplate.HerbGroveFlowers).</summary>
		public static List<GameObject> GroveFlowers(BiomeTemplate biome)
		{
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.HerbGroveFlowers) || Any(biome.HerbGroveFlowers)))
			{
				return Distinct(biome.HerbGroveFlowers);
			}
			List<string> names = biome != null ? BiomeArtSpec.For(biome.name)?.GroveFlowers() : null;
			if (biome != null)
			{
				Fallback(biome, "Herb Grove Flowers", "the spec table's flower rules");
			}
			return PointOfInterestHerbGrove.Load(names ?? new List<string> { BiomeArtSpec.MeadowFlowers });
		}

		/// <summary>
		/// The herbs and fungi a herb grove grows its beds of in a biome (BiomeTemplate.HerbGroveHerbs). With no biome, common
		/// herbs, as before; a template never authored with them takes the spec table's (the wild kin of the biome's gathered
		/// herbs, as the grove found them by the biome's name) and warns.
		/// </summary>
		public static List<GameObject> GroveHerbs(BiomeTemplate biome)
		{
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.HerbGroveHerbs) || Any(biome.HerbGroveHerbs)))
			{
				return Distinct(biome.HerbGroveHerbs);
			}
			List<string> names;
			if (biome == null)
			{
				names = new List<string>(BiomeArtSpec.CommonHerbs);
			}
			else
			{
				names = BiomeArtSpec.For(biome.name)?.GroveHerbs() ?? BiomeArtSpec.HerbsOf(Gathering(biome));
				Fallback(biome, "Herb Grove Herbs", "the spec table's (the wild kin of its gathered herbs)");
			}
			return PointOfInterestHerbGrove.Load(names);
		}

		// ── Gathering and vines (2026-10-16) ──────────────────────────

		/// <summary>
		/// The gathering resources (node names) a biome's gathering sites draw from (BiomeTemplate.GatheringResources). None
		/// with no biome (a site there draws from its whole family, as before); a template never authored with them takes
		/// the catalogue's rows that name the biome and warns.
		/// </summary>
		public static IReadOnlyList<string> Gathering(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return Array.Empty<string>();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.GatheringResources) || biome.GatheringResources != null && biome.GatheringResources.Count > 0)
			{
				return biome.GatheringResources ?? (IReadOnlyList<string>)Array.Empty<string>();
			}
			List<string> nodes = BiomeArtSpec.For(biome.name)?.GatheringResources() ?? CatalogueRowsNaming(biome.name);
			Fallback(biome, "Gathering Resources", "the gathering catalogue's rows that name it");
			return nodes;
		}

		// The catalogue's own answer for a biome the spec table has no entry for: the rows that name it (or name none).
		private static List<string> CatalogueRowsNaming(string biomeName)
		{
			var nodes = new List<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				if (GatheringResourceCatalogue.GrowsIn(resource, biomeName) && !nodes.Contains(resource.Node))
				{
					nodes.Add(resource.Node);
				}
			}
			return nodes;
		}

		/// <summary>
		/// The vines a biome grows (BiomeTemplate.Vines), in the form the vine rules read (<see cref="VineBiomes.Habitat"/>), or
		/// null where it grows none (no species), as for a biome the vine table never named. A template never authored with
		/// them takes the vine table's row (by its name) and warns. A new object each call: callers placing many vines keep it.
		/// </summary>
		public static VineBiomes.Habitat Vines(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			BiomeVines vines = biome.Vines;
			if (!biome.IsAuthored(BiomeTemplate.Authored.Vines) && (vines == null || vines.IsEmpty))
			{
				VineBiomes.Habitat row = VineBiomes.For(biome.name);
				Fallback(biome, "Vines", $"the vine table's row ({(row != null ? "by its name" : "none")})");
				return row;
			}
			return HabitatOf(biome.name, vines);
		}

		/// <summary>A template's vines as the vine rules read them; null where no species grows.</summary>
		public static VineBiomes.Habitat HabitatOf(string biomeName, BiomeVines vines)
		{
			if (vines == null || !vines.Grows)
			{
				return null;
			}
			var species = new List<(string, float)>();
			foreach (BiomeVineSpecies s in vines.Species)
			{
				if (!string.IsNullOrEmpty(s.Species))
				{
					species.Add((s.Species, s.Weight));
				}
			}
			return new VineBiomes.Habitat
			{
				Biome = biomeName,
				Caves = vines.Caves,
				Ruins = vines.Ruins,
				Cliffs = vines.Cliffs,
				Trees = vines.Trees,
				Species = species.ToArray(),
			};
		}

		// ── Templates by id ───────────────────────────────────────────

		private static Dictionary<int, BiomeTemplate> projectTemplates;

		/// <summary>
		/// A biome template by its cached-object id (what a scene's biome map stores): the registry's, else, where the registry
		/// does not hold it (a batch run, a template loaded since), the project's template whose id it is. Null for none.
		/// </summary>
		public static BiomeTemplate TemplateById(int id)
		{
			if (id == 0)
			{
				return null;
			}
			if (BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) && biome != null)
			{
				return biome;
			}
			if (projectTemplates == null || projectTemplates.TryGetValue(id, out biome) && biome == null)
			{
				projectTemplates = new Dictionary<int, BiomeTemplate>();
				foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
				{
					var template = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
					if (template != null)
					{
						projectTemplates[BiomeRegistry.IDOf(template)] = template;
					}
				}
			}
			return projectTemplates.TryGetValue(id, out biome) ? biome : null;
		}

		/// <summary>What grows and lies round a natural cave's mouth in a biome, as prefabs.</summary>
		public sealed class CaveMouthArt
		{
			public List<GameObject> Bushes = new List<GameObject>();
			public List<GameObject> Ferns = new List<GameObject>();
			public GameObject Creeper;
			public List<GameObject> Boulders = new List<GameObject>();
			public List<GameObject> Logs = new List<GameObject>();
		}

		/// <summary>
		/// A cave mouth's growth in a biome (BiomeTemplate.CaveMouthBushes, Ferns, Creeper, Boulders, Logs). With no biome,
		/// a woodland's ferns and bushes and the ivy, as before.
		/// </summary>
		public static CaveMouthArt CaveMouth(BiomeTemplate biome)
		{
			BiomeArtSpec.CaveMouthGrowth spec = null;
			BiomeArtSpec.CaveMouthGrowth Spec()
			{
				if (spec == null)
				{
					BiomeArtSpec.Entry entry = biome != null ? BiomeArtSpec.For(biome.name) : null;
					spec = entry != null ? entry.CaveMouth() : new BiomeArtSpec.Entry().CaveMouth();
				}
				return spec;
			}
			List<GameObject> Prefabs(string field, List<GameObject> own, Func<BiomeArtSpec.CaveMouthGrowth, List<string>> old, string label)
			{
				if (biome != null && (biome.IsAuthored(field) || Any(own)))
				{
					return Distinct(own);
				}
				if (biome != null)
				{
					Fallback(biome, label, "the spec table's rules");
				}
				return PointOfInterestHerbGrove.Load(old(Spec()));
			}
			var art = new CaveMouthArt
			{
				Bushes = Prefabs(BiomeTemplate.Authored.CaveMouthBushes, biome?.CaveMouthBushes, g => g.Bushes, "Cave Mouth Bushes"),
				Ferns = Prefabs(BiomeTemplate.Authored.CaveMouthFerns, biome?.CaveMouthFerns, g => g.Ferns, "Cave Mouth Ferns"),
				Boulders = Prefabs(BiomeTemplate.Authored.CaveMouthBoulders, biome?.CaveMouthBoulders, g => g.Boulders, "Cave Mouth Boulders"),
				Logs = Prefabs(BiomeTemplate.Authored.CaveMouthLogs, biome?.CaveMouthLogs, g => g.Logs, "Cave Mouth Logs"),
			};
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.CaveMouthCreeper) || biome.CaveMouthCreeper != null))
			{
				art.Creeper = biome.CaveMouthCreeper;
			}
			else
			{
				if (biome != null)
				{
					Fallback(biome, "Cave Mouth Creeper", "the ivy every cave mouth grew");
				}
				string creeper = Spec().Creeper;
				List<GameObject> loaded = creeper != null ? PointOfInterestHerbGrove.Load(new List<string> { creeper }) : null;
				art.Creeper = loaded != null && loaded.Count > 0 ? loaded[0] : null;
			}
			return art;
		}

		private static bool Any(List<GameObject> prefabs)
		{
			if (prefabs != null)
			{
				foreach (GameObject prefab in prefabs)
				{
					if (prefab != null)
					{
						return true;
					}
				}
			}
			return false;
		}

		// The template's list without its gaps or repeats, in its order: what the old by-name load gave.
		private static List<GameObject> Distinct(List<GameObject> prefabs)
		{
			var list = new List<GameObject>();
			if (prefabs != null)
			{
				foreach (GameObject prefab in prefabs)
				{
					if (prefab != null && !list.Contains(prefab))
					{
						list.Add(prefab);
					}
				}
			}
			return list;
		}
	}
}
#endif
