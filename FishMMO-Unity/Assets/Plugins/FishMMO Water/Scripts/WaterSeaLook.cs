using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// The sea's look at the eye by the biomes of the coast round it (2026-10-18, phase I2h; <see cref="BiomeSeaLooks.Coast"/>),
	/// for the underwater pass (<see cref="WaterSurface"/>): peat-brown off a bog, milky off a glacier, the scene's own sea out
	/// past every coast with a look of its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Cheap by construction.</b> Asked once a frame and only while the eye is under the sea. A scene whose map holds no
	/// biome with a sea look of its own (every scene until a biome's round gives one) is found once, when its map is first read
	/// or a template registers, and costs nothing more: the pass is set exactly as before. Otherwise about a hundred map reads
	/// round the eye, again only once the eye has moved <see cref="MoveMetres"/>, and nothing on the GPU: the pass's own colour,
	/// visibility and murk are set from the answer.
	/// </para>
	/// <para>
	/// The biome map is the scene's WorldSceneSettings', as the inland water's looks read it; the templates are read live, so a
	/// look edited on a template shows when it registers again, without a re-cut.
	/// </para>
	/// </remarks>
	public sealed class WaterSeaLook
	{
		/// <summary>How far the eye moves before the look round it is read again, metres (the reach is 160 m: a few metres is a few per cent of it).</summary>
		public const float MoveMetres = 4f;

		private static int registryVersion;
		private static bool registryHooked;

		private Scene mapScene;
		private SceneBiomeMap map;
		private int mapVersion = -1;
		private bool anyOwn;
		private readonly Dictionary<int, BiomeSeaLook?> looks = new Dictionary<int, BiomeSeaLook?>();
		private Vector2 lastAt;
		private bool hasLast;
		private float lastCoverage;
		private BiomeSeaLook lastCoast;

		private static void OnBiomesChanged() => registryVersion++;

		/// <summary>
		/// The sea at <paramref name="eye"/> in <paramref name="scene"/>: true with the coast's looks blended into the scene's own
		/// (<paramref name="own"/>, <see cref="BiomeSeaLooks.Mix"/>), false (and <paramref name="look"/> the scene's own) where no
		/// biome round the eye has a look of its own.
		/// </summary>
		public bool TryAt(Scene scene, Vector3 eye, BiomeSeaLook own, out BiomeSeaLook look)
		{
			look = own;
			Prepare(scene);
			if (!anyOwn)
			{
				return false;
			}
			var at = new Vector2(eye.x, eye.z);
			if (!hasLast || (at - lastAt).sqrMagnitude > MoveMetres * MoveMetres)
			{
				lastCoast = BiomeSeaLooks.Coast(at.x, at.y, IdAt, LookOf, BiomeSeaLooks.ReachMetres, BiomeSeaLooks.SpacingMetres, out lastCoverage);
				lastAt = at;
				hasLast = true;
			}
			if (!(lastCoverage > 0f))
			{
				return false;
			}
			look = BiomeSeaLooks.Mix(own, lastCoast, lastCoverage);
			return true;
		}

		// ── The surface's coastal look (2026-10-18, I2k) ─────────────

		private Texture2D coastTexture;
		private byte[] coastBuiltFrom;

		/// <summary>
		/// The sea SURFACE's coastal look in <paramref name="scene"/> (BiomeSeaCoast), as the ocean shader reads it: true with the
		/// texture, its world rectangle (xy the minimum, zw one over the size) and half a texel in its uv (xy), false where the
		/// scene's biome map carries none, when the sea draws as it always did and the shader reads no texture.
		/// </summary>
		/// <remarks>
		/// Baked with the map at the cut and every repaint, so it is the templates' looks as they stood then (the underwater look
		/// above reads them live). The texture is made once the map is first read and refilled in place if the map's look is
		/// rewritten (a repaint in the editor), never destroyed from the camera callback this is asked from: <see cref="Dispose"/>.
		/// </remarks>
		public bool SurfaceCoast(Scene scene, out Texture2D texture, out Vector4 rect, out Vector4 texel)
		{
			texture = null;
			rect = Vector4.zero;
			texel = Vector4.zero;
			Prepare(scene);
			if (map == null || !map.HasCoastLook)
			{
				return false;
			}
			BiomeSeaCoast.Bake bake = map.CoastLookBake;
			if (coastTexture == null || coastBuiltFrom != bake.Texels)
			{
				if (coastTexture == null)
				{
					coastTexture = new Texture2D(bake.Width, bake.Height, TextureFormat.RGBA32, false, true)
					{
						name = "Sea coast look",
						hideFlags = HideFlags.HideAndDontSave,
						wrapMode = TextureWrapMode.Clamp,
						filterMode = FilterMode.Bilinear,
					};
				}
				else if (coastTexture.width != bake.Width || coastTexture.height != bake.Height)
				{
					coastTexture.Reinitialize(bake.Width, bake.Height);
				}
				coastTexture.SetPixelData(bake.Texels, 0);
				coastTexture.Apply(false, false);
				coastBuiltFrom = bake.Texels;
			}
			texture = coastTexture;
			rect = new Vector4(bake.Rect.x, bake.Rect.y, 1f / bake.Rect.z, 1f / bake.Rect.w);
			texel = new Vector4(0.5f / bake.Width, 0.5f / bake.Height, 0f, 0f);
			return true;
		}

		/// <summary>Destroys the coastal look's texture. Never from a camera callback.</summary>
		public void Dispose()
		{
			if (coastTexture != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(coastTexture);
				}
				else
				{
					Object.DestroyImmediate(coastTexture);
				}
			}
			coastTexture = null;
			coastBuiltFrom = null;
		}

		private int IdAt(float x, float z) => map != null ? map.IDAt(new Vector3(x, 0f, z)) : 0;

		private BiomeSeaLook? LookOf(int id)
		{
			if (id == 0)
			{
				return null;
			}
			if (!looks.TryGetValue(id, out BiomeSeaLook? look))
			{
				look = BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) ? BiomeSeaLooks.Of(biome) : null;
				looks[id] = look;
			}
			return look;
		}

		/// <summary>Finds the scene's biome map, and whether any biome on it has a sea look of its own: once a scene and a registration.</summary>
		private void Prepare(Scene scene)
		{
			if (!registryHooked)
			{
				BiomeRegistry.Changed += OnBiomesChanged;
				registryHooked = true;
			}
			if (mapVersion == registryVersion && mapScene == scene)
			{
				return;
			}
			mapScene = scene;
			mapVersion = registryVersion;
			map = SceneMap(scene);
			looks.Clear();
			hasLast = false;
			anyOwn = false;
			if (map == null || map.BiomeIDs == null)
			{
				return;
			}
			var seen = new HashSet<int>();
			foreach (int id in map.BiomeIDs)
			{
				if (seen.Add(id) && LookOf(id) is BiomeSeaLook l && BiomeSeaLooks.OwnLook(l))
				{
					anyOwn = true;
					break;
				}
			}
		}

		/// <summary>The biome map of a scene (its WorldSceneSettings'), or null.</summary>
		private static SceneBiomeMap SceneMap(Scene scene)
		{
			if (!scene.IsValid())
			{
				return null;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				WorldSceneSettings settings = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (settings != null)
				{
					return settings.BiomeMap;
				}
			}
			return null;
		}
	}
}
