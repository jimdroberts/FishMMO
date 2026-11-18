#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A dungeon's doorway in its piece's own frame (<see cref="StructurePieces.TryDoorwayOf"/>): the middle of its sill on
	/// the outer face, its clear width and height over the sill, and the passage behind it as a box (inward is +z, the piece's
	/// front looking down −z as every piece's does). What the dungeon entrance's interaction box fills and what the client
	/// darkens as indoors (SceneRoom).
	/// </summary>
	public struct StructureDoorway
	{
		public Vector3 Sill;
		public float Width, Height;
		/// <summary>The passage's clear inside: its middle and half its width, height and length.</summary>
		public Vector3 PassageCentre, PassageHalfSize;
	}

	/// <summary>
	/// Dungeon entrances (2026-10-18: "Dungeon entrances look boring and misaligned"): doorways into the dark set into a rock
	/// face or a hillside, and one built against the back wall of a great cavern. Each is an OPEN door: the leaves stand
	/// swung back, and steps run down from the threshold into a short passage whose far end is soot-black, so the way in
	/// reads as going down into the earth (the client darkens the passage as indoors, from <see cref="TryDoorwayOf"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Anchored at the threshold.</b> Unlike the kit's free-standing pieces, these are set INTO the ground, so the origin is
	/// the middle of the doorway's outer face line at the ground (the forecourt's level): the face of the rock the site's
	/// shaper cut stands on it (DungeonEntranceShaper), the forecourt runs out down −z and the rear mass and passage into the
	/// hill up +z. The forecourt in front reaches about as far as the mass behind, so the drawn bounds still sit about the
	/// origin and the measured footprint is the piece's own. Every variant keeps inside <see cref="DungeonHalfWidth"/>,
	/// <see cref="DungeonFrontReach"/> and <see cref="DungeonRearReach"/>: the shaper cuts the ground to that frame before it
	/// knows which variant the site will draw.
	/// </para>
	/// <para>
	/// <b>A raised sill.</b> The doorway stands <see cref="DungeonSillRise"/> over the forecourt up three steps, so the stair
	/// inside falls nearly two metres to the passage floor while nothing of the piece reaches deeper than the kit's 1.3 m
	/// under its ground line.
	/// </para>
	/// <para>
	/// Only the "dungeon" tag (no query a template already makes names them, so adding them moved no other site's pick):
	/// laid by id. The rune bands glow with rune-light (GlowHueOf); the fire bowls burn (SceneEmitterKind.Fire).
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The frame every dungeon entrance keeps at scale 1, metres: half its width, its forecourt's reach before the threshold, its mass's behind.</summary>
		public const float DungeonHalfWidth = 6f, DungeonFrontReach = 7.5f, DungeonRearReach = 8f;
		/// <summary>How far a dungeon doorway's sill stands over the forecourt, metres: three risers.</summary>
		public const float DungeonSillRise = 0.6f;

		private static void AddDungeon(List<StructurePiece> list)
		{
			Piece(list, "DungeonPortal", StructureStyle.Stone, StructureSize.Building,
				"A dungeon's door carved into a rock face, anchored at its threshold: v0 a pointed portal of three recessed orders, a rune frieze and a gable, octagonal pillars burning at their heads; v1 a square dwarven gate under a rune-cut lintel, its portcullis raised, fire bowls on its pillars. Both open on a stair down into the dark.",
				DungeonPortal, "dungeon");
			Piece(list, "DungeonBarrow", StructureStyle.Stone, StructureSize.Building,
				"A dungeon's door in a turfed bank, anchored at its threshold: v0 a dolmen doorway between facade stones, v1 a kerbed passage grave; a slab door dragged aside and a stair down into the dark.",
				DungeonBarrow, "dungeon");
			Piece(list, "DungeonGate", StructureStyle.Stone, StructureSize.Building,
				"A dungeon's gate built against a great cavern's wall, anchored at its threshold: the portal's two fronts (v0 pointed, v1 square) on a short apron, rough wing walls splaying back into the rock.",
				DungeonGate, "dungeon");
		}

		// ── The fronts ──────────────────────────────────────────────

		/// <summary>The dimensions one front is built to: the builder's and the doorway query's one source.</summary>
		private struct DungeonFront
		{
			/// <summary>Half the door's clear width, and its clear height over the sill.</summary>
			public float Half, Height;
			/// <summary>The wall's thickness at the door (the reveal), metres.</summary>
			public float Reveal;
			/// <summary>The sill over the ground line.</summary>
			public float Sill;
			/// <summary>Level floor inside the door before the stair, the stair's risers and treads, the floor at its foot.</summary>
			public float Landing, Tread, Foot;
			public int Risers;
			/// <summary>The lower vault's soffit over the stair's foot.</summary>
			public float LowVault;

			/// <summary>Where the stair starts and where the passage's back wall's face stands (z).</summary>
			public float StairStart => Reveal + Landing;
			public float Floor => Sill - Risers * StepRise;
			public float BackFace => StairStart + Risers * Tread + Foot;
		}

		private const float PassageWall = 0.5f, BackWall = 0.4f, PassageBottom = -1.28f;

		/// <summary>The front a dungeon piece's variant is built to; false for a piece that is not one.</summary>
		private static bool FrontOf(string id, int variant, out DungeonFront front)
		{
			switch (id)
			{
				case "DungeonPortal":
				case "DungeonGate":
					front = variant == 0
						? new DungeonFront { Half = 1.4f, Height = 3.4f, Reveal = 1.2f, Sill = DungeonSillRise, Landing = 1.5f, Tread = 0.36f, Risers = 9, Foot = 0.9f, LowVault = 2.4f }
						: new DungeonFront { Half = 1.6f, Height = 3.6f, Reveal = 1.2f, Sill = DungeonSillRise, Landing = 1.7f, Tread = 0.36f, Risers = 9, Foot = 0.9f, LowVault = 2.4f };
					return true;
				case "DungeonBarrow":
					front = new DungeonFront { Half = 0.9f, Height = 2.5f, Reveal = 1.0f, Sill = DungeonSillRise, Landing = 1.4f, Tread = 0.36f, Risers = 9, Foot = 0.8f, LowVault = 1.9f };
					return true;
				case "CryptEntrance":
					// Its sill on the ground, its stair six risers down (the crypt is a free-standing piece: origin at its middle).
					front = new DungeonFront { Half = 0.675f, Height = 2.3f, Reveal = 0.8f, Sill = 0f, Landing = 0.8f, Tread = 0.34f, Risers = 6, Foot = 0.7f, LowVault = 1.5f };
					return true;
			}
			front = default;
			return false;
		}

		/// <summary>Where the crypt's doorway stands in its frame (z of its outer face): forward of the middle, the mound behind it.</summary>
		private const float CryptDoorZ = -2.7f;

		/// <summary>
		/// The doorway of a piece's variant in its own frame (<see cref="StructureDoorway"/>): the dungeon entrances and the
		/// crypt's. False for any other piece. Pure: read from the same dimensions the builders use, nothing is built.
		/// </summary>
		public static bool TryDoorwayOf(StructurePiece piece, int variant, out StructureDoorway doorway)
		{
			doorway = default;
			if (piece == null || variant < 0 || variant >= piece.Variants || !FrontOf(piece.Id, variant, out DungeonFront f))
			{
				return false;
			}
			float z0 = piece.Id == "CryptEntrance" ? CryptDoorZ : 0f;
			float top = f.Sill + f.Height, back = z0 + f.BackFace;
			doorway = new StructureDoorway
			{
				Sill = new Vector3(0f, f.Sill, z0),
				Width = 2f * f.Half,
				Height = f.Height,
				// From the inner face of the wall to the back wall, from the stair's foot to the soffit at the door.
				PassageCentre = new Vector3(0f, 0.5f * (f.Floor + top), z0 + 0.5f * (f.Reveal + f.BackFace)),
				PassageHalfSize = new Vector3(f.Half, 0.5f * (top - f.Floor), 0.5f * (f.BackFace - f.Reveal)),
			};
			return back > z0;
		}

		// ── The passage ─────────────────────────────────────────────

		/// <summary>
		/// The way in behind a door whose outer face is at <paramref name="z0"/>: a level landing at the sill (where the open
		/// leaves stand), a stair down to the passage's foot, its walls stone near the door and soot-black (Ash) deeper in, a
		/// vault at the door's head and a lower one over the stair's foot, and a black back wall. Solid to the same depth at
		/// every level: the last level is the collider.
		/// </summary>
		private static void DungeonPassage(StructureDraft d, in DungeonFront f, float z0, StructureMaterial lining, int key)
		{
			float half = f.Half, wall = half + PassageWall, top = f.Sill + f.Height;
			float start = z0 + f.Reveal, stair = z0 + f.StairStart, back = z0 + f.BackFace;
			float black = Mathf.Lerp(stair, back, 0.35f);
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (half + 0.5f * PassageWall);
				d.Add(StructureShapes.BoxBetween(new Vector3(x - 0.5f * PassageWall, PassageBottom, start), new Vector3(x + 0.5f * PassageWall, top + 0.4f, black), lining, key + 1 + s));
				d.Add(StructureShapes.BoxBetween(new Vector3(x - 0.5f * PassageWall, PassageBottom, black), new Vector3(x + 0.5f * PassageWall, top + 0.4f, back + BackWall), StructureMaterial.Ash, key + 4 + s));
			}
			// The vaults: the high one at the door's head, then the drop to the low one over the stair's foot.
			float drop = Mathf.Lerp(stair, back, 0.45f);
			d.Add(StructureShapes.BoxBetween(new Vector3(-wall, top, start), new Vector3(wall, top + 0.4f, drop + 0.2f), lining, key + 7, StructureRole.Roof));
			d.Add(StructureShapes.BoxBetween(new Vector3(-wall, f.Floor + f.LowVault, drop), new Vector3(wall, top + 0.4f, back + BackWall), StructureMaterial.Ash, key + 8, StructureRole.Roof));
			d.Add(StructureShapes.BoxBetween(new Vector3(-wall, PassageBottom, back), new Vector3(wall, top + 0.4f, back + BackWall), StructureMaterial.Ash, key + 9));
			// The floor: the landing, the stair, the foot.
			using (d.Builds(StepRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(-half, PassageBottom, start), new Vector3(half, f.Sill, stair), lining, key + 10));
				for (int i = 0; i < f.Risers; i++)
				{
					float tread = f.Sill - (i + 1) * StepRise;
					float near = stair + i * f.Tread, far = i == f.Risers - 1 ? back : stair + (i + 1) * f.Tread;
					d.Add(StructureShapes.BoxBetween(new Vector3(-half, PassageBottom, near), new Vector3(half, tread, far), i >= f.Risers / 2 ? StructureMaterial.Ash : lining, key + 11 + i));
				}
			}
		}

		/// <summary>
		/// The door's leaves, swung back into the passage against its walls (hinged at the wall's inner face, <paramref name="z"/>):
		/// iron-bound oak on <paramref name="straps"/> straps, studded at the finest level.
		/// </summary>
		private static void OpenLeaves(StructureDraft d, in DungeonFront f, float z, int straps, int key)
		{
			const float thick = 0.12f, open = 84f;
			float leaf = f.Half - 0.04f, height = f.Height - 0.05f;
			for (int s = -1; s <= 1; s += 2)
			{
				// Closed, a leaf runs from its hinge toward the middle (−s along x); open it turns into the passage, nearly flat to the wall.
				float a = open * Mathf.Deg2Rad;
				var along = new Vector3(-s * Mathf.Cos(a), 0f, Mathf.Sin(a));
				var hinge = new Vector3(s * (f.Half - 0.5f * thick - 0.02f), f.Sill + 0.02f, z + 0.02f);
				Quaternion turn = Quaternion.LookRotation(along, Vector3.up);
				Vector3 Leaf(float u, float y, float w) => hinge + along * u + Vector3.up * y + turn * new Vector3(w, 0f, 0f);
				StructureSolid board = d.Add(StructureShapes.Block(Leaf(0.5f * leaf, 0.5f * height, 0f), new Vector3(thick, height, leaf), turn, StructureMaterial.Timber, key + (s + 1) * 10, StructureRole.Detail));
				board.Grain = Vector3.up;
				for (int k = 0; k < straps; k++)
				{
					float y = height * (k + 0.5f) / straps;
					// On the leaf's inner face, which now faces out of the wall into the passage.
					d.Add(StructureShapes.Block(Leaf(0.5f * leaf, y, -s * (0.5f * thick + 0.012f)), new Vector3(0.02f, 0.12f, leaf * 0.94f), turn, StructureMaterial.Iron, key + (s + 1) * 10 + 1 + k, StructureRole.Detail));
					if (d.Fine)
					{
						for (int n = 0; n < 2; n++)
						{
							d.Add(StructureShapes.Box(Leaf(leaf * (0.3f + 0.4f * n), y, -s * (0.5f * thick + 0.03f)), new Vector3(0.05f, 0.05f, 0.05f), StructureMaterial.Iron, key + (s + 1) * 10 + 5 + k * 2 + n, StructureRole.Detail));
						}
					}
				}
			}
		}

		/// <summary>A run of flagstones from <paramref name="z0"/> to <paramref name="z1"/> across ±<paramref name="half"/>: a grid of slabs, fewer at each level, set a hair proud of the ground.</summary>
		private static void Flagstones(StructureDraft d, float half, float z0, float z1, StructureMaterial stone, bool rough, int key)
		{
			int cols = d.Lod == 0 ? 3 : d.Lod == 1 ? 2 : 1, rows = d.Lod == 0 ? 4 : d.Lod == 1 ? 2 : 1;
			float gap = d.Fine ? 0.05f : 0f;
			float w = 2f * half / cols, l = (z1 - z0) / rows;
			using (d.Builds(StepRoles))
			{
				for (int r = 0; r < rows; r++)
				{
					for (int c = 0; c < cols; c++)
					{
						int k = key + r * 8 + c;
						float lift = d.Fine ? d.Range(k, 1, -0.012f, 0.012f) : 0f;
						var centre = new Vector3(-half + (c + 0.5f) * w, -0.11f + lift, z0 + (r + 0.5f) * l);
						var size = new Vector3(w - gap, 0.3f, l - gap);
						if (rough && d.Mid)
						{
							d.Add(StructureShapes.Block(centre, size, Quaternion.Euler(0f, d.Range(k, 2, -6f, 6f), 0f), stone, k));
						}
						else
						{
							d.Add(StructureShapes.Box(centre, size, stone, k));
						}
					}
				}
			}
		}

		/// <summary>A pointed (equilateral, two-centred) arch's curve of half-span <paramref name="a"/> springing at <paramref name="spring"/>: at t = 0 the springing, at 1 the apex.</summary>
		private static Vector2 PointedArch(float a, float spring, float t, float side)
		{
			float theta = t * Mathf.PI / 3f;
			return new Vector2(side * (-a + 2f * a * Mathf.Cos(theta)), spring + 2f * a * Mathf.Sin(theta));
		}

		/// <summary>
		/// One order of a pointed doorway, <paramref name="z0"/>..<paramref name="z1"/> deep: its jambs from the ground to the
		/// springing between the opening's half-width <paramref name="inner"/> and <paramref name="outer"/>, and the voussoirs
		/// between the two arcs (or, with <paramref name="top"/> set, the wall over the arc up to that height).
		/// </summary>
		private static void ArchOrder(StructureDraft d, float inner, float outer, float spring, float z0, float z1, float top, StructureMaterial stone, int key)
		{
			int n = d.Lod == 0 ? 6 : d.Lod == 1 ? 4 : 3;
			for (int s = -1; s <= 1; s += 2)
			{
				int k = key + (s + 1) * 20;
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? inner : -outer, -0.5f, z0), new Vector3(s > 0 ? outer : -inner, spring, z1), stone, k));
				for (int i = 0; i < n; i++)
				{
					Vector2 a0 = PointedArch(inner, spring, (float)i / n, s), a1 = PointedArch(inner, spring, (float)(i + 1) / n, s);
					List<Vector2> outline;
					if (top > 0f)
					{
						outline = new List<Vector2> { a0, a1, new Vector2(a1.x, top), new Vector2(a0.x, top) };
						if (i == 0)
						{
							// The first strip reaches out over the jamb to the order's edge.
							outline = new List<Vector2> { a0, a1, new Vector2(a1.x, top), new Vector2(s * outer, top), new Vector2(s * outer, spring) };
						}
					}
					else
					{
						Vector2 b0 = PointedArch(outer, spring, (float)i / n, s), b1 = PointedArch(outer, spring, (float)(i + 1) / n, s);
						outline = new List<Vector2> { a0, a1, b1, b0 };
					}
					d.Add(StructureShapes.Prism(outline, z0, z1, stone, k + 1 + i));
				}
			}
		}

		/// <summary>
		/// The forecourt before a front: three steps up to the door's plinth, the flagstones out to the forecourt's end (an
		/// apron only, before a cavern's gate), kerbstones along its sides on the open ground.
		/// </summary>
		private static void Forecourt(StructureDraft d, in DungeonFront f, float plinthHalf, bool cavern, StructureMaterial stone, bool rough, int key)
		{
			const float plinthFront = -1.8f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.5f * (f.Sill - 0.5f), 0.5f * (plinthFront + f.Reveal)), new Vector3(2f * plinthHalf, f.Sill + 0.5f, f.Reveal - plinthFront),
				d.Bevel(0.03f), stone, key));
			Steps(d, new Vector3(0f, 0f, plinthFront), Vector3.back, 2f * plinthHalf - 0.4f, f.Sill, stone, key + 1);
			float stepsEnd = plinthFront - (Mathf.CeilToInt(f.Sill / StepRise) - 1) * StepTread;
			float end = cavern ? -4.5f : -DungeonFrontReach;
			float half = cavern ? plinthHalf + 0.6f : 4.2f;
			Flagstones(d, half, end, stepsEnd, stone, rough, key + 10);
			if (!cavern && d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 0; i < 2; i++)
					{
						int k = key + 60 + (s + 1) * 4 + i;
						float z = Mathf.Lerp(end + 1.2f, stepsEnd - 0.6f, i);
						d.Add(RoughStone(d, new Vector3(s * (half + 0.3f), -0.25f, z), new Vector3(0.45f, 0.55f, d.Range(k, 1, 0.9f, 1.4f)), d.Range(k, 2, -8f, 8f), k, 2));
					}
				}
			}
		}

		/// <summary>
		/// The rear mass behind a front: rough stone either side of the passage and over its vault, falling back from the
		/// facade's top toward the passage's vault at the back, into the hill (the ground meets it there; a heightfield cannot run in over a doorway).
		/// </summary>
		private static void RearMass(StructureDraft d, in DungeonFront f, float half, float height, float rear, int key)
		{
			float inner = f.Half + PassageWall, top = f.Sill + f.Height + 0.4f;
			// Down to a little over the passage's vault at the back, so the passage stays covered.
			float low = Mathf.Min(height, top + 0.6f);
			var n = new Vector3(0f, rear - f.Reveal, height - low).normalized;
			float plane = Vector3.Dot(n, new Vector3(0f, height, f.Reveal));
			void Mass(Vector3 min, Vector3 max, int k)
			{
				StructureSolid s = StructureShapes.BoxBetween(min, max, StructureMaterial.Fieldstone, k);
				if (s.Clip(n, plane))
				{
					d.Add(s);
				}
			}
			Mass(new Vector3(-half, -0.5f, f.Reveal), new Vector3(-inner, height, rear), key);
			Mass(new Vector3(inner, -0.5f, f.Reveal), new Vector3(half, height, rear), key + 1);
			Mass(new Vector3(-inner, top, f.Reveal), new Vector3(inner, height, rear), key + 2);
		}

		/// <summary>A fire bowl of iron (or stone) whose coals sit at <paramref name="at"/>: it burns (SceneEmitterKind.Fire).</summary>
		private static void FireBowl(StructureDraft d, Vector3 at, bool stone, int key)
		{
			int seg = d.Seg(10, 6);
			StructureMaterial m = stone ? StructureMaterial.Stone : StructureMaterial.Iron;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.12f, -0.36f), new Vector2(0.4f, -0.08f), new Vector2(0.46f, 0.02f), new Vector2(0.38f, 0.02f), new Vector2(0.1f, -0.22f) },
				seg, m, key, true, 0f, true, false).Transform(Matrix4x4.Translate(at)));
			d.Add(StructureShapes.Cylinder(0.37f, -0.08f, -0.02f, seg, StructureMaterial.Ash, key + 1, true, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
			d.Emit(at, SceneEmitterKind.Fire);
		}

		/// <summary>A row of runes across a face at <paramref name="z"/> (its front), centred on (0, <paramref name="y"/>): rune-light, finest level only.</summary>
		private static void RuneRow(StructureDraft d, float y, float span, int count, float z, int key)
		{
			if (!d.Fine)
			{
				return;
			}
			for (int r = 0; r < count; r++)
			{
				float x = count > 1 ? Mathf.Lerp(-span, span, (float)r / (count - 1)) : 0f;
				Rune(d, new Vector2(x, y), 0.13f, r + key, z, key + 10 * r);
			}
		}

		// ── The pointed portal (v0) ─────────────────────────────────

		private static void PointedFront(StructureDraft d, in DungeonFront f, bool cavern)
		{
			const StructureMaterial stone = StructureMaterial.Stone, carved = StructureMaterial.Monolith;
			const float wallHalf = 4.4f, wallTop = 9f;
			float spring = f.Sill + f.Height;
			float a0 = f.Half, a1 = a0 + 0.4f, a2 = a1 + 0.4f;
			Forecourt(d, f, a2 + 0.5f, cavern, stone, false, 1);
			// Three orders stepping in from the facade's face to the door: the wall and its spandrels, then two rings of voussoirs.
			ArchOrder(d, a2, wallHalf, spring, 0f, 0.35f, wallTop, stone, 100);
			ArchOrder(d, a1, a2 + 0.05f, spring, 0.35f, 0.7f, 0f, carved, 200);
			ArchOrder(d, a0, a1 + 0.05f, spring, 0.7f, f.Reveal, 0f, carved, 300);
			// The wall behind the orders over the arcs, to the facade's top.
			d.Add(StructureShapes.BoxBetween(new Vector3(-a2, PointedArch(a1, spring, 1f, 1f).y, 0.35f), new Vector3(a2, wallTop, f.Reveal), stone, 400));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? a2 : -wallHalf, spring, 0.35f), new Vector3(s > 0 ? wallHalf : -a2, wallTop, f.Reveal), stone, 401 + s));
			}
			// The tympanum: the inner arc's head over the door, carved stone with a rune ring cut in it.
			var head = new List<Vector2>();
			int n = d.Lod == 0 ? 6 : d.Lod == 1 ? 4 : 3;
			for (int i = 0; i <= n; i++)
			{
				head.Add(PointedArch(a0, spring, (float)i / n, 1f));
			}
			for (int i = n - 1; i >= 0; i--)
			{
				head.Add(PointedArch(a0, spring, (float)i / n, -1f));
			}
			d.Add(StructureShapes.Prism(head, 0.75f, f.Reveal, carved, 410));
			RuneRow(d, spring + 0.75f, 0.55f, 3, 0.75f - 0.004f, 420);
			// The rune frieze over the arch, a coping along the top and a gable over the door.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 8.5f, -0.09f), new Vector3(6.2f, 0.7f, 0.22f), d.Bevel(0.025f), carved, 430));
			RuneRow(d, 8.5f, 2.6f, 9, -0.204f, 440);
			d.Emit(new Vector3(0f, 8.5f, -0.6f), SceneEmitterKind.Glow);
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, wallTop + 0.15f, 0.5f * (f.Reveal - 0.25f)), new Vector3(2f * wallHalf + 0.3f, 0.3f, f.Reveal + 0.25f), d.Bevel(0.03f), stone, 450));
			d.Add(StructureShapes.Prism(new[] { new Vector2(-3.4f, wallTop + 0.3f), new Vector2(3.4f, wallTop + 0.3f), new Vector2(0f, wallTop + 2.1f) }, 0.05f, f.Reveal, stone, 451));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, wallTop + 2f), new Vector2(0f, wallTop + 2.7f) }, 4, carved, 452, false, Mathf.PI * 0.25f));
			}
			// The pillars: octagonal shafts on square bases before the wall, a fire bowl on each capital.
			for (int s = -1; s <= 1; s += 2)
			{
				int k = 500 + (s + 1) * 10;
				var at = new Vector3(s * 3.45f, 0f, -0.55f);
				d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 1.1f, 1.1f), d.Bevel(0.03f), stone, k));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.42f, 0.6f), new Vector2(0.36f, 0.8f), new Vector2(0.33f, 6.7f), new Vector2(0.46f, 6.95f), new Vector2(0f, 6.95f) },
					d.Seg(8, 6), carved, k + 1, false, Mathf.PI / 8f).Transform(Matrix4x4.Translate(at)));
				d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, 7.1f, 0f), new Vector3(1.05f, 0.3f, 1.05f), d.Bevel(0.025f), stone, k + 2));
				FireBowl(d, at + new Vector3(0f, 7.62f, 0f), false, k + 3);
			}
			OpenLeaves(d, f, f.Reveal, 3, 600);
			DungeonPassage(d, f, 0f, stone, 700);
			RearMass(d, f, wallHalf, wallTop, DungeonRearReach, 800);
			if (cavern)
			{
				Wings(d, wallHalf, wallTop - 1f, 900);
			}
		}

		// ── The square dwarven gate (v1) ────────────────────────────

		private static void SquareFront(StructureDraft d, in DungeonFront f, bool cavern)
		{
			const StructureMaterial stone = StructureMaterial.Stone, carved = StructureMaterial.Monolith;
			const float wallHalf = 4.4f, wallTop = 7.6f, outerBand = 0.5f;
			float p = f.Half, q = p + 0.5f, head = f.Sill + f.Height, lintel = head + 0.6f;
			Forecourt(d, f, q + 0.8f, cavern, stone, false, 1);
			// The outer frame at the face, the inner jambs and head behind it, corbels stepping the head in.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? q : -wallHalf, -0.5f, 0f), new Vector3(s > 0 ? wallHalf : -q, wallTop, f.Reveal), stone, 100 + s));
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? p : -q, -0.5f, outerBand), new Vector3(s > 0 ? q : -p, head, f.Reveal), carved, 110 + s));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (p + 0.25f), head + 0.3f, 0.25f), new Vector3(0.5f, 0.6f, 0.5f), d.Bevel(0.03f), carved, 120 + s));
				// A battered footing course either side of the door's plinth.
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 3.75f, 0.2f, -0.15f), new Vector3(1.3f, 1.4f, 0.3f), d.Bevel(0.03f), stone, 130 + s));
			}
			d.Add(StructureShapes.BoxBetween(new Vector3(-q, head, outerBand), new Vector3(q, lintel, f.Reveal), carved, 140));
			// The lintel: one great rune-cut slab, proud of the wall; the wall over it; a cornice and a stepped attic.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, lintel + 0.55f, 0.125f), new Vector3(5.8f, 1.1f, 0.75f), d.Bevel(0.04f), carved, 150));
			RuneRow(d, lintel + 0.55f, 2.3f, 8, -0.254f, 160);
			d.Emit(new Vector3(0f, lintel + 0.55f, -0.7f), SceneEmitterKind.Glow);
			d.Add(StructureShapes.BoxBetween(new Vector3(-q, lintel, 0.25f), new Vector3(q, wallTop, f.Reveal), stone, 170));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, wallTop + 0.2f, 0.5f * (f.Reveal - 0.3f)), new Vector3(2f * wallHalf + 0.4f, 0.4f, f.Reveal + 0.3f), d.Bevel(0.03f), stone, 171));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, wallTop + 0.8f, 0.5f * f.Reveal), new Vector3(6f, 0.8f, f.Reveal), d.Bevel(0.03f), stone, 172));
			// The portcullis, raised into its slot: only its spiked foot shows under the head.
			int bars = d.Lod == 0 ? 7 : d.Lod == 1 ? 5 : 4;
			float shown = 0.75f, slot = outerBand + 0.15f;
			for (int i = 0; i < bars; i++)
			{
				float x = Mathf.Lerp(-p + 0.12f, p - 0.12f, (float)i / (bars - 1));
				d.Add(StructureShapes.Box(new Vector3(x, head - 0.5f * shown, slot), new Vector3(0.07f, shown, 0.08f), StructureMaterial.Iron, 180 + i, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0f, -0.14f) }, 4, StructureMaterial.Iron, 190 + i, false)
						.Transform(Matrix4x4.Translate(new Vector3(x, head - shown, slot))));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, head - 0.25f * shown, slot + 0.06f), new Vector3(2f * p - 0.1f, 0.06f, 0.04f), StructureMaterial.Iron, 199, StructureRole.Detail));
			// The pillars: squared, with stepped capitals and a stone fire bowl on each.
			for (int s = -1; s <= 1; s += 2)
			{
				int k = 500 + (s + 1) * 10;
				var at = new Vector3(s * 3.5f, 0f, -0.45f);
				d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, 3.05f, 0f), new Vector3(1.2f, 7.1f, 1.2f), d.Bevel(0.04f), carved, k));
				d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, 6.78f, 0f), new Vector3(1.5f, 0.35f, 1.5f), d.Bevel(0.03f), stone, k + 1));
				d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, 7.1f, 0f), new Vector3(1.7f, 0.3f, 1.7f), d.Bevel(0.03f), stone, k + 2));
				FireBowl(d, at + new Vector3(0f, 7.62f, 0f), true, k + 3);
			}
			OpenLeaves(d, f, f.Reveal, 4, 600);
			DungeonPassage(d, f, 0f, stone, 700);
			RearMass(d, f, wallHalf, wallTop, DungeonRearReach, 800);
			if (cavern)
			{
				Wings(d, wallHalf, wallTop, 900);
			}
		}

		/// <summary>A cavern gate's wing walls: rough masonry splaying back from the facade's ends into the rock it is built against.</summary>
		private static void Wings(StructureDraft d, float wallHalf, float height, int key)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				var centre = new Vector3(s * (wallHalf + 0.35f), 0.5f * (height - 0.5f), 1.6f);
				d.Add(StructureShapes.Block(centre, new Vector3(1.0f, height + 0.5f, 3.6f), Quaternion.Euler(0f, s * 22f, 0f), StructureMaterial.Fieldstone, key + s, StructureRole.Structure, d.Bevel(0.05f)));
			}
		}

		private static void DungeonPortal(StructureDraft d)
		{
			FrontOf("DungeonPortal", d.Variant, out DungeonFront f);
			if (d.Variant == 0) PointedFront(d, f, false); else SquareFront(d, f, false);
		}

		private static void DungeonGate(StructureDraft d)
		{
			FrontOf("DungeonGate", d.Variant, out DungeonFront f);
			if (d.Variant == 0) PointedFront(d, f, true); else SquareFront(d, f, true);
		}

		// ── The barrow (DungeonBarrow) ──────────────────────────────

		private static void DungeonBarrow(StructureDraft d)
		{
			FrontOf("DungeonBarrow", d.Variant, out DungeonFront f);
			bool kerbed = d.Variant == 1;
			float p = f.Half, top = f.Sill + f.Height;
			// The forecourt: rough flags and fieldstone steps up to a rough sill; the uprights and capstone of the door.
			Forecourt(d, f, p + 0.9f, false, StructureMaterial.Fieldstone, true, 1);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(RoughStone(d, new Vector3(s * (p + 0.32f), -0.5f, 0.5f * f.Reveal), new Vector3(0.62f, top + 0.5f, f.Reveal + 0.2f), 0f, 100 + s, 2, StructureMaterial.Monolith));
			}
			d.Add(RoughStone(d, new Vector3(0f, top - 0.02f, 0.5f * f.Reveal), new Vector3(2f * p + 1.8f, 0.75f, f.Reveal + 0.6f), 0f, 105, 3, StructureMaterial.Monolith));
			if (kerbed)
			{
				// A second capstone stacked behind the first, and the kerb: stones on edge in an arc round the mound's foot.
				d.Add(RoughStone(d, new Vector3(0f, top + 0.6f, 0.5f * f.Reveal + 0.5f), new Vector3(2f * p + 1.2f, 0.6f, 1.2f), 0f, 106, 2, StructureMaterial.Monolith));
				int stones = d.Lod == 0 ? 6 : d.Lod == 1 ? 4 : 3;
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 0; i < stones; i++)
					{
						int k = 120 + (s + 1) * 10 + i;
						float a = Mathf.Lerp(24f, 86f, (float)i / (stones - 1)) * Mathf.Deg2Rad;
						var at = new Vector3(s * 4.3f * Mathf.Sin(a), -0.4f, 3.6f - 4.3f * Mathf.Cos(a));
						d.Add(RoughStone(d, at, new Vector3(d.Range(k, 1, 0.9f, 1.3f), d.Range(k, 2, 1.1f, 1.6f), 0.5f), -s * a * Mathf.Rad2Deg, k, 2));
					}
					// A horn stone standing forward at each end of the facade.
					d.Add(RoughStone(d, new Vector3(s * 2.6f, -0.45f, -1.0f), new Vector3(0.7f, 2.4f, 0.55f), s * 25f, 140 + s, 3, StructureMaterial.Monolith));
				}
			}
			else
			{
				// Facade stones either side of the door, in line with it, falling in height outward.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(RoughStone(d, new Vector3(s * (p + 1.25f), -0.45f, 0.35f), new Vector3(1.2f, 2.9f, 0.6f), 0f, 120 + s, 2, StructureMaterial.Monolith));
					d.Add(RoughStone(d, new Vector3(s * (p + 2.45f), -0.45f, 0.55f), new Vector3(1.0f, 2.1f, 0.6f), s * 6f, 130 + s, 2, StructureMaterial.Monolith));
				}
			}
			// The slab that shut it, dragged aside and leaning on the passage wall.
			d.Add(StructureShapes.Block(new Vector3(-p + 0.14f, f.Sill + 1.1f, f.Reveal + 0.8f), new Vector3(0.16f, 2.2f, 1.3f), Quaternion.Euler(0f, 0f, -5f),
				StructureMaterial.Monolith, 150, StructureRole.Detail));
			DungeonPassage(d, f, 0f, StructureMaterial.Fieldstone, 700);
			// The turfed bank: one mound split round the passage (left, right and over its vault), so the way in stays open.
			Mound(d, new Vector3(0f, -1.0f, 3.8f), new Vector3(5.2f, top + 1.6f, 3.9f), p + PassageWall, top + 0.4f, 0f, 800);
		}

		/// <summary>
		/// An earth mound split round a passage: the ellipsoid's parts beyond ±<paramref name="split"/> either side, and over
		/// <paramref name="roof"/> between them (forward of <paramref name="front"/>), each closed by its cut, so the passage
		/// running in through it is not filled.
		/// </summary>
		private static void Mound(StructureDraft d, Vector3 centre, Vector3 radii, float split, float roof, float front, int key)
		{
			int seg = d.Seg(14, 8), rings = d.Fine ? 8 : 5;
			StructureSolid Part(int k)
			{
				StructureSolid s = StructureShapes.Sphere(centre, radii, seg, rings, StructureMaterial.Earth, k);
				s.Clip(Vector3.down, 0.5f);
				return s;
			}
			StructureSolid left = Part(key);
			if (left.Clip(Vector3.right, -split))
			{
				d.Add(left);
			}
			StructureSolid right = Part(key + 1);
			if (right.Clip(Vector3.left, -split))
			{
				d.Add(right);
			}
			StructureSolid over = Part(key + 2);
			if (over.Clip(Vector3.right, split) && over.Clip(Vector3.left, split) && over.Clip(Vector3.down, -roof) && over.Clip(Vector3.back, -front))
			{
				d.Add(over);
			}
		}
	}
}
#endif
