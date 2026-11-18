#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A settlement church's yard closed in (2026-10-19: "a church with a graveyard and mausoleum or two"): the low wall round its
	/// graves, the roofed lychgate where the yard meets the street, and a mausoleum or two in its back corners. Laid by PropsFeature
	/// round each church (and cathedral) it lays, after the rows of graves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The yard.</b> In the church's own frame (x to its right, its front toward the street): beside the church on both sides,
	/// wider on the side its sunny graves stand, and behind it past its rows of graves; the mausoleums in a band behind the rows, in the
	/// back corners, their fronts toward the church. The wall's front runs from each of the church's flanks out to the yard's corners a
	/// little behind its front (so the church's door opens straight onto the street, nothing before it), and the lychgate stands in the
	/// sunny side's front run, its passage across the wall, its front to the street.
	/// </para>
	/// <para>
	/// <b>Walled</b> in the kit's drystone (LinearDrystone, at about a metre: <see cref="WallScale"/>), in the site's palette, its
	/// modules joined as a field's fence's are (PointOfInterestLinearFeatures.Joins); in the kit's railing on its low wall (IronFence's
	/// second variant) where the drystone has no art. A module is left out where a house, a street, the water or anything laid before
	/// stands, so the yard's wall breaks against its neighbours as a real one runs up to their walls.
	/// </para>
	/// <para>
	/// <b>Which yards.</b> A village's, a town's, a port's, a city's and the capital's: one or two mausoleums in a town's or a city's,
	/// one in some villages' (<see cref="MausoleumsFor"/>), never more than two in all a settlement's yards (2026-10-20,
	/// PointOfInterestStructureCaps). A castle's chapel keeps only its graves: its ward has no room for a yard.
	/// </para>
	/// <para>
	/// <b>Its own draws.</b> Every choice is a hash of the site's seed, this file's salt and the church's place, never the site's
	/// sequence: the clutter laid after it draws as it did, and only drops a piece where the yard now stands.
	/// </para>
	/// </remarks>
	public static class PointOfInterestChurchyards
	{
		private const int YardSalt = 0x43485944;

		/// <summary>The drystone's height (1.3 m) scaled to a churchyard's low wall, about a metre.</summary>
		public const float WallScale = 0.75f;

		/// <summary>A lychgate's footprint where the kit has none yet (its passage along z), metres.</summary>
		public static readonly Vector2 LychgateFallback = new Vector2(3.6f, 2.6f);

		/// <summary>The kinds whose churches keep a walled yard.</summary>
		public static bool Walled(POIType kind)
			=> kind == POIType.Village || kind == POIType.Town || kind == POIType.Port || kind == POIType.City || kind == POIType.Capital;

		/// <summary>The type a churchyard's and a cemetery's tombs are, capped across the settlement (PointOfInterestStructureCaps).</summary>
		public const string MausoleumType = "Mausoleum";

		/// <summary>
		/// How many mausoleums a churchyard of a kind keeps by a roll (0..1): one in a third of villages', one or two in a town's or a
		/// city's, before the settlement's cap on them all (<see cref="MausoleumType"/>).
		/// </summary>
		public static int MausoleumsFor(POIType kind, float roll)
		{
			if (kind == POIType.Village)
			{
				return roll < 0.35f ? 1 : 0;
			}
			return Walled(kind) ? (roll < 0.5f ? 2 : 1) : 0;
		}

		/// <summary>A churchyard laid out in its church's frame (x to the church's right, y toward its front), its sunny side at +x.</summary>
		public sealed class Plan
		{
			/// <summary>The wall's reach to each side of the church's middle, its front line (behind the church's front) and its back.</summary>
			public float Left, Right, Front, Back;
			/// <summary>The wall's runs, each from its one end to the other, the lychgate's gap left out.</summary>
			public readonly List<(Vector2 A, Vector2 B)> Runs = new List<(Vector2, Vector2)>();
			/// <summary>The lychgate's centre, on the front line.</summary>
			public Vector2 Gate;
			/// <summary>The mausoleums' centres (each faces the church, as the church faces).</summary>
			public readonly List<Vector2> Mausoleums = new List<Vector2>();
		}

		/// <summary>How far the last row of graves reaches behind the church's middle (PropsFeature lays them 2.2 m behind its back, 2.3 m apart), metres.</summary>
		public static float RowsReach(float churchDepth, int rows) => churchDepth * 0.5f + 2.2f + Mathf.Max(0, rows - 1) * 2.3f + 0.6f;

		/// <summary>
		/// A churchyard's plan (pure): round a church of <paramref name="church"/> (x wide, y deep) with <paramref name="rows"/> rows of
		/// graves behind it, <paramref name="mausoleums"/> (0..2) mausoleums of <paramref name="mausoleum"/> and a lychgate of
		/// <paramref name="lychgate"/>; the one mausoleum of a yard that keeps one stands in the corner <paramref name="corner"/> (0 the
		/// shaded side, 1 the sunny). Two that would not stand side by side in its back are one.
		/// </summary>
		public static Plan Layout(Vector2 church, int rows, int mausoleums, Vector2 mausoleum, Vector2 lychgate, int corner)
		{
			var plan = new Plan();
			float halfW = church.x * 0.5f, halfD = church.y * 0.5f;
			// The sunny side keeps its graves (2 m off the church) and the lychgate's run beside them; the other a walk round it.
			plan.Right = halfW + Mathf.Max(4.2f, 0.3f + 0.6f + lychgate.x + 1.2f);
			plan.Left = halfW + 2.5f;
			plan.Front = halfD - lychgate.y * 0.5f - 0.3f;
			float rowsReach = RowsReach(church.y, rows);
			float width = plan.Left + plan.Right;
			int count = Mathf.Clamp(mausoleums, 0, 2);
			if (count == 2 && 2f * mausoleum.x + 3f > width)
			{
				count = 1;
			}
			plan.Back = -(rowsReach + (count > 0 ? 1f + mausoleum.y + 1.2f : 1.2f));
			float mz = -(rowsReach + 1f + mausoleum.y * 0.5f);
			float leftX = -plan.Left + 1f + mausoleum.x * 0.5f, rightX = plan.Right - 1f - mausoleum.x * 0.5f;
			if (count == 2)
			{
				plan.Mausoleums.Add(new Vector2(leftX, mz));
				plan.Mausoleums.Add(new Vector2(rightX, mz));
			}
			else if (count == 1)
			{
				plan.Mausoleums.Add(new Vector2(corner == 0 ? leftX : rightX, mz));
			}
			// The lychgate in the sunny front run, clear of the church's flank.
			plan.Gate = new Vector2(halfW + 0.3f + 0.6f + lychgate.x * 0.5f, plan.Front);
			float gateFrom = plan.Gate.x - lychgate.x * 0.5f, gateTo = plan.Gate.x + lychgate.x * 0.5f;
			// Round the yard from the church's sunny flank, the gate's gap left out: front, side, back, side, front to the other flank.
			plan.Runs.Add((new Vector2(halfW + 0.3f, plan.Front), new Vector2(gateFrom, plan.Front)));
			plan.Runs.Add((new Vector2(gateTo, plan.Front), new Vector2(plan.Right, plan.Front)));
			plan.Runs.Add((new Vector2(plan.Right, plan.Front), new Vector2(plan.Right, plan.Back)));
			plan.Runs.Add((new Vector2(plan.Right, plan.Back), new Vector2(-plan.Left, plan.Back)));
			plan.Runs.Add((new Vector2(-plan.Left, plan.Back), new Vector2(-plan.Left, plan.Front)));
			plan.Runs.Add((new Vector2(-plan.Left, plan.Front), new Vector2(-halfW - 0.3f, plan.Front)));
			plan.Runs.RemoveAll(r => (r.B - r.A).magnitude < 0.5f);
			return plan;
		}

		/// <summary>
		/// Lays a church's yard (see the class remarks) round the church laid at <paramref name="at"/> turned <paramref name="turn"/>
		/// (its front, −z, to the street) over <paramref name="size"/>, its sunny graves on the side <paramref name="sunny"/> (±1, as
		/// PropsFeature drew it) and <paramref name="rows"/> rows of graves behind it. <paramref name="taken"/> is the ground laid on so
		/// far (what the yard adds is added to it), <paramref name="onStreet"/> whether a point lies within a pad of a street. Returns the
		/// pieces laid.
		/// </summary>
		public static int Lay(PointOfInterestSiteContext context, Vector3 at, Quaternion turn, Vector2 size, float sunny, int rows, float stature,
			List<(Vector2 Centre, float Yaw, Vector2 Size)> taken, Func<Vector2, float, bool> onStreet)
		{
			PointOfInterestRecord record = context?.Record;
			if (record == null || context.GroundAt == null || !Walled(record.Kind))
			{
				return 0;
			}
			var origin = new Vector2(at.x, at.z);
			float yaw = turn.eulerAngles.y;
			int px = Mathf.RoundToInt(at.x), pz = Mathf.RoundToInt(at.z);
			float U(int k) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, YardSalt, px * 7919 + pz, k));
			bool allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
			int missing = 0, laid = 0;
			StructurePiece mausoleumPiece = StructurePieces.Find("Mausoleum"), gatePiece = StructurePieces.Find("Lychgate");
			Vector2 mausoleum = (mausoleumPiece != null ? mausoleumPiece.Footprint : new Vector2(5f, 7f)) * stature;
			Vector2 lychgate = (gatePiece != null ? gatePiece.Footprint : LychgateFallback) * stature;
			// No more than the settlement's two mausoleums in all its yards (2026-10-20, PointOfInterestStructureCaps): a second church's
			// yard, and the cemetery after them (PointOfInterestOutskirts), keep what room the ones laid before left.
			int tombs = Mathf.Min(MausoleumsFor(record.Kind, U(1)), PointOfInterestStructureCaps.Tally.Of(context).Room(MausoleumType));
			Plan plan = Layout(size, rows, tombs, mausoleum, lychgate, U(2) < 0.5f ? 0 : 1);
			float side = sunny < 0f ? -1f : 1f;
			Vector2 right = PointOfInterestLayouts.Rotate(Vector2.right, yaw), front = -PointOfInterestLayouts.Rotate(Vector2.up, yaw);
			Vector2 World(Vector2 local) => origin + right * (local.x * side) + front * local.y;

			// What a piece of the yard may not stand on: anything laid but the church itself, a street, the water.
			bool Blocked(Vector2 centre, float pieceYaw, Vector2 extent)
			{
				foreach ((Vector2 c, float y, Vector2 s) in taken)
				{
					if ((c - origin).sqrMagnitude < 0.01f)
					{
						continue;
					}
					if (PointOfInterestLayouts.Overlaps(centre, pieceYaw, extent, c, y, s))
					{
						return true;
					}
				}
				var corners = new Vector2[4];
				PointOfInterestLayouts.Corners(centre, pieceYaw, extent, corners);
				foreach (Vector2 c in corners)
				{
					if (onStreet != null && onStreet(c, 0.3f) || context.IsWet(c.x, c.y, 0.1f))
					{
						return true;
					}
				}
				return onStreet != null && onStreet(centre, 0.3f);
			}

			// The mausoleums, facing the church across the graves.
			for (int m = 0; m < plan.Mausoleums.Count; m++)
			{
				Vector2 c = World(plan.Mausoleums[m]);
				if (Blocked(c, yaw, mausoleum))
				{
					continue;
				}
				if (mausoleumPiece == null)
				{
					missing++;
					continue;
				}
				GameObject prefab = StructureKit.Prefab("Mausoleum", U(10 + m) < 0.5f ? 0 : 1, StructureFinish.None, allowLocal);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				Seat(context, prefab, c, yaw, mausoleum, mausoleumPiece.Depth * stature, stature);
				taken.Add((c, yaw, mausoleum + Vector2.one * 0.3f));
				laid++;
			}

			// The lychgate in its gap, its front to the street; a worn way through it.
			Vector2 gate = World(plan.Gate);
			if (!Blocked(gate, yaw, lychgate))
			{
				GameObject prefab = gatePiece != null ? StructureKit.Prefab("Lychgate", 0, StructureFinish.None, allowLocal) : null;
				if (prefab == null)
				{
					missing++;
				}
				else
				{
					Seat(context, prefab, gate, yaw, lychgate, gatePiece.Depth * stature, stature);
					taken.Add((gate, yaw, lychgate + Vector2.one * 0.3f));
					laid++;
				}
				PointOfInterestOutskirts.Track(context, gate - front * (lychgate.y * 0.5f + 2.5f), gate + front * (lychgate.y * 0.5f + 1.2f), 0.7f, 0.85f,
					ScenePathPointFlags.None, record.Id);
			}

			// The wall, module by module, joined at its corners.
			var runs = new List<(Vector2 A, Vector2 B)>();
			foreach ((Vector2 a, Vector2 b) in plan.Runs)
			{
				runs.Add((World(a), World(b)));
			}
			laid += LayWall(context, runs, Blocked, taken, allowLocal, ref missing);
			if (missing > 0)
			{
				PointOfInterestFeatureKit.Note(context, $"a churchyard's {missing} piece(s) (Mausoleum, Lychgate or its wall) have no art yet (regenerate the structure art)");
			}
			return laid;
		}

		/// <summary>
		/// A low wall along <paramref name="runs"/> (world ends), module by module, joined at its corners (PointOfInterestLinearFeatures.Joins):
		/// the kit's drystone at <see cref="WallScale"/>, else its railing on a low wall (IronFence's second variant); a module left out
		/// where <paramref name="blocked"/> (its middle, yaw and extent) says the ground is taken, each one laid added to
		/// <paramref name="taken"/>. A churchyard's and a cemetery's (PointOfInterestOutskirts). Returns the props laid.
		/// </summary>
		public static int LayWall(PointOfInterestSiteContext context, IReadOnlyList<(Vector2 A, Vector2 B)> runs, Func<Vector2, float, Vector2, bool> blocked,
			List<(Vector2 Centre, float Yaw, Vector2 Size)> taken, bool allowLocal, ref int missing)
		{
			string wallId = "LinearDrystone";
			int wallVariant = 0;
			float yScale = WallScale;
			GameObject wall = StructurePieces.Find(wallId) != null ? StructureKit.Prefab(wallId, wallVariant, StructureFinish.None, allowLocal) : null;
			if (wall == null)
			{
				wallId = "IronFence";
				wallVariant = 1;
				yScale = 1f;
				wall = StructurePieces.Find(wallId) != null ? StructureKit.Prefab(wallId, wallVariant, StructureFinish.None, allowLocal) : null;
			}
			if (wall == null)
			{
				missing++;
				return 0;
			}
			float module = Mathf.Max(0.5f, StructurePieces.Find(wallId).ModuleLength);
			var joins = new PointOfInterestLinearFeatures.Joins();
			foreach ((Vector2 wa, Vector2 wb) in runs)
			{
				float length = (wb - wa).magnitude;
				int count = Mathf.Max(1, Mathf.RoundToInt(length / module));
				for (int k = 0; k < count; k++)
				{
					Vector2 ma = Vector2.Lerp(wa, wb, k / (float)count), mb = Vector2.Lerp(wa, wb, (k + 1f) / count);
					Vector2 mid = 0.5f * (ma + mb), run = mb - ma;
					float runYaw = PointOfInterestLayouts.DirectionOf(run) - 90f;
					var extent = new Vector2(Mathf.Max(0.1f, run.magnitude - 0.4f), 0.6f);
					if (blocked != null && blocked(mid, runYaw, extent))
					{
						continue;
					}
					joins.Module(context, context.Palette, wall, wallId, wallVariant, PointOfInterestLinearFeatures.Joint(context, ma),
						PointOfInterestLinearFeatures.Joint(context, mb), module, 0f, yScale, 0f);
					taken?.Add((mid, runYaw, extent));
				}
			}
			return joins.Close();
		}

		/// <summary>A building seated as a settlement's are: no higher than its centre's ground, low enough that its foundation reaches its lowest corner.</summary>
		private static void Seat(PointOfInterestSiteContext context, GameObject prefab, Vector2 at, float yaw, Vector2 footprint, float depth, float stature)
		{
			var corners = new Vector2[4];
			PointOfInterestLayouts.Corners(at, yaw, footprint, corners);
			float centre = context.GroundAt(at.x, at.y), lowest = centre;
			foreach (Vector2 c in corners)
			{
				lowest = Mathf.Min(lowest, context.GroundAt(c.x, c.y));
			}
			context.AddProp(prefab, new Vector3(at.x, Mathf.Min(centre, lowest + Mathf.Max(0f, depth) * 0.9f), at.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
		}
	}
}
#endif
