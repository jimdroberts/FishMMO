#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a scene cut reads off a biome template for its ground, rock, ways and river bars, for the growth a point of
	/// interest takes from its biome, for its vines and gathering resources, and for the points of interest it hosts and how
	/// they are built (kinds, template variants, weathering, crops, structure palette, moat water, street lights): the template's own values, and the old
	/// code mapping only where a template has never been authored with them.
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
		/// Sand, Bar Gravel), and for a slot it has never been given, what the cut used before the template carried them; and
		/// the ways its ground layers carry of their own (<see cref="LayerWays"/>). One per cut: it keeps the generated layers
		/// it loads for those fallbacks.
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

			/// <summary>
			/// The ways a biome's ground layers carry of their own (<see cref="TerrainTextureLayer.ways"/>, decision 19,
			/// 2026-10-16): for its main layer and each detail layer that is painted, every way given a ground there, keyed
			/// <see cref="BiomeLocalArt.WaySlot"/> (<c>detail/2/road</c>), as a texture layer for the palette to carry beside the
			/// biome's own way grounds. Empty where none is set: such a ground's ways are the biome's (<see cref="Path"/>,
			/// <see cref="Road"/>, <see cref="Cobbles"/>), as every ground's were before, which is no fallback but the field's
			/// meaning, so nothing is logged.
			/// </summary>
			public List<(string slot, TerrainTextureLayer source)> LayerWays(BiomeTemplate biome)
			{
				var ways = new List<(string, TerrainTextureLayer)>();
				if (biome == null)
				{
					return ways;
				}
				AddWays(ways, BiomeLocalArt.SlotMain, biome.MainTextureLayer);
				if (biome.DetailTextureLayers != null)
				{
					for (int i = 0; i < biome.DetailTextureLayers.Count; i++)
					{
						AddWays(ways, BiomeLocalArt.DetailSlot(i), biome.DetailTextureLayers[i]);
					}
				}
				return ways;
			}

			private void AddWays(List<(string, TerrainTextureLayer)> ways, string groundSlot, TerrainTextureLayer ground)
			{
				// A ground the biome does not paint (no art) has no pixels for its ways to cross.
				if (ground == null || !ground.HasAlbedo || ground.ways.IsEmpty)
				{
					return;
				}
				foreach (string way in BiomeLocalArt.WaySlots)
				{
					TerrainTextureLayer layer = Wrapped(BiomeLocalArt.WayGround(ground.ways, way));
					if (layer != null)
					{
						ways.Add((BiomeLocalArt.WaySlot(groundSlot, way), layer));
					}
				}
			}

			private readonly Dictionary<TerrainLayer, TerrainTextureLayer> wrapped = new Dictionary<TerrainLayer, TerrainTextureLayer>();

			// A way's terrain layer as the texture layer a palette entry is sourced from: one per layer a cut, so two grounds
			// (or biomes) naming the same corduroy share it.
			private TerrainTextureLayer Wrapped(TerrainLayer terrainLayer)
			{
				if (terrainLayer == null)
				{
					return null;
				}
				if (!wrapped.TryGetValue(terrainLayer, out TerrainTextureLayer layer))
				{
					layer = AsTextureLayer(terrainLayer);
					wrapped[terrainLayer] = layer;
				}
				return layer;
			}

			// A terrain layer with art as a texture layer carrying it whole; null without art.
			private static TerrainTextureLayer AsTextureLayer(TerrainLayer terrainLayer)
			{
				return terrainLayer != null && terrainLayer.diffuseTexture != null
					? new TerrainTextureLayer
					{
						terrainLayer = terrainLayer,
						albedoTexture = terrainLayer.diffuseTexture,
						normalTexture = terrainLayer.normalMapTexture,
						maskTexture = terrainLayer.maskMapTexture,
						tileSize = terrainLayer.tileSize,
					}
					: null;
			}

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
					layer = AsTextureLayer(AssetDatabase.LoadAssetAtPath<TerrainLayer>(ProceduralArtCatalogue.GroundLayerPath(ground)));
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

		// ── Points of interest and structures (2026-10-16, phase I5) ──

		/// <summary>
		/// The point-of-interest kinds sited on a biome's ground and how gladly (BiomeTemplate.SiteKinds), as the planner reads
		/// them (PointOfInterestBiome.Kinds): the first entry for each kind, never below 0; a kind not listed is never sited
		/// there. Null with no biome (every kind at its row's default weight, as before). A template never authored with them
		/// takes the weight the cut's catalogue rows give it by its name (<paramref name="rules"/>, else the code's) and warns.
		/// </summary>
		public static IReadOnlyDictionary<POIType, float> SiteKinds(BiomeTemplate biome, PointOfInterestRules rules)
		{
			return SiteKinds(biome, rules, true);
		}

		/// <summary>
		/// <see cref="SiteKinds(BiomeTemplate, PointOfInterestRules)"/> on a world whose air is or is not breathable
		/// (BiomeAir.IsBreathable, decision 39, 2026-10-18): where it is not, every kind whose entry needs breathable air
		/// (<see cref="BiomeSiteKind.NeedsBreathableAir"/>) weighs 0 there, so only natural sites and relics are placed. A template
		/// never authored with its kinds takes BiomeSiteAir's default for each kind instead. Breathable air changes nothing.
		/// </summary>
		public static IReadOnlyDictionary<POIType, float> SiteKinds(BiomeTemplate biome, PointOfInterestRules rules, bool breathable)
			=> SiteKinds(biome, rules, breathable, true);

		/// <summary>
		/// <see cref="SiteKinds(BiomeTemplate, PointOfInterestRules, bool)"/> on a world that has (<paramref name="liquidWater"/>) or
		/// lacks liquid water on its surface (decision 68): where it lacks it, every kind whose entry needs it
		/// (<see cref="BiomeSiteKind.NeedsLiquidWater"/>) weighs 0 too. No kind needs it by default, so liquid water or none changes
		/// nothing until a biome's file says so.
		/// </summary>
		public static IReadOnlyDictionary<POIType, float> SiteKinds(BiomeTemplate biome, PointOfInterestRules rules, bool breathable, bool liquidWater)
		{
			if (biome == null)
			{
				return null;
			}
			var kinds = new Dictionary<POIType, float>();
			if (biome.IsAuthored(BiomeTemplate.Authored.SiteKinds) || biome.SiteKinds != null && biome.SiteKinds.Count > 0)
			{
				if (biome.SiteKinds != null)
				{
					foreach (BiomeSiteKind entry in biome.SiteKinds)
					{
						if (!kinds.ContainsKey(entry.Kind))
						{
							kinds[entry.Kind] = BiomeSiteAir.WeightOn(entry, breathable, liquidWater);
						}
					}
				}
				return kinds;
			}
			foreach ((POIType kind, float weight) in (rules ?? PointOfInterestRules.Default()).KindWeightsFor(biome.name))
			{
				// The defaults' water rule too: only the biome rounds' own wet kinds have one (2026-10-25), and no row hosts them.
				kinds[kind] = (breathable || !BiomeSiteAir.NeedsAirByDefault(kind)) && (liquidWater || !BiomeSiteAir.NeedsWaterByDefault(kind)) ? weight : 0f;
			}
			Fallback(biome, "Point of Interest Kinds", "the point-of-interest catalogue's rows that name it");
			return kinds;
		}

		/// <summary>
		/// The weight a biome gives each point-of-interest template, for the template pick (PointOfInterestTemplateLibrary.Pick):
		/// the template's variant weight in the biome (BiomeTemplate.SiteVariants; a variant not listed at the template's weight
		/// elsewhere) times its style's (BiomeTemplate.SiteStyles; a style not listed at 1). Null with no biome (every template
		/// at its weight elsewhere, as before). A template never authored with its variant weights takes each template's own
		/// biome list by the biome's name, as the pick did before, and warns.
		/// </summary>
		public static Func<PointOfInterestTemplate, float> SiteVariantWeights(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			List<BiomeSiteStyle> styles = biome.SiteStyles;
			if (biome.IsAuthored(BiomeTemplate.Authored.SiteVariants) || biome.SiteVariants != null && biome.SiteVariants.Count > 0)
			{
				List<BiomeSiteVariant> variants = biome.SiteVariants;
				return template => template == null ? 0f
					: SiteVariantWeight(variants, styles, template.Kind, template.VariantKey, template.Style, template.DefaultBiomeWeight);
			}
			Fallback(biome, "Point of Interest Variants", "each point-of-interest template's own biome list, by the biome's name");
			string name = biome.name;
			return template => template == null ? 0f
				: template.BiomeWeightByName(name) * BiomeSiteStyle.WeightOf(styles, BiomeSiteStyle.Parse(template.Style));
		}

		/// <summary>
		/// A template's weight in a biome from the biome's lists (pure): its variant's entry (<see cref="BiomeSiteVariant.TryWeight"/>),
		/// else <paramref name="elsewhere"/> (the template's weight in a biome that does not weigh it), times its style's
		/// (<see cref="BiomeSiteStyle.WeightOf"/>; a template with no style of its own at 1).
		/// </summary>
		public static float SiteVariantWeight(IReadOnlyList<BiomeSiteVariant> variants, IReadOnlyList<BiomeSiteStyle> styles, POIType kind,
			string variant, string style, float elsewhere)
		{
			float weight = BiomeSiteVariant.TryWeight(variants, kind, variant, out float own) ? own : elsewhere;
			return weight * BiomeSiteStyle.WeightOf(styles, BiomeSiteStyle.Parse(style));
		}

		/// <summary>
		/// How the weather finishes a biome's buildings and stones (BiomeTemplate.Weathering). With no biome, moss on half the
		/// worn pieces, as a name with no keyword gave before; a template never authored with it takes what its name gave
		/// (the spec table's, BiomeArtSpec.WeatheringByName) and warns.
		/// </summary>
		public static BiomeWeathering Weathering(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeArtSpec.WeatheringByName(null);
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.Weathering))
			{
				return biome.Weathering;
			}
			BiomeWeathering old = BiomeArtSpec.For(biome.name)?.Weathering() ?? BiomeArtSpec.WeatheringByName(biome.name);
			Fallback(biome, "Weathering", $"what its name gave ({old})");
			return old;
		}

		/// <summary>
		/// True when a biome is authored to sow nothing (decision 25, 2026-10-25, <see cref="BiomeFieldCrops.SowsNothing"/>): its
		/// Field Crops marked with no entry weighing anything, and no orchard (<see cref="Orchards"/>). False with no biome and for a
		/// template never authored with crops (it grows wheat, <see cref="FieldCrops"/>).
		/// </summary>
		public static bool SowsNothing(BiomeTemplate biome)
			=> biome != null && biome.IsAuthored(BiomeTemplate.Authored.FieldCrops)
				&& BiomeFieldCrops.SowsNothing(true, biome.FieldCrops, Orchards(biome).Count);

		/// <summary>
		/// The crops a biome's fields grow (BiomeTemplate.FieldCrops): its entries with a prefab and a weight, in order. With no
		/// biome, wheat, as every field grew; a template never authored with them takes wheat and warns. Empty where the
		/// template grows none (or the wheat's art is missing): the fields are cleared and left unsown.
		/// </summary>
		public static List<BiomeFieldCrop> FieldCrops(BiomeTemplate biome)
		{
			var crops = new List<BiomeFieldCrop>();
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.FieldCrops) || biome.FieldCrops != null && biome.FieldCrops.Exists(c => c.Prefab != null)))
			{
				foreach (BiomeFieldCrop crop in biome.FieldCrops)
				{
					if (crop.Prefab != null && crop.Weight > 0f)
					{
						crops.Add(crop);
					}
				}
				return crops;
			}
			if (biome != null)
			{
				Fallback(biome, "Field Crops", "wheat, as every field grew");
			}
			GameObject wheat = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(BiomeArtSpec.WheatCrop));
			if (wheat != null)
			{
				crops.Add(new BiomeFieldCrop(wheat, 1f));
			}
			return crops;
		}

		/// <summary>
		/// What a biome's buildings are made of (BiomeTemplate.StructurePalette). With no biome, the structure kit's own
		/// materials (<see cref="BiomeStructurePalette.Kit"/>); a template never authored with one takes them too and warns.
		/// </summary>
		public static BiomeStructurePalette StructurePalette(BiomeTemplate biome)
		{
			if (biome != null && biome.StructurePalette != null && (biome.IsAuthored(BiomeTemplate.Authored.StructurePalette) || !biome.StructurePalette.IsEmpty))
			{
				return biome.StructurePalette;
			}
			if (biome != null)
			{
				Fallback(biome, "Structure Palette", "the structure kit's own materials");
			}
			return BiomeStructurePalette.Kit();
		}

		/// <summary>
		/// What stands in a stronghold's moat in a biome (BiomeTemplate.MoatWater, 2026-10-16, I5b). With no biome, water, as
		/// before; a template never authored with it (no mark, still Wet) takes what its climate envelope gave the moat shaper
		/// before (<see cref="BiomeMoatWaters.FromClimate"/>) and warns.
		/// </summary>
		public static BiomeMoatWater MoatWater(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeMoatWater.Wet;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.MoatWater) || biome.MoatWater != BiomeMoatWater.Wet)
			{
				return biome.MoatWater;
			}
			BiomeMoatWater old = BiomeMoatWaters.FromClimate(biome.MinHumidity, biome.MaxHumidity, biome.MinTemperature, biome.MaxTemperature);
			Fallback(biome, "Moat Water", $"what its climate gave the moat ({old})");
			return old;
		}

		/// <summary>
		/// The colour and clarity of a biome's fresh water at the cut (BiomeTemplate.WaterLook, 2026-10-16, I2e). With no biome,
		/// the inland water's own look (<see cref="BiomeWaterLooks.Today"/>); a template whose look is not filled in takes it too
		/// and warns. The running game reads it the same way (<see cref="BiomeWaterLooks.Of"/>, InlandWaterRenderer).
		/// </summary>
		public static BiomeWaterLook WaterLook(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeWaterLooks.Today;
			}
			if (biome.WaterLook.Defined)
			{
				return biome.WaterLook;
			}
			Fallback(biome, "Water Look", "the inland water's own look");
			return BiomeWaterLooks.Today;
		}

		/// <summary>
		/// How a biome lights one kind of settlement's streets (BiomeTemplate.WayLights, 2026-10-16, I5b): false where the kind is
		/// not lit there. With no biome, the props' old rule by kind (<see cref="BiomeWayLights.Default"/>); a template never
		/// authored with its lights (no mark, empty list) takes that rule too and warns.
		/// </summary>
		public static bool WayLight(BiomeTemplate biome, POIType kind, out BiomeWayLight light)
		{
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.WayLights) || biome.WayLights != null && biome.WayLights.Count > 0))
			{
				return BiomeWayLights.TryFor(biome.WayLights, kind, out light);
			}
			List<BiomeWayLight> old = BiomeWayLights.Default();
			bool lit = BiomeWayLights.TryFor(old, kind, out light);
			if (biome != null && lit)
			{
				Fallback(biome, "Way Lights", "the old lights by kind (lanterns in towns, torches in villages)");
			}
			return lit;
		}

		/// <summary>
		/// How a biome lights one place in its caves and mines (BiomeTemplate.CaveLights, 2026-10-16, I6c): false where the place
		/// is not lit there. With no biome, the cave dressing's old rule (<see cref="BiomeCaveLights.Default"/>) silently; a
		/// template never authored with its cave lights (no mark, empty list) takes that rule too and warns.
		/// </summary>
		public static bool CaveLight(BiomeTemplate biome, BiomeCaveLightPlace place, out BiomeCaveLight light)
		{
			if (biome != null && (biome.IsAuthored(BiomeTemplate.Authored.CaveLights) || biome.CaveLights != null && biome.CaveLights.Count > 0))
			{
				return BiomeCaveLights.TryFor(biome.CaveLights, place, out light);
			}
			bool lit = BiomeCaveLights.TryFor(BiomeCaveLights.Default(), place, out light);
			if (biome != null && lit)
			{
				Fallback(biome, "Cave Lights", "the cave dressing's old lights (lantern stands, wall torches, torch posts, hanging lanterns)");
			}
			return lit;
		}

		/// <summary>
		/// The crystals a biome's caves grow (BiomeTemplate.CaveCrystals, 2026-10-16, I6c). With no biome, selenite (what every
		/// cave grew before) silently; a template never authored with them (no mark, empty list) takes its spec's (selenite unless
		/// its file names its own) and warns. An authored empty list grows none.
		/// </summary>
		public static IReadOnlyList<BiomeCaveCrystal> CaveCrystals(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeCaveCrystals.Default();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.CaveCrystals) || biome.CaveCrystals != null && biome.CaveCrystals.Count > 0)
			{
				return biome.CaveCrystals;
			}
			List<BiomeCaveCrystal> spec = BiomeArtSpec.For(biome.name)?.CaveCrystals() ?? BiomeCaveCrystals.Default();
			Fallback(biome, "Cave Crystals", $"the spec table's ({string.Join(", ", spec)})");
			return spec;
		}

		/// <summary>The crystal a biome's crystal way-light posts carry (<see cref="BiomeCaveCrystals.PostCrystal"/> of its cave crystals).</summary>
		public static BiomeCaveCrystalKind PostCrystal(BiomeTemplate biome) => BiomeCaveCrystals.PostCrystal(CaveCrystals(biome));

		// ── Cave interiors (2026-10-25, phase I2n) ────────────────────

		/// <summary>
		/// A host's cave-interior links (BiomeTemplate.CaveInteriors, 2026-10-25, I2n; read through CaveInteriorArt). None with no host;
		/// a template never authored with them takes its spec's (the Cave, Ice Cave and Lava Tube by default) and warns.
		/// </summary>
		public static IReadOnlyList<BiomeCaveInterior> CaveInteriors(BiomeTemplate host)
		{
			if (host == null)
			{
				return Array.Empty<BiomeCaveInterior>();
			}
			if (host.IsAuthored(BiomeTemplate.Authored.CaveInteriors) || host.CaveInteriors != null && host.CaveInteriors.Count > 0)
			{
				return host.CaveInteriors;
			}
			List<BiomeCaveInterior> spec = BiomeArtSpec.For(host.name)?.CaveInteriors() ?? BiomeCaveInteriors.DefaultLinks(host.name);
			Fallback(host, "Cave Interiors", $"the spec table's ({string.Join(", ", spec)})");
			return spec;
		}

		/// <summary>The shell rocks an interior dresses the inside of (BiomeTemplate.CaveInteriorRocks; empty: any).</summary>
		public static IReadOnlyList<string> CaveInteriorRocks(BiomeTemplate interior)
		{
			if (interior == null)
			{
				return Array.Empty<string>();
			}
			if (interior.IsAuthored(BiomeTemplate.Authored.CaveInteriorRocks) || interior.CaveInteriorRocks != null && interior.CaveInteriorRocks.Count > 0)
			{
				return interior.CaveInteriorRocks;
			}
			List<string> spec = BiomeArtSpec.For(interior.name)?.CaveInteriorRocks() ?? BiomeCaveInteriors.DefaultInteriorRocks(interior.name);
			Fallback(interior, "Cave Interior Rocks", $"the spec table's ({(spec.Count > 0 ? string.Join(", ", spec) : "any")})");
			return spec;
		}

		/// <summary>The formations a biome's caves take by form and place (BiomeTemplate.CaveFormations); the dressing's own with no biome.</summary>
		public static IReadOnlyList<BiomeCaveFormation> CaveFormations(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeCaveInteriors.DefaultFormations();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.CaveFormations) || biome.CaveFormations != null && biome.CaveFormations.Count > 0)
			{
				return biome.CaveFormations;
			}
			List<BiomeCaveFormation> spec = BiomeArtSpec.For(biome.name)?.CaveFormations() ?? BiomeCaveInteriors.DefaultFormations();
			Fallback(biome, "Cave Formations", "the spec table's (the dressing's own set by form)");
			return spec;
		}

		/// <summary>
		/// What lights a biome's caves of a form by itself (BiomeTemplate.CaveGlows): its entry for the form; the dressing's own by form
		/// with no biome, an unauthored template (with a warning) or an authored list without the form.
		/// </summary>
		public static BiomeCaveGlow CaveGlow(BiomeTemplate biome, CaveForm form)
		{
			var f = (BiomeCaveForm)form;
			if (biome != null)
			{
				IReadOnlyList<BiomeCaveGlow> list = biome.CaveGlows;
				if (!biome.IsAuthored(BiomeTemplate.Authored.CaveGlows) && (list == null || list.Count == 0))
				{
					list = BiomeArtSpec.For(biome.name)?.CaveGlows() ?? BiomeCaveInteriors.DefaultGlows();
					Fallback(biome, "Cave Glows", "the spec table's (the dressing's own by form)");
				}
				if (BiomeCaveInteriors.TryGlow(list, f, out BiomeCaveGlow glow))
				{
					return glow;
				}
			}
			return BiomeCaveInteriors.DefaultGlow(f);
		}

		/// <summary>
		/// How a biome's caves stand in the water table by form (BiomeTemplate.CaveFloods, 2026-10-25, I2n-c; read through
		/// CaveInteriorArt). None with no biome; a template never authored with them takes its spec's (warning only when it names any).
		/// </summary>
		public static IReadOnlyList<BiomeCaveFlood> CaveFloods(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return Array.Empty<BiomeCaveFlood>();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.CaveFloods) || biome.CaveFloods != null && biome.CaveFloods.Count > 0)
			{
				return biome.CaveFloods;
			}
			List<BiomeCaveFlood> spec = BiomeArtSpec.For(biome.name)?.CaveFloods();
			if (spec != null && spec.Count > 0)
			{
				Fallback(biome, "Cave Floods", "the spec table's");
				return spec;
			}
			return Array.Empty<BiomeCaveFlood>();
		}

		/// <summary>
		/// What hangs from a biome's caves' roofs by form (BiomeTemplate.CaveHangings, 2026-10-25, I2n-c; read through
		/// CaveInteriorArt). Null with no biome (the dressing's own by form); a template never authored with them takes its spec's,
		/// with a warning.
		/// </summary>
		public static IReadOnlyList<BiomeCaveHanging> CaveHangings(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.CaveHangings) || biome.CaveHangings != null && biome.CaveHangings.Count > 0)
			{
				return biome.CaveHangings;
			}
			List<BiomeCaveHanging> spec = BiomeArtSpec.For(biome.name)?.CaveHangings() ?? BiomeArtSpec.Entry.DefaultCaveHangings();
			Fallback(biome, "Cave Hangings", "the spec table's (the dressing's own by form)");
			return spec;
		}

		/// <summary>
		/// The tea ring a biome's cave water leaves by form (BiomeTemplate.CaveStains, 2026-10-25, I2n-c; read through CaveInteriorArt).
		/// None with no biome; a template never authored with them takes its spec's (warning only when it names any).
		/// </summary>
		public static IReadOnlyList<BiomeCaveStain> CaveStains(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return Array.Empty<BiomeCaveStain>();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.CaveStains) || biome.CaveStains != null && biome.CaveStains.Count > 0)
			{
				return biome.CaveStains;
			}
			List<BiomeCaveStain> spec = BiomeArtSpec.For(biome.name)?.CaveStains();
			if (spec != null && spec.Count > 0)
			{
				Fallback(biome, "Cave Stains", "the spec table's");
				return spec;
			}
			return Array.Empty<BiomeCaveStain>();
		}

		/// <summary>
		/// The plants a biome's still water carries (BiomeTemplate.FloatingPlants, 2026-10-18, I2i). With no biome, none; a template
		/// never authored with them (no mark, empty list) takes its spec's and warns when the spec names any. An authored empty list
		/// carries none, which is every biome's default.
		/// </summary>
		public static IReadOnlyList<BiomeFloatingPlant> FloatingPlants(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeFloatingPlants.None();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.FloatingPlants) || biome.FloatingPlants != null && biome.FloatingPlants.Count > 0)
			{
				return biome.FloatingPlants ?? BiomeFloatingPlants.None();
			}
			List<BiomeFloatingPlant> spec = BiomeArtSpec.For(biome.name)?.FloatingPlants() ?? BiomeFloatingPlants.None();
			if (spec.Count > 0)
			{
				Fallback(biome, "Floating Plants", $"the spec table's ({string.Join(", ", spec)})");
			}
			return spec;
		}

		/// <summary>
		/// The landforms the cut sculpts into a biome's ground (BiomeTemplate.Landforms, 2026-10-18, I2g). With no biome, none; a template
		/// never authored with them (no mark, empty list) takes its spec's and warns when the spec names any. An authored empty list
		/// carves none, which is every biome's default.
		/// </summary>
		public static IReadOnlyList<BiomeLandform> Landforms(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeLandforms.None();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.Landforms) || biome.Landforms.Count > 0)
			{
				return biome.Landforms;
			}
			List<BiomeLandform> spec = BiomeArtSpec.For(biome.name)?.LandformList() ?? BiomeLandforms.None();
			if (spec.Count > 0)
			{
				Fallback(biome, "Landforms", $"the spec table's ({string.Join(", ", spec.ConvertAll(f => f.Kind))})");
			}
			return spec;
		}

		/// <summary>
		/// Which of a biome's ways are raised boardwalks (BiomeTemplate.Boardwalks, 2026-10-18, I2i). With no biome, none; a template
		/// never authored with it takes its spec's and warns when that raises any.
		/// </summary>
		public static BiomeBoardwalks Boardwalks(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeBoardwalks.None;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.Boardwalks) || biome.Boardwalks != BiomeBoardwalks.None)
			{
				return biome.Boardwalks;
			}
			BiomeBoardwalks spec = BiomeArtSpec.For(biome.name)?.Boardwalks ?? BiomeBoardwalks.None;
			if (spec != BiomeBoardwalks.None)
			{
				Fallback(biome, "Boardwalks", $"the spec table's ({spec})");
			}
			return spec;
		}

		/// <summary>
		/// How a biome's raised routed ways are built (BiomeTemplate.BoardwalkStyle, 2026-10-25, I2i-b). With no biome, planked; a
		/// template never authored with it takes its spec's and warns when that names another.
		/// </summary>
		public static BiomeBoardwalkStyle BoardwalkStyle(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeBoardwalkStyle.Planked;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.BoardwalkStyle) || biome.BoardwalkStyle != BiomeBoardwalkStyle.Planked)
			{
				return biome.BoardwalkStyle;
			}
			BiomeBoardwalkStyle spec = BiomeArtSpec.For(biome.name)?.BoardwalkStyle ?? BiomeBoardwalkStyle.Planked;
			if (spec != BiomeBoardwalkStyle.Planked)
			{
				Fallback(biome, "BoardwalkStyle", $"the spec table's ({spec})");
			}
			return spec;
		}

		/// <summary>
		/// A biome's hedges, walls, fences, tree rows, channels and clearance rows (BiomeTemplate.LinearFeatures, 2026-10-18, I2f). With no
		/// biome, none; a template never authored with them (no mark, empty list) takes its spec's, its species loaded from the generated
		/// prefabs, and warns when the spec names any. An authored empty list lays none, which is every biome's default.
		/// </summary>
		public static IReadOnlyList<BiomeLinearStyle> LinearFeatures(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeLinearFeatures.None();
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.LinearFeatures) || biome.LinearFeatures.Count > 0)
			{
				return biome.LinearFeatures;
			}
			BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome.name);
			if (entry == null || entry.LinearLines().Count == 0)
			{
				return BiomeLinearFeatures.None();
			}
			List<BiomeLinearStyle> spec = entry.LinearFeatures(name => AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name)));
			Fallback(biome, "Linear Features", $"the spec table's ({string.Join("; ", spec)})");
			return spec;
		}

		/// <summary>
		/// A biome's orchards (BiomeTemplate.Orchards, 2026-10-18, I2j-b): its entries with a tree and a weight, in order. With no biome,
		/// none; a template never authored with them (no mark, empty list) takes its spec's, its prefabs loaded from the generated art,
		/// with a warning only when the spec names any (none did before).
		/// </summary>
		public static List<BiomeOrchard> Orchards(BiomeTemplate biome)
		{
			var orchards = BiomeOrchards.None();
			if (biome == null)
			{
				return orchards;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.Orchards) || biome.Orchards.Count > 0)
			{
				foreach (BiomeOrchard orchard in biome.Orchards)
				{
					if (orchard.Tree != null && orchard.Weight > 0f)
					{
						orchards.Add(orchard);
					}
				}
				return orchards;
			}
			BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome.name);
			if (entry == null || entry.OrchardEntries().Count == 0)
			{
				return orchards;
			}
			foreach (BiomeOrchard orchard in entry.FieldOrchards(name => AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name))))
			{
				if (orchard.Weight > 0f)
				{
					orchards.Add(orchard);
				}
			}
			Fallback(biome, "Orchards", $"the spec table's ({string.Join("; ", orchards)})");
			return orchards;
		}

		/// <summary>How a biome lays its fields out (BiomeTemplate.FieldLayout, 2026-10-18, I2j-b): anywhere, as drawn, with no biome; a template never authored with it takes its spec's (a warning only when that says anything).</summary>
		public static BiomeFieldLayout FieldLayout(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return default;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.FieldLayout) || biome.FieldLayout.Any)
			{
				return biome.FieldLayout;
			}
			BiomeFieldLayout spec = BiomeArtSpec.For(biome.name)?.FieldLayout() ?? default;
			if (spec.Any)
			{
				Fallback(biome, "Field Layout", $"the spec table's ({spec})");
			}
			return spec;
		}

		/// <summary>The old terraces round a biome's settlements (BiomeTemplate.AbandonedTerraces, 2026-10-18, I2j-b): none with no biome; a template never authored with them takes its spec's (a warning only when that names any).</summary>
		public static BiomeAbandonedTerraces AbandonedTerraces(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeAbandonedTerraces.None;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.AbandonedTerraces) || biome.AbandonedTerraces.Any)
			{
				return biome.AbandonedTerraces;
			}
			BiomeAbandonedTerraces spec = BiomeArtSpec.For(biome.name)?.OldTerraces() ?? BiomeAbandonedTerraces.None;
			if (spec.Any)
			{
				Fallback(biome, "Abandoned Terraces", $"the spec table's ({spec})");
			}
			return spec;
		}

		/// <summary>The ground a biome paints along its channels (BiomeTemplate.ChannelMargin, 2026-10-18, I2j-b): none with no biome; a template never authored with it takes its spec's (a warning only when that names one).</summary>
		public static BiomeChannelMargin ChannelMargin(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeChannelMargin.None;
			}
			if (biome.IsAuthored(BiomeTemplate.Authored.ChannelMargin) || biome.ChannelMargin.Defined)
			{
				return biome.ChannelMargin;
			}
			BiomeChannelMargin spec = BiomeArtSpec.For(biome.name)?.WetMargin() ?? BiomeChannelMargin.None;
			if (spec.Defined)
			{
				Fallback(biome, "Channel Margin", $"the spec table's ({spec})");
			}
			return spec;
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
