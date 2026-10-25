#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The two biomes a cave answers to (2026-10-25, phase I2n "caves have a biome", decisions 37, 46, 51, 57): its HOST, the biome
	/// it opens in, whose are its mouth, its shell's rock and every mine; and, where the host links the cave's form to a cave biome
	/// whose rocks the shell is cut in, its INTERIOR, whose template dresses the inside (formations, crystals, glow, lights, floor
	/// grounds, cover, animals, gathering).
	/// </summary>
	public readonly struct CaveBiome
	{
		/// <summary>The biome the cave opens in (its host site's), or null.</summary>
		public readonly BiomeTemplate Host;
		/// <summary>The cave biome dressing its inside, or null where the host dresses it (no link, a shell the interior does not form in, a mine).</summary>
		public readonly BiomeTemplate Interior;

		public CaveBiome(BiomeTemplate host, BiomeTemplate interior)
		{
			Host = host;
			Interior = interior;
		}

		/// <summary>True when an interior dresses the inside.</summary>
		public bool Linked => Interior != null;

		/// <summary>The biome whose template dresses the inside: the interior where linked, else the host.</summary>
		public BiomeTemplate Content => Interior != null ? Interior : Host;

		public override string ToString() => Linked ? $"{Host?.name ?? "(none)"} → {Interior.name}" : Host?.name ?? "(none)";
	}

	/// <summary>
	/// The cave-interior readers and the one resolver of a cave's interior (<see cref="CaveBiome"/>, 2026-10-25, I2n). Every reader
	/// takes the template's field; a template never authored with it (no mark, an empty list) takes its spec's and warns once a
	/// session (<see cref="BiomeTemplateArt.Fallback"/>), as the other template readers do.
	/// </summary>
	public static class CaveInteriorArt
	{
		// ── Template fields (the readers, with their spec fallbacks, are BiomeTemplateArt's) ──

		/// <summary>A host's cave-interior links (BiomeTemplateArt.CaveInteriors).</summary>
		public static IReadOnlyList<BiomeCaveInterior> Links(BiomeTemplate host) => BiomeTemplateArt.CaveInteriors(host);

		/// <summary>The shell rocks an interior dresses the inside of (BiomeTemplateArt.CaveInteriorRocks; empty: any).</summary>
		public static IReadOnlyList<string> InteriorRocks(BiomeTemplate interior) => BiomeTemplateArt.CaveInteriorRocks(interior);

		/// <summary>The formations a biome's caves take by form and place (BiomeTemplateArt.CaveFormations).</summary>
		public static IReadOnlyList<BiomeCaveFormation> Formations(BiomeTemplate biome) => BiomeTemplateArt.CaveFormations(biome);

		/// <summary>What lights a biome's caves of a form by itself (BiomeTemplateArt.CaveGlow).</summary>
		public static BiomeCaveGlow Glow(BiomeTemplate biome, CaveForm form) => BiomeTemplateArt.CaveGlow(biome, form);

		// ── The resolver ──────────────────────────────────────────────

		private static Dictionary<string, BiomeTemplate> projectByName;

		/// <summary>
		/// A biome template by its asset's name: the registry's, else (a batch run whose registry is empty, a template made since) the
		/// project's. Null for none.
		/// </summary>
		public static BiomeTemplate Named(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			BiomeTemplate found = BiomeRegistry.Get(name);
			if (found != null)
			{
				return found;
			}
			if (projectByName == null || projectByName.TryGetValue(name, out found) && found == null)
			{
				projectByName = new Dictionary<string, BiomeTemplate>(StringComparer.Ordinal);
				foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
				{
					var template = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
					if (template != null)
					{
						projectByName[template.name] = template;
					}
				}
			}
			return projectByName.TryGetValue(name, out found) ? found : null;
		}

		/// <summary>
		/// The cave biomes of a cave of <paramref name="form"/> opening in <paramref name="host"/>, cut in <paramref name="shell"/> (the
		/// resolved shell rock, CaveShaper.ShellOf): the host's link for the form, where it names a template other than the host and
		/// the interior forms in the shell (<see cref="BiomeCaveInteriors.Holds"/>); never for a mine.
		/// </summary>
		public static CaveBiome Of(BiomeTemplate host, CaveForm form, string shell)
		{
			var f = (BiomeCaveForm)form;
			if (host == null || !BiomeCaveInteriors.Links(f) || !BiomeCaveInteriors.TryLink(Links(host), f, out string name) || name == host.name)
			{
				return new CaveBiome(host, null);
			}
			BiomeTemplate interior = Named(name);
			if (interior == null || interior == host || !BiomeCaveInteriors.Holds(f, InteriorRocks(interior), shell))
			{
				return new CaveBiome(host, null);
			}
			return new CaveBiome(host, interior);
		}

		/// <summary>A carved system's cave biomes: its host site's biome, its form, its shell (the rock the shaper cut it in).</summary>
		public static CaveBiome Of(SceneGenerationRequest request, PointOfInterestRecord site, CaveSystem system)
		{
			if (site == null || system == null)
			{
				return default;
			}
			return Of(PointOfInterestRock.BiomeOf(site), system.Form, CaveShaper.ShellOf(request, site, system));
		}

		/// <summary>
		/// A cave site's cave biomes before (or without) its system: its form by its kind, its shell at the site's anchor (what the
		/// template pick, the site's palette and its gathering read); a host-only result for a site that is no cave.
		/// </summary>
		public static CaveBiome OfSite(SceneGenerationRequest request, PointOfInterestRecord site)
		{
			if (site == null)
			{
				return default;
			}
			BiomeTemplate host = PointOfInterestRock.BiomeOf(site);
			if (!CaveShaper.TryForm(site.Kind, out CaveForm form) || form == CaveForm.Mine || host == null)
			{
				return new CaveBiome(host, null);
			}
			return Of(host, form, CaveShaper.RockOf(form, request, site, site.Position));
		}

		// ── What the inside takes ─────────────────────────────────────

		/// <summary>
		/// The crystals a cave grows: its interior's, with the host's glowing ones as accents at half their weight (decision 51,
		/// <see cref="BiomeCaveInteriors.WithHostAccents"/>); the host's own where not linked.
		/// </summary>
		public static IReadOnlyList<BiomeCaveCrystal> Crystals(in CaveBiome caves)
		{
			if (!caves.Linked)
			{
				return BiomeTemplateArt.CaveCrystals(caves.Host);
			}
			return BiomeCaveInteriors.WithHostAccents(BiomeTemplateArt.CaveCrystals(caves.Interior), caves.Host != null ? BiomeTemplateArt.CaveCrystals(caves.Host) : null);
		}

		/// <summary>
		/// How a place in a cave is lit: a linked cave by its interior's Cave Lights, else by its host's, either under the world's air
		/// (the cold Glow kinds where it will not burn a flame, decision 51, <see cref="BiomeCaveInteriors.ForAir"/>). Since decision 62
		/// (2026-10-25) a host's own lights take the same fallback: a biome of any air painted on an airless or methane world has
		/// fire cave lights authored for its Earth-like worlds. On breathable air every light is exactly as authored.
		/// </summary>
		public static bool CaveLight(in CaveBiome caves, BiomeCaveLightPlace place, bool breathable, out BiomeCaveLight light)
		{
			bool lit = BiomeTemplateArt.CaveLight(caves.Linked ? caves.Interior : caves.Host, place, out light);
			if (lit)
			{
				light = BiomeCaveInteriors.ForAir(in light, breathable);
			}
			return lit;
		}

		/// <summary>The gathering a cave's chamber offers: its interior's where linked, else its host's (BiomeTemplateArt.Gathering).</summary>
		public static IReadOnlyList<string> Gathering(in CaveBiome caves) => BiomeTemplateArt.Gathering(caves.Content);

		/// <summary>The world a scene is cut on (its body's conditions), the home world without a body.</summary>
		public static BiomeWorldConditions World(SceneGenerationRequest request)
		{
			if (request == null || request.Body == null)
			{
				return BiomeWorldConditions.Earthlike;
			}
			return BiomeWorldConditions.For(SolarSystemProfile.Resolve(request.Body), request.Body);
		}

		/// <summary>
		/// Whether the world a scene is cut on has air a flame burns in (BiomeAir.IsBreathable: Standard or Thick, decision 39's rule,
		/// and not a methane, nitrogen, acid or molten-rock world, decision 62); true without a body (the home world).
		/// </summary>
		public static bool Breathable(SceneGenerationRequest request) => BiomeAir.IsBreathable(World(request));

		// ── Site variants and the palette (decision 51) ───────────────

		/// <summary>
		/// The template weights a cave site's pick uses: its host's (BiomeTemplateArt.SiteVariantWeights) where not linked or where
		/// either template's variants are unauthored; where linked, the two lists merged
		/// (<see cref="BiomeCaveInteriors.MergedVariantWeight"/>: the host's entry, else the interior's, at ×0.4 where the host names
		/// its own for the kind), times the host's style weight.
		/// </summary>
		public static Func<PointOfInterestTemplate, float> SiteVariantWeights(in CaveBiome caves)
		{
			BiomeTemplate host = caves.Host, interior = caves.Interior;
			if (!caves.Linked || host == null || !VariantsAuthored(host) || !VariantsAuthored(interior))
			{
				return BiomeTemplateArt.SiteVariantWeights(host);
			}
			List<BiomeSiteVariant> own = host.SiteVariants, inside = interior.SiteVariants;
			List<BiomeSiteStyle> styles = host.SiteStyles;
			return template => template == null ? 0f
				: BiomeCaveInteriors.MergedVariantWeight(own, inside, template.Kind, template.VariantKey, template.DefaultBiomeWeight, out _)
					* BiomeSiteStyle.WeightOf(styles, BiomeSiteStyle.Parse(template.Style));
		}

		/// <summary>
		/// Whether a cave site's template is the interior's naming (the host names no weight for its variant, the interior does): its
		/// pieces then take the interior's structure palette (decision 51).
		/// </summary>
		public static bool InteriorNamed(in CaveBiome caves, PointOfInterestTemplate template)
		{
			if (!caves.Linked || template == null || !VariantsAuthored(caves.Host) || !VariantsAuthored(caves.Interior))
			{
				return false;
			}
			BiomeCaveInteriors.MergedVariantWeight(caves.Host.SiteVariants, caves.Interior.SiteVariants, template.Kind, template.VariantKey, 0f, out bool named);
			return named;
		}

		private static bool VariantsAuthored(BiomeTemplate biome)
			=> biome != null && (biome.IsAuthored(BiomeTemplate.Authored.SiteVariants) || biome.SiteVariants != null && biome.SiteVariants.Count > 0);
	}
}
#endif
