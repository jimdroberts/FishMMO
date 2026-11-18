using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's ambient life look: sea life and what floats on the sea, the land's small animals, motes in the air, falling leaves, and the life of the world's sites (embers, flames, lighthouse beams, cave drips and water life, vents, rockfalls).
	/// </summary>
	/// <remarks>
	/// Client-only; loaded with the client's static assets. Ambient life's own profile (Phase 4 of the core and modules plan, 2026-11-17): these settings were part of <see cref="WeatherRenderProfile"/>, and the asset was split field for field.
	/// </remarks>
	[CreateAssetMenu(fileName = "Ambient Life Render Profile", menuName = "FishMMO/World/Ambient Life Render Profile", order = 21)]
	public class AmbientLifeRenderProfile : CachedScriptableObject<AmbientLifeRenderProfile>, ICachedObject
	{
		[Header("Sea life")]
		[Tooltip("FishMMO/Sea Life: the background fish, rays, turtles, whales, jellies and crabs (SeaLifeSystem). Referenced here so a client build includes it.")]
		public Shader SeaLifeShader;
		public SeaLifeSettings SeaLife = new SeaLifeSettings();

		[Header("Sea floats")]
		[Tooltip("FishSeaFloats.compute: expands what drifts on the open sea (sargassum, sailors, drift logs, ice…) round each camera every frame (SeaFloatSystem). Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public ComputeShader SeaFloatsCompute;
		[Tooltip("FishMMO/Sea Floats: draws them riding the sea's waves. Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public Shader SeaFloatsShader;
		public SeaFloatSettings SeaFloats = new SeaFloatSettings();

		[Header("Ambient life")]
		[Tooltip("FishMMO/Ambient Life: the land's birds, bats and small animals (AmbientLifeSystem). Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public Shader AmbientLifeShader;
		public AmbientLifeSettings AmbientLife = new AmbientLifeSettings();

		[Header("Air motes")]
		[Tooltip("FishMMO/Weather/Air Motes: dust in the light, pollen and seed fluff, diamond dust, fireflies and marsh lights round the camera (AirMotesField). Referenced here so a client build includes it; empty, it is found by name, which only works where something else ships it (the editor).")]
		public Shader AirMotesShader;

		[Header("Site embers")]
		[Tooltip("FishMMO/World/Site Embers: sparks off the campfires, cooking fires and forges near the camera (SiteEmbers). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader SiteEmbersShader;
		[Tooltip("FishMMO/World/Site Flames: the flames on the campfires, hearths, forges and torches near the camera (SiteEmbers). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader SiteFlamesShader;
		[Tooltip("FishMMO/World/Lighthouse Beams: the turning beams and flash of the lighthouses' lanterns at night (LighthouseBeams). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader LighthouseBeamsShader;
		[Tooltip("FishMMO/World/Cave Drips: the drops falling from the caves' roofs, their spatter and the rings on their puddles near the camera (CaveDripField). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader CaveDripsShader;
		[Tooltip("FishMMO/World/Cave Water Life: the marsh-gas beads rising in the caves' still water and the leeches swimming in it near the camera (CaveWaterLife, 2026-10-26). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader CaveWaterLifeShader;
		[Tooltip("FishMMO/World/Sea Hall Vents: a sea hall's smoker plumes, smoke ceiling, flange mirrors, carbon-dioxide beads, brinicles and brine mirrors near the camera (SeaHallVents, 2026-10-26). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader SeaHallVentsShader;
		[Tooltip("FishMMO/World/Rockfall Stones: the stones falling down the scene's stored chutes and the Strombolian bombs (RockfallField, 2026-10-25, I2u). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader RockfallStonesShader;
		[Tooltip("FishMMO/World/Rockfall Marks: the chutes' fresh scars, the breathing taluses' rime, mist and steam, and the falls' dust (RockfallField, I2u). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader RockfallMarksShader;

		[Header("Falling leaves")]
		[Tooltip("FishMMO/Weather/Falling Leaves: leaves coming down under and downwind of the trees near the camera, mostly in autumn (FallingLeavesPresenter). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader FallingLeavesShader;
		public FallingLeavesSettings FallingLeaves = new FallingLeavesSettings();

		/// <summary>
		/// The loaded profile, if any. In the editor, a world scene played straight from its own file never runs the
		/// client boot that loads the profile into the cache; there the project's profile asset is loaded once instead.
		/// Builds always have it cached.
		/// </summary>
		public static AmbientLifeRenderProfile Active
		{
			get
			{
				AmbientLifeRenderProfile cached = GetFirst<AmbientLifeRenderProfile>();
#if UNITY_EDITOR
				if (cached == null)
				{
					if (editorFallback == null && !editorFallbackTried)
					{
						editorFallbackTried = true;
						editorFallback = UnityEditor.AssetDatabase.LoadAssetAtPath<AmbientLifeRenderProfile>(EditorProfilePath);
					}
					return editorFallback;
				}
#endif
				return cached;
			}
		}

#if UNITY_EDITOR
		/// <summary>The project's profile asset, for <see cref="Active"/>'s editor fallback.</summary>
		public const string EditorProfilePath = "Assets/FishMMO/Modules/AmbientLife/Settings/Ambient Life Render Profile.asset";
		private static AmbientLifeRenderProfile editorFallback;
		private static bool editorFallbackTried;

		[UnityEditor.InitializeOnEnterPlayMode]
		private static void ResetEditorFallback()
		{
			editorFallback = null;
			editorFallbackTried = false;
		}
#endif
	}
}
