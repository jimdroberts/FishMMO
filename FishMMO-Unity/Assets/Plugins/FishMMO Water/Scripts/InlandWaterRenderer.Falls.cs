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
			/// <summary>How far round its foot the pool reaches, metres (the basin it scoured).</summary>
			public float PoolRadius;
			/// <summary>
			/// How far out from where the curtain lands the water is thrown and boils, metres: the curtain's width and a
			/// share of the drop. The spray and mist crowd it and the pool's foam fades over half as far again; the churn
			/// had reached the whole basin's radius (48 m for Flo Monolith's fall), so a 15 m pool was white to its outlet.
			/// </summary>
			public float ImpactRadius;
			/// <summary>Where the curtain's streams strike rock on their way down (SheetMesh): each throws a burst of spray.</summary>
			public List<Vector3> Strikes;
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
							ImpactRadius = Mathf.Clamp(0.5f * width + 0.15f * drop, 2f, Mathf.Max(2f, 0.9f * poolWidth + 0.35f * drop)),
						});
					}
					i = last + 1;
				}
			}
			return found;
		}

		/// <summary>The value <see cref="SceneHydrology.River.Reach"/> holds at a falling point (the generator's RiverReach.Fall).</summary>
		public const byte FallReach = 4;

		/// <summary>Droplets and puffs in each burst where a stream strikes rock on the way down, and how far they are thrown out, metres.</summary>
		private const int StrikeSpray = 14, StrikeMist = 2;
		private const float StrikeRadius = 1.2f;

		/// <summary>How far the pool churns, in impact radii (FishInlandWater.hlsl FallChurn fades to nothing there).</summary>
		public const float ChurnShare = 1.6f;

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
					Mesh sheet = SheetMesh(river, fall, gameObject.scene, solved, out Vector3 landing, out List<Vector3> strikes);
					if (sheet != null)
					{
						AddFallPart($"Fall {fall.River}.{fall.Lip}", sheet, FallMaterial, CurtainSortingOrder);
						// The boil and the spray where the curtain really lands, and where its streams strike rock on the way.
						fall.Landing = landing;
						fall.Strikes = strikes;
						falls[f] = fall;
					}
				}
				if (SprayMaterial != null)
				{
					Mesh spray = SprayMesh(river, fall);
					if (spray != null)
					{
						AddFallPart($"Spray {fall.River}.{fall.Lip}", spray, SprayMaterial, SpraySortingOrder);
					}
				}
				if (published < MaxShaderFalls)
				{
					shaderFalls[published++] = new Vector4(fall.Landing.x, fall.Landing.y, fall.Landing.z, ChurnShare * fall.ImpactRadius);
				}
			}
			for (int k = published; k < MaxShaderFalls; k++)
			{
				shaderFalls[k] = Vector4.zero;
			}
			Shader.SetGlobalVectorArray(FallsId, shaderFalls);
			Shader.SetGlobalFloat(FallCountId, published);
		}

		/// <summary>
		/// Sorting orders above every water surface (InlandWaterRenderer.Add numbers lakes and rivers 0, 1, 2…): Unity sorts
		/// transparent renderers by sorting order before their render queue, so at order 0 the falls and their spray were
		/// drawn first and every river surface, the plunge pool among them, painted over the splash (Jim, 2026-10-07).
		/// </summary>
		private const int CurtainSortingOrder = 30000, SpraySortingOrder = 30001;

		private void AddFallPart(string name, Mesh mesh, Material material, int sortingOrder)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.position = Vector3.zero;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
			renderer.sortingOrder = sortingOrder;
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
		/// The curtain is one bent slab (strips joined edge to edge, cut where rock parts them), the fall's width across and
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


		/// <summary>How much wider a free curtain grows per metre fallen, and the most (times the lip's width).</summary>
		private const float SpreadPerMetre = 0.02f, MaxSpread = 2f;

		/// <summary>Metres below where a stream rode rock that its water stays churned white.</summary>
		private const float RideTrailMetres = 4f;

		/// <summary>How far below the lip the curtain is still taken to be in its channel, clear of rock, metres.</summary>
		private const float BrinkClearMetres = 0.6f;

		/// <summary>How far above the lip the curtain begins, on the river's surface, metres.</summary>
		private const float BrinkRunMetres = 1.6f;

		/// <summary>About how wide each strip of a curtain is, metres (SheetMesh): the finest thing the water parts round.</summary>
		private const float StripMetres = 0.7f;
		/// <summary>The most strips a curtain is cut into.</summary>
		private const int MaxStrips = 24;
		/// <summary>Metres between a curtain's rows down its own path.</summary>
		private const float StripRowMetres = 0.6f;
		/// <summary>The most rows down a curtain (a very tall fall takes longer rows).</summary>
		private const int MaxStripRows = 220;
		/// <summary>The share of strips that must clear the rock for the face, not an obstacle, to say how far out the curtain stands.</summary>
		private const float FaceShare = 0.6f;
		/// <summary>The furthest a strip slides sideways round rock, metres; past it, it is thrown out over the rock.</summary>
		private const float MaxPartingMetres = 4f;
		/// <summary>How fast a strip's sideways slide eases in above what it meets, metres sideways per metre down.</summary>
		private const float PartRampPerMetre = 0.35f;
		/// <summary>How fast a parted strip comes back to its place below the rock, metres sideways per metre down.</summary>
		private const float RejoinPerMetre = 0.2f;
		/// <summary>How far the water behind a gap at the lip spreads into it as it falls, metres per metre fallen.</summary>
		private const float LipSpreadPerMetre = 0.05f;

		/// <summary>Debug: logs each curtain's strips as it is built (how far each slid sideways, how far it was thrown out).</summary>
		public static bool LogSheets;

		/// <summary>The lip profile at <paramref name="across"/> (0…1), averaged over ±<paramref name="spread"/> of the width.</summary>
		private static float Spread(float[] lip, float across, float spread)
		{
			float sum = 0f;
			for (int k = -2; k <= 2; k++)
			{
				float at = Mathf.Clamp01(across + spread * k * 0.5f) * (lip.Length - 1);
				int k0 = Mathf.FloorToInt(at), k1 = Mathf.Min(lip.Length - 1, k0 + 1);
				sum += Mathf.Lerp(lip[k0], lip[k1], at - k0);
			}
			return sum / 5f;
		}

		private static readonly Collider[] nearColliders = new Collider[16];

		/// <summary>Whether any collider but terrain overlaps a sphere at <paramref name="at"/>.</summary>
		private static bool PropAt(UnityEngine.PhysicsScene physics, Vector3 at, float radius)
		{
			int found = physics.OverlapSphere(at, radius, nearColliders, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < found; i++)
			{
				if (nearColliders[i] != null && !(nearColliders[i] is TerrainCollider))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether anything but terrain has collision round the fall: baked props' colliders the curtain must clear too.</summary>
		private static bool PropsNear(UnityEngine.PhysicsScene physics, Fall fall)
		{
			Vector3 centre = 0.5f * (fall.LipPoint + fall.FootPoint);
			var half = new Vector3(0.5f * fall.Width + 12f, 0.5f * fall.Drop + 4f, 12f + 0.5f * Vector3.Distance(new Vector3(fall.LipPoint.x, 0f, fall.LipPoint.z), new Vector3(fall.FootPoint.x, 0f, fall.FootPoint.z)));
			int found = physics.OverlapBox(centre, half, nearColliders, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < found; i++)
			{
				if (nearColliders[i] != null && !(nearColliders[i] is TerrainCollider))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// How thick a curtain is <paramref name="fallen"/> metres below its lip. The river's whole depth rolls over the brink
		/// (at about two thirds of it, the critical depth), and the sheet thins as the water speeds up, the same water spread
		/// along a faster stream; lower down it takes in air and bulks out, to about a third of its width at most. It was a
		/// film of 4 cm at the brink, so the top of every fall, glassy there, showed only the rock behind it: no water.
		/// </summary>
		private static float CurtainThickness(float fallen, float width, float depth, float lipSpeed, float drop)
		{
			const float gravity = 9.81f;
			float f = Mathf.Max(0f, fallen);
			float speed = Mathf.Sqrt(lipSpeed * lipSpeed + 2f * gravity * f);
			float core = Mathf.Clamp(0.67f * depth * lipSpeed / Mathf.Max(0.1f, speed), 0.04f, Mathf.Max(0.04f, depth));
			float bulked = Mathf.Min(0.3f + 0.04f * f, 0.3f * width + 0.3f);
			return Mathf.Max(core, Mathf.Lerp(core, bulked, Mathf.Clamp01((f - 2f) / (2f + 0.15f * drop))));
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

		private static Mesh SheetMesh(SceneHydrology.River river, Fall fall, UnityEngine.SceneManagement.Scene scene, SceneRiverFlow.River solved, out Vector3 landing, out List<Vector3> strikes)
		{
			landing = fall.Landing;
			strikes = null;
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
			/* Starting on the river's own surface BrinkRunMetres above the lip, so the curtain carries the water over the brink
			 * as one rolling surface; the shader fades it in over the river there (uv3.y, metres before the lip). Begun 0.3 m
			 * short of the lip, the river ended in a dark band at the edge and the sheet began below it. */
			Vector3 upstream = river.Points[Mathf.Max(0, fall.Lip - 1)] - river.Points[fall.Lip];
			upstream.y = 0f;
			if (upstream.sqrMagnitude > 1e-6f)
			{
				float run = Mathf.Min(BrinkRunMetres, upstream.magnitude * 0.9f);
				for (float back = 0.5f; back < run; back += 0.5f)
				{
					line.Insert(0, river.Points[fall.Lip] + upstream.normalized * back);
					widths.Insert(0, widths[0]);
				}
				line.Insert(0, river.Points[fall.Lip] + upstream.normalized * run);
				widths.Insert(0, widths[0]);
			}
			float lipDepth = river.Depth != null && fall.Lip < river.Depth.Length ? Mathf.Max(0.1f, river.Depth[fall.Lip]) : 0.6f;
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
			var uv3 = new List<Vector2>();
			var uv4 = new List<Vector2>();
			var uv5 = new List<Vector2>();
			var tangents = new List<Vector4>();
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
			float travelledAtEnd = Mathf.Max(0f, travelled);
			/* All the way down. The water's arc off the lip may carry it past the river's foot before it reaches the pool (a fast
			 * lip, a short run): the curtain went on along the arc beyond the foot until it does, or it ended in mid-air, 25 m
			 * over the pool of a 52 m fall (WaterfallBedProbe, 2026-10-07). */
			{
				float poolY = line[line.Count - 1].y;
				Vector3 onward = line[line.Count - 1] - line[Mathf.Max(0, line.Count - 2)];
				onward.y = 0f;
				onward = onward.sqrMagnitude > 1e-6f ? onward.normalized : Vector3.forward;
				float reached = travelledAtEnd;
				int guard = 0;
				while (heights[heights.Length - 1] > poolY + 0.3f && guard++ < 400)
				{
					reached += 0.25f;
					Vector3 next = line[line.Count - 1] + onward * 0.25f;
					next.y = poolY;
					float arcHere = fall.LipPoint.y - gravity * reached * reached / (2f * v * v);
					line.Add(next);
					widths.Add(fall.Width);
					System.Array.Resize(ref heights, heights.Length + 1);
					System.Array.Resize(ref airborne, airborne.Length + 1);
					heights[heights.Length - 1] = Mathf.Max(arcHere, poolY) + 0.04f;
					airborne[airborne.Length - 1] = Mathf.Clamp01((arcHere - poolY) / 0.6f);
				}
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
			// ── Down its own length ─────────────────────────────────────────
			/* Rows every StripRowMetres down the path itself, not every half metre across the ground: a tall fall drops tens
			 * of metres over a few metres of run, and rows laid by the ground stood ten metres apart down its face, too
			 * coarse for the water to part round anything (a 50 m fall was eight rows). */
			var path = new List<Vector3>();
			var pathAhead = new List<Vector3>();
			var pathAir = new List<float>();
			{
				var points = new Vector3[line.Count];
				float total = 0f;
				for (int r = 0; r < line.Count; r++)
				{
					points[r] = new Vector3(line[r].x, heights[r], line[r].z);
					total += r > 0 ? Vector3.Distance(points[r - 1], points[r]) : 0f;
				}
				float stepLength = Mathf.Max(StripRowMetres, total / MaxStripRows);
				path.Add(points[0]);
				pathAhead.Add(aheads[0]);
				pathAir.Add(airborne[0]);
				float carry = 0f;
				for (int r = 0; r + 1 < points.Length; r++)
				{
					float segment = Vector3.Distance(points[r], points[r + 1]);
					float at = stepLength - carry;
					while (at <= segment && segment > 1e-5f)
					{
						float t = at / segment;
						path.Add(Vector3.Lerp(points[r], points[r + 1], t));
						Vector3 a = Vector3.Lerp(aheads[r], aheads[r + 1], t);
						pathAhead.Add(a.sqrMagnitude > 1e-8f ? a.normalized : aheads[r]);
						pathAir.Add(Mathf.Lerp(airborne[r], airborne[r + 1], t));
						at += stepLength;
					}
					carry = segment - (at - stepLength);
				}
				if ((path[path.Count - 1] - points[points.Length - 1]).sqrMagnitude > 0.01f)
				{
					path.Add(points[points.Length - 1]);
					pathAhead.Add(aheads[aheads.Length - 1]);
					pathAir.Add(airborne[airborne.Length - 1]);
				}
			}
			int rows = path.Count;
			float rowStep = rows > 1 ? Vector3.Distance(path[0], path[1]) : StripRowMetres;
			var lefts = new Vector3[rows];
			var fallens = new float[rows];
			// Metres above the lip along the river (0 from the lip down): where the curtain still lies on the river's surface.
			var befores = new float[rows];
			Vector3 lipAhead = pathAhead[0];
			var rowWidths = new float[rows];
			var thicks = new float[rows];
			for (int r = 0; r < rows; r++)
			{
				lefts[r] = new Vector3(-pathAhead[r].z, 0f, pathAhead[r].x);
				fallens[r] = Mathf.Max(0f, fall.LipPoint.y - path[r].y);
				befores[r] = Mathf.Max(0f, -Vector3.Dot(new Vector3(path[r].x - fall.LipPoint.x, 0f, path[r].z - fall.LipPoint.z), lipAhead));
				/* A tall free fall fans out as it breaks up (air drag on the jets spreads them): a fifth wider after 10 m, up to
				 * twice the lip's width. At 1.2 % a metre a narrow river's 50 m fall stayed a thin cord seen from afar. */
				rowWidths[r] = fall.Width * Mathf.Min(MaxSpread, 1f + SpreadPerMetre * fallens[r] * pathAir[r]);
				thicks[r] = CurtainThickness(fallens[r], fall.Width, lipDepth, v, fall.Drop);
			}
			/* Metres along the water's own path from the lip (negative above it, on the river): what the streaks, ripples and
			 * jets are laid out along and carried down. Laid out by the vertical drop, they had no length at all over the river
			 * above the lip (every row 0 m fallen) and almost none just past the brink, where the water still travels mostly
			 * forward: the pattern there flickered and seemed to run back up the fall (Jim, 2026-10-07). */
			var pathMetres = new float[rows];
			{
				int lipRow = 0;
				while (lipRow < rows - 1 && befores[lipRow] > 0f)
				{
					lipRow++;
				}
				for (int r = 1; r < rows; r++)
				{
					pathMetres[r] = pathMetres[r - 1] + Vector3.Distance(path[r - 1], path[r]);
				}
				float atLip = pathMetres[lipRow];
				for (int r = 0; r < rows; r++)
				{
					pathMetres[r] -= atLip;
				}
			}

			// ── The rock, terrain and any baked prop's collision ─────────────
			UnityEngine.PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
			bool colliders = PropsNear(physics, fall);

			// ── The strips ──────────────────────────────────────────────────
			/* The curtain as strips of water side by side, each finding its own way down: where rock stands in a strip's way
			 * it slides off to the side of it, into the nearest open air, and the strips rejoin below; where the rock is too
			 * broad to go round, the strip is thrown out over it. One column, pushed out as a whole wherever any part of it
			 * met rock, jumped a pillar a third its width as one block. */
			int strips = Mathf.Clamp(Mathf.RoundToInt(fall.Width / StripMetres), 3, MaxStrips);
			float BaseOffset(int strip, int r) => ((strip + 0.5f) / strips - 0.5f) * rowWidths[r];
			bool Clear(int r, float offset, float pushed)
			{
				// Still in the channel (on the river, or just over the brink): its banks are the river's, not rock in the way.
				// Tested there, they pushed the whole top of the curtain forward off the brink, leaving a gap under the river.
				if (fallens[r] < BrinkClearMetres)
				{
					return true;
				}
				/* Across the strip, not at its middle alone: tested at its centre line, a strip came back over the edge of a
				 * long obstacle as soon as its middle cleared, half of it inside the rock. */
				Vector3 back = path[r] + lefts[r] * offset + pathAhead[r] * (pushed - 0.5f * thicks[r]);
				float top = path[r].y - SheetClearance;
				Vector3 halfStrip = lefts[r] * (0.4f * rowWidths[r] / strips);
				if (GroundAt(scene, back.x, back.z) >= top || GroundAt(scene, back.x + halfStrip.x, back.z + halfStrip.z) >= top
					|| GroundAt(scene, back.x - halfStrip.x, back.z - halfStrip.z) >= top)
				{
					return false;
				}
				/* A prop's rock where the water itself is, in three dimensions: tested by the highest surface below the point,
				 * as the terrain is, a boulder lodged on the face stood in the way of everything under it too, and the water
				 * stayed parted all the way to the pool instead of closing in below the rock. */
				if (!colliders)
				{
					return true;
				}
				Vector3 middle = path[r] + lefts[r] * offset + pathAhead[r] * pushed;
				float radius = 0.5f * thicks[r] + SheetClearance;
				return !PropAt(physics, middle, radius) && !PropAt(physics, middle + halfStrip, radius) && !PropAt(physics, middle - halfStrip, radius);
			}
			/* The face itself first: how far the curtain must stand off the rock for most of its strips to clear it (a
			 * narrow channel's slot is only a couple of terrain samples wide, and the terrain bulges between samples). Never
			 * less than the row above: water leaving a ledge does not come back to the rock. */
			var basePush = new float[rows];
			float carried = 0f;
			for (int r = 0; r < rows; r++)
			{
				for (float extra = carried; extra <= carried + 8f; extra += 0.25f)
				{
					int clear = 0;
					for (int strip = 0; strip < strips; strip++)
					{
						clear += Clear(r, BaseOffset(strip, r), extra) ? 1 : 0;
					}
					if (clear >= Mathf.CeilToInt(FaceShare * strips))
					{
						carried = extra;
						break;
					}
				}
				basePush[r] = carried;
			}
			// Smoothed, and eased in from above (a column stepping out row by row drew chevrons; see PushRamp).
			{
				var smoothed = new float[rows];
				for (int r = 0; r < rows; r++)
				{
					float sum = 0f;
					int taken = 0;
					for (int k = Mathf.Max(0, r - 4); k <= Mathf.Min(rows - 1, r + 4); k++)
					{
						sum += basePush[k];
						taken++;
					}
					smoothed[r] = Mathf.Max(basePush[r], sum / taken);
				}
				basePush = smoothed;
				float ramp = PushRamp * rowStep / 0.5f;
				for (int r = rows - 2; r >= 0; r--)
				{
					basePush[r] = Mathf.Max(basePush[r], basePush[r + 1] - ramp);
				}
			}
			// Then each strip down its own way.
			var shifts = new float[strips, rows];
			var pushes = new float[strips, rows];
			// Where each strip rode rock (struck it, or was turned aside by it), then trailed downward: churned white there.
			var riding = new float[strips, rows];
			strikes = new List<Vector3>();
			float parting = Mathf.Clamp(0.75f * fall.Width, 1.5f, MaxPartingMetres);
			for (int strip = 0; strip < strips; strip++)
			{
				float shift = 0f, pushed = 0f;
				bool touching = false;
				for (int r = 0; r < rows; r++)
				{
					float home = BaseOffset(strip, r);
					pushed = Mathf.Max(pushed, basePush[r]);
					// Back toward its own place below whatever parted it: the streams rejoin as they fall.
					if (shift != 0f)
					{
						float relaxed = Mathf.MoveTowards(shift, 0f, RejoinPerMetre * rowStep);
						if (Clear(r, home + relaxed, pushed))
						{
							shift = relaxed;
						}
					}
					if (!Clear(r, home + shift, pushed))
					{
						riding[strip, r] = 1f;
						// It strikes the rock here: spray. Not at the brink, where the strips only brush the channel's own banks.
						if (!touching && fallens[r] > 2f)
						{
							strikes.Add(path[r] + lefts[r] * (home + shift) + pathAhead[r] * pushed);
						}
						touching = true;
						// The nearest open air to either side; failing that, out over the rock.
						float found = float.NaN;
						for (float d = 0.25f; d <= parting && float.IsNaN(found); d += 0.25f)
						{
							float towardEdge = Mathf.Sign(home + shift == 0f ? 1f : home + shift);
							if (Clear(r, home + shift + towardEdge * d, pushed))
							{
								found = shift + towardEdge * d;
							}
							else if (Clear(r, home + shift - towardEdge * d, pushed))
							{
								found = shift - towardEdge * d;
							}
						}
						if (!float.IsNaN(found))
						{
							shift = found;
						}
						else
						{
							float extra = pushed;
							while (extra < pushed + 8f && !Clear(r, home + shift, extra))
							{
								extra += 0.25f;
							}
							pushed = extra;
						}
					}
					else
					{
						touching = false;
					}
					shifts[strip, r] = shift;
					pushes[strip, r] = pushed;
				}
				/* Smoothed down the strip, never less far aside than the rock needs (a slide that is sudden kinked the strip),
				 * then eased in from above: the water bends round what it meets over a few metres, and leaves the rock smoothly. */
				var raw = new float[rows];
				for (int r = 0; r < rows; r++)
				{
					raw[r] = shifts[strip, r];
				}
				int window = Mathf.Max(2, Mathf.RoundToInt(2.5f / rowStep));
				for (int r = 0; r < rows; r++)
				{
					float sum = 0f;
					int taken = 0;
					for (int k = Mathf.Max(0, r - window); k <= Mathf.Min(rows - 1, r + window); k++)
					{
						sum += raw[k];
						taken++;
					}
					float mean = sum / taken;
					shifts[strip, r] = Mathf.Abs(mean) > Mathf.Abs(raw[r]) && Mathf.Sign(mean) == Mathf.Sign(raw[r] == 0f ? mean : raw[r]) ? mean : raw[r];
				}
				float bend = PartRampPerMetre * rowStep, rampOut = PushRamp * rowStep / 0.5f;
				for (int r = rows - 2; r >= 0; r--)
				{
					float below = shifts[strip, r + 1];
					if (Mathf.Abs(below) > Mathf.Abs(shifts[strip, r]) + bend)
					{
						shifts[strip, r] = below - Mathf.Sign(below) * bend;
					}
					pushes[strip, r] = Mathf.Max(pushes[strip, r], pushes[strip, r + 1] - rampOut);
				}
				// The churn trails down below the contact, fading over RideTrailMetres, and starts half a metre above it.
				float fade = rowStep / RideTrailMetres;
				for (int r = 1; r < rows; r++)
				{
					riding[strip, r] = Mathf.Max(riding[strip, r], riding[strip, r - 1] - fade);
				}
				for (int r = rows - 2; r >= 0; r--)
				{
					riding[strip, r] = Mathf.Max(riding[strip, r], riding[strip, r + 1] - rowStep / 0.5f);
				}
				if (LogSheets && strip == strips / 2)
				{
					var head = new System.Text.StringBuilder();
					for (int r = 0; r < Mathf.Min(rows, 10); r++)
					{
						head.Append($" [{path[r].x:0.00},{path[r].y:0.00},{path[r].z:0.00} before {befores[r]:0.0} fallen {fallens[r]:0.0} thick {thicks[r]:0.00} push {pushes[strip, r]:0.00}]");
					}
					Debug.Log($"[Fall strips] head{head} lip {fall.LipPoint}; colliders {colliders}");
				}
				if (LogSheets)
				{
					float mostAside = 0f, mostOut = 0f;
					for (int r = 0; r < rows; r++)
					{
						mostAside = Mathf.Abs(shifts[strip, r]) > Mathf.Abs(mostAside) ? shifts[strip, r] : mostAside;
						mostOut = Mathf.Max(mostOut, pushes[strip, r] - basePush[r]);
					}
					Debug.Log($"[Fall strips] {fall.River}.{fall.Lip} strip {strip}/{strips} at {BaseOffset(strip, 0):0.00} m: slid {mostAside:+0.00;-0.00} m, thrown out {mostOut:0.00} m past the face's {basePush[rows - 1]:0.00}; {rows} rows of {rowStep:0.00} m");
				}
			}

			/* Rows held still by rock: wherever any strip is turned aside or rides rock, eased over a few metres either side.
			 * The shader's sway moves the curtain across as a whole, and swung there it carried each parting off its rock
			 * and the water back over it (a boulder seen through the sheet). */
			var pinned = new float[rows];
			for (int r = 0; r < rows; r++)
			{
				for (int strip = 0; strip < strips; strip++)
				{
					pinned[r] = Mathf.Max(pinned[r], Mathf.Max(riding[strip, r], Mathf.Clamp01(Mathf.Abs(shifts[strip, r]) / 0.3f)));
				}
			}
			{
				var eased = new float[rows];
				int reach = Mathf.Max(1, Mathf.RoundToInt(3f / rowStep));
				for (int r = 0; r < rows; r++)
				{
					for (int k = Mathf.Max(0, r - reach); k <= Mathf.Min(rows - 1, r + reach); k++)
					{
						eased[r] = Mathf.Max(eased[r], pinned[k] * (1f - Mathf.Abs(k - r) / (float)(reach + 1)));
					}
				}
				pinned = eased;
			}

			// ── The slab ────────────────────────────────────────────────────
			/* One bent slab of water with width, depth and height: a front and a back face across the whole curtain, and
			 * sides. Neighbouring strips meet edge to edge, both at the point midway between them, so where nothing parts
			 * them they are one solid sheet; where rock has parted them, each side of the cut is the slab's own edge, with a
			 * side face of its own. (Separate rounded tubes, overlapping, read as a row of cords.) */
			// How open a strip's side is, 0 … 1, smoothly: by how far apart it and its neighbour have drawn, or how far one stands
			// out from the other. A yes-or-no flipping from row to row drew hard steps in the fraying down the curtain.
			float Openness(int strip, int r)
			{
				if (strip < 0 || strip + 1 >= strips)
				{
					return 1f;
				}
				float stripWidth = rowWidths[r] / strips;
				float gap = BaseOffset(strip + 1, r) + shifts[strip + 1, r] - BaseOffset(strip, r) - shifts[strip, r];
				float apart = Mathf.Clamp01((gap - 1.05f * stripWidth) / (0.6f * stripWidth));
				float outward = Mathf.Clamp01((Mathf.Abs(pushes[strip + 1, r] - pushes[strip, r]) - 0.4f * thicks[r]) / Mathf.Max(0.05f, thicks[r]));
				return Mathf.Max(apart, outward);
			}
			Vector3 landingSum = Vector3.zero;
			for (int strip = 0; strip < strips; strip++)
			{
				int first = positions.Count;
				// Whether a side has opened at all in a row: its face is drawn there (faded by the shader as it opens).
				var openHigh = new bool[rows];
				var openLow = new bool[rows];
				for (int r = 0; r < rows; r++)
				{
					Vector3 ahead = pathAhead[r];
					Vector3 left = lefts[r];
					int up = Mathf.Max(0, r - 1), down = Mathf.Min(rows - 1, r + 1);
					float dy = path[down].y - path[up].y;
					float dh = new Vector2(path[down].x - path[up].x, path[down].z - path[up].z).magnitude;
					Vector3 tangent = (ahead * Mathf.Max(1e-3f, dh) + Vector3.up * dy).normalized;
					Vector3 facing = Vector3.Cross(tangent, left).normalized;
					if (facing.y < 0f && Vector3.Dot(facing, ahead) < 0f)
					{
						facing = -facing;
					}
					float width = rowWidths[r];
					float stripWidth = width / strips;
					float offset = BaseOffset(strip, r) + shifts[strip, r];
					float pushed = pushes[strip, r];
					/* Each side: from midway to the neighbour (joined, one sheet) to this strip's own edge (cut), by how open that
					 * side is, smoothly. Both neighbours use the one figure for the side they share, so they meet exactly while it
					 * is shut and draw apart together as it opens: no edge jumps from row to row. */
					float highOpen = Openness(strip, r), lowOpen = Openness(strip - 1, r);
					openHigh[r] = highOpen > 0.01f;
					openLow[r] = lowOpen > 0.01f;
					float highOffset = offset + 0.5f * stripWidth, highPush = pushed;
					if (strip + 1 < strips)
					{
						highOffset = Mathf.Lerp(0.5f * (offset + BaseOffset(strip + 1, r) + shifts[strip + 1, r]), highOffset, highOpen);
						highPush = Mathf.Lerp(0.5f * (pushed + pushes[strip + 1, r]), pushed, highOpen);
					}
					float lowOffset = offset - 0.5f * stripWidth, lowPush = pushed;
					if (strip > 0)
					{
						lowOffset = Mathf.Lerp(0.5f * (offset + BaseOffset(strip - 1, r) + shifts[strip - 1, r]), lowOffset, lowOpen);
						lowPush = Mathf.Lerp(0.5f * (pushed + pushes[strip - 1, r]), pushed, lowOpen);
					}
					float halfThick = 0.5f * thicks[r];
					// Lying on the river (facing up) its top face is the river's surface, the body under it; hanging, centred.
					Vector3 sink = -facing * (halfThick * Mathf.Clamp01(facing.y));
					Vector3 high = path[r] + left * highOffset + ahead * highPush + sink;
					Vector3 low = path[r] + left * lowOffset + ahead * lowPush + sink;
					// Eight corners a row: front low, front high, back low, back high, then the sides' own (their own normals).
					Vector3[] corner =
					{
						low + facing * halfThick, high + facing * halfThick, low - facing * halfThick, high - facing * halfThick,
						high + facing * halfThick, high - facing * halfThick, low + facing * halfThick, low - facing * halfThick,
					};
					Vector3 sideHigh = (left * 0.8f + facing * 0.2f).normalized, sideLow = (-left * 0.8f + facing * 0.2f).normalized;
					Vector3[] normal = { facing, facing, -facing, -facing, sideHigh, (left * 0.8f - facing * 0.2f).normalized, sideLow, (-left * 0.8f - facing * 0.2f).normalized };
					float[] across = { lowOffset, highOffset, lowOffset, highOffset, highOffset, highOffset, lowOffset, lowOffset };
					float[] open = { lowOpen, highOpen, lowOpen, highOpen, highOpen, highOpen, lowOpen, lowOpen };
					for (int k = 0; k < 8; k++)
					{
						positions.Add(corner[k]);
						normals.Add(normal[k]);
						// Across the whole curtain, front and back alike, so the streaks run on from strip to strip.
						uv0.Add(new Vector2(0.5f + across[k] / Mathf.Max(0.01f, width), fallens[r]));
						uv1.Add(new Vector2(width, pathAir[r]));
						uv2.Add(new Vector2(v, fall.Drop));
						// x the sheet's thickness here, y metres above the lip (the shader fades it in over the river there).
						uv3.Add(new Vector2(thicks[r], befores[r]));
						// x how churned it is from riding rock (1 in contact), y how held still by rock (no sway).
						uv4.Add(new Vector2(riding[strip, r], pinned[r]));
						// x metres along the water's path from the lip (negative above it), y spare.
						uv5.Add(new Vector2(pathMetres[r], 0f));
						// Across the curtain: the shader sways it, and flutters its open edges, along this.
						tangents.Add(new Vector4(left.x, left.y, left.z, 1f));
						/* r: how much water goes over the lip at this place, from the river's solved flow (0.5 its mean, 0 none
						 * behind a rock), spreading as it falls, so a gap behind a boulder at the lip fills in lower down.
						 * g: 1 on the slab's open edges (its outer sides and either side of a cut), where the shader frays it. */
						float lipAcross = 0.5f + (BaseOffset(strip, r) + (across[k] - offset)) / Mathf.Max(0.01f, width);
						float share = lip != null ? Spread(lip, lipAcross, LipSpreadPerMetre * fallens[r] / Mathf.Max(0.01f, width)) : 1f;
						// b: 1 on a side face, which the shader fades in with its openness (shut, it lies inside the sheet).
						colours.Add(new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(share * 0.5f)), (byte)Mathf.RoundToInt(255f * open[k]), (byte)(k >= 4 ? 255 : 0), 255));
					}
				}
				for (int r = 0; r + 1 < rows; r++)
				{
					int a = first + r * 8, b = a + 8;
					// Front, back and both sides; a side shut against its neighbour is faded out by the shader, not left out here.
					AddOutward(indices, positions, normals, a + 0, b + 0, b + 1);
					AddOutward(indices, positions, normals, a + 0, b + 1, a + 1);
					AddOutward(indices, positions, normals, a + 2, b + 3, b + 2);
					AddOutward(indices, positions, normals, a + 2, a + 3, b + 3);
					if (strip == strips - 1 || openHigh[r] || openHigh[r + 1])
					{
						AddOutward(indices, positions, normals, a + 4, b + 4, b + 5);
						AddOutward(indices, positions, normals, a + 4, b + 5, a + 5);
					}
					if (strip == 0 || openLow[r] || openLow[r + 1])
					{
						AddOutward(indices, positions, normals, a + 6, b + 7, b + 6);
						AddOutward(indices, positions, normals, a + 6, a + 7, b + 7);
					}
				}
				landingSum += path[rows - 1] + lefts[rows - 1] * (BaseOffset(strip, rows - 1) + shifts[strip, rows - 1]) + pathAhead[rows - 1] * pushes[strip, rows - 1];
			}
			landing = landingSum / strips;
			landing.y = Mathf.Max(path[rows - 1].y, line[line.Count - 1].y);
			var mesh = new Mesh { name = $"Fall {fall.River}.{fall.Lip}", hideFlags = HideFlags.DontSave };
			if (positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uv0);
			mesh.SetUVs(1, uv1);
			mesh.SetUVs(2, uv2);
			mesh.SetUVs(3, uv3);
			mesh.SetUVs(4, uv4);
			mesh.SetUVs(5, uv5);
			mesh.SetTangents(tangents);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateBounds();
			// Room for the sway (FishWaterfall: up to a metre either way across a tall curtain).
			Bounds swayBounds = mesh.bounds;
			swayBounds.Expand(new Vector3(2f, 0.5f, 2f));
			mesh.bounds = swayBounds;
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
			/* The impact zone: where the curtain hits the pool, as wide as the curtain and reaching out by its width and a
			 * share of the drop. Spray and mist are dense there and thin out with distance from it: every landing particle
			 * starts on the curtain's landing line and is set out from it by R·u², so most crowd the impact and only a few
			 * reach its edge, thrown and drifting downstream more than back against the rock. They were 440 droplets of
			 * 2-6 cm and 72 puffs pushed out sideways to the pool's edges: nothing showed where the water landed. */
			float impactRadius = fall.ImpactRadius;
			int spray = Mathf.Clamp(Mathf.RoundToInt(3f * SprayDensity * fall.Width * Mathf.Sqrt(fall.Drop)), 64, 4000);
			int mist = Mathf.Clamp(spray / 5, 16, 600);
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
			var impacts = new List<Vector2>();
			var indices = new List<int>();
			var rng = new System.Random(fall.River * 7919 + fall.Lip * 104729);
			byte drop = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(fall.Drop / 30f));
			void Particle(float kind) => ParticleAt(foot, fall.Width * 0.9f, impactRadius, kind);
			void ParticleAt(Vector3 centre, float lineWidth, float radius, float kind)
			{
				// Home: on the curtain's landing line, then out from it by R·u² (dense at the impact, sparse at its edge),
				// mostly downstream: an offset back toward the rock is shortened to a third.
				Vector3 onLine = centre + left * (((float)rng.NextDouble() - 0.5f) * lineWidth);
				float u = (float)rng.NextDouble();
				float r = radius * u * u;
				float turn = (float)rng.NextDouble() * Mathf.PI * 2f;
				Vector3 offset = (downstream * Mathf.Cos(turn) + left * Mathf.Sin(turn)) * r;
				float back = Vector3.Dot(offset, downstream);
				if (back < 0f)
				{
					offset -= downstream * (back * (2f / 3f));
				}
				Vector3 home = onLine + offset;
				var seed = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
				// The shader's own measure of how far out from the impact it starts: its share of the zone, and which way.
				var impact = new Vector2(offset.x, offset.z) / radius;
				int start = positions.Count;
				foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
				{
					positions.Add(home);
					normals.Add(downstream);
					uv0.Add(corner);
					uv1.Add(seed);
					uv2.Add(new Vector2(kind, radius));
					colours.Add(new Color32(drop, 0, 0, 255));
					impacts.Add(impact);
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
			// A burst where each of the curtain's streams strikes rock on its way down (SheetMesh's strikes).
			if (fall.Strikes != null)
			{
				foreach (Vector3 strike in fall.Strikes)
				{
					for (int k = 0; k < StrikeSpray; k++)
					{
						ParticleAt(strike, StripMetres, StrikeRadius, 0f);
					}
					for (int k = 0; k < StrikeMist; k++)
					{
						ParticleAt(strike, StripMetres, StrikeRadius, 1f);
					}
				}
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
					impacts.Add(Vector2.zero);
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
			mesh.SetUVs(3, impacts);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			// The particles move in the shader: bound where they can reach (up and out from the foot, drifting downstream).
			float reach = fall.PoolRadius * 2.5f + fall.Drop;
			mesh.bounds = new Bounds(foot + Vector3.up * (0.5f * fall.Drop) + downstream * (0.25f * reach), new Vector3(2f * reach, 2f * fall.Drop + reach, 2f * reach));
			return mesh;
		}
	}
}
