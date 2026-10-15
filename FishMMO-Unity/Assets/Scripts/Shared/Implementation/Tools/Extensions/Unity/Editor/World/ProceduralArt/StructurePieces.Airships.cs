#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Airships and their landing stages, for the cloud worlds (2026-10-14: "small floating islands, flying ships"): a
	/// skiff and a sky-cog, timber hulls hung under stitched-hide gasbags in rope netting, steered by sail-fins; and the
	/// timber stage they tie up at, cantilevered out from an island's edge over the void.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>An airship's frame.</b> y = 0 is its main deck, the surface a rider walks on (<see cref="StructureAnchor.Deck"/>):
	/// its hull hangs below it, its gasbag above. Bow along +x. The deck is flush with the hull's rim, fenced by a rail
	/// (<see cref="AirshipRail"/> high, solid at every level, so the collider keeps riders aboard) that is open only at the
	/// bow, where a boarding plank runs <see cref="AirshipBowPlank"/> past the stem at deck level: the ship berths bow-on
	/// to a stage, the plank's tip a hand's breadth off the stage's end, and its passengers walk straight across
	/// (<see cref="TryAirship"/> gives the plank's reach; PointOfInterestSkyTraffic puts the ship there).
	/// </para>
	/// <para>
	/// <b>Variants.</b> v0 under way: the sail-fins spread like wings either side and a pennant streaming from the gasbag's
	/// yard. v1 moored: the fins folded up against the rigging, their cloth furled. The hull, deck, rails, masts and gasbag
	/// are the same in both, so the collider (the coarsest level of v0) is the moored ship's too.
	/// </para>
	/// <para>
	/// <b>Its swing.</b> At a stage it turns end for end about its origin before it leaves: everything it carries stays
	/// within the plank's reach of the origin (the stern fin, the fins' tips), so that turn never sweeps the stage.
	/// </para>
	/// <para>
	/// <b>A landing stage</b> runs along +z, its deck at y = 0: <see cref="SkyDockLength"/> long, the inner end (−z) over
	/// the island's ground and the outer end (+z) over the void, held out by two heavy timbers that run back into the island
	/// and by diagonal struts braced down into the rock under its edge, with a mast whose stays reach the outer corners.
	/// Its end is open between two posts, for the plank.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>How far an airship's boarding plank runs past its hull's stem, metres.</summary>
		public const float AirshipBowPlank = 1.8f;

		/// <summary>The boarding plank's width, metres: two abreast, between its rails.</summary>
		public const float AirshipPlankWidth = 1.6f;

		/// <summary>An airship's rail, metres over its deck.</summary>
		public const float AirshipRail = 1.05f;

		/// <summary>A landing stage's length along z, metres: its inner end overlaps the island's ground.</summary>
		public const float SkyDockLength = 14f;

		/// <summary>A landing stage's deck width, metres.</summary>
		public const float SkyDockWidth = 4f;

		private static void AddAirships(List<StructurePiece> list)
		{
			StructurePiece skiff = Piece(list, "SkySkiff", StructureStyle.Timber, StructureSize.Building,
				"A sky-skiff, 14 m: a clinker hull hung under one stitched-hide gasbag in rope netting, a mast to its yard, sail-fins either side and a rudder-fin aft, a boarding plank at the bow; v0 under way (fins spread), v1 moored (fins folded). Deck at y = 0.",
				SkySkiff, "airship", "sky");
			skiff.Anchor = StructureAnchor.Deck;
			StructurePiece cog = Piece(list, "SkyCog", StructureStyle.Timber, StructureSize.Building,
				"A sky-cog, 26 m: a deep trading hull with an aftcastle under a long gasbag on two masts, rope netting and rigging, sail-fins and a rudder-fin, a carved beast under its boarding plank; v0 under way, v1 moored. Deck at y = 0.",
				SkyCog, "airship", "sky");
			cog.Anchor = StructureAnchor.Deck;
			StructurePiece dock = Piece(list, "SkyDock", StructureStyle.Timber, StructureSize.Building,
				"A landing stage, 14 × 4 m along +z, deck at y = 0: cantilevered from an island's edge (−z over the ground) on two heavy timbers and diagonal struts into the rock, rope rails, a mast stayed to its outer corners, its end open for a ship's plank; v0 lantern posts at the end, v1 a gate arch with a hanging lantern and a capstan.",
				SkyDock, "skydock", "sky");
			dock.Anchor = StructureAnchor.Deck;
		}

		/// <summary>An airship's lines: its hull (gunwale at y = 0, no sheer, so the deck runs flush to the bow) and its gasbag.</summary>
		private struct AirshipLines
		{
			public HullLines Hull;
			/// <summary>The gasbag's length, its radius, and its axis's height over the deck, metres.</summary>
			public float BagLength, BagRadius, BagLift;
			/// <summary>How far each sail-fin reaches out from the hull's side, and where along the hull it is set (x).</summary>
			public float FinSpan, FinAt;
			/// <summary>A raised aftcastle over the stern (the cog's), else a tiller at an open stern.</summary>
			public bool Castle;
			public int Masts;
		}

		private static readonly AirshipLines SkySkiffLines = new AirshipLines
		{
			Hull = new HullLines { Length = 14f, Beam = 4.2f, Depth = 1.7f, Draft = 1.7f, Sheer = 0f, Fullness = 2.4f, Round = 2.3f, Planking = 0.07f, Stations = 12, Side = 4 },
			BagLength = 12.6f, BagRadius = 2.5f, BagLift = 5.3f, FinSpan = 3.2f, FinAt = 0.8f, Castle = false, Masts = 1,
		};

		private static readonly AirshipLines SkyCogLines = new AirshipLines
		{
			Hull = new HullLines { Length = 26f, Beam = 7f, Depth = 2.8f, Draft = 2.8f, Sheer = 0f, Fullness = 3f, Round = 2.8f, Planking = 0.1f, Stations = 16, Side = 5 },
			BagLength = 23.5f, BagRadius = 4.2f, BagLift = 8f, FinSpan = 4.6f, FinAt = 2.5f, Castle = true, Masts = 2,
		};

		/// <summary>
		/// An airship, for whoever sails it (PointOfInterestSkyTraffic): its hull's length and beam, and how far its boarding
		/// plank's tip reaches forward of its origin, metres (where a stage's end must be for it to berth). False for a piece
		/// that is not one.
		/// </summary>
		public static bool TryAirship(string pieceId, out float length, out float beam, out float bowReach)
		{
			AirshipLines a;
			switch (pieceId)
			{
				case "SkySkiff": a = SkySkiffLines; break;
				case "SkyCog": a = SkyCogLines; break;
				default:
					length = beam = bowReach = 0f;
					return false;
			}
			length = a.Hull.Length;
			beam = a.Hull.Beam;
			bowReach = a.Hull.Length * 0.5f + AirshipBowPlank;
			return true;
		}

		private static void SkySkiff(StructureDraft d) => Airship(d, SkySkiffLines);

		private static void SkyCog(StructureDraft d) => Airship(d, SkyCogLines);

		/// <summary>The station along a hull at <paramref name="x"/> metres from midships (0 the stern … 1 the bow).</summary>
		private static float StationAt(HullLines h, float x) => Mathf.Clamp(0.5f + x / h.Length, 0.015f, 0.985f);

		/// <summary>
		/// The deck's plan (or a castle's) at this level: the inner skin at the gunwale from <paramref name="from"/> to
		/// <paramref name="to"/> down the port side and back up the starboard, as (x, z), at the stations the hull is lofted
		/// through at this level, so its edge meets the planking exactly.
		/// </summary>
		private static List<Vector2> DeckPlan(StructureDraft d, HullLines h, float from, float to)
		{
			int stations = Mathf.Max(4, d.Fine ? h.Stations : d.Mid ? h.Stations * 2 / 3 : h.Stations / 2);
			int n = Mathf.Max(2, Mathf.CeilToInt(stations * (to - from)) + 1);
			var plan = new List<Vector2>(2 * n);
			for (int i = 0; i < n; i++)
			{
				Vector3 p = HullPoint(h, Mathf.Clamp(Mathf.Lerp(from, to, i / (float)(n - 1)), 0.015f, 0.985f), -1f, h.Planking);
				plan.Add(new Vector2(p.x, p.z));
			}
			for (int i = n - 1; i >= 0; i--)
			{
				Vector3 p = HullPoint(h, Mathf.Clamp(Mathf.Lerp(from, to, i / (float)(n - 1)), 0.015f, 0.985f), 1f, h.Planking);
				plan.Add(new Vector2(p.x, p.z));
			}
			return plan;
		}

		/// <summary>A horizontal slab of a plan (x, z) from <paramref name="y0"/> up to <paramref name="y1"/>.</summary>
		private static StructureSolid Slab(List<Vector2> plan, float y0, float y1, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			// The prism's local y is the plan's z and its local z the height: the swap is a reflection, which Transform turns right side out.
			Matrix4x4 swap = Matrix4x4.identity;
			swap.m11 = 0f;
			swap.m22 = 0f;
			swap.m12 = 1f;
			swap.m21 = 1f;
			StructureSolid slab = StructureShapes.Prism(plan, y0, y1, material, key, role).Transform(swap);
			slab.Grain = Vector3.right;
			return slab;
		}

		private static void Airship(StructureDraft d, AirshipLines a)
		{
			HullLines h = a.Hull;
			bool underWay = d.Variant == 0;
			float halfLength = h.Length * 0.5f;

			// The hull, its rim at the deck all round, and the deck across it.
			d.Add(Hull(d, h, 1));
			d.Add(Slab(DeckPlan(d, h, 0f, 1f), -0.12f, 0f, StructureMaterial.Timber, 2));

			// The boarding plank out past the stem on a bowsprit timber, and the carved beast under its tip.
			float crossX = halfLength - 2.5f, tipX = halfLength + AirshipBowPlank;
			float plankHalf = AirshipPlankWidth * 0.5f;
			d.Add(StructureShapes.Box(new Vector3((crossX + tipX) * 0.5f, -0.06f, 0f), new Vector3(tipX - crossX, 0.12f, AirshipPlankWidth), StructureMaterial.Timber, 3)).Grain = Vector3.right;
			d.Add(StructureShapes.Beam(new Vector3(halfLength - 1.6f, -0.32f, 0f), new Vector3(tipX - 0.05f, -0.24f, 0f), 0.22f, 0.22f, StructureMaterial.Timber, 4));
			d.Add(StructureShapes.Beam(HullPoint(h, 0.94f, 0f, 0.05f), new Vector3(halfLength + 0.5f, -0.36f, 0f), 0.2f, 0.26f, StructureMaterial.Timber, 5));
			if (d.Mid)
			{
				d.Add(StructureShapes.Block(new Vector3(halfLength + 1.05f, -0.52f, 0f), new Vector3(0.95f, 0.36f, 0.36f), Quaternion.Euler(0f, 0f, -8f), StructureMaterial.Timber, 6, StructureRole.Detail, d.Bevel(0.04f)));
				d.Add(StructureShapes.Block(new Vector3(halfLength + 1.3f, -0.74f, 0f), new Vector3(0.6f, 0.12f, 0.26f), Quaternion.Euler(0f, 0f, 10f), StructureMaterial.Timber, 7, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					// Horns swept back under the plank.
					d.Add(StructureShapes.Beam(new Vector3(halfLength + 0.75f, -0.42f, s * 0.12f), new Vector3(halfLength + 0.3f, -0.24f, s * 0.3f), 0.06f, 0.06f, StructureMaterial.Bone, 8 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The stern: a raised aftcastle (a cabin following the hull) or a tiller at an open stern; and the rudder-fin.
			float castleX = -halfLength + 4.6f;
			float railFrom = a.Castle ? StationAt(h, castleX) + 0.01f : 0.05f;
			if (a.Castle)
			{
				d.Add(Slab(DeckPlan(d, h, 0.015f, StationAt(h, castleX)), 0f, 2.2f, StructureMaterial.Timber, 10));
				if (d.Mid)
				{
					float wall = HalfBreadth(h, StationAt(h, castleX)) - h.Planking;
					d.Add(StructureShapes.Box(new Vector3(castleX + 0.03f, 0.98f, 0f), new Vector3(0.08f, 1.95f, 1.05f), StructureMaterial.Timber, 11, StructureRole.Detail));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(castleX + 0.03f, 1.45f, s * wall * 0.55f), new Vector3(0.06f, 0.5f, 0.42f), StructureMaterial.Glass, 12 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
					Lantern(d, new Vector3(-halfLength + 1.6f, 2.2f, 0f), true, 15);
				}
			}
			else if (d.Mid)
			{
				float tillerX = -halfLength + 1.4f;
				d.Add(StructureShapes.Beam(new Vector3(tillerX, -0.05f, 0f), new Vector3(tillerX, 0.95f, 0f), 0.14f, 0.14f, StructureMaterial.Timber, 16, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(tillerX - 0.1f, 0.92f, 0f), new Vector3(tillerX + 1.3f, 0.98f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 17, StructureRole.Detail));
			}
			d.Add(StructureShapes.Beam(HullPoint(h, 0.03f, 0f, 0.05f), new Vector3(-halfLength - 0.2f, 2.6f, 0f), 0.2f, 0.22f, StructureMaterial.Timber, 18));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(-halfLength - 0.8f, 0.95f, 0f), new Vector3(1.15f, 3f, 0.05f), StructureMaterial.Dyed, 19, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-halfLength - 1.38f, -0.6f, 0f), new Vector3(-halfLength - 1.38f, 2.5f, 0f), 0.08f, 0.08f, StructureMaterial.Timber, 20, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-halfLength - 0.25f, 2.45f, 0f), new Vector3(-halfLength - 1.4f, 2.45f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 21, StructureRole.Detail));
			}

			Rails(d, h, railFrom, StationAt(h, crossX), crossX, tipX, plankHalf, !a.Castle, 30);
			Gasbag(d, a, underWay, 200);
			for (int s = -1; s <= 1; s += 2)
			{
				Fin(d, a, s, underWay, 300 + (s > 0 ? 0 : 20));
			}
		}

		/// <summary>
		/// The rail round the deck: posts on the gunwale from <paramref name="from"/> to the bow cross at
		/// <paramref name="to"/>, a top rail between them, a rail across from each side in to the plank, and along the plank
		/// to its tip, which stays open. Posts and top rails at every level (the collider keeps riders aboard by them); the
		/// rope at mid-height at the finer levels.
		/// </summary>
		private static void Rails(StructureDraft d, HullLines h, float from, float to, float crossX, float tipX, float plankHalf, bool sternRail, int key)
		{
			float span = (to - from) * h.Length;
			int posts = Mathf.Max(3, Mathf.RoundToInt(span / (d.Fine ? 1.9f : d.Mid ? 2.6f : 3.5f)) + 1);
			Vector3 up = Vector3.up * AirshipRail;
			for (int s = -1; s <= 1; s += 2)
			{
				int side = key + (s > 0 ? 0 : 100);
				Vector3 previous = Vector3.zero;
				for (int i = 0; i < posts; i++)
				{
					float t = Mathf.Lerp(from, to, i / (float)(posts - 1));
					Vector3 p = HullPoint(h, t, s, h.Planking + 0.07f);
					d.Add(StructureShapes.Beam(p + Vector3.down * 0.05f, p + up + Vector3.up * 0.04f, 0.12f, 0.12f, StructureMaterial.Timber, side + i));
					if (i > 0)
					{
						d.Add(StructureShapes.Beam(previous + up, p + up, 0.1f, 0.08f, StructureMaterial.Timber, side + 40 + i)).Grain = (p - previous).normalized;
						if (d.Mid)
						{
							Vector3 mid = Vector3.up * (AirshipRail * 0.5f);
							d.Add(StructureShapes.Beam(previous + mid, p + mid, 0.04f, 0.04f, StructureMaterial.Canvas, side + 70 + i, StructureRole.Detail));
						}
					}
					previous = p;
				}
				// In from the side to the plank, and along the plank to just short of its tip.
				Vector3 plankPost = new Vector3(crossX, 0f, s * (plankHalf + 0.06f));
				Vector3 tipPost = new Vector3(tipX - 0.15f, 0f, s * (plankHalf + 0.06f));
				d.Add(StructureShapes.Beam(previous + up, plankPost + up, 0.1f, 0.08f, StructureMaterial.Timber, side + 90));
				d.Add(StructureShapes.Beam(plankPost + Vector3.down * 0.05f, plankPost + up + Vector3.up * 0.04f, 0.12f, 0.12f, StructureMaterial.Timber, side + 91));
				d.Add(StructureShapes.Beam(plankPost + up, tipPost + up, 0.1f, 0.08f, StructureMaterial.Timber, side + 92));
				d.Add(StructureShapes.Beam(tipPost + Vector3.down * 0.1f, tipPost + up + Vector3.up * 0.1f, 0.14f, 0.14f, StructureMaterial.Timber, side + 93));
				if (s < 0 && d.Mid)
				{
					// A lantern on the plank's port post: a light to come in by.
					Lantern(d, tipPost + up + Vector3.up * 0.1f, false, side + 94);
				}
			}
			if (sternRail)
			{
				Vector3 port = HullPoint(h, from, -1f, h.Planking + 0.07f), starboard = HullPoint(h, from, 1f, h.Planking + 0.07f);
				d.Add(StructureShapes.Beam(port + up, starboard + up, 0.1f, 0.08f, StructureMaterial.Timber, key + 200));
			}
		}

		/// <summary>
		/// The gasbag: a long stitched-hide envelope on a timber yard, the masts up to the yard from the deck, rope netting
		/// round it (hoops and ridge lines), the rigging from the net down to the rails, and under way a pennant off the yard.
		/// </summary>
		private static void Gasbag(StructureDraft d, AirshipLines a, bool underWay, int key)
		{
			HullLines h = a.Hull;
			float half = a.BagLength * 0.5f, r = a.BagRadius, lift = a.BagLift, bottom = lift - r;
			int seg = d.Seg(16, 8), rings = d.Fine ? 10 : d.Mid ? 8 : 6;
			Matrix4x4 along = Matrix4x4.TRS(new Vector3(0f, lift, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
			d.Add(StructureShapes.Sphere(Vector3.zero, new Vector3(r, half, r), seg, rings, StructureMaterial.Dyed, key).Transform(along));
			float yard = a.BagLength * 0.42f;
			d.Add(StructureShapes.Beam(new Vector3(-yard, bottom - 0.12f, 0f), new Vector3(yard, bottom - 0.12f, 0f), 0.24f, 0.24f, StructureMaterial.Timber, key + 1));
			for (int m = 0; m < a.Masts; m++)
			{
				float x = a.Masts == 1 ? 0.5f : (m == 0 ? -0.22f : 0.22f) * h.Length;
				d.Add(StructureShapes.Cylinder(0.15f, -0.3f, bottom - 0.12f, d.Seg(10, 6), StructureMaterial.Timber, key + 2 + m, true).Transform(Matrix4x4.Translate(new Vector3(x, 0f, 0f))));
			}
			if (!d.Mid)
			{
				return;
			}
			float Radius(float x) => r * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (half * half)));
			Vector3 OnBag(float x, float angle, float lift2)
			{
				float rr = Radius(x) + lift2;
				return new Vector3(x, lift + rr * Mathf.Sin(angle * Mathf.Deg2Rad), rr * Mathf.Cos(angle * Mathf.Deg2Rad));
			}
			// The netting's hoops round the bag.
			int hoops = d.Fine ? 4 : 3;
			for (int k = 0; k < hoops; k++)
			{
				float x = half * (-0.62f + 1.24f * k / (hoops - 1));
				d.Add(StructureShapes.Cylinder(Radius(x) + 0.05f, -0.05f, 0.05f, seg, StructureMaterial.Canvas, key + 10 + k, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(x, lift, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
			}
			// Ridge lines along it: over the top, and either side at the finest level.
			float[] ridges = d.Fine ? new[] { 90f, 35f, 145f } : new[] { 90f };
			int runs = d.Fine ? 6 : 5;
			for (int g = 0; g < ridges.Length; g++)
			{
				for (int k = 0; k < runs; k++)
				{
					float x0 = half * (-0.92f + 1.84f * k / runs), x1 = half * (-0.92f + 1.84f * (k + 1) / runs);
					d.Add(StructureShapes.Beam(OnBag(x0, ridges[g], 0.04f), OnBag(x1, ridges[g], 0.04f), 0.05f, 0.05f, StructureMaterial.Canvas, key + 20 + g * 10 + k, StructureRole.Detail));
				}
			}
			// The rigging from the net's lower edge down to the rails, fanned along the hull.
			int lines = d.Fine ? 5 : 3;
			for (int k = 0; k < lines; k++)
			{
				float x = half * (-0.6f + 1.2f * k / (lines - 1));
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 net = OnBag(x, -25f, 0.04f);
					net.z *= s;
					Vector3 rail = HullPoint(h, StationAt(h, x), s, h.Planking + 0.07f) + Vector3.up * AirshipRail;
					d.Add(StructureShapes.Beam(net, rail, 0.045f, 0.045f, StructureMaterial.Canvas, key + 60 + k * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (underWay)
			{
				// A pennant streaming aft off the yard's end.
				d.Add(StructureShapes.Block(new Vector3(-yard - 1.5f, bottom - 0.45f, 0f), new Vector3(3f, 0.45f, 0.03f), Quaternion.Euler(0f, 0f, 6f), StructureMaterial.Dyed, key + 80, StructureRole.Detail));
			}
		}

		/// <summary>
		/// One sail-fin on side <paramref name="side"/> (+1 starboard, −1 port): a batten-sail like a dragon's wing on a spar
		/// and a boom from the hull's side, spread and raised a little under way, folded up against the rigging with its cloth
		/// furled along the spar when moored.
		/// </summary>
		private static void Fin(StructureDraft d, AirshipLines a, int side, bool underWay, int key)
		{
			if (!d.Mid)
			{
				return;
			}
			HullLines h = a.Hull;
			float span = a.FinSpan;
			var root = new Vector3(a.FinAt, 0.55f, HalfBreadth(h, StationAt(h, a.FinAt)) + 0.02f);
			Matrix4x4 place = Matrix4x4.Translate(root) * Matrix4x4.Rotate(Quaternion.Euler(underWay ? -24f : -78f, 0f, 0f));
			if (side < 0)
			{
				place = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * place;
			}
			int first = d.Solids.Count;
			d.Add(StructureShapes.Beam(new Vector3(0.7f, 0.08f, -0.1f), new Vector3(0.4f, 0.04f, span), 0.1f, 0.1f, StructureMaterial.Timber, key));
			d.Add(StructureShapes.Beam(new Vector3(-1.7f, -0.05f, -0.1f), new Vector3(-0.9f, 0f, span * 0.85f), 0.08f, 0.08f, StructureMaterial.Timber, key + 1));
			if (underWay)
			{
				// The wing's cloth: broad at the root, swept to a narrow tip, between the spar and the boom.
				var wing = new List<Vector2> { new Vector2(0.62f, -0.05f), new Vector2(0.42f, span * 0.97f), new Vector2(-0.8f, span * 0.84f), new Vector2(-1.65f, -0.05f) };
				d.Add(Slab(wing, -0.015f, 0.02f, StructureMaterial.Dyed, key + 2, StructureRole.Detail));
				if (d.Fine)
				{
					for (int b = 1; b <= 2; b++)
					{
						float z = span * (b == 1 ? 0.38f : 0.72f);
						d.Add(StructureShapes.Beam(new Vector3(0.58f, 0.05f, z), new Vector3(-1.25f, 0.03f, z * 0.95f), 0.05f, 0.05f, StructureMaterial.Timber, key + 3 + b, StructureRole.Detail));
					}
				}
			}
			else
			{
				// The cloth furled along the spar.
				d.Add(StructureShapes.Rod(new Vector3(0.45f, -0.12f, 0.2f), new Vector3(0.3f, -0.1f, span * 0.9f), 0.16f, d.Seg(8, 5), StructureMaterial.Canvas, key + 2, StructureRole.Detail));
			}
			TransformSince(d, first, place);
		}

		// ── The landing stage ─────────────────────────────────────

		private static void SkyDock(StructureDraft d)
		{
			const float W = SkyDockWidth, half = SkyDockLength * 0.5f;
			bool gate = d.Variant == 1;

			// The deck on its stringers, and under them the two heavy timbers that carry it out from the island: they run
			// 2.5 m back past its inner end, under the ground.
			Deck(d, W, -half, half, 1);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 1.2f, -0.52f, -half - 2.5f), new Vector3(s * 1.2f, -0.52f, half - 0.1f), 0.3f, 0.4f, StructureMaterial.Timber, 200 + s)).Grain = Vector3.forward;
			}
			// Needle beams across under the timbers, where the struts meet them.
			float[] needles = { -1f, 3.5f, half - 0.4f };
			for (int i = 0; i < needles.Length; i++)
			{
				d.Add(StructureShapes.Beam(new Vector3(-W * 0.5f - 0.1f, -0.84f, needles[i]), new Vector3(W * 0.5f + 0.1f, -0.84f, needles[i]), 0.24f, 0.24f, StructureMaterial.Timber, 210 + i));
			}
			// The struts: from the needle beams down and back to iron-shod pads braced into the rock under the edge.
			for (int s = -1; s <= 1; s += 2)
			{
				var foot = new Vector3(s * 1.5f, -5.2f, -half - 1.2f);
				d.Add(StructureShapes.Beam(new Vector3(s * 1.5f, -0.9f, -1f), foot, 0.26f, 0.26f, StructureMaterial.Timber, 220 + s));
				d.Add(StructureShapes.Beam(new Vector3(s * 1.5f, -0.9f, 3.5f), foot, 0.26f, 0.26f, StructureMaterial.Timber, 223 + s));
				d.Add(StructureShapes.Box(foot + new Vector3(0f, -0.05f, -0.15f), new Vector3(0.7f, 0.35f, 0.7f), StructureMaterial.Timber, 226 + s));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(foot + new Vector3(0f, -0.05f, -0.15f), new Vector3(0.74f, 0.08f, 0.74f), StructureMaterial.Iron, 229 + s, StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				// A brace across between the struts, under the outer needle.
				d.Add(StructureShapes.Beam(new Vector3(-1.5f, -2.6f, -2.2f), new Vector3(1.5f, -1.2f, 1.6f), 0.14f, 0.14f, StructureMaterial.Timber, 232, StructureRole.Detail));
			}

			// Rope rails along both sides on posts; the end stays open.
			int posts = d.Fine ? 7 : d.Mid ? 5 : 4;
			float z0 = -half + 0.6f, z1 = half - 0.45f;
			for (int s = -1; s <= 1; s += 2)
			{
				int side = 300 + (s > 0 ? 0 : 50);
				float x = s * (W * 0.5f + 0.02f);
				for (int i = 0; i < posts; i++)
				{
					float z = Mathf.Lerp(z0, z1, i / (float)(posts - 1));
					d.Add(StructureShapes.Beam(new Vector3(x, -0.45f, z), new Vector3(x, 1.12f, z), 0.14f, 0.14f, StructureMaterial.Timber, side + i));
					if (i > 0)
					{
						float zp = Mathf.Lerp(z0, z1, (i - 1) / (float)(posts - 1));
						float rope = d.Lod == 2 ? 0.12f : 0.06f;
						if (d.Fine)
						{
							// Hung between the posts in a shallow sag.
							float zm = (zp + z) * 0.5f;
							d.Add(StructureShapes.Beam(new Vector3(x, 1.02f, zp), new Vector3(x, 0.94f, zm), rope, rope, StructureMaterial.Canvas, side + 10 + 2 * i));
							d.Add(StructureShapes.Beam(new Vector3(x, 0.94f, zm), new Vector3(x, 1.02f, z), rope, rope, StructureMaterial.Canvas, side + 11 + 2 * i));
						}
						else
						{
							d.Add(StructureShapes.Beam(new Vector3(x, 0.99f, zp), new Vector3(x, 0.99f, z), rope, rope, StructureMaterial.Canvas, side + 10 + 2 * i));
						}
						if (d.Mid)
						{
							d.Add(StructureShapes.Beam(new Vector3(x, 0.5f, zp), new Vector3(x, 0.5f, z), 0.045f, 0.045f, StructureMaterial.Canvas, side + 30 + i, StructureRole.Detail));
						}
					}
				}
			}

			// The end's two posts: lantern posts, or the uprights of a gate arch.
			float endZ = half - 0.2f, postTop = gate ? 3.7f : 2.3f;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (W * 0.5f + 0.02f);
				d.Add(StructureShapes.Beam(new Vector3(x, -0.6f, endZ), new Vector3(x, postTop, endZ), 0.24f, 0.24f, StructureMaterial.Timber, 400 + s));
				if (!gate && d.Mid)
				{
					Lantern(d, new Vector3(x, postTop, endZ), false, 410 + (s > 0 ? 0 : 20));
				}
			}
			if (gate)
			{
				d.Add(StructureShapes.Beam(new Vector3(-W * 0.5f - 0.5f, postTop - 0.15f, endZ), new Vector3(W * 0.5f + 0.5f, postTop - 0.15f, endZ), 0.26f, 0.3f, StructureMaterial.Timber, 440)).Grain = Vector3.right;
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						// Carved ends on the crossbeam, and knee braces under it.
						d.Add(StructureShapes.Block(new Vector3(s * (W * 0.5f + 0.62f), postTop - 0.05f, endZ), new Vector3(0.26f, 0.5f, 0.32f), Quaternion.Euler(0f, 0f, s * -18f), StructureMaterial.Timber, 441 + (s > 0 ? 1 : 0), StructureRole.Detail, d.Bevel(0.04f)));
						d.Add(StructureShapes.Beam(new Vector3(s * (W * 0.5f + 0.02f), postTop - 1.0f, endZ), new Vector3(s * (W * 0.5f - 0.7f), postTop - 0.3f, endZ), 0.12f, 0.12f, StructureMaterial.Timber, 443 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
					// The hanging lantern over the way aboard, high over a passing head.
					float foot = 2.45f;
					Chain(d, new Vector3(0f, postTop - 0.3f, endZ), new Vector3(0f, foot + 0.38f, endZ), 450);
					Lantern(d, new Vector3(0f, foot, endZ), true, 500);
					// A capstan at the inner end, for warping a ship in.
					var capstan = new Vector3(W * 0.5f - 0.75f, 0f, -half + 1.8f);
					d.Add(StructureShapes.Cylinder(0.32f, 0f, 0.85f, d.Seg(10, 6), StructureMaterial.Timber, 520, true).Transform(Matrix4x4.Translate(capstan)));
					d.Add(StructureShapes.Beam(capstan + new Vector3(-0.75f, 0.7f, 0f), capstan + new Vector3(0.75f, 0.7f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 521, StructureRole.Detail));
					d.Add(StructureShapes.Beam(capstan + new Vector3(0f, 0.75f, -0.75f), capstan + new Vector3(0f, 0.75f, 0.75f), 0.07f, 0.07f, StructureMaterial.Timber, 522, StructureRole.Detail));
				}
			}

			// The mast at the inner end, stayed out to the end's posts and back to a stake in the ground.
			var mast = new Vector3(-(W * 0.5f - 0.45f), 0f, -half + 1.2f);
			const float MastTop = 7.5f;
			d.Add(StructureShapes.Cylinder(0.17f, -0.45f, MastTop, d.Seg(10, 6), StructureMaterial.Timber, 600, true).Transform(Matrix4x4.Translate(mast)));
			if (d.Mid)
			{
				Vector3 top = mast + Vector3.up * (MastTop - 0.2f);
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(top, new Vector3(s * (W * 0.5f + 0.02f), postTop - 0.1f, endZ), 0.05f, 0.05f, StructureMaterial.Canvas, 601 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
				d.Add(StructureShapes.Beam(top, new Vector3(mast.x, -0.1f, -half - 2.3f), 0.05f, 0.05f, StructureMaterial.Canvas, 603, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(mast.x, -0.7f, -half - 2.3f), new Vector3(mast.x, 0.3f, -half - 2.3f), 0.16f, 0.16f, StructureMaterial.Timber, 604, StructureRole.Detail));
				// A pennant at the masthead: the wind's way, for a ship coming in.
				d.Add(StructureShapes.Block(mast + new Vector3(0f, MastTop - 0.35f, 0.9f), new Vector3(0.03f, 0.4f, 1.8f), Quaternion.Euler(4f, 0f, 0f), StructureMaterial.Dyed, 605, StructureRole.Detail));
			}
		}
	}
}
#endif
