using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// The scene's lava lakes and rivers (<see cref="SceneHydrology.Lake.Lava"/>: a lava tube's pits and the river down its
	/// floor), drawn on the same lake and river meshes as the water with the lava's look (FishMMO/Water/Inland Lava).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Lava, not water, everywhere it shows.</b> No fall sheet, spray or roar (FindFalls leaves lava out: a lava fall is
	/// its own ribbon, molten rock pouring down its run), no foam, wakes, looks or solved flow: none of the water's
	/// machinery. Opaque, so where a river runs into its lake the depth test settles it, the lake laid
	/// <see cref="LavaLakeSinkMetres"/> under its level so the river's surface shows over it rather than fighting it.
	/// </para>
	/// <para>
	/// <b>Lit by itself in the dark.</b> Each vertex carries how much of the open sky reaches it (uv6.x), from how far under
	/// the terrain it lies, on the curve the lit surfaces' FishUnderground uses: deep in a cave the sun and the sky add
	/// nothing to the crust and the eye adapts to the lava's own glow.
	/// </para>
	/// </remarks>
	public sealed partial class InlandWaterRenderer
	{
		[Header("Lava")]
		[Tooltip("The lava lakes' and rivers' material (FishMMO/Water/Inland Lava). Without it they are not drawn.")]
		public Material LavaMaterial;

		/// <summary>How far under its level a lava lake's surface is laid, metres: a river running into it shows over it.</summary>
		public const float LavaLakeSinkMetres = 0.03f;

		/// <summary>The UV channels a lava vertex's open sky and plate scale (6), and its kind and speed (7) ride in (FishLavaPasses.hlsl).</summary>
		private const int SkyChannel = 6;
		private const int KindChannel = 7;

		/// <summary>What a lava vertex is, for the shader (uv7.x): a lake's surface, a river's ribbon, a pour down a fall.</summary>
		public const float LakeKind = 0f, RiverKind = 1f, PourKind = 2f;

		/// <summary>A channel this wide carries plates the material's own size; a narrower one smaller, in proportion (down to 0.3).</summary>
		public const float FullPlateWidthMetres = 4f;

		/// <summary>The least drop a run of falling points must make to be drawn as a pour rather than a steep ribbon, metres.</summary>
		public const float MinPourDropMetres = 0.5f;

		private static bool warnedNoLavaMaterial;

		/// <summary>The plates' scale on a lava river this wide (FishLavaFlow.scale): smaller in a narrow channel.</summary>
		public static float RiverPlateScale(float width) => Mathf.Clamp(width / FullPlateWidthMetres, 0.3f, 1f);

		/// <summary>The plates' scale on a lava lake, by how much lava it holds: a small pit's finer than a chamber's lake.</summary>
		public static float LakePlateScale(SceneHydrology.Lake lake)
		{
			int cells = 0;
			if (lake.Mask != null)
			{
				foreach (byte b in lake.Mask)
				{
					for (int bit = b; bit != 0; bit &= bit - 1)
					{
						cells++;
					}
				}
			}
			float across = Mathf.Sqrt(cells) * lake.MaskCell;
			return Mathf.Clamp(across / 10f, 0.5f, 1.5f);
		}

		/// <summary>Draws a lava lake: its lake surface, a little under its level.</summary>
		private void AddLavaLake(SceneHydrology.Lake lake, SceneWaterBodies water)
		{
			if (!LavaReady())
			{
				return;
			}
			Mesh mesh = LakeMesh(lake, water);
			if (mesh == null)
			{
				return;
			}
			var positions = new List<Vector3>(mesh.vertexCount);
			mesh.GetVertices(positions);
			for (int i = 0; i < positions.Count; i++)
			{
				positions[i] -= new Vector3(0f, LavaLakeSinkMetres, 0f);
			}
			mesh.SetVertices(positions);
			mesh.RecalculateBounds();
			float scale = LakePlateScale(lake);
			var kinds = new List<Vector2>(positions.Count);
			var scales = new List<float>(positions.Count);
			for (int i = 0; i < positions.Count; i++)
			{
				kinds.Add(new Vector2(LakeKind, 0f));
				scales.Add(scale);
			}
			AddLava($"Lava Lake {lake.Id}", mesh, positions, kinds, scales);
		}

		/// <summary>
		/// Draws a lava river: its ribbon where it runs, and a pour (<see cref="BuildPour"/>) down each fall in it
		/// (<see cref="PourSpans"/>), the ribbon given up over the fall's run.
		/// </summary>
		private void AddLavaRiver(SceneHydrology.River river, SceneWaterBodies water, List<Fall> falls)
		{
			if (!LavaReady())
			{
				return;
			}
			int n = river.Points.Length;
			var along = new float[n];
			for (int i = 1; i < n; i++)
			{
				Vector3 step = river.Points[i] - river.Points[i - 1];
				step.y = 0f;
				along[i] = along[i - 1] + step.magnitude;
			}
			List<Vector2Int> spans = PourSpans(river);
			Mesh mesh = RiverMesh(river, water, falls);
			if (mesh != null && StripSpans(mesh, along, spans))
			{
				var positions = new List<Vector3>(mesh.vertexCount);
				mesh.GetVertices(positions);
				// The ribbon's banks (uv4): the half-width and the river's mean speed at each row.
				var banks = new List<Vector4>(positions.Count);
				mesh.GetUVs(4, banks);
				var kinds = new List<Vector2>(positions.Count);
				var scales = new List<float>(positions.Count);
				for (int i = 0; i < positions.Count; i++)
				{
					Vector4 bank = i < banks.Count ? banks[i] : new Vector4(2f, 0.5f, 0f, 0f);
					kinds.Add(new Vector2(RiverKind, Mathf.Max(0f, bank.y)));
					scales.Add(RiverPlateScale(2f * bank.x));
				}
				AddLava($"Lava River {river.Id}", mesh, positions, kinds, scales);
			}
			foreach (Vector2Int span in spans)
			{
				var positions = new List<Vector3>();
				var frames = new List<Vector2>();
				var kinds = new List<Vector2>();
				var scales = new List<float>();
				var colours = new List<Color32>();
				var indices = new List<int>();
				BuildPour(river, span.x, span.y, along[span.x], positions, frames, kinds, scales, colours, indices);
				if (indices.Count == 0)
				{
					continue;
				}
				var pour = new Mesh { name = $"Lava Pour {river.Id}.{span.x}", hideFlags = HideFlags.DontSave };
				pour.SetVertices(positions);
				pour.SetUVs(0, frames);
				pour.SetUVs(1, new List<Vector2>(new Vector2[positions.Count]));
				pour.SetColors(colours);
				pour.SetTriangles(indices, 0);
				pour.RecalculateNormals();
				pour.RecalculateBounds();
				AddLava(pour.name, pour, positions, kinds, scales);
			}
		}

		/// <summary>Surface slope (drop over run) from which a falling stretch of lava pours: CaveLavaWater.FallSlope's.</summary>
		public const float PourSlope = 0.15f;

		/// <summary>
		/// The runs of a lava river drawn as pours: each run of steep segments (<see cref="PourSlope"/> or more) between falling
		/// points (reach 4), from its lip to where it lands, dropping at least <see cref="MinPourDropMetres"/>. x the lip, y the
		/// foot. The gentle runs either side stay ribbon, though the writer marks their end points falling too.
		/// </summary>
		public static List<Vector2Int> PourSpans(SceneHydrology.River river)
		{
			var spans = new List<Vector2Int>();
			int n = river.Points != null ? river.Points.Length : 0;
			if (n < 2 || river.Reach == null || river.Reach.Length != n)
			{
				return spans;
			}
			bool Steep(int i)
			{
				if (river.Reach[i] != FallReach || river.Reach[i + 1] != FallReach)
				{
					return false;
				}
				Vector3 a = river.Points[i], b = river.Points[i + 1];
				float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				return a.y - b.y >= PourSlope * Mathf.Max(run, 0.01f);
			}
			int s = 0;
			while (s + 1 < n)
			{
				if (!Steep(s))
				{
					s++;
					continue;
				}
				int last = s;
				while (last + 2 < n && Steep(last + 1))
				{
					last++;
				}
				int lip = s, foot = last + 1;
				if (river.Points[lip].y - river.Points[foot].y >= MinPourDropMetres)
				{
					spans.Add(new Vector2Int(lip, foot));
				}
				s = last + 1;
			}
			return spans;
		}

		/// <summary>Takes out of a ribbon every triangle wholly within a pour's run (by its rows' distance along, uv0.x). False when none is left.</summary>
		private static bool StripSpans(Mesh mesh, float[] along, List<Vector2Int> spans)
		{
			if (spans.Count == 0)
			{
				return true;
			}
			var frames = new List<Vector2>(mesh.vertexCount);
			mesh.GetUVs(0, frames);
			int[] triangles = mesh.GetTriangles(0);
			var kept = new List<int>(triangles.Length);
			bool Within(int v, Vector2Int span) => frames[v].x >= along[span.x] - 1e-3f && frames[v].x <= along[span.y] + 1e-3f;
			for (int t = 0; t + 2 < triangles.Length; t += 3)
			{
				bool inPour = false;
				foreach (Vector2Int span in spans)
				{
					if (Within(triangles[t], span) && Within(triangles[t + 1], span) && Within(triangles[t + 2], span))
					{
						inPour = true;
						break;
					}
				}
				if (!inPour)
				{
					kept.Add(triangles[t]);
					kept.Add(triangles[t + 1]);
					kept.Add(triangles[t + 2]);
				}
			}
			mesh.SetTriangles(kept, 0);
			return kept.Count > 0;
		}

		/// <summary>Columns across a pour's rounded front, and rows down it.</summary>
		private const int PourColumns = 8, PourRows = 20;

		/// <summary>
		/// A pour: molten rock going over a lip and down into its pool, between the river's points <paramref name="lip"/> and
		/// <paramref name="foot"/>. A sheet curving over the lip (level as it leaves it, plunging as it lands), its front rounded
		/// like a rope of melt rather than a flat pane, necking a little as it falls; and at its foot a skirt of melt spreading
		/// out over the pool on the side away from the rock, ragged at its rim. Two points make the curve a quarter ellipse,
		/// more follow the river's own line. Its frame (uv0) is metres down the run from <paramref name="alongLip"/> and metres
		/// across; uv6.y its half-width there, uv7 (<see cref="PourKind"/>, its speed).
		/// </summary>
		public static void BuildPour(SceneHydrology.River river, int lip, int foot, float alongLip, List<Vector3> positions, List<Vector2> frames,
			List<Vector2> kinds, List<float> scales, List<Color32> colours, List<int> indices)
		{
			Vector3[] points = river.Points;
			Vector3 top = points[lip], bottom = points[foot];
			float width = river.Width != null && lip < river.Width.Length ? Mathf.Max(0.5f, river.Width[lip]) : 2f;
			float half = 0.5f * width;
			Vector3 flat = bottom - top;
			flat.y = 0f;
			float run = flat.magnitude;
			Vector3 ahead = run > 0.01f ? flat / run : Vector3.forward;
			if (run <= 0.01f && lip > 0)
			{
				Vector3 before = points[lip] - points[lip - 1];
				before.y = 0f;
				ahead = before.sqrMagnitude > 1e-6f ? before.normalized : ahead;
			}
			float drop = Mathf.Max(0f, top.y - bottom.y);
			// A viscous pour falls slower than water would, and it is still quick: a few metres a second.
			float speed = Mathf.Clamp(0.4f * Mathf.Sqrt(2f * 9.81f * drop), 1.5f, 5f);

			// The line down its middle.
			var line = new List<Vector3>(PourRows + 1);
			if (foot - lip == 1)
			{
				for (int k = 0; k <= PourRows; k++)
				{
					float a = 0.5f * Mathf.PI * k / PourRows;
					line.Add(top + ahead * (run * Mathf.Sin(a)) - Vector3.up * (drop * (1f - Mathf.Cos(a))));
				}
			}
			else
			{
				int segments = foot - lip;
				int per = Mathf.Max(2, Mathf.CeilToInt(PourRows / (float)segments));
				for (int seg = 0; seg < segments; seg++)
				{
					Vector3 p0 = points[Mathf.Max(lip, lip + seg - 1)], p1 = points[lip + seg], p2 = points[lip + seg + 1], p3 = points[Mathf.Min(foot, lip + seg + 2)];
					for (int k = 0; k < per; k++)
					{
						// Never over the lip nor under the landing, wherever the spline swings.
						Vector3 at = CatmullRom(p0, p1, p2, p3, k / (float)per);
						at.y = Mathf.Clamp(at.y, bottom.y, top.y);
						line.Add(at);
					}
				}
				line.Add(bottom);
			}

			// The sheet: rows down the line, a rounded front across each.
			int rows = line.Count;
			float down = 0f;
			int first = positions.Count;
			for (int r = 0; r < rows; r++)
			{
				if (r > 0)
				{
					down += Vector3.Distance(line[r], line[r - 1]);
				}
				Vector3 tangent = (line[Mathf.Min(rows - 1, r + 1)] - line[Mathf.Max(0, r - 1)]).normalized;
				Vector3 level = tangent;
				level.y = 0f;
				Vector3 across = Vector3.Cross(Vector3.up, level.sqrMagnitude > 1e-4f ? level.normalized : ahead).normalized;
				Vector3 front = Vector3.Cross(tangent, across).normalized;
				if (Vector3.Dot(front, ahead + 0.5f * Vector3.up) < 0f)
				{
					front = -front;
				}
				float s = r / (float)(rows - 1);
				float rowHalf = half * (1f - 0.22f * Mathf.Sin(Mathf.PI * s));
				float thick = 0.35f * rowHalf;
				for (int c = 0; c <= PourColumns; c++)
				{
					float a = -1f + 2f * c / PourColumns;
					float offset = a * rowHalf;
					positions.Add(line[r] + across * offset + front * (thick * Mathf.Sqrt(Mathf.Max(0f, 1f - a * a))));
					frames.Add(new Vector2(alongLip + down, offset));
					kinds.Add(new Vector2(PourKind, speed));
					scales.Add(rowHalf);
					colours.Add(new Color32(255, 255, 255, 255));
				}
			}
			for (int r = 0; r + 1 < rows; r++)
			{
				int row = first + r * (PourColumns + 1), next = row + PourColumns + 1;
				for (int c = 0; c < PourColumns; c++)
				{
					indices.Add(row + c); indices.Add(next + c); indices.Add(next + c + 1);
					indices.Add(row + c); indices.Add(next + c + 1); indices.Add(row + c + 1);
				}
			}

			// The skirt: the melt landing and spreading over the pool, away from the rock, slowing as it goes; ragged at its rim.
			const int spokes = 16, rings = 4;
			const float sweep = 115f * Mathf.Deg2Rad;
			float endHalf = half * 1.0f;
			float inner = 0.5f * endHalf, outer = 2.6f * endHalf;
			Vector3 centre = bottom + ahead * (0.1f * half) + Vector3.up * 0.02f;
			int skirt = positions.Count;
			uint seed = (uint)river.Id * 2654435761u ^ (uint)lip * 40503u;
			for (int j = 0; j <= spokes; j++)
			{
				float angle = Mathf.Lerp(-sweep, sweep, j / (float)spokes);
				Vector3 spoke = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up) * ahead;
				seed = seed * 1664525u + 1013904223u;
				float ragged = 0.8f + 0.45f * ((seed >> 8) & 0xFFFF) / 65535f;
				for (int q = 0; q <= rings; q++)
				{
					float t = q / (float)rings;
					float radius = Mathf.Lerp(inner, outer * ragged, t);
					Vector3 at = centre + spoke * radius;
					at.y = centre.y + 0.12f * (1f - t) * (1f - t);
					positions.Add(at);
					frames.Add(new Vector2(alongLip + down + (radius - inner), angle * radius));
					kinds.Add(new Vector2(PourKind, speed * 0.5f * (1f - t)));
					scales.Add(10f * outer);
					colours.Add(new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * (1f - t))));
				}
			}
			for (int j = 0; j < spokes; j++)
			{
				int a = skirt + j * (rings + 1), b = a + rings + 1;
				for (int q = 0; q < rings; q++)
				{
					indices.Add(a + q); indices.Add(a + q + 1); indices.Add(b + q + 1);
					indices.Add(a + q); indices.Add(b + q + 1); indices.Add(b + q);
				}
			}
		}

		private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			float t2 = t * t, t3 = t2 * t;
			return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
		}

		private bool LavaReady()
		{
			if (LavaMaterial != null)
			{
				return true;
			}
			if (!warnedNoLavaMaterial)
			{
				warnedNoLavaMaterial = true;
				Debug.LogWarning($"[Inland water] '{name}' has lava lakes or rivers and no Lava Material: they are not drawn. Repaint the scene (it sets FishMMO/Water/Inland Lava's material).");
			}
			return false;
		}

		/// <summary>Adds a lava surface: its open sky and plate scale (uv6) and its kind and speed (uv7) on every vertex.</summary>
		private void AddLava(string name, Mesh mesh, List<Vector3> positions, List<Vector2> kinds, List<float> scales)
		{
			var sky = new List<Vector2>(positions.Count);
			Scene scene = gameObject.scene;
			for (int i = 0; i < positions.Count; i++)
			{
				sky.Add(new Vector2(OpenSky(scene, positions[i]), scales[i]));
			}
			mesh.SetUVs(SkyChannel, sky);
			mesh.SetUVs(KindChannel, kinds);
			mesh.name = name;
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.position = Vector3.zero;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = LavaMaterial;
			// No shadow: a flat sheet shadows only what it hides (FishLava.shader). It takes the cave's own shadows.
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
			built.Add(go);
		}

		/// <summary>
		/// How much of the open sky reaches a point, 0..1: 1 within six metres under the terrain over it, none from twenty
		/// (FishGroundMap.hlsl's FishUnderground, from the terrain here rather than the camera's ground map). 1 off every terrain.
		/// </summary>
		public static float OpenSky(Scene scene, Vector3 p)
		{
			float ground = float.NegativeInfinity;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null || (scene.IsValid() && terrain.gameObject.scene != scene))
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
				if (p.x < origin.x || p.z < origin.z || p.x > origin.x + size.x || p.z > origin.z + size.z)
				{
					continue;
				}
				ground = Mathf.Max(ground, terrain.SampleHeight(p) + origin.y);
			}
			return float.IsNegativeInfinity(ground) ? 1f : OpenSkyUnder(ground - p.y);
		}

		/// <summary>The open sky at <paramref name="depth"/> metres under the ground: 1 to six metres, none from twenty.</summary>
		public static float OpenSkyUnder(float depth) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 20f, depth));
	}
}
