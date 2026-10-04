#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a backdrop build produced.</summary>
	public sealed class SceneBackdropResult
	{
		/// <summary>How far past the scene's edge the backdrop reaches, in metres.</summary>
		public float ReachMetres;
		/// <summary>How far the camera must see to reach its far edge from anywhere in the scene.</summary>
		public float FarPlaneMetres;
		/// <summary>The lowest ground the backdrop holds, in metres above sea level.</summary>
		public float LowestMetres = float.MaxValue;
		public int Vertices;
		public int Meshes;
		public readonly List<string> Wrote = new List<string>();
	}

	/// <summary>
	/// Builds the terrain around a generated scene, out to the horizon: meshes, a colour bake
	/// and a material, under a <see cref="SceneBackdrop"/> so server builds drop it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same ground as the scene, only coarser.</b> Every height is the generator's own ground
	/// (<see cref="SceneHeightField.MetresAt"/>): the scene's grid at its edge, and past it
	/// <see cref="SceneGeneration.AltitudeMetres"/> for the scene's own request — the same planet,
	/// radius, vertical scale and local detail — so at the scene's edge the backdrop meets the
	/// terrain, and past it the land goes on exactly as the globe draws it.
	/// </para>
	/// <para>
	/// <b>Rings that coarsen with distance.</b> A metre of ground ten kilometres away covers a
	/// hundredth of the pixels it does at a hundred, so the vertex spacing grows from 8 m at the
	/// scene's edge to 160 m at the horizon: about 300,000 vertices for a 6.5 km scene, against
	/// 7.9 million for the scene's own terrain. Each ring is four rectangles, and every rectangle
	/// hangs a skirt from all four edges, so the small cracks where a coarse ring meets a fine one
	/// show ground-coloured skirt instead of sky.
	/// </para>
	/// <para>
	/// <b>Meshes, not Unity terrains.</b> Terrain tiles carry heightmaps, colliders and per-tile
	/// draw costs that a surface nobody stands on does not need, and a ring is not square.
	/// </para>
	/// </remarks>
	public static class SceneBackdropBuilder
	{
		/// <summary>How far past the scene's edge the backdrop reaches, in metres, at most.</summary>
		public const float ReachMetres = 10000f;

		/// <summary>
		/// The furthest the backdrop reaches as an angle of the atlas globe, in radians.
		/// </summary>
		/// <remarks>
		/// A scene is a flat piece of a sphere. Ten kilometres on a 30 km atlas globe is a third of
		/// a radian, about 19°, and the further the flat ground runs the more it stretches what it
		/// shows; past about a third of a radian the stretch starts to read. A small atlas globe
		/// gets a shorter backdrop rather than a distorted one.
		/// </remarks>
		public const double MaximumArcRadians = 0.35;

		/// <summary>The rings: how far each reaches past the scene's edge, and its vertex spacing, in metres.</summary>
		private static readonly (float Outer, float Spacing)[] Rings =
		{
			(600f, 8f),
			(2000f, 24f),
			(5000f, 64f),
			(float.MaxValue, 160f),
		};

		/// <summary>Width and height of the colour bake. 1024 over a 27 km backdrop is about 26 m a texel.</summary>
		public const int AlbedoResolution = 1024;

		public const string LitShaderName = "Universal Render Pipeline/Lit";

		/// <summary>How far the backdrop reaches for this scene.</summary>
		public static float ReachFor(SceneGenerationRequest request)
		{
			return Mathf.Min(ReachMetres, (float)(request.ResolvedRadiusKm * 1000.0 * MaximumArcRadians));
		}

		/// <summary>How far the camera must see to reach the backdrop's far edge from anywhere in the scene.</summary>
		public static float FarPlaneFor(TerrainTilePlan plan, float reach)
		{
			float x = plan.WidthMetres + reach, z = plan.DepthMetres + reach;
			return Mathf.Sqrt(x * x + z * z) * 1.02f;
		}

		/// <summary>
		/// Builds the backdrop into <paramref name="scene"/>, writing its assets beside the scene's
		/// terrain.
		/// </summary>
		/// <param name="floorMetres">Altitude of the scene's terrain floor, which its colour bands are measured from.</param>
		/// <param name="reliefMetres">The scene's terrain height span, which its colour bands are measured against.</param>
		/// <param name="groundColour">
		/// The ground's colour at (east, north, altitude in scene metres, steepness in degrees) — the
		/// scene's biomes, so the horizon carries the ground the scene is painted with. Null paints
		/// the plain height bands, which is what a scene no biome fitted is painted with too.
		/// </param>
		/// <param name="ground">
		/// The ground in scene metres at (east, north). Null asks the planet
		/// (<see cref="SceneGeneration.AltitudeMetres"/>), which meets a terrain cut straight from it.
		/// </param>
		public static SceneBackdropResult Build(Scene scene, SceneGenerationRequest request, TerrainTilePlan plan,
			float floorMetres, float reliefMetres, string folder, System.Func<float, float, float, float, Color> groundColour = null,
			System.Func<float, float, float> ground = null)
		{
			if (ground == null)
			{
				var planet = new SceneAltitude(request);
				ground = planet.At;
			}
			var result = new SceneBackdropResult();
			float reach = ReachFor(request);
			result.ReachMetres = reach;
			result.FarPlaneMetres = FarPlaneFor(plan, reach);
			if (reach <= 1f)
			{
				return result;
			}

			float halfW = plan.WidthMetres * 0.5f;
			float halfD = plan.DepthMetres * 0.5f;
			string stem = $"{folder}/{WorldEditorAssets.Sanitize(request.SceneName)} Backdrop";

			Material material = BakeMaterial(ground, halfW, halfD, reach, floorMetres, reliefMetres, stem, result, groundColour);

			var root = new GameObject("Backdrop");
			SceneManager.MoveGameObjectToScene(root, scene);
			root.transform.position = Vector3.zero;
			SceneBackdrop backdrop = root.AddComponent<SceneBackdrop>();
			backdrop.ReachMetres = reach;
			backdrop.FarPlaneMetres = result.FarPlaneMetres;
			backdrop.SceneSizeMetres = new Vector2(plan.WidthMetres, plan.DepthMetres);

			var meshes = new List<Mesh>();
			float inner = 0f;
			for (int ring = 0; ring < Rings.Length && inner < reach; ring++)
			{
				float outer = Mathf.Min(Rings[ring].Outer, reach);
				float spacing = Rings[ring].Spacing;
				float ox = halfW + outer, oz = halfD + outer, ix = halfW + inner, iz = halfD + inner;

				// North and south run the ring's full width; east and west fill between them.
				AddRect(meshes, root, material, request, ground, $"Ring {ring} North", -ox, ox, iz, oz, spacing, halfW, halfD, reach, result);
				AddRect(meshes, root, material, request, ground, $"Ring {ring} South", -ox, ox, -oz, -iz, spacing, halfW, halfD, reach, result);
				AddRect(meshes, root, material, request, ground, $"Ring {ring} East", ix, ox, -iz, iz, spacing, halfW, halfD, reach, result);
				AddRect(meshes, root, material, request, ground, $"Ring {ring} West", -ox, -ix, -iz, iz, spacing, halfW, halfD, reach, result);
				inner = outer;
			}

			// One asset holds every mesh, so a scene's backdrop is one file to move or delete.
			string meshPath = $"{stem}.asset";
			for (int i = 0; i < meshes.Count; i++)
			{
				if (i == 0)
				{
					AssetDatabase.CreateAsset(meshes[i], meshPath);
				}
				else
				{
					AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
				}
			}
			if (meshes.Count > 0)
			{
				result.Wrote.Add(meshPath);
			}
			result.Meshes = meshes.Count;
			return result;
		}

		// ── Geometry ──────────────────────────────────────────────────

		/// <summary>One rectangle of a ring: a regular grid with a skirt down all four edges.</summary>
		private static void AddRect(List<Mesh> meshes, GameObject root, Material material, SceneGenerationRequest request,
			System.Func<float, float, float> ground,
			string name, float x0, float x1, float z0, float z1, float spacing, float halfW, float halfD, float reach,
			SceneBackdropResult result)
		{
			float width = x1 - x0, depth = z1 - z0;
			if (width <= 0.01f || depth <= 0.01f)
			{
				return;
			}
			// Spacing adjusted so the grid lands exactly on the rectangle's edges, where it meets
			// the scene and the next ring.
			int nx = Mathf.Max(1, Mathf.CeilToInt(width / spacing));
			int nz = Mathf.Max(1, Mathf.CeilToInt(depth / spacing));
			float sx = width / nx, sz = depth / nz;
			int columns = nx + 1, rows = nz + 1;

			var heights = new float[columns * rows];
			for (int z = 0; z < rows; z++)
			{
				float wz = z0 + z * sz;
				for (int x = 0; x < columns; x++)
				{
					float h = ground(x0 + x * sx, wz);
					heights[z * columns + x] = h;
					result.LowestMetres = Mathf.Min(result.LowestMetres, h);
				}
			}

			float extentX = halfW + reach, extentZ = halfD + reach;
			int edgeCount = 2 * (nx + nz);
			var positions = new Vector3[columns * rows + edgeCount];
			var normals = new Vector3[positions.Length];
			var uvs = new Vector2[positions.Length];
			for (int z = 0; z < rows; z++)
			{
				for (int x = 0; x < columns; x++)
				{
					int i = z * columns + x;
					float wx = x0 + x * sx, wz = z0 + z * sz;
					positions[i] = new Vector3(wx, heights[i], wz);
					float east = heights[z * columns + Mathf.Min(nx, x + 1)] - heights[z * columns + Mathf.Max(0, x - 1)];
					float north = heights[Mathf.Min(nz, z + 1) * columns + x] - heights[Mathf.Max(0, z - 1) * columns + x];
					float dx = sx * (Mathf.Min(nx, x + 1) - Mathf.Max(0, x - 1));
					float dz = sz * (Mathf.Min(nz, z + 1) - Mathf.Max(0, z - 1));
					normals[i] = new Vector3(-east / dx, 1f, -north / dz).normalized;
					uvs[i] = new Vector2((wx + extentX) / (2f * extentX), (wz + extentZ) / (2f * extentZ));
				}
			}

			var triangles = new List<int>(nx * nz * 6 + edgeCount * 12);
			for (int z = 0; z < nz; z++)
			{
				for (int x = 0; x < nx; x++)
				{
					int a = z * columns + x, b = a + 1, c = a + columns, d = c + 1;
					// Wound for a surface seen from above.
					triangles.Add(a); triangles.Add(c); triangles.Add(b);
					triangles.Add(b); triangles.Add(c); triangles.Add(d);
				}
			}

			/* The skirt: every edge vertex again, hung straight down. Deep enough to cover the
			 * worst crack a coarser neighbour can leave, which grows with the spacing. Wound both
			 * ways, because which side of a skirt faces the camera depends on which neighbour it
			 * is hiding. */
			float skirt = Mathf.Max(12f, Mathf.Max(sx, sz) * 1.5f);
			var ring = new List<int>(edgeCount);
			for (int x = 0; x < nx; x++) ring.Add(x);                                  // south edge, west to east
			for (int z = 0; z < nz; z++) ring.Add(z * columns + nx);                   // east edge, south to north
			for (int x = nx; x > 0; x--) ring.Add(nz * columns + x);                   // north edge, east to west
			for (int z = nz; z > 0; z--) ring.Add(z * columns);                        // west edge, north to south
			int skirtStart = columns * rows;
			for (int k = 0; k < ring.Count; k++)
			{
				int top = ring[k];
				positions[skirtStart + k] = positions[top] - new Vector3(0f, skirt, 0f);
				normals[skirtStart + k] = normals[top];
				uvs[skirtStart + k] = uvs[top];
			}
			for (int k = 0; k < ring.Count; k++)
			{
				int a = ring[k], b = ring[(k + 1) % ring.Count];
				int c = skirtStart + k, d = skirtStart + (k + 1) % ring.Count;
				triangles.Add(a); triangles.Add(b); triangles.Add(c);
				triangles.Add(b); triangles.Add(d); triangles.Add(c);
				triangles.Add(a); triangles.Add(c); triangles.Add(b);
				triangles.Add(b); triangles.Add(c); triangles.Add(d);
			}

			var mesh = new Mesh { name = $"{request.SceneName} Backdrop {name}" };
			mesh.indexFormat = positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.normals = normals;
			mesh.uv = uvs;
			mesh.SetTriangles(triangles, 0, true);
			meshes.Add(mesh);
			result.Vertices += positions.Length;

			var host = new GameObject(name);
			host.transform.SetParent(root.transform, false);
			host.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = host.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			// Far ground: shadows from it never reach the scene's cascades, and it receives none.
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = LightProbeUsage.Off;
			renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
		}

		// ── Colour ────────────────────────────────────────────────────

		/// <summary>
		/// Bakes the backdrop's colour and makes its material.
		/// </summary>
		/// <remarks>
		/// The same bands the scene's terrain is painted with (<see cref="GeneratedTerrainLayers"/>),
		/// measured against the scene's own floor and relief: sand at the bottom of the scene's
		/// range, snow at the top, rock on the steep. Ground beyond the scene that rises above its
		/// highest point reads as snow, which is what a range that much higher would carry.
		/// </remarks>
		private static Material BakeMaterial(System.Func<float, float, float> ground, float halfW, float halfD, float reach,
			float floorMetres, float reliefMetres, string stem, SceneBackdropResult result,
			System.Func<float, float, float, float, Color> groundColour)
		{
			float extentX = halfW + reach, extentZ = halfD + reach;
			int width = AlbedoResolution;
			int height = Mathf.Max(16, Mathf.RoundToInt(AlbedoResolution * extentZ / extentX));
			float texelX = 2f * extentX / width, texelZ = 2f * extentZ / height;

			// The scene itself is under its own terrain; only its outermost texels are ever seen.
			float skipX = halfW - texelX * 2f, skipZ = halfD - texelZ * 2f;

			var heights = new float[width * height];
			for (int z = 0; z < height; z++)
			{
				float wz = -extentZ + (z + 0.5f) * texelZ;
				for (int x = 0; x < width; x++)
				{
					float wx = -extentX + (x + 0.5f) * texelX;
					heights[z * width + x] = Mathf.Abs(wx) < skipX && Mathf.Abs(wz) < skipZ
						? float.NaN
						: ground(wx, wz);
				}
			}

			var pixels = new Color32[width * height];
			var weights = new float[4];
			float relief = Mathf.Max(1f, reliefMetres);
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float h = heights[z * width + x];
					if (float.IsNaN(h))
					{
						pixels[z * width + x] = GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
						continue;
					}
					float east = Neighbour(heights, width, height, x + 1, z, h) - Neighbour(heights, width, height, x - 1, z, h);
					float north = Neighbour(heights, width, height, x, z + 1, h) - Neighbour(heights, width, height, x, z - 1, h);
					float gradient = Mathf.Sqrt(Sq(east / (2f * texelX)) + Sq(north / (2f * texelZ)));
					float steepness = Mathf.Atan(gradient) * Mathf.Rad2Deg;

					if (groundColour != null)
					{
						float wx = -extentX + (x + 0.5f) * texelX;
						float wz = -extentZ + (z + 0.5f) * texelZ;
						pixels[z * width + x] = groundColour(wx, wz, h, steepness);
						continue;
					}
					GeneratedTerrainLayers.Weights(Mathf.Clamp01((h - floorMetres) / relief), steepness, weights);
					Color colour = Color.black;
					for (int i = 0; i < 4; i++)
					{
						colour += GeneratedTerrainLayers.Colours[i] * weights[i];
					}
					colour.a = 1f;
					pixels[z * width + x] = colour;
				}
			}

			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			string texturePath = $"{stem}.png";
			File.WriteAllBytes(texturePath, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
			if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
			{
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.mipmapEnabled = true;
				importer.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(width, height)));
				importer.SaveAndReimport();
			}
			result.Wrote.Add(texturePath);
			Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

			Shader shader = Shader.Find(LitShaderName);
			if (shader == null)
			{
				Debug.LogWarning($"[Scene backdrop] '{LitShaderName}' was not found, so the backdrop has no material.");
				return null;
			}
			var material = new Material(shader) { name = Path.GetFileName(stem) };
			material.SetTexture("_BaseMap", albedo);
			material.SetTexture("_MainTex", albedo);
			material.SetFloat("_Smoothness", 0f);
			// Matte ground: no highlights or sky reflections glinting off a mountain ten kilometres away.
			material.SetFloat("_SpecularHighlights", 0f);
			material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
			material.SetFloat("_EnvironmentReflections", 0f);
			material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
			string materialPath = $"{stem}.mat";
			AssetDatabase.CreateAsset(material, materialPath);
			result.Wrote.Add(materialPath);
			return material;
		}

		private static float Neighbour(float[] heights, int width, int height, int x, int z, float fallback)
		{
			if (x < 0 || z < 0 || x >= width || z >= height)
			{
				return fallback;
			}
			float h = heights[z * width + x];
			return float.IsNaN(h) ? fallback : h;
		}

		private static float Sq(float v) => v * v;
	}
}
#endif
