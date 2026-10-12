using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Client
{
	/// <summary>
	/// How damp a biome keeps its rock and timber, for the moss layer in the world lit shader (FishMoss.hlsl): 0 grows
	/// none, 1 grows all a surface's material allows (2026-10-12: "a moss layer that can be applied to objects").
	/// Pure functions of the biome's own definition, so a scene's moss follows its climate and not today's weather:
	/// a desert after a shower is still a desert. The rain's wetness is applied on top of the moss by the weather
	/// (FishWeatherSurface), which darkens it like the rest.
	/// </summary>
	/// <remarks>
	/// Read from what <see cref="BiomeTemplate"/> already says:
	/// <list type="bullet">
	/// <item>its humidity envelope (-1 driest .. 1 wettest): the middle of it, when the envelope says anything. Many cold
	/// and sea biomes take any humidity (-1..1), which tells nothing, and fall back on a neutral guess;</item>
	/// <item>heat bakes a dry biome drier (a savanna is not a meadow), never a wet one (a jungle is all moss);</item>
	/// <item>a cold biome that needs no liquid water is ice (glacier, ice sheet, alpine, snow): almost none. A cold one that
	/// does (tundra, taiga) keeps its moss, as real tundra does;</item>
	/// <item>loose ground (sand, dust, regolith) and an emitting ground (ash, vents) grow next to none;</item>
	/// <item>a wetland's terrain process (<see cref="AirMotesField.WetlandOf"/>) is wet whatever its envelope says.</item>
	/// </list>
	/// Local damp — open water near, a canopy over it, a hollow — is the shader's, per pixel, from the fine ground map,
	/// so a coast's rocks are mossier than its open ground without the biome having to say so.
	/// </remarks>
	public static class MossClimate
	{
		/// <summary>When no biome is known: some moss, as a temperate place has.</summary>
		public const float Neutral = 0.45f;

		/// <summary>Seconds for the scene's dampness to cover most of the way to a new biome's, so walking over an
		/// ecotone fades the moss rather than switching it.</summary>
		public const float EaseSeconds = 20f;

		/// <summary>The dampness of a biome, 0..1; <see cref="Neutral"/> for none.</summary>
		public static float Dampness(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return Neutral;
			}
			return Dampness(biome.MinHumidity, biome.MaxHumidity, biome.MinTemperature, biome.MaxTemperature,
				biome.RequiresLiquidWater, biome.LooseGround != null, biome.Emits != null, AirMotesField.WetlandOf(biome.ResolvedTerrainProcess));
		}

		/// <summary>The dampness of a biome from its values, 0..1 (see the class remarks). Humidity and temperature
		/// are the biome's envelope, -1..1; wetland is <see cref="AirMotesField.WetlandOf"/> of its terrain process.</summary>
		public static float Dampness(float minHumidity, float maxHumidity, float minTemperature, float maxTemperature,
			bool requiresLiquidWater, bool looseGround, bool emits, float wetland)
		{
			float low = Mathf.Clamp(minHumidity, -1f, 1f);
			float high = Mathf.Clamp(maxHumidity, -1f, 1f);
			float span = Mathf.Max(0f, high - low);
			// The envelope's middle: none at -0.6 and below (steppe, desert), all from +0.8 (swamp, wetlands).
			float fromEnvelope = Mathf.Clamp01((0.5f * (low + high) + 0.6f) / 1.4f);
			// An envelope that takes nearly any humidity says nothing about it: then a neutral guess, a little damper
			// where the biome needs liquid water at all.
			float informative = 1f - Smooth(1.2f, 1.9f, span);
			float damp = Mathf.Lerp(requiresLiquidWater ? Neutral : 0.3f, fromEnvelope, informative);

			float meanTemperature = 0.5f * (Mathf.Clamp(minTemperature, -1f, 1f) + Mathf.Clamp(maxTemperature, -1f, 1f));
			// Heat dries a dry place further and leaves a wet one alone.
			float hot = Smooth(0.4f, 0.8f, meanTemperature);
			damp *= 1f - 0.6f * hot * (1f - damp);
			// Cold with no liquid water is ice.
			if (!requiresLiquidWater)
			{
				float cold = 1f - Smooth(-0.6f, -0.1f, meanTemperature);
				damp *= 1f - 0.85f * cold;
			}
			if (looseGround)
			{
				damp *= 0.1f;
			}
			if (emits)
			{
				damp *= 0.05f;
			}
			// A wetland is wet, whatever else.
			damp = Mathf.Max(damp, Mathf.Clamp01(wetland));
			return Mathf.Clamp01(damp);
		}

		/// <summary>One step of easing toward a new dampness; NaN (nothing shown yet) lands at once.</summary>
		public static float Ease(float current, float target, float deltaTime, float seconds = EaseSeconds)
		{
			if (float.IsNaN(current) || seconds <= 0f)
			{
				return target;
			}
			return Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds));
		}

		private static float Smooth(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
			return t * t * (3f - 2f * t);
		}
	}

	/// <summary>
	/// Writes the moss layer's globals (FishMoss.hlsl): how much grows in this scene, the moss's tile and textures, and
	/// which way is poleward. The only writer of them: <see cref="WeatherPresentation"/> in play, the editor's preview
	/// (MossEditorPreview) out of it.
	/// </summary>
	public static class MossShaderGlobals
	{
		/// <summary>x strength × dampness, y 1 / tile metres, z 1 when the textures are bound, w poleward (+1 north, -1 south).</summary>
		public static readonly int Params = Shader.PropertyToID("_FishMossParams");
		/// <summary>x the moss recipe's mean smoothness, y 1 / its smoothness variation (the height is read back from the albedo's alpha).</summary>
		public static readonly int Surface = Shader.PropertyToID("_FishMossSurface");
		public static readonly int Albedo = Shader.PropertyToID("_FishMossAlbedo");
		public static readonly int Normal = Shader.PropertyToID("_FishMossNormal");

		/// <summary>
		/// The generated moss surface's smoothness and its variation (SurfaceCatalogue, Ground.Moss: smoothness 0.25,
		/// variation 0.15). SurfaceSynth stores smoothness = mean + (0.5 - height) × variation in the albedo's alpha, so
		/// the shader turns that back into the moss's height. A test holds these to the recipe.
		/// </summary>
		public const float RecipeSmoothness = 0.25f;
		public const float RecipeSmoothnessVariation = 0.15f;

		/// <summary>Where the generator writes the moss surface (gitignored, path-derived GUIDs: ProceduralArtCatalogue.GroundTexture).</summary>
		public const string AlbedoPath = "Assets/Prefabs/Shared/Biomes/Generated/Payload/Textures/Ground_Moss_Albedo.png";
		public const string NormalPath = "Assets/Prefabs/Shared/Biomes/Generated/Payload/Textures/Ground_Moss_Normal.png";

		/// <summary>The params vector for a scene. Poleward eases from +1 to -1 across ten degrees of the equator,
		/// where the sun stands overhead and no side is the shaded one.</summary>
		public static Vector4 ParamsFor(float strength, float dampness, float tileMetres, bool texturesBound, float latitudeDegrees)
		{
			return new Vector4(
				Mathf.Max(0f, strength) * Mathf.Clamp01(dampness),
				1f / Mathf.Max(0.05f, tileMetres),
				texturesBound ? 1f : 0f,
				Mathf.Clamp(latitudeDegrees / 10f, -1f, 1f));
		}

		/// <summary>Publishes the moss for this frame.</summary>
		public static void Apply(float strength, float dampness, float tileMetres, Texture2D albedo, Texture2D normal, float latitudeDegrees)
		{
			bool bound = albedo != null && normal != null;
			Shader.SetGlobalVector(Params, ParamsFor(strength, dampness, tileMetres, bound, latitudeDegrees));
			Shader.SetGlobalVector(Surface, new Vector4(RecipeSmoothness, 1f / RecipeSmoothnessVariation, 0f, 0f));
			if (bound)
			{
				Shader.SetGlobalTexture(Albedo, albedo);
				Shader.SetGlobalTexture(Normal, normal);
			}
		}

		/// <summary>Publishes the moss from a profile: its strength and tile, its textures (in the editor, loaded by path
		/// when the profile has none yet, so a fresh "Generate biome art" shows without another step).</summary>
		public static void Apply(WeatherRenderProfile profile, float dampness, float latitudeDegrees)
		{
			if (profile == null)
			{
				Clear();
				return;
			}
			Texture2D albedo = profile.MossAlbedo;
			Texture2D normal = profile.MossNormal;
#if UNITY_EDITOR
			if (albedo == null)
			{
				albedo = editorAlbedo != null ? editorAlbedo : editorAlbedo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
			}
			if (normal == null)
			{
				normal = editorNormal != null ? editorNormal : editorNormal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
			}
#endif
			Apply(profile.MossStrength, dampness, profile.MossTileMetres, albedo, normal, latitudeDegrees);
		}

#if UNITY_EDITOR
		// Held so the editor does not ask the asset database every frame; a texture deleted or re-imported reads as
		// null (Unity's fake null) and is loaded again.
		private static Texture2D editorAlbedo;
		private static Texture2D editorNormal;
#endif

		/// <summary>No moss anywhere.</summary>
		public static void Clear()
		{
			Shader.SetGlobalVector(Params, Vector4.zero);
		}
	}
}
