#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Medieval ships and boats (2026-10-12: "We need docks…, accurate ships and boats"; "Shipwrecks don't have a wrecked
	/// ship mesh?"): a rowboat, a fishing smack, a cog and a longship, built on lofted hulls (<see cref="Hull"/>), and the wrecks
	/// of a cog and a longship. A floating ship stands at its waterline (y = 0, <see cref="StructureAnchor.Deck"/>), its keel
	/// below; length along +x, bow at +x. A wreck and an upturned boat stand on the ground.
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddShips(List<StructurePiece> list)
		{
			StructurePiece rowboat = Piece(list, "Rowboat", StructureStyle.Timber, StructureSize.Small,
				"A clinker rowboat, 4 m: v0 afloat with its oars shipped, v1 hauled up and turned over on its beach (on the ground).",
				Rowboat, "boat", "dock");
			rowboat.Anchor = StructureAnchor.Deck;
			StructurePiece smack = Piece(list, "FishingBoat", StructureStyle.Timber, StructureSize.Medium,
				"A fishing smack, 7 m, one mast: v0 its sail furled and nets heaped aft, v1 its sail set.",
				FishingBoat, "boat", "dock");
			smack.Anchor = StructureAnchor.Deck;
			StructurePiece cog = Piece(list, "Cog", StructureStyle.Timber, StructureSize.Building,
				"A cog, 18 m: a deep round-bellied hull, castles fore and aft, one mast with a square sail; v0 sail furled, v1 sail set.",
				Cog, "ship", "dock");
			cog.Anchor = StructureAnchor.Deck;
			StructurePiece longship = Piece(list, "Longship", StructureStyle.Timber, StructureSize.Building,
				"A longship, 20 m: a low clinker hull sweeping up to a dragon's head and tail, shields along the gunwale; v0 oars out and sail set, v1 oars shipped and sail furled.",
				Longship, "ship", "dock");
			longship.Anchor = StructureAnchor.Deck;
			Piece(list, "Shipwreck", StructureStyle.Timber, StructureSize.Building,
				"A wreck on its beach: v0 a cog's stern half, heeled over and sunk in the sand, its ribs bare where it broke, the mast fallen beside it; v1 a longship's bones, keel and ribs.",
				Shipwreck, "ship", "wreck");
			Piece(list, "Slipway", StructureStyle.Timber, StructureSize.Building,
				"A ship building on its slipway, on the ground and launched toward +x: its keel on blocks over the ground ways, the stern half planked, the bow still bare frames, shores either side and staging along one; v0 a cog, v1 a longship.",
				Slipway, "ship", "shipyard");
		}

		/// <summary>A hull's lines (metres, at the waterline y = 0).</summary>
		private struct HullLines
		{
			public float Length, Beam, Depth, Draft, Sheer;
			/// <summary>How full the hull is toward its ends (higher: blunter, a cog's; lower: finer, a longship's).</summary>
			public float Fullness;
			/// <summary>The section's roundness (2: a round bilge; higher: flatter-floored, harder-sided).</summary>
			public float Round;
			public float Planking;
			public int Stations, Side;
		}

		/// <summary>The gunwale's height at a station (0 stern … 1 bow).</summary>
		private static float Gunwale(HullLines h, float t)
		{
			float e = Mathf.Abs(2f * t - 1f);
			return h.Depth - h.Draft + h.Sheer * e * e;
		}

		/// <summary>The half-breadth at the gunwale at a station.</summary>
		private static float HalfBreadth(HullLines h, float t)
		{
			float e = Mathf.Abs(2f * t - 1f);
			return Mathf.Max(0.05f, h.Beam * 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(e, h.Fullness))));
		}

		/// <summary>The depth from the gunwale to the keel at a station: shallower toward the ends, where the stems rise.</summary>
		private static float KeelDepth(HullLines h, float t)
		{
			float e = Mathf.Abs(2f * t - 1f);
			return h.Depth * (0.55f + 0.45f * (1f - e * e * e * e));
		}

		/// <summary>A point on the hull's skin: station <paramref name="t"/>, across <paramref name="u"/> (−1 port gunwale … 1 starboard), <paramref name="inset"/> in from the outside.</summary>
		private static Vector3 HullPoint(HullLines h, float t, float u, float inset)
		{
			float b = Mathf.Max(0.02f, HalfBreadth(h, t) - inset), depth = Mathf.Max(0.05f, KeelDepth(h, t) - inset);
			float y = Gunwale(h, t) - depth * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(u), h.Round)), 1f / h.Round);
			return new Vector3((t - 0.5f) * h.Length, y, b * u);
		}

		/// <summary>
		/// A hull as one closed shell of planking: lofted through its stations from <paramref name="from"/> to
		/// <paramref name="to"/> (0 the stern … 1 the bow; a wreck's broken hull stops short), each station the outside skin
		/// across and the inside skin back.
		/// </summary>
		private static StructureSolid Hull(StructureDraft d, HullLines h, int key, float from = 0f, float to = 1f)
		{
			int stations = Mathf.Max(4, d.Fine ? h.Stations : d.Mid ? h.Stations * 2 / 3 : h.Stations / 2);
			int side = Mathf.Max(2, d.Fine ? h.Side : d.Mid ? h.Side - 1 : h.Side - 2);
			var rings = new List<Vector3[]>(stations + 1);
			for (int i = 0; i <= stations; i++)
			{
				float t = Mathf.Lerp(from, to, i / (float)stations);
				// The very ends a little in from the posts, so the stem's section keeps its breadth.
				t = Mathf.Clamp(t, 0.015f, 0.985f);
				var ring = new Vector3[4 * side + 2];
				int n = 0;
				for (int k = -side; k <= side; k++)
				{
					ring[n++] = HullPoint(h, t, k / (float)side, 0f);
				}
				for (int k = side; k >= -side; k--)
				{
					ring[n++] = HullPoint(h, t, k / (float)side, h.Planking);
				}
				rings.Add(ring);
			}
			StructureSolid hull = StructureShapes.Loft(rings, StructureMaterial.Timber, key);
			hull.Grain = Vector3.right;
			return hull;
		}

		/// <summary>The stem and sternpost: timbers up the ends from the keel to over the gunwale.</summary>
		private static void Posts(StructureDraft d, HullLines h, float rise, int key)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				float t = s > 0 ? 0.985f : 0.015f;
				Vector3 foot = HullPoint(h, t, 0f, 0f) + new Vector3(-s * 0.4f, 0f, 0f);
				Vector3 top = new Vector3(s * h.Length * 0.5f + s * 0.25f, Gunwale(h, t) + rise, 0f);
				d.Add(StructureShapes.Beam(foot, top, 0.18f, 0.22f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
		}

		/// <summary>A deck of planks across the hull at <paramref name="below"/> under the midship gunwale, from <paramref name="from"/> to <paramref name="to"/>.</summary>
		private static void Deck(StructureDraft d, HullLines h, float below, float from, float to, int key)
		{
			float y = Gunwale(h, 0.5f) - below;
			float x0 = (from - 0.5f) * h.Length, x1 = (to - 0.5f) * h.Length;
			float width = 2f * Mathf.Min(HalfBreadth(h, from), HalfBreadth(h, to)) - 2f * h.Planking - 0.1f;
			d.Add(StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, y, 0f), new Vector3(x1 - x0, 0.08f, Mathf.Max(0.3f, width)), StructureMaterial.Timber, key)).Grain = Vector3.right;
		}

		/// <summary>A mast at <paramref name="x"/> from <paramref name="foot"/> up <paramref name="height"/>, its yard across, and its sail set (a billowed sheet) or furled on the yard.</summary>
		private static void Rig(StructureDraft d, float x, float foot, float height, float yard, float sail, bool set, StructureMaterial cloth, int key)
		{
			d.Add(StructureShapes.Cylinder(0.12f + 0.006f * height, foot, foot + height, d.Seg(10, 6), StructureMaterial.Timber, key, true).Transform(Matrix4x4.Translate(new Vector3(x, 0f, 0f))));
			float yardY = foot + height - 0.6f;
			d.Add(StructureShapes.Beam(new Vector3(x + 0.18f, yardY, -yard * 0.5f), new Vector3(x + 0.18f, yardY, yard * 0.5f), 0.12f, 0.12f, StructureMaterial.Timber, key + 1));
			if (set)
			{
				// The square sail, bellied forward in two panes.
				for (int s = 0; s < 2; s++)
				{
					float top = yardY - s * sail * 0.5f, bottom = top - sail * 0.5f;
					float belly = s == 0 ? 0.35f : 0.45f;
					d.Add(StructureShapes.Block(new Vector3(x + 0.25f + belly, (top + bottom) * 0.5f, 0f), new Vector3(0.04f, sail * 0.5f, yard * 0.92f),
						Quaternion.Euler(0f, 0f, s == 0 ? -8f : 8f), cloth, key + 2 + s, StructureRole.Detail));
				}
			}
			else if (d.Mid)
			{
				// Furled: bundled along the yard.
				d.Add(StructureShapes.Cylinder(0.18f, -yard * 0.42f, yard * 0.42f, d.Seg(8, 5), cloth, key + 2, true, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(x + 0.18f, yardY - 0.2f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			if (d.Mid)
			{
				// Stays from the masthead fore and aft.
				d.Add(StructureShapes.Beam(new Vector3(x, foot + height, 0f), new Vector3(x + height * 0.75f, foot + 0.4f, 0f), 0.025f, 0.025f, StructureMaterial.Canvas, key + 5, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(x, foot + height, 0f), new Vector3(x - height * 0.6f, foot + 0.4f, 0f), 0.025f, 0.025f, StructureMaterial.Canvas, key + 6, StructureRole.Detail));
			}
		}

		private static readonly HullLines RowboatLines = new HullLines { Length = 4f, Beam = 1.35f, Depth = 0.62f, Draft = 0.22f, Sheer = 0.28f, Fullness = 2f, Round = 2.2f, Planking = 0.05f, Stations = 10, Side = 4 };
		private static readonly HullLines SmackLines = new HullLines { Length = 7f, Beam = 2.4f, Depth = 1.35f, Draft = 0.6f, Sheer = 0.45f, Fullness = 2.2f, Round = 2.3f, Planking = 0.06f, Stations = 12, Side = 5 };
		private static readonly HullLines CogLines = new HullLines { Length = 18f, Beam = 6.2f, Depth = 3.8f, Draft = 1.9f, Sheer = 0.9f, Fullness = 3.2f, Round = 3f, Planking = 0.1f, Stations = 16, Side = 6 };
		private static readonly HullLines LongshipLines = new HullLines { Length = 20f, Beam = 4.4f, Depth = 1.6f, Draft = 0.7f, Sheer = 1.5f, Fullness = 1.8f, Round = 2f, Planking = 0.06f, Stations = 18, Side = 5 };

		private static void Rowboat(StructureDraft d)
		{
			HullLines h = RowboatLines;
			bool upturned = d.Variant == 1;
			var parts = new List<StructureSolid> { Hull(d, h, 1) };
			// Two thwarts across, and oars shipped along them.
			for (int i = 0; i < 2; i++)
			{
				float x = i == 0 ? -0.4f : 0.7f;
				float y = Gunwale(h, 0.5f) - 0.22f;
				parts.Add(StructureShapes.Box(new Vector3(x, y, 0f), new Vector3(0.25f, 0.05f, 2f * HalfBreadth(h, 0.5f + x / h.Length) - 0.12f), StructureMaterial.Timber, 2 + i));
			}
			if (d.Mid && !upturned)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					parts.Add(StructureShapes.Beam(new Vector3(-1.4f, Gunwale(h, 0.5f) - 0.1f, s * 0.35f), new Vector3(1.5f, Gunwale(h, 0.5f) - 0.05f, s * 0.3f), 0.05f, 0.05f, StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			// Upturned on the shore: keel up, gunwales on the ground (y = 0 the ground, its gunwale's sheer resting on it).
			Matrix4x4 lay = upturned ? Matrix4x4.TRS(new Vector3(0f, Gunwale(h, 0.5f) + 0.05f, 0f), Quaternion.Euler(180f, 0f, 0f), Vector3.one) : Matrix4x4.identity;
			foreach (StructureSolid part in parts)
			{
				d.Add(upturned ? part.Transform(lay) : part);
			}
		}

		private static void FishingBoat(StructureDraft d)
		{
			HullLines h = SmackLines;
			d.Add(Hull(d, h, 1));
			Posts(d, h, 0.25f, 2);
			Deck(d, h, 0.55f, 0.62f, 0.9f, 4);
			Deck(d, h, 0.75f, 0.1f, 0.62f, 5);
			Rig(d, 0.6f, Gunwale(h, 0.5f) - 0.75f, 6.2f, 3.6f, 3.4f, d.Variant == 1, StructureMaterial.Canvas, 10);
			if (d.Mid)
			{
				// The tiller and rudder at the stern, and the nets heaped aft.
				d.Add(StructureShapes.Box(new Vector3(-h.Length * 0.5f - 0.15f, -0.2f, 0f), new Vector3(0.12f, 1.4f, 0.7f), StructureMaterial.Timber, 20));
				d.Add(StructureShapes.Sphere(new Vector3(-1.9f, Gunwale(h, 0.5f) - 0.55f, 0.2f), new Vector3(0.8f, 0.3f, 0.6f), d.Seg(10, 6), 4, StructureMaterial.Canvas, 21, StructureRole.Detail));
			}
		}

		private static void Cog(StructureDraft d)
		{
			HullLines h = CogLines;
			bool set = d.Variant == 1;
			d.Add(Hull(d, h, 1));
			Posts(d, h, 0.5f, 2);
			Deck(d, h, 1.1f, 0.06f, 0.94f, 4);
			// The castles: a raised aftcastle and a smaller forecastle on posts over the stem, each with a rail.
			float g = Gunwale(h, 0.5f);
			float aftX = -h.Length * 0.5f + 2.6f, foreX = h.Length * 0.5f - 1.8f;
			d.Add(StructureShapes.Box(new Vector3(aftX, Gunwale(h, 0.1f) + 0.9f, 0f), new Vector3(4.6f, 1.9f, 2f * HalfBreadth(h, 0.14f)), StructureMaterial.Timber, 6));
			d.Add(StructureShapes.Box(new Vector3(foreX, Gunwale(h, 0.92f) + 1.2f, 0f), new Vector3(3.2f, 0.25f, 2f * HalfBreadth(h, 0.88f) + 0.4f), StructureMaterial.Timber, 7)).Grain = Vector3.right;
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(aftX, Gunwale(h, 0.1f) + 2.15f, s * HalfBreadth(h, 0.14f)), new Vector3(4.6f, 0.6f, 0.1f), StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(foreX, Gunwale(h, 0.92f) + 1.6f, s * (HalfBreadth(h, 0.88f) + 0.2f)), new Vector3(3.2f, 0.6f, 0.1f), StructureMaterial.Timber, 10 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(foreX, g, s * HalfBreadth(h, 0.88f)), new Vector3(foreX, Gunwale(h, 0.92f) + 1.1f, s * (HalfBreadth(h, 0.88f) + 0.2f)), 0.16f, 0.16f, StructureMaterial.Timber, 12 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
				// The stern rudder and a bowsprit.
				d.Add(StructureShapes.Box(new Vector3(-h.Length * 0.5f - 0.3f, 0.2f, 0f), new Vector3(0.18f, 4f, 1.1f), StructureMaterial.Timber, 14));
				d.Add(StructureShapes.Beam(new Vector3(foreX, g + 0.6f, 0f), new Vector3(h.Length * 0.5f + 3.2f, g + 2f, 0f), 0.2f, 0.2f, StructureMaterial.Timber, 15));
			}
			Rig(d, 0.6f, g - 1.1f, 15f, 10f, 8.5f, set, StructureMaterial.Canvas, 20);
		}

		private static void Longship(StructureDraft d)
		{
			HullLines h = LongshipLines;
			bool rowing = d.Variant == 0;
			d.Add(Hull(d, h, 1));
			// The stems sweep up from the keel to a dragon's head at the bow and its tail at the stern, in a few carved timbers.
			for (int s = -1; s <= 1; s += 2)
			{
				float x0 = s * h.Length * 0.5f, tEnd = s > 0 ? 0.985f : 0.015f;
				// From the keel inside the hull's end, up along its stem to over the gunwale, then on up to the head or the tail.
				Vector3 a = HullPoint(h, s > 0 ? 0.93f : 0.07f, 0f, 0.05f), b = new Vector3(x0 + s * 0.05f, Gunwale(h, tEnd) + 0.2f, 0f);
				Vector3 c = new Vector3(x0 + s * 0.3f, b.y + 1.2f, 0f), e = new Vector3(x0 + s * (s > 0 ? 0.8f : 0.15f), c.y + (s > 0 ? 0.5f : 0.9f), 0f);
				int key = 2 + (s > 0 ? 0 : 10);
				d.Add(StructureShapes.Beam(a, b, 0.2f, 0.22f, StructureMaterial.Timber, key));
				d.Add(StructureShapes.Beam(b, c, 0.18f, 0.2f, StructureMaterial.Timber, key + 1));
				d.Add(StructureShapes.Beam(c, e, 0.16f, s > 0 ? 0.3f : 0.14f, StructureMaterial.Timber, key + 2));
				if (s > 0 && d.Mid)
				{
					// The dragon's head: a jaw and a crest.
					d.Add(StructureShapes.Block(e + new Vector3(0.25f, -0.08f, 0f), new Vector3(0.6f, 0.28f, 0.3f), Quaternion.Euler(0f, 0f, -12f), StructureMaterial.Timber, key + 3, StructureRole.Detail));
					d.Add(StructureShapes.Block(e + new Vector3(-0.15f, 0.25f, 0f), new Vector3(0.4f, 0.3f, 0.06f), Quaternion.Euler(0f, 0f, 25f), StructureMaterial.Timber, key + 4, StructureRole.Detail));
				}
			}
			float g = Gunwale(h, 0.5f);
			Deck(d, h, 0.65f, 0.12f, 0.88f, 30);
			// Shields along both gunwales, painted in turn, and the oars out through the oar-holes (or shipped inboard).
			int shields = d.Fine ? 13 : d.Mid ? 9 : 0;
			for (int i = 0; i < shields; i++)
			{
				float t = 0.2f + 0.6f * (i + 0.5f) / shields;
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 at = HullPoint(h, t, s, 0f) + new Vector3(0f, -0.22f, s * 0.04f);
					d.Add(StructureShapes.Cylinder(0.42f, 0f, 0.04f, d.Seg(10, 6), i % 2 == 0 ? StructureMaterial.Dyed : StructureMaterial.Timber, 40 + i * 2 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.TRS(at, Quaternion.Euler(s * 90f, 0f, 0f), Vector3.one)));
				}
			}
			int oars = d.Mid ? 8 : 0;
			for (int i = 0; i < oars; i++)
			{
				float t = 0.25f + 0.5f * (i + 0.5f) / oars;
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 port = HullPoint(h, t, s, 0f) + new Vector3(0f, -0.35f, 0f);
					Vector3 blade = rowing ? port + new Vector3(0.6f, -1.4f, s * 3.4f) : new Vector3(port.x + 3.6f, g - 0.4f, s * 0.8f);
					d.Add(StructureShapes.Beam(port - (blade - port).normalized * 0.8f, blade, 0.05f, 0.05f, StructureMaterial.Timber, 80 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			Rig(d, 0.5f, g - 0.65f, 10f, 7.5f, 6f, rowing, StructureMaterial.Dyed, 100);
		}

		private static void Shipwreck(StructureDraft d)
		{
			var parts = new List<StructureSolid>();
			if (d.Variant == 1)
			{
				// A longship's bones half in the sand: the keel, and ribs standing up out of it, some broken.
				HullLines h = LongshipLines;
				parts.Add(StructureShapes.Beam(new Vector3(-h.Length * 0.45f, -0.9f, 0f), new Vector3(h.Length * 0.45f, -0.7f, 0f), 0.3f, 0.3f, StructureMaterial.Timber, 1));
				int ribs = d.Fine ? 12 : d.Mid ? 8 : 5;
				for (int i = 0; i < ribs; i++)
				{
					float t = 0.12f + 0.76f * (i + 0.5f) / ribs;
					for (int s = -1; s <= 1; s += 2)
					{
						float broken = d.Rand(10 + i, s > 0 ? 1 : 0);
						float top = broken < 0.35f ? 0.45f : broken < 0.7f ? 0.75f : 1f;
						Vector3 prev = HullPoint(h, t, 0f, 0f);
						for (int k = 1; k <= 3; k++)
						{
							float u = s * top * k / 3f;
							Vector3 next = HullPoint(h, t, u, 0f);
							parts.Add(StructureShapes.Beam(prev, next, 0.14f, 0.16f, StructureMaterial.Timber, 20 + i * 8 + (s > 0 ? 4 : 0) + k, k == 1 ? StructureRole.Structure : StructureRole.Detail));
							prev = next;
						}
					}
				}
				Matrix4x4 sink = Matrix4x4.TRS(new Vector3(0f, -0.05f, 0f), Quaternion.Euler(4f, 12f, 0f), Vector3.one);
				foreach (StructureSolid part in parts)
				{
					d.Add(part.Transform(sink));
				}
				return;
			}
			// A cog's stern half: the hull broken off at 60% of its length, ribs bare at the break, heeled over and half buried.
			HullLines cog = CogLines;
			const float broke = 0.6f;
			parts.Add(Hull(d, cog, 1, 0f, broke));
			parts.Add(StructureShapes.Box(new Vector3(-cog.Length * 0.5f + 2.6f, Gunwale(cog, 0.1f) + 0.9f, 0f), new Vector3(4.6f, 1.9f, 2f * HalfBreadth(cog, 0.14f)), StructureMaterial.Timber, 2));
			int bare = d.Mid ? 4 : 2;
			for (int i = 0; i < bare; i++)
			{
				float t = broke + 0.04f * (i + 1);
				Vector3 prev = HullPoint(cog, t, -1f, 0f);
				for (int k = -2; k <= 2; k++)
				{
					Vector3 next = HullPoint(cog, t, k / 2f, 0f);
					if (k > -2)
					{
						parts.Add(StructureShapes.Beam(prev, next, 0.2f, 0.22f, StructureMaterial.Timber, 10 + i * 6 + k + 2));
					}
					prev = next;
				}
			}
			// The hull lies heeled 28° and settled a metre into the sand (y = 0 the ground), its stern toward −x.
			Matrix4x4 lie = Matrix4x4.TRS(new Vector3(3f, 1.1f, 0f), Quaternion.Euler(28f, 0f, 0f), Vector3.one);
			foreach (StructureSolid part in parts)
			{
				d.Add(part.Transform(lie));
			}
			if (d.Mid)
			{
				// The mast fallen on the sand beside it, its yard across it.
				d.Add(StructureShapes.Cylinder(0.2f, 0f, 13f, d.Seg(8, 5), StructureMaterial.Timber, 50, true).Transform(Matrix4x4.TRS(new Vector3(-2f, 0.2f, 4.5f), Quaternion.Euler(0f, 12f, -90f), Vector3.one)));
				d.Add(StructureShapes.Beam(new Vector3(6f, 0.15f, 1f), new Vector3(7.5f, 0.15f, 8.5f), 0.14f, 0.14f, StructureMaterial.Timber, 51));
			}
		}

		/// <summary>
		/// A floating boat's hull, for whoever puts it on the water (PointOfInterestHarbour, PointOfInterestSeaTraffic): its
		/// length and beam, and its draft, the depth of its keel under the waterline, metres. False for a piece that is not one.
		/// </summary>
		public static bool TryHull(string pieceId, out float length, out float beam, out float draft)
		{
			HullLines h;
			switch (pieceId)
			{
				case "Rowboat": h = RowboatLines; break;
				case "FishingBoat": h = SmackLines; break;
				case "Cog": h = CogLines; break;
				case "Longship": h = LongshipLines; break;
				default:
					length = beam = draft = 0f;
					return false;
			}
			length = h.Length;
			beam = h.Beam;
			draft = h.Draft;
			return true;
		}

		/// <summary>How high a hull on the stocks stands: its keel this far over the ground, metres.</summary>
		private const float StocksHeight = 0.7f;

		private static void Slipway(StructureDraft d)
		{
			bool longship = d.Variant == 1;
			HullLines h = longship ? LongshipLines : CogLines;
			// Lifted so the keel's lowest point (midships, a draft under the waterline) sits on the blocks.
			float lift = StocksHeight + h.Draft;
			Matrix4x4 stocks = Matrix4x4.Translate(new Vector3(0f, lift, 0f));
			float planked = longship ? 0.7f : 0.55f;

			// The ground ways the hull will slide down, the keel blocks on them, the keel along the blocks.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(-h.Length * 0.55f, 0.05f, s * 0.9f), new Vector3(h.Length * 0.6f, 0.05f, s * 0.9f), 0.3f, 0.22f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
			}
			int blocks = d.Fine ? 7 : d.Mid ? 5 : 3;
			for (int i = 0; i < blocks; i++)
			{
				float t = 0.12f + 0.76f * i / (blocks - 1);
				float keel = HullPoint(h, t, 0f, 0f).y + lift;
				d.Add(StructureShapes.Box(new Vector3((t - 0.5f) * h.Length, (keel + 0.12f) * 0.5f, 0f), new Vector3(0.45f, keel - 0.12f, 2.2f), StructureMaterial.Timber, 3 + i));
			}
			Vector3 keelAft = HullPoint(h, 0.03f, 0f, 0f), keelFore = HullPoint(h, 0.97f, 0f, 0f);
			d.Add(StructureShapes.Beam(stocks.MultiplyPoint3x4(keelAft), stocks.MultiplyPoint3x4(keelFore), 0.3f, 0.32f, StructureMaterial.Timber, 12)).Grain = Vector3.right;

			// The planked stern half, open where the planking stops, and the posts up its ends.
			d.Add(Hull(d, h, 13, 0f, planked).Transform(stocks));
			for (int s = -1; s <= 1; s += 2)
			{
				float t = s > 0 ? 0.985f : 0.015f;
				Vector3 foot = HullPoint(h, t, 0f, 0f) + new Vector3(-s * 0.4f, 0f, 0f);
				Vector3 top = new Vector3(s * h.Length * 0.5f + s * 0.25f, Gunwale(h, t) + (longship ? 1.1f : 0.5f), 0f);
				d.Add(StructureShapes.Beam(stocks.MultiplyPoint3x4(foot), stocks.MultiplyPoint3x4(top), 0.18f, 0.22f, StructureMaterial.Timber, 14 + (s > 0 ? 1 : 0)));
			}

			// The frames standing bare forward of the planking: each a rib up either side from the keel.
			int frames = d.Fine ? 9 : d.Mid ? 6 : 3;
			for (int i = 0; i < frames; i++)
			{
				float t = planked + (0.95f - planked) * (i + 0.5f) / frames;
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 prev = HullPoint(h, t, 0f, 0f);
					int pieces = d.Mid ? 3 : 2;
					for (int k = 1; k <= pieces; k++)
					{
						Vector3 next = HullPoint(h, t, s * k / (float)pieces, 0f);
						d.Add(StructureShapes.Beam(stocks.MultiplyPoint3x4(prev), stocks.MultiplyPoint3x4(next), 0.14f, 0.16f, StructureMaterial.Timber, 20 + i * 8 + (s > 0 ? 4 : 0) + k,
							k == 1 ? StructureRole.Structure : StructureRole.Detail));
						prev = next;
					}
				}
			}

			// Shores from the ground up to the bilge either side, holding her upright.
			int shores = d.Mid ? 4 : 2;
			for (int i = 0; i < shores; i++)
			{
				float t = 0.2f + 0.6f * i / Mathf.Max(1, shores - 1);
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 bilge = stocks.MultiplyPoint3x4(HullPoint(h, t, s * 0.8f, 0f));
					var foot = new Vector3(bilge.x, 0f, bilge.z + s * (bilge.y * 0.55f + 0.4f));
					d.Add(StructureShapes.Beam(foot, bilge, 0.13f, 0.13f, StructureMaterial.Timber, 100 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			if (d.Mid)
			{
				// Staging along the port side: trestles and a plank walk at half the hull's height.
				float y = lift + (Gunwale(h, 0.5f) - h.Depth * 0.5f);
				float z = -HalfBreadth(h, 0.5f) - 1.1f;
				float x0 = -h.Length * 0.32f, x1 = h.Length * 0.32f;
				d.Add(StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, y, z), new Vector3(x1 - x0, 0.07f, 0.6f), StructureMaterial.Timber, 120)).Grain = Vector3.right;
				int trestles = 4;
				for (int i = 0; i < trestles; i++)
				{
					float x = Mathf.Lerp(x0 + 0.4f, x1 - 0.4f, i / (float)(trestles - 1));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(x, 0f, z + s * 0.45f), new Vector3(x, y - 0.04f, z + s * 0.22f), 0.09f, 0.09f, StructureMaterial.Timber, 121 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
				// A ladder up to it.
				d.Add(StructureShapes.Beam(new Vector3(x0 - 0.2f, 0f, z - 0.9f), new Vector3(x0 + 0.3f, y, z - 0.25f), 0.07f, 0.07f, StructureMaterial.Timber, 140, StructureRole.Detail));
			}
		}
	}
}
#endif
