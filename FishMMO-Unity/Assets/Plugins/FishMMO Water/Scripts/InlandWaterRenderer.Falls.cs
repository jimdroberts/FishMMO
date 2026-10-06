using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// The falls: where a river drops (its <see cref="SceneHydrology.River.Reach"/> marks a fall), a sheet of falling
	/// water from its lip to its foot, spray and mist at its foot, and the foot published to the inland water's
	/// shader, which churns the pool there white.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The sheet's shape.</b> Water leaves the lip at the river's speed and falls under gravity; at every point
	/// it is the higher of that arc and the river's own surface there, so where the rock drops away faster than the
	/// water falls the sheet hangs free of it, and where it does not the water rides down the rock as a cascade.
	/// The river's ribbon gives the falling stretch to the sheet.
	/// </para>
	/// <para>
	/// <b>Stateless spray.</b> Every droplet and wisp of mist is a quad whose whole life is worked out in the
	/// shader from its seed and the inland clock: nothing is simulated or stored per frame.
	/// </para>
	/// </remarks>
	public sealed partial class InlandWaterRenderer
	{
		[Header("Falls")]
		[Tooltip("The falls' sheets (FishMMO/Water/Waterfall).")]
		public Material FallMaterial;

		[Tooltip("The spray and mist at the foot of each fall (FishMMO/Water/Waterfall Spray).")]
		public Material SprayMaterial;

		[Tooltip("The least drop, metres, that is drawn as a fall; less is left to the river's own rapids.")]
		public float MinFallMetres = 1.5f;

		[Tooltip("Spray particles per metre of fall width, per metre of drop (capped).")]
		public float SprayDensity = 14f;

		/// <summary>The most falls the inland water's shader churns at once.</summary>
		public const int MaxShaderFalls = 16;

		private static readonly int FallsId = Shader.PropertyToID("_FishFalls");
		private static readonly int FallCountId = Shader.PropertyToID("_FishFallCount");
		private readonly Vector4[] shaderFalls = new Vector4[MaxShaderFalls];

		/// <summary>One fall of a river: its lip and foot points on the river's line, and what it does.</summary>
		public struct Fall
		{
			public int River;
			/// <summary>The last point before the water drops, and the first after it lands.</summary>
			public int Lip;
			public int Foot;
			/// <summary>
			/// The first point down at the pool's level. A knickpoint gathers the drop into a short step, so the river is
			/// at the pool well before <see cref="Foot"/>: the pool is drawn from here, and its row before, lowered to the
			/// pool, carries the water back under the curtain to the rock.
			/// </summary>
			public int Plunge;
			public Vector3 LipPoint;
			public Vector3 FootPoint;
			/// <summary>
			/// Where the curtain meets the pool: its last row, pushed out from the rock with it. The pool's boil and the
			/// spray stand here, not at <see cref="FootPoint"/>, which can lie metres downstream of where the water lands.
			/// </summary>
			public Vector3 Landing;
			/// <summary>How far its surface drops, metres.</summary>
			public float Drop;
			/// <summary>
			/// How wide the water is where it goes over, metres: the curtain's width. The river is wider round the foot, where
			/// the plunge pool has scoured a basin, but the water that falls is the water at the lip.
			/// </summary>
			public float Width;
			/// <summary>The water's speed at the lip, m/s.</summary>
			public float Speed;
			/// <summary>How far round its foot the pool churns, metres.</summary>
			public float PoolRadius;
		}

		/// <summary>The falls the drawn sheets stand for, for tools and tests.</summary>
		public IReadOnlyList<Fall> Falls => falls;
		private readonly List<Fall> falls = new List<Fall>();

		/// <summary>
		/// Every fall in a scene's rivers: each run of points the river marks as falling (gaps of one point bridged),
		/// from the point before it to the point after, where its surface drops at least <paramref name="minDrop"/>.
		/// </summary>
		public static List<Fall> FindFalls(SceneHydrology hydrology, float minDrop)
		{
			var found = new List<Fall>();
			if (hydrology == null)
			{
				return found;
			}
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				int n = river.Points != null ? river.Points.Length : 0;
				if (!river.Perennial || n < 3 || river.Reach == null || river.Reach.Length != n)
				{
					continue;
				}
				int i = 0;
				while (i < n)
				{
					if (river.Reach[i] != FallReach)
					{
						i++;
						continue;
					}
					int last = i;
					while (last + 1 < n && (river.Reach[last + 1] == FallReach || (last + 2 < n && river.Reach[last + 2] == FallReach)))
					{
						last++;
					}
					int lip = Mathf.Max(0, i - 1), foot = Mathf.Min(n - 1, last + 1);
					float drop = river.Points[lip].y - river.Points[foot].y;
					if (drop >= minDrop)
					{
						float width = river.Width != null && lip < river.Width.Length ? river.Width[lip] : 4f;
						float poolWidth = width;
						for (int k = lip; k <= foot; k++)
						{
							poolWidth = Mathf.Max(poolWidth, river.Width != null && k < river.Width.Length ? river.Width[k] : 4f);
						}
						float speed = river.Speed != null && lip < river.Speed.Length ? river.Speed[lip] : 1f;
						int plunge = foot;
						for (int k = lip + 1; k <= foot; k++)
						{
							if (river.Points[k].y <= river.Points[foot].y + PoolLevelMetres)
							{
								plunge = k;
								break;
							}
						}
						found.Add(new Fall
						{
							River = river.Id,
							Lip = lip,
							Foot = foot,
							Plunge = plunge,
							Landing = river.Points[plunge],
							LipPoint = river.Points[lip],
							FootPoint = river.Points[foot],
							Drop = drop,
							Width = width,
							// At least a walking pace over the lip: a stream's mean speed undersells the jet that leaves a ledge.
							Speed = Mathf.Max(1.2f, speed),
							// A plunge pool churns out to about its width and a share of the drop: a higher fall stirs more water.
							PoolRadius = Mathf.Max(3f, 0.9f * poolWidth + 0.35f * drop),
						});
					}
					i = last + 1;
				}
			}
			return found;
		}

		/// <summary>The value <see cref="SceneHydrology.River.Reach"/> holds at a falling point (the generator's RiverReach.Fall).</summary>
		public const byte FallReach = 4;

		/// <summary>How near the foot's level a point's surface must be to count as the pool, metres.</summary>
		private const float PoolLevelMetres = 0.5f;

		/// <summary>True for a point whose ribbon the sheet stands in for: between the lip and the row before the plunge.</summary>
		private static bool InsideFall(List<Fall> falls, int river, int point)
		{
			foreach (Fall fall in falls)
			{
				if (fall.River == river && point > fall.Lip && point < fall.Plunge - 1)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The row before a fall's plunge, which is drawn lowered to the pool (<paramref name="poolLevel"/>) so the pool
		/// reaches back under the curtain; false for every other point. Never the lip itself.
		/// </summary>
		private static bool IsPlungeRow(List<Fall> falls, SceneHydrology.River river, int point, out float poolLevel)
		{
			foreach (Fall fall in falls)
			{
				if (fall.River == river.Id && point == fall.Plunge - 1 && point > fall.Lip)
				{
					poolLevel = river.Points[fall.Plunge].y;
					return true;
				}
			}
			poolLevel = 0f;
			return false;
		}

		/// <summary>True for a fall's lip point.</summary>
		private static bool IsLip(List<Fall> falls, int river, int point)
		{
			foreach (Fall fall in falls)
			{
				if (fall.River == river && point == fall.Lip)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Whether the ribbon's quad between two points is left out for a fall: either point is the sheet's, or the quad
		/// would hang from the lip straight down to the pool-level row (the tall glass pane).
		/// </summary>
		private static bool FallHidesQuad(List<Fall> falls, SceneHydrology.River river, int a, int b)
		{
			if (a < 0 || b < 0)
			{
				return false;
			}
			int low = Mathf.Min(a, b), high = Mathf.Max(a, b);
			foreach (Fall fall in falls)
			{
				if (fall.River != river.Id || high <= fall.Lip || low >= fall.Plunge)
				{
					continue;
				}
				/* Every quad from the lip down to the plunge is the curtain's: hanging down the drop it drew a pane of dark
				 * glass (worst where the pool starts at the very next point, so no row was lowered and nothing was hidden).
				 * The one kept is the flat quad from the lowered row to the plunge, carrying the pool under the curtain. */
				bool poolUnderCurtain = low == fall.Plunge - 1 && low > fall.Lip;
				if (!poolUnderCurtain)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Builds every fall's sheet and spray, and publishes their feet to the inland water's shader.</summary>
		private void BuildFalls(SceneHydrology hydrology, List<Fall> found)
		{
			falls.Clear();
			falls.AddRange(found);
			int published = 0;
			for (int f = 0; f < falls.Count; f++)
			{
				Fall fall = falls[f];
				SceneHydrology.River river = hydrology.Rivers.Find(r => r.Id == fall.River);
				if (river == null)
				{
					continue;
				}
				if (FallMaterial != null)
				{
					SceneRiverFlow.River solved = flow != null ? flow.Rivers.Find(r => r.Id == fall.River) : null;
					Mesh sheet = SheetMesh(river, fall, gameObject.scene, solved, out Vector3 landing);
					if (sheet != null)
					{
						AddFallPart($"Fall {fall.River}.{fall.Lip}", sheet, FallMaterial);
						// The boil and the spray where the curtain really lands.
						fall.Landing = landing;
						falls[f] = fall;
					}
				}
				if (SprayMaterial != null)
				{
					Mesh spray = SprayMesh(river, fall);
					if (spray != null)
					{
						AddFallPart($"Spray {fall.River}.{fall.Lip}", spray, SprayMaterial);
					}
				}
				if (published < MaxShaderFalls)
				{
					shaderFalls[published++] = new Vector4(fall.Landing.x, fall.Landing.y, fall.Landing.z, fall.PoolRadius);
				}
			}
			for (int k = published; k < MaxShaderFalls; k++)
			{
				shaderFalls[k] = Vector4.zero;
			}
			Shader.SetGlobalVectorArray(FallsId, shaderFalls);
			Shader.SetGlobalFloat(FallCountId, published);
		}

		private void AddFallPart(string name, Mesh mesh, Material material)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.position = Vector3.zero;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
			built.Add(go);
		}

		/// <summary>A point on the river's line between its points <paramref name="i"/> and i + 1, <paramref name="t"/> of the way.</summary>
		private static Vector3 OnLine(SceneHydrology.River river, int i, float t)
		{
			int j = Mathf.Min(river.Points.Length - 1, i + 1);
			return Vector3.Lerp(river.Points[i], river.Points[j], t);
		}

		/// <summary>
		/// A fall's sheet: rows across it every half metre along the river from the lip to the foot, each at the higher of
		/// the water's arc off the lip and the river's surface there. Vertex data for FishMMO/Water/Waterfall:
		/// uv0 (0…1 across, metres fallen), uv1 (width m, airborne 0…1), uv2 (speed at the lip m/s, the fall's drop m),
		/// colour a: how solid the sheet is across it (fraying at its very edges), normal: out of the column.
		/// Each row is a closed ellipse (<see cref="CurtainRing"/> points), the fall's width across and
		/// <see cref="CurtainThickness"/> front to back; <paramref name="landing"/> is its last row, where it meets the pool.
		/// </summary>
		/// <summary>The highest terrain of <paramref name="scene"/> at (x, z); negative infinity off every terrain.</summary>
		private static float GroundAt(UnityEngine.SceneManagement.Scene scene, float x, float z)
		{
			float best = float.NegativeInfinity;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null || terrain.gameObject.scene != scene)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
				if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
				{
					continue;
				}
				best = Mathf.Max(best, terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y);
			}
			return best;
		}

		/// <summary>
		/// Adds triangle (a, b, c) wound to face along the vertices' normals (out of the curtain's column): the face the
		/// rasteriser draws is the one whose cross product points the normal's way, so a triangle wound the other way is flipped.
		/// </summary>
		private static void AddOutward(List<int> indices, List<Vector3> positions, List<Vector3> normals, int a, int b, int c)
		{
			Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
			Vector3 outward = normals[a] + normals[b] + normals[c];
			if (Vector3.Dot(face, outward) < 0f)
			{
				(b, c) = (c, b);
			}
			indices.Add(a);
			indices.Add(b);
			indices.Add(c);
		}

		/// <summary>The most a curtain's row may stand further out from the rock than the row above it, metres (rows are half a metre apart).</summary>
		private const float PushRamp = 0.12f;

		/// <summary>How far clear of the rock a falling sheet hangs, metres.</summary>
		private const float SheetClearance = 0.25f;

		/// <summary>Points round a curtain's section: a closed ellipse, so it is a column of water from any side.</summary>
		private const int CurtainRing = 16;

		/// <summary>
		/// How thick a curtain is <paramref name="fallen"/> metres below its lip: a film where it leaves the ledge, filling
		/// out as it takes in air and spreads, to about a third of its width at most.
		/// </summary>
		private static float CurtainThickness(float fallen, float width)
		{
			float full = Mathf.Min(0.35f + 0.05f * Mathf.Max(0f, fallen), 0.3f * width + 0.3f);
			return Mathf.Lerp(0.04f, full, Mathf.Clamp01(Mathf.Max(0f, fallen) / 1.5f));
		}

		/// <summary>
		/// How the water spreads across a fall's lip, from the river's solved flow: per share of the width (0 right bank, 1
		/// left), the speed there over the section's mean (1 average, 0 still water: behind a boulder at the lip, nothing
		/// goes over). Null without a solved flow, when the curtain is even.
		/// </summary>
		private static float[] LipProfile(SceneHydrology.River river, Fall fall, SceneRiverFlow.River solved, int columns)
		{
			if (solved == null || solved.Field == null || !solved.Field.isReadable)
			{
				return null;
			}
			float along = 0f;
			for (int i = 1; i <= fall.Lip && i < river.Points.Length; i++)
			{
				Vector3 step = river.Points[i] - river.Points[i - 1];
				step.y = 0f;
				along += step.magnitude;
			}
			// A little above the lip, where the water still runs as the river: over the edge the field has nothing to say.
			float u = Mathf.Clamp01((along - 1f) / Mathf.Max(1f, solved.Length * solved.AlongMetres));
			var profile = new float[columns];
			for (int k = 0; k < columns; k++)
			{
				float v = (k + 0.5f) / columns;
				Color c = solved.Field.GetPixelBilinear(u, v);
				profile[k] = Mathf.Clamp(c.r, 0f, 2f);
			}
			return profile;
		}

		private static Mesh SheetMesh(SceneHydrology.River river, Fall fall, UnityEngine.SceneManagement.Scene scene, SceneRiverFlow.River solved, out Vector3 landing)
		{
			landing = fall.Landing;
			float[] lip = LipProfile(river, fall, solved, 32);
			// The line from the lip to the foot, resampled every half metre along it (horizontally).
			var line = new List<Vector3>();
			var widths = new List<float>();
			for (int i = fall.Lip; i < fall.Foot; i++)
			{
				Vector3 a = river.Points[i], b = river.Points[i + 1];
				float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				int steps = Mathf.Max(1, Mathf.CeilToInt(run / 0.5f));
				for (int s = 0; s < steps; s++)
				{
					line.Add(OnLine(river, i, s / (float)steps));
					// The lip's width all the way down: the rows under it widen into the plunge basin, the water does not.
					widths.Add(fall.Width);
				}
			}
			line.Add(river.Points[fall.Foot]);
			widths.Add(fall.Width);
			// Starting a little above the lip, at its level, over the river's last row: no seam where the river tips over.
			Vector3 upstream = river.Points[Mathf.Max(0, fall.Lip - 1)] - river.Points[fall.Lip];
			upstream.y = 0f;
			if (upstream.sqrMagnitude > 1e-6f)
			{
				line.Insert(0, river.Points[fall.Lip] + upstream.normalized * 0.3f);
				widths.Insert(0, widths[0]);
			}
			if (line.Count < 2)
			{
				return null;
			}

			const float gravity = 9.81f;
			float v = fall.Speed;
			var positions = new List<Vector3>();
			var normals = new List<Vector3>();
			var uv0 = new List<Vector2>();
			var uv1 = new List<Vector2>();
			var uv2 = new List<Vector2>();
			var colours = new List<Color32>();
			var indices = new List<int>();
			// Measured from the lip: the first row lies a little above it, where the water has not begun to fall.
			float travelled = -new Vector2(line[0].x - fall.LipPoint.x, line[0].z - fall.LipPoint.z).magnitude;
			var heights = new float[line.Count];
			var airborne = new float[line.Count];
			for (int r = 0; r < line.Count; r++)
			{
				if (r > 0)
				{
					travelled += new Vector2(line[r].x - line[r - 1].x, line[r].z - line[r - 1].z).magnitude;
				}
				// The arc off the lip: a horizontal throw at the lip's speed, falling under gravity.
				float fallenFor = Mathf.Max(0f, travelled);
				float arc = fall.LipPoint.y - gravity * fallenFor * fallenFor / (2f * v * v);
				float surface = line[r].y;
				heights[r] = Mathf.Max(arc, surface) + 0.04f;
				airborne[r] = Mathf.Clamp01((arc - surface) / 0.6f);
			}
			/* Into the pool, not across it: the curtain ends at the first row where the water has come down to the pool's
			 * level, a little under its surface, and the pool's own churn takes over. Run on along the surface, its last
			 * rows lay flat over the pool and their streaks drew chevrons there, seen from above. */
			float poolLevel = line[line.Count - 1].y;
			for (int r = 1; r < line.Count - 1; r++)
			{
				if (airborne[r] <= 0f && line[r].y <= poolLevel + 0.5f)
				{
					int keep = r + 1;
					line.RemoveRange(keep, line.Count - keep);
					widths.RemoveRange(keep, widths.Count - keep);
					System.Array.Resize(ref heights, keep);
					System.Array.Resize(ref airborne, keep);
					heights[r] = line[r].y - 0.3f;
					break;
				}
			}
			// Each row's way downstream, taken from the line before any row is moved (below): read off rows already
			// pushed out, a row a few metres on could point back upstream and be pushed back into the curtain above it.
			var aheads = new Vector3[line.Count];
			for (int r = 0; r < line.Count; r++)
			{
				Vector3 ahead = line[Mathf.Min(line.Count - 1, r + 1)] - line[Mathf.Max(0, r - 1)];
				ahead.y = 0f;
				aheads[r] = ahead.sqrMagnitude > 1e-8f ? ahead.normalized : Vector3.forward;
			}
			/* Clear of the rock. A narrow river's channel is only a couple of terrain samples wide, and the terrain between
			 * samples bulges over a slot that deep: the sheet would pass inside the cliff. Each row is moved out from the face
			 * until the rock under its middle and both edges stands below it, and never less than the row above, so the
			 * curtain hangs free and smooth in front of the rock, as a real one leaves its ledge. */
			float push = 0f;
			var pushes = new float[line.Count];
			for (int r = 0; r < line.Count; r++)
			{
				Vector3 ahead = aheads[r];
				var edge = new Vector3(-ahead.z, 0f, ahead.x) * (0.4f * widths[r]);
				// The curtain's back, half its thickness toward the rock, is what has to clear it.
				float back = 0.5f * CurtainThickness(fall.LipPoint.y - heights[r], widths[r]);
				for (float extra = push; extra <= push + 8f; extra += 0.25f)
				{
					Vector3 at = line[r] + ahead * (extra - back);
					float top = heights[r] - SheetClearance;
					if (GroundAt(scene, at.x, at.z) < top && GroundAt(scene, at.x + edge.x, at.z + edge.z) < top && GroundAt(scene, at.x - edge.x, at.z - edge.z) < top)
					{
						push = extra;
						break;
					}
				}
				pushes[r] = push;
			}
			// Smoothed down the curtain (the search steps a quarter metre at a time, and stepped rows drew a zigzag edge),
			// never letting a row back into the rock it was pushed out of.
			var smoothed = new float[line.Count];
			for (int r = 0; r < line.Count; r++)
			{
				float sum = 0f;
				int taken = 0;
				for (int k = Mathf.Max(0, r - 4); k <= Mathf.Min(line.Count - 1, r + 4); k++)
				{
					sum += pushes[k];
					taken++;
				}
				smoothed[r] = Mathf.Max(pushes[r], sum / taken);
			}
			pushes = smoothed;
			/* And eased in from above: a row may stand at most PushRamp further out than the row above it, the rows above
			 * moving out early instead, so the curtain bows out ahead of a talus foot rather than stepping out row by row
			 * (a column stepping out drew chevrons down its lower half). */
			for (int r = line.Count - 2; r >= 0; r--)
			{
				pushes[r] = Mathf.Max(pushes[r], pushes[r + 1] - PushRamp);
			}
			for (int r = 0; r < line.Count; r++)
			{
				line[r] += aheads[r] * pushes[r];
			}
			for (int r = 0; r < line.Count; r++)
			{
				Vector3 forward = line[Mathf.Min(line.Count - 1, r + 1)] - line[Mathf.Max(0, r - 1)];
				forward.y = 0f;
				forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
				var left = new Vector3(-forward.z, 0f, forward.x);
				// The sheet's own slope here: down the arc or the rock, whichever it follows.
				float dy = heights[Mathf.Min(line.Count - 1, r + 1)] - heights[Mathf.Max(0, r - 1)];
				float dh = new Vector2(line[Mathf.Min(line.Count - 1, r + 1)].x - line[Mathf.Max(0, r - 1)].x,
					line[Mathf.Min(line.Count - 1, r + 1)].z - line[Mathf.Max(0, r - 1)].z).magnitude;
				Vector3 tangent = (forward * Mathf.Max(1e-3f, dh) + Vector3.up * dy).normalized;
				Vector3 facing = Vector3.Cross(tangent, left).normalized;
				if (facing.y < 0f && Vector3.Dot(facing, forward) < 0f)
				{
					facing = -facing;
				}
				float fallen = fall.LipPoint.y - heights[r];
				// Where a curtain hangs free it spreads a little as it falls: a third wider after 30 m, not three times.
				float width = widths[r] * (1f + 0.012f * Mathf.Max(0f, fallen) * airborne[r]);
				Vector3 centre = new Vector3(line[r].x, heights[r], line[r].z);
				/* A column, not a ribbon: the section is a closed ellipse, the width across and the curtain's thickness front
				 * to back, so from the side it is a body of falling water rather than a flat strip seen edge-on. */
				float halfWidth = 0.5f * width, halfThick = 0.5f * CurtainThickness(fallen, widths[r]);
				for (int k = 0; k <= CurtainRing; k++)
				{
					float phi = k / (float)CurtainRing * Mathf.PI * 2f;
					float c = Mathf.Cos(phi), sn = Mathf.Sin(phi);
					positions.Add(centre + left * (c * halfWidth) + facing * (sn * halfThick));
					// The ellipse's own normal: out of the column all round.
					normals.Add((left * (c / Mathf.Max(0.01f, halfWidth)) + facing * (sn / Mathf.Max(0.01f, halfThick))).normalized);
					// Across it, front and back alike, so the streaks run down both faces.
					uv0.Add(new Vector2(0.5f + 0.5f * c, fallen));
					uv1.Add(new Vector2(width, airborne[r]));
					uv2.Add(new Vector2(v, fall.Drop));
					/* r: how much water goes over the lip here, from the river's solved flow (0.5 its mean, 0 none, behind a rock:
					 * the curtain parts round what stands in it). a: whole all round; the shader frays the silhouette from
					 * whichever side it is seen, so the column stays a body of water from the side. */
					float share = 1f;
					if (lip != null)
					{
						float across = Mathf.Clamp01(0.5f + 0.5f * c) * (lip.Length - 1);
						int k0 = Mathf.FloorToInt(across), k1 = Mathf.Min(lip.Length - 1, k0 + 1);
						share = Mathf.Lerp(lip[k0], lip[k1], across - k0);
					}
					colours.Add(new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(share * 0.5f)), 255, 255, 255));
				}
			}
			landing = new Vector3(line[line.Count - 1].x, Mathf.Max(heights[line.Count - 1], line[line.Count - 1].y), line[line.Count - 1].z);
			for (int r = 0; r + 1 < line.Count; r++)
			{
				int row = r * (CurtainRing + 1), next = row + CurtainRing + 1;
				for (int k = 0; k < CurtainRing; k++)
				{
					// Wound to face out of the column (the shader culls the inside): checked against the outward normal.
					AddOutward(indices, positions, normals, row + k, next + k, next + k + 1);
					AddOutward(indices, positions, normals, row + k, next + k + 1, row + k + 1);
				}
			}
			var mesh = new Mesh { name = $"Fall {fall.River}.{fall.Lip}", hideFlags = HideFlags.DontSave };
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uv0);
			mesh.SetUVs(1, uv1);
			mesh.SetUVs(2, uv2);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>
		/// A fall's spray and mist: quads round its foot, each a particle whose life the shader works out from its seed
		/// (FishMMO/Water/Waterfall Spray). Vertex data: position the particle's home at the foot, uv0 its corner
		/// (−1…1), uv1 two seeds, uv2 (kind: 0 spray, 1 mist; the pool's radius), normal the river's way downstream,
		/// colour r the fall's drop over 30 m.
		/// </summary>
		private Mesh SprayMesh(SceneHydrology.River river, Fall fall)
		{
			int spray = Mathf.Clamp(Mathf.RoundToInt(SprayDensity * fall.Width * Mathf.Sqrt(fall.Drop)), 24, 1200);
			int mist = Mathf.Clamp(spray / 6, 8, 160);
			Vector3 downstream = fall.FootPoint - fall.LipPoint;
			Vector3 foot = fall.Landing;
			downstream.y = 0f;
			downstream = downstream.sqrMagnitude > 1e-6f ? downstream.normalized : Vector3.forward;
			var left = new Vector3(-downstream.z, 0f, downstream.x);
			var positions = new List<Vector3>();
			var normals = new List<Vector3>();
			var uv0 = new List<Vector2>();
			var uv1 = new List<Vector2>();
			var uv2 = new List<Vector2>();
			var colours = new List<Color32>();
			var indices = new List<int>();
			var rng = new System.Random(fall.River * 7919 + fall.Lip * 104729);
			byte drop = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(fall.Drop / 30f));
			void Particle(float kind)
			{
				// Home: across the fall's landing line, where the curtain hits the pool.
				Vector3 home = foot + left * (((float)rng.NextDouble() - 0.5f) * fall.Width * 0.9f)
					+ downstream * ((float)rng.NextDouble() * 0.25f * fall.PoolRadius);
				var seed = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
				int start = positions.Count;
				foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
				{
					positions.Add(home);
					normals.Add(downstream);
					uv0.Add(corner);
					uv1.Add(seed);
					uv2.Add(new Vector2(kind, fall.PoolRadius));
					colours.Add(new Color32(drop, 0, 0, 255));
				}
				indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
				indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
			}
			for (int k = 0; k < spray; k++)
			{
				Particle(0f);
			}
			for (int k = 0; k < mist; k++)
			{
				Particle(1f);
			}
			/* Down the whole fall, not only round its foot: strands of water peeling off the curtain and falling with it
			 * (2), and a veil of mist hanging over it, thickest low down where the water shatters (3). Homes at the lip,
			 * across its width; the shader carries them down the curtain's path to where it lands. */
			Vector3 toLanding = fall.Landing - fall.LipPoint;
			float run = new Vector2(toLanding.x, toLanding.z).magnitude;
			byte runByte = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(run / 20f));
			void FallParticle(float kind)
			{
				Vector3 home = fall.LipPoint + left * (((float)rng.NextDouble() - 0.5f) * fall.Width * 0.95f);
				var seed = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
				int start = positions.Count;
				foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
				{
					positions.Add(home);
					normals.Add(downstream);
					uv0.Add(corner);
					uv1.Add(seed);
					uv2.Add(new Vector2(kind, fall.Drop));
					colours.Add(new Color32(drop, runByte, 0, 255));
				}
				indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
				indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
			}
			int strands = Mathf.Clamp(Mathf.RoundToInt(fall.Width * fall.Drop * 1.2f), 16, 600);
			int veil = Mathf.Clamp(Mathf.RoundToInt(fall.Width * fall.Drop * 0.3f), 8, 160);
			for (int k = 0; k < strands; k++)
			{
				FallParticle(2f);
			}
			for (int k = 0; k < veil; k++)
			{
				FallParticle(3f);
			}
			var mesh = new Mesh { name = $"Spray {fall.River}.{fall.Lip}", hideFlags = HideFlags.DontSave };
			if (positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uv0);
			mesh.SetUVs(1, uv1);
			mesh.SetUVs(2, uv2);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			// The particles move in the shader: bound where they can reach (up and out from the foot, drifting downstream).
			float reach = fall.PoolRadius * 2.5f + fall.Drop;
			mesh.bounds = new Bounds(foot + Vector3.up * (0.5f * fall.Drop) + downstream * (0.25f * reach), new Vector3(2f * reach, 2f * fall.Drop + reach, 2f * reach));
			return mesh;
		}
	}
}
