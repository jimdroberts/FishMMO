#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a suggested scene name came from, so the prompt can explain itself.</summary>
	public struct SuggestedSceneName
	{
		/// <summary>The name, or null when none could be produced.</summary>
		public string Name;
		/// <summary>The biome the ground under the rectangle resolved to, for display.</summary>
		public string Biome;
		/// <summary>The climate variant that flavoured it ("frozen"), or null.</summary>
		public string Variant;
		/// <summary>Why there is no name, or null when there is one.</summary>
		public string Problem;

		public bool Usable => !string.IsNullOrEmpty(Name);
	}

	/// <summary>
	/// Names a scene after the ground it is being cut from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The project already has a name generator that builds place names out of a biome's own
	/// phonology and vocabulary — "Frozen Kelbrin Spring" from a tundra, "The Sunken Mire" from a
	/// peat bog. Cutting a scene out of a globe knows exactly which biome that ground carries,
	/// because the same climate field that shades the planet says so, so the two fit together with
	/// nothing in between: ask the planet what is there, ask the generator what such a place is
	/// called.
	/// </para>
	/// <para>
	/// <b>Unseeded on purpose.</b> A seeded request would answer the same thing every time it was
	/// asked about the same rectangle, and the button exists to be pressed again when the first
	/// suggestion is not liked. The scene's position is not lost by this — it is on the atlas
	/// entry, which is where a position belongs.
	/// </para>
	/// </remarks>
	public static class GeneratedSceneNames
	{
		/// <summary>How many names to draw before giving up on finding an unused one.</summary>
		private const int Attempts = 24;

		/// <summary>
		/// A name for a scene about to be cut at a point on a body.
		/// </summary>
		/// <param name="body">The body being cut. Null gives no name.</param>
		/// <param name="layer">The scene's layer; an underground one is named as a dungeon.</param>
		/// <param name="latitude">Centre of the rectangle, in degrees.</param>
		/// <param name="longitude">Centre of the rectangle, in degrees.</param>
		/// <param name="taken">Names already in use, so a suggestion is never one that is refused.</param>
		public static SuggestedSceneName Suggest(
			WorldBody body, WorldAtlasLayer layer, double latitude, double longitude, IEnumerable<string> taken)
		{
			if (body == null)
			{
				return new SuggestedSceneName { Problem = "No body to take a name from." };
			}

			// In the editor outside play mode nothing has loaded the naming templates, and the
			// biome registry the resolver reads is filled by the same pass.
			NamingTemplateEditorLoader.EnsureLoaded();

			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			PlanetClimateField field = PlanetClimateField.For(system, body);
			BiomeTemplate biome = field.BiomeAt(latitude, longitude, out PlanetSurfacePoint point);

			if (biome == null)
			{
				return new SuggestedSceneName
				{
					Problem = BiomeRegistry.Count == 0
						? "No BiomeTemplate is registered, so there is nothing to name this after."
						: $"Nothing in the biome catalogue fits this ground ({point.Climate.Temperature:0.00} temperature, {point.Climate.Humidity:0.00} humidity on a {body.Atmosphere} world).",
				};
			}
			if (biome.Naming == null || !biome.Naming.IsUsable)
			{
				return new SuggestedSceneName
				{
					Biome = biome.ResolvedDisplayName,
					Problem = $"'{biome.ResolvedDisplayName}' carries no naming data. Run the biome naming generator, or type a name.",
				};
			}

			BiomeClimateVariant variant = biome.ResolveOwnVariant(point.Climate.Temperature, point.Climate.Humidity);
			bool underground = layer != null && layer.Underground;

			var generator = new NameGenerator();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (taken != null)
			{
				foreach (string name in taken)
				{
					seen.Add(name);
				}
			}

			string last = null;
			for (int attempt = 0; attempt < Attempts; attempt++)
			{
				string candidate;
				try
				{
					candidate = underground
						? generator.Generate(new DungeonRequest { Biome = biome.Key, Variant = variant }).Name
						: generator.Generate(new POIRequest { Biome = biome.Key, Variant = variant }).Name;
				}
				catch (Exception ex)
				{
					return new SuggestedSceneName
					{
						Biome = biome.ResolvedDisplayName,
						Problem = $"The name generator refused: {ex.Message}",
					};
				}

				last = candidate;
				if (!seen.Contains(candidate) && SceneGeneration.NameProblem(candidate, seen) == null)
				{
					return new SuggestedSceneName
					{
						Name = candidate,
						Biome = biome.ResolvedDisplayName,
						Variant = variant != null ? variant.Name : null,
					};
				}
			}

			/* Every draw was taken. Handing back the last one anyway is better than handing back
			 * nothing: the prompt refuses it out loud with the reason, which is far clearer than a
			 * button that does nothing when pressed. */
			return new SuggestedSceneName
			{
				Name = last,
				Biome = biome.ResolvedDisplayName,
				Variant = variant != null ? variant.Name : null,
				Problem = $"Every name drawn for '{biome.ResolvedDisplayName}' is already in use.",
			};
		}
	}
}
#endif
