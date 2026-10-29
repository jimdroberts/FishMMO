using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Water
{
	/// <summary>
	/// The falls: where a river drops (its <see cref="SceneHydrology.River.Reach"/> marks a fall), its water traced
	/// down from the lip to the pool, drawn as a sheet that breaks into ropes of white water, with spray and mist round
	/// it, and published to everything round a fall that is not the water (<see cref="Waterfalls"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The water's own path.</b> Each strip of the curtain leaves the brink at the speed and depth the river's
	/// discharge gives it (<see cref="FallHydraulics"/>) and is followed down under gravity
	/// (InlandWaterRenderer.FallSheet.cs). Rock it meets turns it: it runs along a face, is thrown off a ledge, parts
	/// round a boulder. Nothing else steers it, because water falling free has nothing to push it sideways.
	/// </para>
	/// <para>
	/// <b>Sheet, then ropes.</b> The water is one glassy sheet only for its break-up length, a few metres for a
	/// mountain stream; below that it falls as ropes of white water with air between them, and the sheet gives way to
	/// them.
	/// </para>
	/// <para>
	/// <b>Stateless spray.</b> Every droplet, strand and wisp of mist is a quad whose whole life is worked out in the
	/// shader from its seed and the inland clock; strands read the traced paths from a small texture, so they fall
	/// where the water falls, partings and all.
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

		[Tooltip("The jet's turbulence intensity leaving a lip (FallHydraulics): higher breaks the water up sooner and spreads it more. Measured nappes 0.012–0.013.")]
		[Range(0.005f, 0.06f)]
		public float LipTurbulence = FallHydraulics.DefaultTurbulence;

		/// <summary>The most falls (or separate parts of falls) the inland water's shader churns at once.</summary>
		public const int MaxShaderFalls = 16;

		/// <summary>The most falls the pool shader lays a foam trail down one river for.</summary>
		public const int MaxRiverFalls = 4;

		private static readonly int FallsId = Shader.PropertyToID("_FishFalls");
		private static readonly int FallsBId = Shader.PropertyToID("_FishFallsB");
		private static readonly int FallCountId = Shader.PropertyToID("_FishFallCount");
		/// <summary>A river's falls on its copy of the material: <c>_RiverFall0</c> … <c>_RiverFall3</c>, loose vectors in UnityPerMaterial.</summary>
		private static readonly int[] RiverFallIds =
		{
			Shader.PropertyToID("_RiverFall0"), Shader.PropertyToID("_RiverFall1"), Shader.PropertyToID("_RiverFall2"), Shader.PropertyToID("_RiverFall3"),
		};
		private static readonly int RiverFallCountId = Shader.PropertyToID("_RiverFallCount");
		private static readonly int FoamTextureId = Shader.PropertyToID("_FoamTexture");
		private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
		private static readonly int FallPathTexId = Shader.PropertyToID("_FallPathTex");
		private static readonly int FallPathId = Shader.PropertyToID("_FallPath");
		private static readonly int FallOriginId = Shader.PropertyToID("_FallOrigin");
		private static readonly int FallMistNoiseId = Shader.PropertyToID("_FallMistNoise");
		private static readonly int FallMistNoiseCellsId = Shader.PropertyToID("_FallMistNoiseCells");
		private readonly Vector4[] shaderFalls = new Vector4[MaxShaderFalls];
		private readonly Vector4[] shaderFallsB = new Vector4[MaxShaderFalls];

		/// <summary>Per river, the falls whose foam it carries downstream: (metres along at the landing, trail metres, strength, metres across).</summary>
		private readonly Dictionary<int, Vector4[]> riverFalls = new Dictionary<int, Vector4[]>();
		private readonly Dictionary<int, int> riverFallCounts = new Dictionary<int, int>();

		/// <summary>The curtains and sprays built, each kind's next slot among its copies of the material (Looks: FallPart).</summary>
		private int curtainParts, sprayParts;

		/// <summary>The textures the falls own (their traced paths), destroyed with them.</summary>
		private readonly List<Texture> fallTextures = new List<Texture>();

		/// <summary>Where a stream of the curtain strikes rock on its way down: a burst of spray is thrown off it there.</summary>
		public struct Strike
		{
			public Vector3 Point;
			/// <summary>Out of the rock.</summary>
			public Vector3 Normal;
			/// <summary>How fast the water struck it, m/s.</summary>
			public float Speed;
		}

		/// <summary>One separate part of a fall, where ledges or boulders have split it, or the whole fall.</summary>
		public struct Part
		{
			/// <summary>Where it lands in the pool.</summary>
			public Vector3 Landing;
			/// <summary>How wide it is where it lands, metres.</summary>
			public float Width;
			/// <summary>The water in it, m³/s.</summary>
			public float Discharge;
			/// <summary>How thick it lands, metres (B_j).</summary>
			public float ImpactThickness;
		}

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
			/// Where the curtain meets the pool, its parts' landings weighted by their water. The pool's boil and the spray
			/// stand here, not at <see cref="FootPoint"/>, which can lie metres downstream of where the water lands.
			/// </summary>
			public Vector3 Landing;
			/// <summary>How far its surface drops, metres.</summary>
			public float Drop;
			/// <summary>How wide the water is where it goes over, metres: the lip's width, not the plunge basin's.</summary>
			public float Width;
			/// <summary>The water's speed leaving the brink, m/s (<see cref="FallHydraulics.BrinkSpeed"/>).</summary>
			public float Speed;
			/// <summary>The water going over, m³/s.</summary>
			public float Discharge;
			/// <summary>The water per metre of lip, m²/s.</summary>
			public float UnitDischarge;
			/// <summary>How far down it stays one sheet, metres.</summary>
			public float BreakupLength;
			/// <summary>How fast it lands, m/s.</summary>
			public float ImpactSpeed;
			/// <summary>How far round its foot the pool reaches, metres (the basin it scoured).</summary>
			public float PoolRadius;
			/// <summary>
			/// How far out from where the curtain lands the water is thrown and boils, metres: the curtain's width and a
			/// share of the drop. The churn had reached the whole basin's radius (48 m for Flo Monolith's fall), so a 15 m
			/// pool was white to its outlet.
			/// </summary>
			public float ImpactRadius;
			/// <summary>Where the curtain's streams strike rock on their way down: each throws a burst of spray.</summary>
			public List<Strike> Strikes;
			/// <summary>Its separate parts where it lands (one, unless rock split it).</summary>
			public List<Part> Parts;
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
				// Lava pours down its own ribbon (Lava): no water's sheet, spray or roar.
				if (!river.Perennial || river.Lava || n < 3 || river.Reach == null || river.Reach.Length != n)
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
						float depth = river.Depth != null && lip < river.Depth.Length ? river.Depth[lip] : 0.6f;
						float speed = river.Speed != null && lip < river.Speed.Length ? river.Speed[lip] : 1f;
						float discharge = river.Discharge != null && lip < river.Discharge.Length ? river.Discharge[lip] : 0f;
						float q = FallHydraulics.UnitDischarge(discharge, width, depth, speed);
						int plunge = foot;
						for (int k = lip + 1; k <= foot; k++)
						{
							if (river.Points[k].y <= river.Points[foot].y + PoolLevelMetres)
							{
								plunge = k;
								break;
							}
						}
						float poolRadius = Mathf.Max(3f, 0.9f * poolWidth + 0.35f * drop);
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
							UnitDischarge = q,
							Discharge = q * width,
							// The brink's own speed (critical flow over a free edge), not the reach's mean: it is what throws the water out.
							Speed = FallHydraulics.BrinkSpeed(q),
							BreakupLength = FallHydraulics.BreakupLength(q),
							ImpactSpeed = FallHydraulics.FreeSpeed(FallHydraulics.BrinkSpeed(q), drop),
							PoolRadius = poolRadius,
							ImpactRadius = Mathf.Clamp(0.5f * width + 0.15f * drop, 2f, Mathf.Max(2f, poolRadius)),
						});
					}
					i = last + 1;
				}
			}
			return found;
		}

		/// <summary>The value <see cref="SceneHydrology.River.Reach"/> holds at a falling point (the generator's RiverReach.Fall).</summary>
		public const byte FallReach = 4;

		/// <summary>How far the pool churns, in impact radii (FishInlandWater.hlsl FallPool fades to nothing there).</summary>
		public const float ChurnShare = 1.6f;

		/// <summary>How near the foot's level a point's surface must be to count as the pool, metres.</summary>
		private const float PoolLevelMetres = 0.5f;

		/// <summary>The value <see cref="SceneHydrology.River.Reach"/> holds at a pool (the generator's RiverReach.Pool).</summary>
		public const byte PoolReach = 2;

		/// <summary>
		/// How fast the current a fall drives through its pool widens, metres of width per metre it runs: a plane turbulent
		/// jet's, each edge spreading at about a tenth of the distance (a half-angle near 6°).
		/// </summary>
		public const float PoolJetSpreadPerMetre = 0.2f;

		/// <summary>
		/// The first point past a fall's pool: the first after its foot that the river no longer marks as pool, where the
		/// pool's tail has shoaled back to the river's own channel (the last point if the pool runs to the river's end).
		/// </summary>
		public static int PoolTail(SceneHydrology.River river, Fall fall)
		{
			int n = river.Points != null ? river.Points.Length : 0;
			int k = fall.Foot + 1;
			while (k < n - 1 && river.Reach != null && k < river.Reach.Length && river.Reach[k] == PoolReach)
			{
				k++;
			}
			return Mathf.Clamp(k, 0, Mathf.Max(0, n - 1));
		}

		/// <summary>
		/// The surface current down the middle of a fall's plunge pool, m/s, <paramref name="metresFromLanding"/> below where
		/// the curtain lands, and the half-width of the core that carries it (<paramref name="coreHalfWidth"/>, metres).
		/// </summary>
		/// <remarks>
		/// <para>
		/// The river's mean speed over a plunge basin is its discharge over the whole basin's section: over Flo Monolith's
		/// tallest falls a 30 m wide, 6.5 m deep scour, a centimetre a second. Drawn at that, the pool and its tail were
		/// dead still water from the landing to the pool's lip, its ripples standing where they lay (2026-10-13).
		/// </para>
		/// <para>
		/// That is not how water crosses a plunge pool. The curtain drives down into it, turns on the floor and wells back
		/// up in the boil, and from there the river's whole discharge runs on near the surface toward the outlet in a
		/// current a little deeper than the river flowing out (<paramref name="outletDepth"/>), widening from the curtain's
		/// width (<paramref name="lipWidth"/>) as a jet does (<see cref="PoolJetSpreadPerMetre"/>), never wider than the
		/// pool (<paramref name="poolWidth"/>); the depth and the sides beneath and beside it turn over slowly in eddies.
		/// Continuity in that core gives its speed: the discharge over its section. Where the pool shoals to its tail the
		/// core fills the channel and its speed meets the river's own.
		/// </para>
		/// </remarks>
		public static float PoolCoreSpeed(float discharge, float lipWidth, float outletDepth, float metresFromLanding, float poolWidth, out float coreHalfWidth)
		{
			float width = Mathf.Max(0.5f, lipWidth) + PoolJetSpreadPerMetre * Mathf.Max(0f, metresFromLanding);
			width = Mathf.Min(width, Mathf.Max(0.5f, poolWidth));
			coreHalfWidth = 0.5f * width;
			return Mathf.Max(0f, discharge) / (width * Mathf.Max(0.2f, outletDepth));
		}

		/// <summary>
		/// The pool's surface current <paramref name="offset"/> metres across from its middle, m/s: the core's speed across
		/// its width, easing out beyond it to the slack water of the eddies at the basin's sides, and never slower than the
		/// river's mean speed there (<paramref name="meanSpeed"/>).
		/// </summary>
		public static float PoolCurrentAcross(float offset, float coreHalfWidth, float coreSpeed, float meanSpeed)
		{
			float outside = Mathf.Max(0f, Mathf.Abs(offset) - coreHalfWidth) / Mathf.Max(0.25f, 0.5f * coreHalfWidth);
			return Mathf.Max(meanSpeed, meanSpeed + (coreSpeed - meanSpeed) * Mathf.Exp(-outside * outside));
		}

		/// <summary>A fall's pool on one river's ribbon: its rows by point, and what drives its surface current.</summary>
		private struct PoolSpan
		{
			/// <summary>The first point of the pool drawn (the row lowered under the curtain, or the plunge) and the first past it.</summary>
			public int First, Tail;
			/// <summary>The point where the curtain lands, from which the core is measured.</summary>
			public int Plunge;
			public float LipWidth, OutletDepth, Discharge;
		}

		/// <summary>The pools of the falls on <paramref name="river"/>, for its ribbon (<see cref="PoolAt"/>).</summary>
		private static List<PoolSpan> PoolsOf(List<Fall> falls, SceneHydrology.River river)
		{
			var pools = new List<PoolSpan>();
			if (falls == null)
			{
				return pools;
			}
			int n = river.Points.Length;
			foreach (Fall fall in falls)
			{
				if (fall.River != river.Id)
				{
					continue;
				}
				int tail = PoolTail(river, fall);
				float outletDepth = river.Depth != null && tail < river.Depth.Length ? river.Depth[tail] : 0.6f;
				float discharge = river.Discharge != null && fall.Plunge < river.Discharge.Length ? river.Discharge[fall.Plunge] : 0f;
				if (discharge <= 0f)
				{
					// No discharge kept: the river's own flow out of the pool (mean speed over its section at the tail).
					float outletWidth = river.Width != null && tail < river.Width.Length ? river.Width[tail] : fall.Width;
					float outletSpeed = river.Speed != null && tail < river.Speed.Length ? river.Speed[tail] : 0.5f;
					discharge = outletSpeed * outletWidth * outletDepth;
				}
				pools.Add(new PoolSpan
				{
					First = Mathf.Max(fall.Lip + 1, fall.Plunge - 1),
					Tail = Mathf.Min(tail, n),
					Plunge = fall.Plunge,
					LipWidth = fall.Width,
					OutletDepth = outletDepth,
					Discharge = discharge,
				});
			}
			return pools;
		}

		/// <summary>The pool a river point lies in, if any (<see cref="PoolsOf"/>).</summary>
		private static bool PoolAt(List<PoolSpan> pools, int point, out PoolSpan pool)
		{
			foreach (PoolSpan span in pools)
			{
				if (point >= span.First && point < span.Tail)
				{
					pool = span;
					return true;
				}
			}
			pool = default;
			return false;
		}

		/// <summary>Whether <paramref name="point"/> is a fall's lip: the last row of the river before its curtain.</summary>
		private static bool IsLipRow(List<Fall> falls, int river, int point)
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

		/// <summary>
		/// Builds every fall's sheet and spray, publishes their landings to the inland water's shader and their size to
		/// <see cref="Waterfalls"/>, and gathers each river's falls for the foam it carries downstream (<see cref="ApplyFlow"/>).
		/// </summary>
		private void BuildFalls(SceneHydrology hydrology, List<Fall> found)
		{
			falls.Clear();
			falls.AddRange(found);
			riverFalls.Clear();
			riverFallCounts.Clear();
			var published = new List<Waterfalls.Fall>();
			int shaderCount = 0;
			for (int f = 0; f < falls.Count; f++)
			{
				Fall fall = falls[f];
				SceneHydrology.River river = hydrology.Rivers.Find(r => r.Id == fall.River);
				if (river == null)
				{
					continue;
				}
				SheetBuild build = null;
				if (FallMaterial != null)
				{
					SceneRiverFlow.River solved = flow != null ? flow.Rivers.Find(r => r.Id == fall.River) : null;
					build = SheetMesh(river, fall, gameObject.scene, solved, LipTurbulence);
					if (build != null && build.Mesh != null)
					{
						MeshRenderer sheet = AddFallPart($"Fall {fall.River}.{fall.Lip}", build.Mesh, FallMaterial, CurtainSortingOrder, ShadowCastingMode.On);
						// Its textures and the look of the water it pours from, on its own copy of the material (ApplyFallLooks, Looks).
						fallLookParts.Add(new FallPart { Renderer = sheet, River = fall.River, Lip = fall.LipPoint, Spray = false, Slot = curtainParts++ });
						fall.Landing = build.Landing;
						fall.Strikes = build.Strikes;
						fall.Parts = build.Parts;
						fall.ImpactSpeed = build.ImpactSpeed;
						if (build.Paths != null)
						{
							fallTextures.Add(build.Paths);
						}
					}
				}
				if (fall.Parts == null || fall.Parts.Count == 0)
				{
					fall.Parts = new List<Part>
					{
						new Part
						{
							Landing = fall.Landing,
							Width = fall.Width,
							Discharge = fall.Discharge,
							ImpactThickness = FallHydraulics.JetThickness(fall.UnitDischarge, fall.Drop, LipTurbulence),
						},
					};
				}
				falls[f] = fall;
				if (SprayMaterial != null)
				{
					Mesh spray = SprayMesh(river, fall, build);
					if (spray != null)
					{
						MeshRenderer sprayRenderer = AddFallPart($"Spray {fall.River}.{fall.Lip}", spray, SprayMaterial, SpraySortingOrder, ShadowCastingMode.Off);
						var part = new FallPart { Renderer = sprayRenderer, River = fall.River, Lip = fall.LipPoint, Spray = true, Slot = sprayParts++ };
						if (build != null && build.Paths != null)
						{
							part.Paths = build.Paths;
							part.PathInfo = new Vector4(build.Strips, build.PathSeconds, build.PoolLevel, fall.BreakupLength);
							part.Origin = new Vector4(fall.LipPoint.x, fall.LipPoint.y, fall.LipPoint.z, fall.ImpactSpeed);
						}
						// Its mist lattice, its paths and the look of the water it pours from, on its own copy (ApplyFallLooks, Looks).
						fallLookParts.Add(part);
					}
				}
				Vector3 downstream = fall.FootPoint - fall.LipPoint;
				downstream.y = 0f;
				downstream = downstream.sqrMagnitude > 1e-6f ? downstream.normalized : Vector3.forward;
				foreach (Part part in fall.Parts)
				{
					float power = Waterfalls.PowerOf(part.Discharge, fall.Drop);
					float impactRadius = Mathf.Clamp(0.5f * part.Width + 0.15f * fall.Drop, 2f, Mathf.Max(2f, fall.PoolRadius));
					if (shaderCount < MaxShaderFalls)
					{
						shaderFalls[shaderCount] = new Vector4(part.Landing.x, part.Landing.y, part.Landing.z, ChurnShare * impactRadius);
						shaderFallsB[shaderCount] = new Vector4(part.ImpactThickness, power * 1e-6f, BoilHeave(power), PoolWhiteness(power));
						shaderCount++;
					}
					published.Add(new Waterfalls.Fall
					{
						Lip = fall.LipPoint,
						Landing = part.Landing,
						Downstream = downstream,
						Width = part.Width,
						Drop = fall.Drop,
						Discharge = part.Discharge,
						Power = power,
						BreakupLength = fall.BreakupLength,
						ImpactThickness = part.ImpactThickness,
						ImpactRadius = impactRadius,
					});
					AddRiverFall(river, fall, part, power);
				}
			}
			for (int k = shaderCount; k < MaxShaderFalls; k++)
			{
				shaderFalls[k] = Vector4.zero;
				shaderFallsB[k] = Vector4.zero;
			}
			Shader.SetGlobalVectorArray(FallsId, shaderFalls);
			Shader.SetGlobalVectorArray(FallsBId, shaderFallsB);
			Shader.SetGlobalFloat(FallCountId, shaderCount);
			Waterfalls.Publish(this, published);
		}

		/// <summary>
		/// How high the boil heaves where a fall lands, metres: a few centimetres under a trickle, a few tenths under a
		/// river, by the decades of its power.
		/// </summary>
		public static float BoilHeave(float power) => Mathf.Clamp(0.05f + 0.08f * Mathf.Log10(Mathf.Max(1f, power) / 1e3f), 0f, 0.5f);

		/// <summary>How white the pool churns where a fall lands, 0 … 1: a quarter for a kilowatt trickle, all of it from a megawatt.</summary>
		public static float PoolWhiteness(float power) => Mathf.Clamp01(0.25f + 0.25f * Mathf.Log10(Mathf.Max(1f, power) / 1e3f));

		/// <summary>
		/// Adds a part's landing to its river's foam trail: where along the river it lands (the ribbon's own metres along,
		/// uv0.x), how far the foam it beats up rides downstream (five to twenty river widths, by its power), and how much.
		/// </summary>
		/// <remarks>
		/// The widths are the river's own, where it leaves the pool (<see cref="PoolTail"/>), not the plunge basin's at the
		/// landing: a basin is scoured up to 30 m across over a 3 m river, and twenty of its widths sent a band of foam
		/// 600 m down a mountain stream.
		/// </remarks>
		private void AddRiverFall(SceneHydrology.River river, Fall fall, Part part, float power)
		{
			if (!RiverFrame(river, part.Landing, out float along, out float across, out float width))
			{
				return;
			}
			int tail = PoolTail(river, fall);
			if (river.Width != null && tail < river.Width.Length)
			{
				width = river.Width[tail];
			}
			if (!riverFalls.TryGetValue(river.Id, out Vector4[] list))
			{
				riverFalls[river.Id] = list = new Vector4[MaxRiverFalls];
				riverFallCounts[river.Id] = 0;
			}
			int count = riverFallCounts[river.Id];
			if (count >= MaxRiverFalls)
			{
				return;
			}
			float decades = Mathf.Log10(Mathf.Max(1f, power) / 1e3f);
			float trail = width * Mathf.Clamp(5f + 5f * decades, 5f, 20f);
			float strength = Mathf.Clamp01(decades / 3f);
			list[count] = new Vector4(along, trail, strength, across);
			riverFallCounts[river.Id] = count + 1;
		}

		/// <summary>
		/// A point in a river's own frame, as its ribbon lays it out: metres along its line from its first point (counted
		/// horizontally, as RiverMesh counts them) and metres across, left of the line positive; and the river's width there.
		/// </summary>
		private static bool RiverFrame(SceneHydrology.River river, Vector3 point, out float along, out float across, out float width)
		{
			along = across = width = 0f;
			if (river.Points == null || river.Points.Length < 2)
			{
				return false;
			}
			float best = float.MaxValue, walked = 0f;
			for (int i = 0; i + 1 < river.Points.Length; i++)
			{
				Vector3 a = river.Points[i], b = river.Points[i + 1];
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				float length = ab.magnitude;
				if (length > 1e-5f)
				{
					Vector2 dir = ab / length;
					var ap = new Vector2(point.x - a.x, point.z - a.z);
					float t = Mathf.Clamp(Vector2.Dot(ap, dir), 0f, length);
					Vector2 off = ap - dir * t;
					float d = off.sqrMagnitude;
					if (d < best)
					{
						best = d;
						along = walked + t;
						across = dir.x * off.y - dir.y * off.x;
						float wa = river.Width != null && i < river.Width.Length ? river.Width[i] : 4f;
						float wb = river.Width != null && i + 1 < river.Width.Length ? river.Width[i + 1] : wa;
						width = Mathf.Lerp(wa, wb, t / length);
					}
				}
				walked += length;
			}
			return best < float.MaxValue;
		}

		/// <summary>
		/// Sorting orders above every water surface (InlandWaterRenderer.Add numbers lakes and rivers 0, 1, 2…): Unity sorts
		/// transparent renderers by sorting order before their render queue, so at order 0 the falls and their spray were
		/// drawn first and every river surface, the plunge pool among them, painted over the splash (2026-10-07).
		/// </summary>
		private const int CurtainSortingOrder = 30000, SpraySortingOrder = 30001;

		private MeshRenderer AddFallPart(string name, Mesh mesh, Material material, int sortingOrder, ShadowCastingMode shadows)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
			go.transform.SetParent(transform, false);
			go.transform.position = Vector3.zero;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			// The curtain casts a soft, moving shadow on the rock behind it and the pool below (its ShadowCaster pass dithers).
			renderer.shadowCastingMode = shadows;
			renderer.receiveShadows = true;
			renderer.sortingOrder = sortingOrder;
			built.Add(go);
			return renderer;
		}

		/// <summary>Destroys what the falls own beyond their meshes, and withdraws them from <see cref="Waterfalls"/>.</summary>
		private void ClearFalls()
		{
			foreach (Texture texture in fallTextures)
			{
				if (texture != null)
				{
					Discard(texture);
				}
			}
			fallTextures.Clear();
			fallLookParts.Clear();
			curtainParts = 0;
			sprayParts = 0;
			riverFalls.Clear();
			riverFallCounts.Clear();
			Waterfalls.Withdraw(this);
		}

		/// <summary>Sets a river's foam trail data on its copy of the material; false (count left 0) for a river with no fall above its water.</summary>
		private bool ApplyRiverFalls(int riverId, Material own)
		{
			if (!riverFalls.TryGetValue(riverId, out Vector4[] list) || !riverFallCounts.TryGetValue(riverId, out int count) || count == 0)
			{
				return false;
			}
			for (int i = 0; i < RiverFallIds.Length; i++)
			{
				own.SetVector(RiverFallIds[i], i < list.Length ? list[i] : Vector4.zero);
			}
			own.SetFloat(RiverFallCountId, count);
			return true;
		}

		/// <summary>A point on the river's line between its points <paramref name="i"/> and i + 1, <paramref name="t"/> of the way.</summary>
		private static Vector3 OnLine(SceneHydrology.River river, int i, float t)
		{
			int j = Mathf.Min(river.Points.Length - 1, i + 1);
			return Vector3.Lerp(river.Points[i], river.Points[j], t);
		}

		/// <summary>Debug: logs each curtain's strips as it is built (where each lands, what it struck, how far rock turned it).</summary>
		public static bool LogSheets;
	}
}
