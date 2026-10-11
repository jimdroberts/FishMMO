using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Hands the GPU a scene's paths: the page table and atlas of its <see cref="ScenePathField"/>, baked by the cut beside
	/// the terrain, as globals the terrain, the grass and the details read (FishGroundPaths.hlsl).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Baked, not built.</b> The cut routes, carves and rasterises the paths (Jim, 2026-10-10: "paths should be baked into
	/// the cut"); this only binds the two textures it wrote and the numbers that address them.
	/// </para>
	/// <para>
	/// <b>One scene's field at a time</b>: the most recently enabled binder's. A world scene is loaded alone; the ground past
	/// its edge is the backdrop, which draws no paths.
	/// </para>
	/// <para>
	/// <b>Edit mode as play mode</b> (<c>ExecuteAlways</c>): the Scene view, the world map's overhead capture and the
	/// player all see the same paths.
	/// </para>
	/// <para>
	/// <b>Client only.</b> The object carries <see cref="ClientOnlyObject"/>, so server builds strip it and the textures
	/// with it; in a server player this compiles to its serialized fields.
	/// </para>
	/// <para>
	/// The path textures' slices in the terrain arrays differ per scene (each scene's palette), so they travel in each
	/// tile's property block (TerrainArrayBinder, <see cref="LayersOf"/>), not as a global.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ClientOnlyObject))]
	public sealed class ScenePathSurfaceBinder : MonoBehaviour
	{
		public const string ObjectName = "Path Surface";

		[Tooltip("The scene's points of interest, whose Paths were baked into the textures below.")]
		public ScenePointsOfInterest Points;
		[Tooltip("The baked page table: a texel a 16 m page, r/g its place in the atlas, a set where a page is stored.")]
		public Texture2D Table;
		[Tooltip("The baked atlas of pages: edge distance, half width, wear and surface, half a metre a texel.")]
		public Texture2D Atlas;
		[Tooltip("xy the world x/z of page (0, 0), z 1 / page metres, w 1 when baked.")]
		public Vector4 Parameters;
		[Tooltip("xy the page table's size in pages, z 1 / the atlas's side in texels, w stored texels a page side.")]
		public Vector4 Size;

		/// <summary>True when the bake left something to bind.</summary>
		public bool IsBaked => Table != null && Atlas != null && Parameters.w > 0.5f;

#if !UNITY_SERVER || UNITY_EDITOR
		private static readonly List<ScenePathSurfaceBinder> active = new List<ScenePathSurfaceBinder>();

		private static readonly int TableId = Shader.PropertyToID("_FishPathTable");
		private static readonly int AtlasId = Shader.PropertyToID("_FishPathAtlas");
		private static readonly int ParamsId = Shader.PropertyToID("_FishPathParams");
		private static readonly int SizeId = Shader.PropertyToID("_FishPathSize");

		private static Texture2D blank;

		/// <summary>
		/// The enabled binder in a scene, or null. From the enabled list, not the scene's roots: a terrain binds while its
		/// scene is still loading (OnEnable), when the scene does not yet count as loaded and its roots cannot be asked
		/// — which left every tile with no path slices (2026-10-10: paths drawn cyan in the debug view).
		/// </summary>
		private static ScenePathSurfaceBinder Of(Scene scene)
		{
			for (int i = active.Count - 1; i >= 0; i--)
			{
				if (active[i] != null && active[i].gameObject.scene == scene && active[i].Points != null)
				{
					return active[i];
				}
			}
			return null;
		}

		/// <summary>
		/// The fallback path slices for a scene's terrain (x earth, y road, z cobbles; −1 none), or all −1 when it has no
		/// enabled binder.
		/// </summary>
		public static Vector4 LayersOf(Scene scene)
		{
			ScenePathSurfaceBinder binder = Of(scene);
			return binder != null ? binder.Points.PathLayers : new Vector4(-1f, -1f, -1f, -1f);
		}

		/// <summary>Per terrain layer, its biome's path ground slice, packed four to a vector (eight vectors; −1 none).</summary>
		public static Vector4[] EarthMapOf(Scene scene)
		{
			ScenePathSurfaceBinder binder = Of(scene);
			return Pack(binder != null ? binder.Points.PathEarthMap : null);
		}

		/// <summary>Per terrain layer, its biome's road ground slice, packed as <see cref="EarthMapOf"/>.</summary>
		public static Vector4[] RoadMapOf(Scene scene)
		{
			ScenePathSurfaceBinder binder = Of(scene);
			return Pack(binder != null ? binder.Points.PathRoadMap : null);
		}

		/// <summary>The vectors a layer map is pushed as: always eight, so the shader array's size never changes.</summary>
		public const int MapVectors = 8;

		private static Vector4[] Pack(float[] map)
		{
			var packed = new Vector4[MapVectors];
			for (int v = 0; v < MapVectors; v++)
			{
				float At(int i) => map != null && i < map.Length ? map[i] : -1f;
				packed[v] = new Vector4(At(v * 4), At(v * 4 + 1), At(v * 4 + 2), At(v * 4 + 3));
			}
			return packed;
		}

		/// <summary>The binder whose field is bound, or null.</summary>
		public static ScenePathSurfaceBinder Current
		{
			get
			{
				for (int i = active.Count - 1; i >= 0; i--)
				{
					if (active[i] != null && active[i].IsBaked)
					{
						return active[i];
					}
				}
				return null;
			}
		}

		private void OnEnable()
		{
			active.Remove(this);
			active.Add(this);
			Publish();
			// The scene's tiles may have bound before this enabled: push them the path slices now.
			TerrainArrayBinder.RepushScene(gameObject.scene);
		}

		private void OnDisable()
		{
			active.Remove(this);
			Publish();
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			if (isActiveAndEnabled)
			{
				UnityEditor.EditorApplication.delayCall += () =>
				{
					if (this != null && isActiveAndEnabled)
					{
						Publish();
					}
				};
			}
		}
#endif

		private static Texture2D Blank
		{
			get
			{
				if (blank == null)
				{
					blank = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
					{
						name = "Path Blank",
						filterMode = FilterMode.Point,
						hideFlags = HideFlags.HideAndDontSave,
					};
					blank.SetPixel(0, 0, new Color(1f, 0f, 0f, 0f));
					blank.Apply(false, false);
				}
				return blank;
			}
		}

		/// <summary>Binds the current binder's field as the globals (or none).</summary>
		public static void Publish()
		{
			active.RemoveAll(b => b == null);
			ScenePathSurfaceBinder current = Current;
			Shader.SetGlobalTexture(TableId, current != null ? current.Table : Blank);
			Shader.SetGlobalTexture(AtlasId, current != null ? current.Atlas : Blank);
			Shader.SetGlobalVector(ParamsId, current != null ? current.Parameters : Vector4.zero);
			Shader.SetGlobalVector(SizeId, current != null ? current.Size : Vector4.zero);
		}

		private static readonly int DebugId = Shader.PropertyToID("_FishPathDebug");

		/// <summary>The terrain draws every path flat magenta (cyan where its surface slices are missing).</summary>
		public static bool DebugView
		{
			get => Shader.GetGlobalFloat(DebugId) > 0.5f;
			set => Shader.SetGlobalFloat(DebugId, value ? 1f : 0f);
		}

		/// <summary>What is bound, for the diagnostics: the binder, its field, the globals and the active scene's slices.</summary>
		public static string Describe()
		{
			ScenePathSurfaceBinder current = Current;
			Vector4 bound = Shader.GetGlobalVector(ParamsId);
			string binder = current == null
				? $"no baked binder enabled ({active.Count} enabled)"
				: $"'{current.gameObject.scene.name}': {(current.Points != null ? current.Points.Paths.Count : 0)} way(s), table {current.Table?.width}x{current.Table?.height}, atlas {current.Atlas?.width}";
			return $"{binder}; globals origin ({bound.x:0}, {bound.y:0}) on {bound.w:0}; active scene's slices {LayersOf(SceneManager.GetActiveScene())}";
		}

		/// <summary>
		/// Binds the field to a compute kernel (compute shaders do not read the render globals): the grass and the detail
		/// scatter call this before they generate.
		/// </summary>
		public static void BindCompute(CommandBuffer cmd, ComputeShader compute, int kernel)
		{
			ScenePathSurfaceBinder current = Current;
			cmd.SetComputeTextureParam(compute, kernel, TableId, current != null ? current.Table : Blank);
			cmd.SetComputeTextureParam(compute, kernel, AtlasId, current != null ? current.Atlas : Blank);
			cmd.SetComputeVectorParam(compute, ParamsId, current != null ? current.Parameters : Vector4.zero);
			cmd.SetComputeVectorParam(compute, SizeId, current != null ? current.Size : Vector4.zero);
		}
#endif
	}
}
