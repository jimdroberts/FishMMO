#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How an overhang or arch site is built (<see cref="OverhangShaper"/>).</summary>
	public enum OverhangBuild : byte
	{
		/// <summary>Retired (2026-10-14): a stretched boulder slab over an alcove. Stored plans of it say "re-cut".</summary>
		Slab = 0,
		/// <summary>Retired (2026-10-14): the 2026-10-10 leaning wall.</summary>
		Section = 1,
		/// <summary>The <c>Arch</c> cliff section as a fin standing out of the face, pierced through its middle.</summary>
		Arch = 2,
		/// <summary>
		/// The <c>Overhang</c> cliff section seated as the face itself: its top flush with the clifftop, its tongue jutting
		/// out over a shelter, the ground before it cut down to its foot, and cliff wings either side.
		/// </summary>
		Perch = 3,
	}

	/// <summary>
	/// The overhang and natural-arch kinds: a cliff perch over a rock shelter, or an arch at a steep face, laid as props in
	/// the scene's "POI" set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the perch (Jim, 2026-10-14: "just a rock sticking out of the ground instead of an actual perch on a cliff
	/// face").</b> The old overhang was the river boulders' slab stretched to a plank and pushed into a 40° earth slope over
	/// a slot cut under it: no cliff above, beside or below it, nothing to stand on, no drop. A real one is the face's own
	/// cap rock: a resistant bed left jutting where the softer beds under it wore back, its top the clifftop, a sheer face
	/// under it and the cliff running on either side. So the site is built as that cliff: the <c>Overhang</c> section
	/// (<see cref="CliffStyle.Overhang"/>) set with its top flush with the measured rim, its back in the plateau and its
	/// tongue out along the site's heading; the slope before it cut down to the section's foot, so the face shows its full
	/// height and the shelter under the tongue is open (a heightfield cannot be undercut; the rock roofs it);
	/// <see cref="WingStyle"/> sections either side, front flush, standing on the cut as it eases back into the hillside so
	/// its ends are cliff, not a notch; and fallen blocks under the lip and in the shelter.
	/// </para>
	/// <para>
	/// <b>Seating.</b> The face is read along the site's heading: the rim is the first point at four fifths of the rise from
	/// which the ground flattens. The section is scaled to that height (0.75–1.35) and its solid runs on 10 m (scaled)
	/// under its foot, so a face up to a third taller than it still meets rock. A face too low or too tall is refused.
	/// </para>
	/// </remarks>
	public sealed class OverhangShaper : IPointOfInterestShaper
	{
		public static readonly OverhangShaper Instance = new OverhangShaper();

		public const string ShaperName = "Overhang";
		private const int Format = 2;
		private const int ValueCount = 29;

		/// <summary>The cliff style either side of a perch.</summary>
		public const string WingStyle = "Scarp";
		/// <summary>The styles a perch on a face taller than itself stands on (by height: the second when the first would be stretched past 1.6).</summary>
		public static readonly string[] PlinthStyles = { "Scarp", "ScarpHigh" };

		/// <summary>How far out in front of a perch's face the ground is held at its foot, beyond the tongue's lip, metres.</summary>
		private const float ApronBeyondLip = 5f;
		/// <summary>Over how far the cut eases back to the hillside, in front, metres.</summary>
		private const float FrontEase = 12f;

		public bool Handles(POIType kind) => kind == POIType.Overhang || kind == POIType.NaturalArch;

		private static int SeedOf(PointOfInterestRecord site) => (int)(PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ 0x0E4A9C17u)) & 0x7FFFFFFFu);

		/// <summary>A cliff section either side of a perch, or under it.</summary>
		public struct Wing
		{
			/// <summary>For a plinth, its index in <see cref="PlinthStyles"/>.</summary>
			public int Style;
			public int Variant;
			public float Scale;
			/// <summary>Its origin (its foot's centre, as cliff sections are made), world.</summary>
			public Vector3 Position;
		}

		/// <summary>What a planned site stores (a shape's values).</summary>
		public struct Plan
		{
			public OverhangBuild Build;
			public int Variant;
			public float Scale;
			/// <summary>The section's origin, world, and its turn about +y. A perch's origin is its wall's own (not recentred).</summary>
			public Vector3 Position;
			public float Yaw;
			/// <summary>The site's heading, out of the face, degrees.</summary>
			public float Facing;
			/// <summary>A perch: the height the ground before its face was cut to.</summary>
			public float Floor;
			public bool HasWings;
			public Wing Left, Right;
			/// <summary>
			/// A perch on a face taller than itself stands at the top on a plinth of cliff from the true foot: its shelter is
			/// then a ledge cave up the face, as cliff dwellings sit (Betatakin's ledge ~18 m up an alcove 138 m high).
			/// </summary>
			public bool HasPlinth;
			public Wing Plinth;

			public float[] ToValues()
				=> new[]
				{
					Format, (float)Build, Variant, Scale, Position.x, Position.y, Position.z, Yaw, Facing, Floor, HasWings ? 1f : 0f,
					Left.Variant, Left.Scale, Left.Position.x, Left.Position.y, Left.Position.z,
					Right.Variant, Right.Scale, Right.Position.x, Right.Position.y, Right.Position.z, HasPlinth ? 1f : 0f,
					Plinth.Style, Plinth.Variant, Plinth.Scale, Plinth.Position.x, Plinth.Position.y, Plinth.Position.z, 0f,
				};

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != ValueCount || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan
				{
					Build = (OverhangBuild)Mathf.RoundToInt(v[1]),
					Variant = Mathf.RoundToInt(v[2]),
					Scale = v[3],
					Position = new Vector3(v[4], v[5], v[6]),
					Yaw = v[7],
					Facing = v[8],
					Floor = v[9],
					HasWings = v[10] > 0.5f,
					Left = new Wing { Variant = Mathf.RoundToInt(v[11]), Scale = v[12], Position = new Vector3(v[13], v[14], v[15]) },
					Right = new Wing { Variant = Mathf.RoundToInt(v[16]), Scale = v[17], Position = new Vector3(v[18], v[19], v[20]) },
					HasPlinth = v[21] > 0.5f,
					Plinth = new Wing { Style = Mathf.RoundToInt(v[22]), Variant = Mathf.RoundToInt(v[23]), Scale = v[24], Position = new Vector3(v[25], v[26], v[27]) },
				};
				return true;
			}
		}

		/// <summary>The section style a build uses.</summary>
		public static string StyleOf(OverhangBuild build) => build == OverhangBuild.Perch ? "Overhang" : build == OverhangBuild.Arch ? "Arch" : null;

		private static Vector3 Outward(float yaw)
		{
			float yr = yaw * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
		}

		/// <summary>
		/// What a site gets, decided from its kind, size and the ground. Pure: no scene, no assets. Null-free: false with
		/// <paramref name="problem"/> where the face cannot take one.
		/// </summary>
		public static bool PlanSite(POIType kind, int size, Vector3 foot, float yaw, int seed, Func<float, float, float> ground, out Plan plan, out string problem)
		{
			plan = default;
			problem = null;
			var random = new DeterministicRNG(seed);
			Vector3 outward = Outward(yaw);
			int variant = random.Next(CliffSections.VariantCount);

			if (kind == POIType.NaturalArch)
			{
				CliffStyle st = CliffSections.StyleOf("Arch", variant);
				float scale = random.Range(0.9f, 1.15f);
				float length = st.Length * scale;
				// Its inner sixth sunk in the face (the arch, through its middle, then opens a third of its length out); its foot
				// on the lowest ground under the part that stands out of the face.
				float centre = 0.5f * length - length / 6f;
				Vector3 at = foot + outward * centre;
				float low = float.MaxValue;
				for (float d = 0f; d <= centre + 0.5f * length; d += 1f)
				{
					Vector3 p = foot + outward * d;
					low = Mathf.Min(low, ground(p.x, p.z));
				}
				// The arch's own frame has its length along x: turn it so x runs out of the face.
				float archYaw = Mathf.Repeat(yaw - 90f, 360f);
				plan = new Plan { Build = OverhangBuild.Arch, Variant = variant, Scale = scale, Position = new Vector3(at.x, low, at.z), Yaw = archYaw, Facing = yaw };
				return true;
			}
			return PlanPerch(size, foot, yaw, random, variant, ground, out plan, out problem);
		}

		/// <summary>The face along the heading: its foot height, the rim's distance into the slope and the rim's height.</summary>
		public static bool MeasureFace(Vector3 foot, Vector3 outward, Func<float, float, float> ground, out float footHeight, out float rimDistance, out float rimHeight)
		{
			const int Reach = 72;
			var g = new float[Reach + 5];
			for (int d = 0; d < g.Length; d++)
			{
				g[d] = ground(foot.x - outward.x * d, foot.z - outward.z * d);
			}
			footHeight = g[0];
			float top = g[0];
			int highest = 0;
			for (int d = 1; d <= Reach; d++)
			{
				if (g[d] > top)
				{
					top = g[d];
					highest = d;
				}
			}
			float rise = top - footHeight;
			rimDistance = highest;
			rimHeight = top;
			for (int d = 3; d <= Reach; d++)
			{
				// Four fifths up, and flattening: under 2.2 m over the next 4 m (29°).
				if (g[d] - footHeight >= 0.8f * rise && g[d + 4] - g[d] < 2.2f)
				{
					rimDistance = d;
					rimHeight = g[d];
					break;
				}
			}
			return rise > 0f;
		}

		private static bool PlanPerch(int size, Vector3 foot, float yaw, DeterministicRNG random, int variant, Func<float, float, float> ground, out Plan plan, out string problem)
		{
			plan = default;
			problem = null;
			Vector3 outward = Outward(yaw), side = new Vector3(outward.z, 0f, -outward.x);
			CliffStyle st = CliffSections.StyleOf("Overhang", variant);
			float top = PerchTop(st);
			MeasureFace(foot, outward, ground, out float h0, out float rimDistance, out float rim);
			float face = rim - h0;
			if (face < 0.75f * top)
			{
				problem = $"the face ({face:0} m) is too low for a perch (it needs {0.75f * top:0} m)";
				return false;
			}
			// A larger site takes the larger end of the range the face allows.
			float scale = Mathf.Clamp(face / top, 0.75f, 1.35f) * Mathf.Lerp(0.94f, 1f, Mathf.Clamp(size, 0, 2) * 0.5f);
			float originY = rim + 0.15f - top * scale;
			// Taller than the perch and its root reach: it stands on a plinth from the true foot.
			bool plinth = originY - 0.6f * st.Root * scale > h0;
			float halfD = 0.5f * st.Depth * scale, halfL = 0.5f * st.Length * scale;
			// Its back a metre and a half past the rim, into the plateau.
			float originInto = rimDistance + 1.5f - halfD;
			Vector3 origin = foot - outward * originInto;
			plan = new Plan
			{
				Build = OverhangBuild.Perch,
				Variant = variant,
				Scale = scale,
				Position = new Vector3(origin.x, originY, origin.z),
				// The section's front faces −z: turn −z to face out.
				Yaw = Mathf.Repeat(yaw + 180f, 360f),
				Facing = Mathf.Repeat(yaw, 360f),
				Floor = plinth ? h0 - 0.3f : Mathf.Max(originY, h0 - 0.5f),
			};
			if (plinth)
			{
				// Its top a little over the perch's floor (the shelter's floor is the plinth's top, the perch's root in it), its
				// front a couple of metres out from the perch's face: a ledge before the shelter.
				float need = originY + 1.2f - (h0 - 0.6f);
				int pv = random.Next(CliffSections.VariantCount);
				// The lower style unless it would be stretched past 1.6 (its variant's own height: they differ by a third).
				int index = need / CliffSections.StyleOf(PlinthStyles[0], pv).Height > 1.6f ? 1 : 0;
				CliffStyle ps = CliffSections.StyleOf(PlinthStyles[index], pv);
				float pScale = need / ps.Height;
				if (pScale > 1.7f)
				{
					problem = $"the face ({face:0} m) is too tall even for a perch on a plinth";
					return false;
				}
				Vector3 perchFront = FrontCentre(plan, st);
				Vector3 at = perchFront + outward * (2f - 0.5f * ps.Depth * pScale);
				plan.HasPlinth = true;
				plan.Plinth = new Wing { Style = index, Variant = pv, Scale = pScale, Position = new Vector3(at.x, h0 - 0.6f, at.z) };
			}

			// The wings: front flush with the perch's, seated on the cut as it eases back up, tops at the ground behind them.
			Vector3 front = FrontCentre(plan, st);
			plan.HasWings = true;
			for (int s = -1; s <= 1; s += 2)
			{
				int wv = random.Next(CliffSections.VariantCount);
				CliffStyle w = CliffSections.StyleOf(WingStyle, wv);
				float along = halfL + 0.36f * w.Length * scale;
				float wDepth = w.Depth * scale;
				Vector3 at = front + side * (s * along) - outward * (0.5f * wDepth);
				float low = float.MaxValue, back = float.MinValue;
				for (int k = -2; k <= 2; k++)
				{
					Vector3 fp = front + side * (s * along + k * 0.22f * w.Length * scale);
					low = Mathf.Min(low, CutHeight(plan, st, fp.x, fp.z, ground(fp.x, fp.z)));
					Vector3 bp = fp - outward * wDepth;
					back = Mathf.Max(back, ground(bp.x, bp.z));
				}
				float wScale = Mathf.Clamp((back + 0.3f - low) / w.Height, 0.45f, 1.7f);
				var wing = new Wing { Variant = wv, Scale = wScale, Position = new Vector3(at.x, low - 0.4f, at.z) };
				if (s < 0) plan.Left = wing; else plan.Right = wing;
			}
			return true;
		}

		/// <summary>The height a perch's tongue (and the wall's top) stands at in its own frame, unscaled.</summary>
		public static float PerchTop(CliffStyle st) => st.Height * (1f - 0.5f * st.HeightVar);

		/// <summary>
		/// Where a perch stands, world, for cameras: its face line's middle at the ground before it, the tongue's lip at the
		/// clifftop, the heading out and across, the shelter's floor (the plinth's top on a plinth) and the top.
		/// </summary>
		public static void PerchFrame(in Plan plan, out Vector3 front, out Vector3 lip, out Vector3 outward, out Vector3 side, out float shelterFloor, out float top)
		{
			CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
			front = FrontCentre(plan, st);
			outward = Outward(plan.Facing);
			side = new Vector3(outward.z, 0f, -outward.x);
			shelterFloor = plan.Position.y;
			top = plan.Position.y + PerchTop(st) * plan.Scale;
			lip = new Vector3(front.x, top, front.z) + outward * (st.Jut * plan.Scale);
		}

		/// <summary>
		/// The views that show a perch for what it is, each (name, eye, target): the whole cliff from well out before it, its
		/// profile from the side (the tongue against the sky), under the tongue in the shelter, and standing on the tongue
		/// looking out over the drop. <paramref name="ground"/> keeps the eyes out of the hillside.
		/// </summary>
		public static List<(string Name, Vector3 Eye, Vector3 At)> PerchViews(in Plan plan, Func<float, float, float> ground)
		{
			PerchFrame(plan, out Vector3 front, out Vector3 lip, out Vector3 outward, out Vector3 side, out float floor, out float top);
			float height = top - front.y;
			Vector3 Above(Vector3 p, float lift) => new Vector3(p.x, Mathf.Max(p.y, ground(p.x, p.z) + lift), p.z);
			var views = new List<(string, Vector3, Vector3)>
			{
				("front", Above(front + outward * (2.6f * height + 30f) + Vector3.up * (0.35f * height), 2f), front + Vector3.up * (0.55f * height)),
				("profile", Above(front + side * (1.6f * height + 25f) + outward * (0.9f * height) + Vector3.up * (0.75f * height), 2f), lip - Vector3.up * (0.25f * height)),
				("shelter", new Vector3(front.x, floor + 1.7f, front.z) + outward * 6f - side * 3f, new Vector3(front.x, floor + 4.5f, front.z) - outward * 3f + side * 2f),
				("tongue", new Vector3(front.x, top + 1.7f, front.z) - outward * 4f + side * 1.5f, lip + outward * 25f - Vector3.up * (0.8f * height)),
			};
			return views;
		}

		/// <summary>The middle of a perch's face line at its floor, world.</summary>
		public static Vector3 FrontCentre(in Plan plan, CliffStyle st)
		{
			Vector3 outward = Outward(plan.Facing);
			Vector3 f = plan.Position + outward * (0.5f * st.Depth * plan.Scale);
			return new Vector3(f.x, plan.Floor, f.z);
		}

		/// <summary>
		/// The ground at (x, z) once a perch's face is cut, from what it was (<paramref name="height"/>): held down at the
		/// floor before the face, in the shelter and under the whole section, easing back to the hillside in front and along
		/// the wings. Only ever lowers.
		/// </summary>
		public static float CutHeight(in Plan plan, CliffStyle st, float x, float z, float height)
		{
			Vector3 outward = Outward(plan.Facing), side = new Vector3(outward.z, 0f, -outward.x);
			Vector3 front = FrontCentre(plan, st);
			float dx = x - front.x, dz = z - front.z;
			float a = dx * outward.x + dz * outward.z, c = Mathf.Abs(dx * side.x + dz * side.z);
			float halfL = 0.5f * st.Length * plan.Scale, depth = st.Depth * plan.Scale;
			float wingReach = 0.8f * CliffSections.StyleOf(WingStyle, 0).Length * plan.Scale;
			float wc = 1f - Mathf.Clamp01((c - (halfL - 1f)) / wingReach);
			float lip = st.Jut * plan.Scale + ApronBeyondLip;
			// Behind the face only under the section itself (the wings are shallower): beyond it the plateau is left be.
			float behind = c <= halfL ? depth - 1f : 0.6f * depth;
			float wa = a >= 0f ? 1f - Mathf.Clamp01((a - lip) / FrontEase) : a >= -behind ? 1f : 0f;
			float w = wa * wc;
			w = w * w * (3f - 2f * w);
			float cut = Mathf.Lerp(height, plan.Floor, w);
			return Mathf.Min(height, cut);
		}

		/// <summary>The disc the plan keeps everything else out of.</summary>
		public static Vector3 KeepOutOf(in Plan plan)
		{
			switch (plan.Build)
			{
				case OverhangBuild.Arch:
				{
					float length = CliffSections.StyleOf("Arch", plan.Variant).Length * plan.Scale;
					return new Vector3(plan.Position.x, 0.5f * length + 2f, plan.Position.z);
				}
				default:
				{
					CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
					Vector3 front = FrontCentre(plan, st);
					float wings = plan.HasWings ? 0.7f * CliffSections.StyleOf(WingStyle, 0).Length * plan.Scale : 0f;
					return new Vector3(front.x, 0.5f * st.Length * plan.Scale + wings + 2f, front.z);
				}
			}
		}

		/// <summary>Cuts a perch's face into the terrain (<see cref="CutHeight"/>). Returns the samples lowered.</summary>
		public static int CutFace(Terrain[,] terrains, TerrainTilePlan tiles, Plan plan)
		{
			CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
			Vector3 front = FrontCentre(plan, st);
			float reach = 0.5f * st.Length * plan.Scale + 0.8f * CliffSections.StyleOf(WingStyle, 0).Length * plan.Scale
				+ st.Depth * plan.Scale + st.Jut * plan.Scale + ApronBeyondLip + FrontEase;
			var area = new Rect(front.x - reach, front.z - reach, 2f * reach, 2f * reach);
			return PointOfInterestTerrain.EditHeights(terrains, tiles, area, (east, north, metres) => CutHeight(plan, st, east, north, metres));
		}

		/// <summary>
		/// The fallen blocks of a perch: big ones under the drip line, smaller ones on the shelter's floor, from its seed.
		/// Each is (style, variant, position on the ground, yaw, scale).
		/// </summary>
		public static List<(string Style, int Variant, Vector3 At, float Yaw, float Scale)> Debris(in Plan plan, int seed, Func<float, float, float> ground)
		{
			var list = new List<(string, int, Vector3, float, float)>();
			if (plan.Build != OverhangBuild.Perch)
			{
				return list;
			}
			CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
			Vector3 outward = Outward(plan.Facing), side = new Vector3(outward.z, 0f, -outward.x);
			Vector3 front = FrontCentre(plan, st);
			var random = new DeterministicRNG(seed ^ 0x2D5B);
			float jut = st.Jut * plan.Scale, shelter = 0.5f * st.TongueWidth * 1.35f * plan.Scale;
			int under = 3 + random.Next(3), inside = 2 + random.Next(3), along = 3 + random.Next(4);
			// On a plinth the shelter's floor is the plinth's top, the perch's own floor; what falls past the ledge lands at the foot.
			Plan p0 = plan;
			void Add(string style, float a, float c, float scale)
			{
				Vector3 p = front + outward * a + side * c;
				float y = p0.HasPlinth && a < 2f ? p0.Position.y : ground(p.x, p.z);
				list.Add((style, random.Next(CliffSections.VariantCount), new Vector3(p.x, y - 0.25f * scale, p.z), random.Range(0f, 360f), scale));
			}
			for (int i = 0; i < under; i++)
			{
				float a = random.Range(0.55f, 1.15f) * jut;
				Add("Rockfall", plan.HasPlinth ? a + 3f : a, random.Range(-0.8f, 0.8f) * shelter, random.Range(0.6f, 1.1f) * plan.Scale);
			}
			for (int i = 0; i < inside; i++)
			{
				Add("Rubble", -random.Range(0.25f, 0.7f) * st.Undercut * plan.Scale, random.Range(-0.7f, 0.7f) * shelter, random.Range(0.6f, 1.2f) * plan.Scale);
			}
			// The talus along the rest of the foot.
			float halfL = 0.5f * st.Length * plan.Scale;
			for (int i = 0; i < along; i++)
			{
				float c = random.Range(shelter + 1f, halfL + 4f) * (random.NextFloat() < 0.5f ? -1f : 1f);
				Add(random.NextFloat() < 0.35f ? "Rockfall" : "Rubble", random.Range(0.3f, 3f) + (plan.HasPlinth ? 3f : 0f), c, random.Range(0.5f, 1f) * plan.Scale);
			}
			return list;
		}

		// ── The seam ──────────────────────────────────────────────

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			if (!PlanSite(site.Kind, site.SizeClass, site.Position, site.Yaw, SeedOf(site), context.GroundAt, out Plan plan, out string problem))
			{
				context.Reject(site, problem);
				return;
			}
			if (plan.Build == OverhangBuild.Perch)
			{
				CutFace(context.Terrains, context.Tiles, plan);
				// The site's own point onto the shelter's floor, before the face, so its marker, way and features meet it.
				CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
				Vector3 front = FrontCentre(plan, st);
				site.Position = new Vector3(front.x, context.GroundAt(front.x, front.z), front.z) + Outward(plan.Facing) * 1.5f;
			}
			Vector3 keep = KeepOutOf(plan);
			context.AddKeepOut(site, keep.x, keep.z, Mathf.Max(keep.y, site.Radius * 0.6f));
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.Position,
				Yaw = plan.Yaw,
				Size = Vector3.one * plan.Scale,
				Seed = SeedOf(site),
				Values = plan.ToValues(),
			});
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			string where = $"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0})";
			PointOfInterestShape shape = context.ShapesOf(site).Find(s => s.Shaper == ShaperName);
			if (shape == null || !Plan.TryFrom(shape.Values, out Plan plan))
			{
				context.Notes?.Add($"{where}: nothing current is stored for it; re-cut the scene.");
				return;
			}
			string rock = PointOfInterestRock.At(context.Request, site, plan.Position);
			string style = StyleOf(plan.Build);
			if (style == null)
			{
				context.Notes?.Add($"{where}: its stored build is retired; re-cut the scene.");
				return;
			}
			if (!PlaceSection(context, style, plan.Variant, plan.Position, plan.Yaw, plan.Scale, rock, false))
			{
				context.Notes?.Add($"{where}: its rock art is missing ({style} in {rock}); run Generate Biome Art, then repaint.");
				return;
			}
			if (plan.Build != OverhangBuild.Perch)
			{
				return;
			}
			if (plan.HasPlinth)
			{
				PlaceSection(context, PlinthStyles[Mathf.Clamp(plan.Plinth.Style, 0, PlinthStyles.Length - 1)], plan.Plinth.Variant, plan.Plinth.Position, plan.Yaw, plan.Plinth.Scale, rock, false);
			}
			if (plan.HasWings)
			{
				PlaceSection(context, WingStyle, plan.Left.Variant, plan.Left.Position, plan.Yaw, plan.Left.Scale, rock, false);
				PlaceSection(context, WingStyle, plan.Right.Variant, plan.Right.Position, plan.Yaw, plan.Right.Scale, rock, false);
			}
			foreach (var block in Debris(plan, shape.Seed, context.GroundAt))
			{
				PlaceSection(context, block.Style, block.Variant, block.At, block.Yaw, block.Scale, rock, true);
			}
		}

		/// <summary>Lays a section as a prop: its shared prefab in the site's stone (made once, as the cliffs' are).</summary>
		private static bool PlaceSection(PointOfInterestPlaceContext context, string style, int variant, Vector3 position, float yaw, float scale, string rock, bool talus)
		{
			int levels = CliffSections.LevelCount;
			var meshes = new Mesh[levels];
			for (int lod = 0; lod < levels; lod++)
			{
				meshes[lod] = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralArtCatalogue.MeshPath(CliffSections.MeshName(style, variant, lod)));
				if (meshes[lod] == null)
				{
					return false;
				}
			}
			Material material = PointOfInterestRock.MaterialOf(rock);
			if (material == null)
			{
				return false;
			}
			GameObject prefab = CliffPlacer.RockPrefab($"Crag Section_{style}_{variant} {material.name}", meshes, meshes[Mathf.Min(CliffRocks.CollisionLod, levels - 1)],
				material, CliffPlacer.ColliderLayer, CliffRocks.LodHeights, talus);
			if (prefab == null)
			{
				return false;
			}
			context.AddProp(prefab, position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
			return true;
		}
	}
}
#endif
