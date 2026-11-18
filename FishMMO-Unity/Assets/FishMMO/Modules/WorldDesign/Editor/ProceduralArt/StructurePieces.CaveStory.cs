#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What others left in the caves and mines (2026-10-20, caves livened: "story props"): the remains of those who never came
	/// out (an adventurer slumped against the rock with a rusted sword across the knees, one fallen face down reaching for a
	/// sword dropped out of reach, a miner by his pick), carvings cut and pictures drawn on smooth walls by those who were
	/// there long before, the snapped timbers of a drift that fell in, and a strongbox hidden in a nook.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Laid by id, never drawn.</b> The pieces carry only the "cave" (and "mine") tags, which no template's query names: a
	/// "bones" or "rubble" tag would put them in every churchyard's and ruin's draw and move what those sites lay.
	/// PointOfInterestCaveDressing.Story.cs lays them.
	/// </para>
	/// <para>
	/// <b>Nothing glows, nothing collides.</b> A dead man's lantern is cold: its pane is soot (Ash), not lamp glass, and no piece
	/// gives off light. None has a collider: the bones and the gear are stepped over, a carving is flush with its wall, the fallen
	/// timbers lie on rubble that collides by itself, and the coffer encloses the lootable chest the cache spawns, whose own box
	/// is what a player's aim must reach.
	/// </para>
	/// <para>
	/// <b>Frames.</b> Remains and timbers stand on the ground (y = 0 the floor): the seated remains have their back to +z, the
	/// wall; the sprawled ones lie along z, the head toward −z. A carving is a wall piece: its origin on the wall's surface at the
	/// bottom of its face, +z into the rock (a root of stone 0.4 m deep behind its face hides an uneven wall), the face looking
	/// down −z. The coffer stands on the ground, its lock at −z and its back (+z) to the wall.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The cache coffer's inside: the lootable chest it is laid round (0.9 × 0.6 × 0.6 m, Dungeon Chest) fits in it.</summary>
		public static readonly Vector3 CacheCofferInside = new Vector3(0.9f, 0.6f, 0.6f);

		/// <summary>How far a carving's face (x across, y up) reaches, by variant: what the dressing measures the wall for.</summary>
		public static readonly Vector2[] CaveCarvingFaces = { new Vector2(2.4f, 1.2f), new Vector2(1.0f, 1.9f), new Vector2(2.0f, 1.3f) };

		/// <summary>How deep a carving's root reaches into the rock behind its face, metres.</summary>
		public const float CaveCarvingRoot = 0.4f;

		private static void AddCaveStory(List<StructurePiece> list)
		{
			StructurePiece remains = Piece(list, "LostRemains", StructureStyle.Earth, StructureSize.Medium,
				"The remains of one who never came out: v0 an adventurer slumped against the rock, a rusted sword across the knees, a helm and a cold lantern beside; v1 fallen face down, reaching for a sword dropped out of reach, a round shield and a spilled satchel; v2 a miner by his pick, his lamp tipped over.",
				LostRemains, "cave");
			remains.Variants = 3;
			remains.Collides = false;
			StructurePiece carving = Piece(list, "CaveCarving", StructureStyle.Stone, StructureSize.Medium,
				"Old work on a smooth cave wall: v0 a frieze of hunters and beasts under a sun; v1 a stele of runes round a sun-wheel cut in the rock; v2 hands and beasts drawn in ochre and charcoal on a pale patch of flowstone.",
				CaveCarving, "cave");
			carving.Variants = 3;
			carving.Collides = false;
			Piece(list, "CaveInTimbers", StructureStyle.Timber, StructureSize.Medium,
				"A drift's timber set where the roof came down: v0 the set broken across the way, a post snapped, the cap fallen aslant; v1 a snapped post and a split cap thrown against the wall.",
				CaveInTimbers, "mine", "cave").Collides = false;
			Piece(list, "CacheCoffer", StructureStyle.Timber, StructureSize.Small,
				"A strongbox hidden in a nook: v0 an iron-bound coffer with a rounded lid and a hasp; v1 a squat flat-lidded box with iron corners.",
				CacheCoffer, "cave").Collides = false;
		}

		// ── The remains ───────────────────────────────────────────────

		private static void LostRemains(StructureDraft d)
		{
			switch (d.Variant)
			{
				case 0:
					SeatedRemains(d);
					break;
				case 1:
					FallenRemains(d, miner: false);
					break;
				default:
					FallenRemains(d, miner: true);
					break;
			}
		}

		/// <summary>
		/// Slumped against the wall (+z): the pelvis on the floor, the spine leant back on the rock, the skull dropped to one
		/// shoulder, the legs out before it, the hands in the lap over a rusted sword; a helm rolled to one side, a cold lantern
		/// and a pack at the other.
		/// </summary>
		private static void SeatedRemains(StructureDraft d)
		{
			var pelvis = new Vector3(0f, 0.09f, 0.1f);
			var neck = new Vector3(0.02f, 0.56f, 0.24f);
			Pelvis(d, pelvis, Quaternion.Euler(-20f, 0f, 0f), 10);
			Spine(d, pelvis + new Vector3(0f, 0.04f, 0.02f), neck, 20);
			Ribcage(d, pelvis + new Vector3(0f, 0.2f, 0.06f), neck, Vector3.back, 30);
			Skull(d, new Vector3(0.1f, 0.6f, 0.17f), Quaternion.Euler(38f, -20f, 24f), 60);
			// The legs out before it, one knee fallen outward.
			for (int s = -1; s <= 1; s += 2)
			{
				var hip = new Vector3(s * 0.09f, 0.08f, 0.06f);
				var knee = new Vector3(s * (s > 0 ? 0.2f : 0.13f), s > 0 ? 0.07f : 0.15f, -0.34f);
				var ankle = new Vector3(s * (s > 0 ? 0.26f : 0.16f), 0.04f, -0.72f);
				d.Add(LongBone(d, hip, knee, 0.026f, 70 + s));
				d.Add(LongBone(d, knee, ankle, 0.022f, 72 + s));
				Foot(d, ankle, s > 0 ? 30f : -10f, 74 + s);
			}
			// The arms down the sides, the hands in the lap.
			for (int s = -1; s <= 1; s += 2)
			{
				var shoulder = new Vector3(s * 0.17f, 0.5f, 0.22f);
				var elbow = new Vector3(s * 0.21f, 0.2f, 0.08f);
				var hand = new Vector3(s * 0.12f, 0.15f, -0.16f);
				d.Add(LongBone(d, shoulder, elbow, 0.02f, 80 + s));
				d.Add(LongBone(d, elbow, hand, 0.017f, 82 + s));
			}
			Sword(d, new Vector3(0.02f, 0.18f, -0.22f), Quaternion.Euler(0f, 6f, 4f), 100);
			Helm(d, new Vector3(0.42f, 0.11f, -0.06f), Quaternion.Euler(0f, 30f, 72f), 120);
			if (d.Mid)
			{
				ColdLantern(d, new Vector3(-0.4f, 0f, -0.08f), Quaternion.Euler(0f, 20f, 0f), 140);
				// A pack against the wall at its side.
				d.Add(StructureShapes.Sphere(new Vector3(-0.3f, 0.16f, 0.24f), new Vector3(0.16f, 0.17f, 0.12f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 150));
			}
		}

		/// <summary>
		/// Fallen face down along z, the head toward −z: one arm reaching for the sword dropped beyond it; a round shield, a
		/// spilled satchel and a lantern on its side about it. A miner instead: his pick fallen beside him, a lamp tipped over, a
		/// sack of ore.
		/// </summary>
		private static void FallenRemains(StructureDraft d, bool miner)
		{
			var pelvis = new Vector3(0.02f, 0.07f, 0.02f);
			var neck = new Vector3(-0.02f, 0.08f, -0.56f);
			Pelvis(d, pelvis, Quaternion.Euler(-82f, 8f, 0f), 10);
			Spine(d, pelvis, neck, 20);
			Ribcage(d, pelvis + new Vector3(0f, 0.01f, -0.16f), neck, Vector3.down, 30);
			Skull(d, new Vector3(0.04f, 0.085f, -0.78f), Quaternion.Euler(-10f, 25f, 80f), 60);
			// The legs out behind, one drawn up a little.
			for (int s = -1; s <= 1; s += 2)
			{
				var hip = new Vector3(s * 0.09f, 0.06f, 0.06f);
				var knee = new Vector3(s * (s > 0 ? 0.22f : 0.11f), 0.05f, s > 0 ? 0.42f : 0.48f);
				var ankle = new Vector3(s * (s > 0 ? 0.16f : 0.13f), 0.04f, s > 0 ? 0.8f : 0.9f);
				d.Add(LongBone(d, hip, knee, 0.026f, 70 + s));
				d.Add(LongBone(d, knee, ankle, 0.022f, 72 + s));
				Foot(d, ankle, s > 0 ? 160f : 185f, 74 + s);
			}
			// The right arm reaching out ahead, the left fallen under the chest.
			d.Add(LongBone(d, new Vector3(0.17f, 0.06f, -0.5f), new Vector3(0.36f, 0.05f, -0.76f), 0.02f, 81));
			d.Add(LongBone(d, new Vector3(0.36f, 0.05f, -0.76f), new Vector3(0.5f, 0.04f, -1.02f), 0.017f, 83));
			d.Add(LongBone(d, new Vector3(-0.17f, 0.06f, -0.5f), new Vector3(-0.22f, 0.05f, -0.24f), 0.02f, 79));
			d.Add(LongBone(d, new Vector3(-0.22f, 0.05f, -0.24f), new Vector3(-0.06f, 0.04f, -0.32f), 0.017f, 82));
			if (miner)
			{
				Pick(d, new Vector3(-0.5f, 0.04f, -0.5f), Quaternion.Euler(0f, -28f, 0f), 100);
				if (d.Mid)
				{
					ColdLantern(d, new Vector3(0.48f, 0.07f, 0.22f), Quaternion.Euler(0f, 40f, 90f), 140, small: true);
					// A sack of ore, tied, a lump or two out of its neck.
					d.Add(StructureShapes.Sphere(new Vector3(-0.42f, 0.15f, 0.42f), new Vector3(0.2f, 0.15f, 0.17f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 150));
					d.Add(StructureShapes.Sphere(new Vector3(-0.2f, 0.05f, 0.5f), new Vector3(0.07f, 0.05f, 0.06f), 6, 3, StructureMaterial.Fieldstone, 151, StructureRole.Detail));
				}
				return;
			}
			Sword(d, new Vector3(0.62f, 0.02f, -1.28f), Quaternion.Euler(0f, 64f, 0f), 100);
			Shield(d, new Vector3(-0.52f, 0.03f, -0.2f), Quaternion.Euler(0f, 0f, 8f), 120);
			if (d.Mid)
			{
				ColdLantern(d, new Vector3(0.48f, 0.07f, 0.3f), Quaternion.Euler(0f, 20f, 90f), 140);
				// The satchel, its flap open, what it held spilled before it.
				d.Add(StructureShapes.Sphere(new Vector3(0.36f, 0.08f, -0.18f), new Vector3(0.15f, 0.08f, 0.12f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 150));
				if (d.Fine)
				{
					for (int i = 0; i < 3; i++)
					{
						var at = new Vector3(0.42f + d.Range(160 + i, 1, -0.12f, 0.16f), 0.006f, -0.36f + d.Range(160 + i, 2, -0.1f, 0.08f));
						d.Add(StructureShapes.Cylinder(0.018f, 0f, 0.006f, 6, StructureMaterial.Iron, 160 + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
					}
				}
			}
		}

		/// <summary>A long bone from <paramref name="a"/> to <paramref name="b"/>: a shaft swelling to its knuckled ends.</summary>
		private static StructureSolid LongBone(StructureDraft d, Vector3 a, Vector3 b, float r, int key)
		{
			float length = Mathf.Max(0.02f, (b - a).magnitude);
			var profile = new List<Vector2> { new Vector2(r * 0.6f, 0f), new Vector2(r, Mathf.Min(0.025f, 0.2f * length)) };
			if (d.Mid)
			{
				profile.Add(new Vector2(r * 0.55f, 0.22f * length));
				profile.Add(new Vector2(r * 0.55f, 0.78f * length));
			}
			profile.Add(new Vector2(r, length - Mathf.Min(0.025f, 0.2f * length)));
			profile.Add(new Vector2(r * 0.6f, length));
			StructureSolid bone = StructureShapes.Lathe(profile, d.Seg(6, 4), StructureMaterial.Bone, key, true, 0f, false, true);
			return bone.Transform(Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, b - a), Vector3.one));
		}

		/// <summary>A skull: the cranium, the jaw under its face (its face toward −z before <paramref name="turn"/>), dark sockets.</summary>
		private static void Skull(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one);
			d.Add(StructureShapes.Sphere(Vector3.zero, new Vector3(0.075f, 0.085f, 0.1f), d.Seg(8, 5), d.Fine ? 5 : 4, StructureMaterial.Bone, key).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(0f, -0.07f, -0.045f), new Vector3(0.08f, 0.045f, 0.075f), StructureMaterial.Bone, key + 1).Transform(m));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 0.03f, -0.005f, -0.088f), new Vector3(0.022f, 0.02f, 0.016f), 6, 3, StructureMaterial.Ash, key + 2 + (s > 0 ? 1 : 0), StructureRole.Detail).Transform(m));
				}
			}
		}

		/// <summary>The spine, a run of vertebrae from the pelvis to the neck.</summary>
		private static void Spine(StructureDraft d, Vector3 from, Vector3 to, int key)
		{
			int bones = d.Fine ? 6 : d.Mid ? 5 : 3;
			Vector3 dir = (to - from).normalized;
			Quaternion turn = Quaternion.FromToRotation(Vector3.up, dir);
			for (int i = 0; i < bones; i++)
			{
				Vector3 at = Vector3.Lerp(from, to, (i + 0.5f) / bones);
				float size = Mathf.Lerp(0.05f, 0.035f, i / (float)bones);
				d.Add(StructureShapes.Block(at, new Vector3(size, 0.6f * Vector3.Distance(from, to) / bones, size * 0.9f), turn, StructureMaterial.Bone, key + i));
			}
		}

		/// <summary>
		/// The ribs: pairs curving round from the spine to the breastbone's side, <paramref name="front"/> the way the chest faced
		/// (toward which they curve).
		/// </summary>
		private static void Ribcage(StructureDraft d, Vector3 low, Vector3 high, Vector3 front, int key)
		{
			int pairs = d.Fine ? 4 : d.Mid ? 3 : 2;
			Vector3 up = (high - low).normalized;
			Vector3 side = Vector3.Cross(up, front).normalized;
			if (side.sqrMagnitude < 1e-4f)
			{
				side = Vector3.right;
			}
			Vector3 ahead = Vector3.Cross(side, up).normalized;
			if (Vector3.Dot(ahead, front) < 0f)
			{
				ahead = -ahead;
			}
			int seg = d.Seg(5, 3);
			for (int i = 0; i < pairs; i++)
			{
				Vector3 root = Vector3.Lerp(low, high, 0.15f + 0.7f * i / Mathf.Max(1, pairs - 1));
				float reach = Mathf.Lerp(0.1f, 0.13f, Mathf.Sin(Mathf.PI * i / Mathf.Max(1, pairs - 1)));
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 a = root + side * (s * 0.025f);
					Vector3 b = root + side * (s * reach) + ahead * (0.05f) - up * 0.025f;
					Vector3 c = root + side * (s * reach * 0.75f) + ahead * (0.14f) - up * 0.06f;
					d.Add(StructureShapes.Rod(a, b, 0.011f, seg, StructureMaterial.Bone, key + i * 4 + (s > 0 ? 0 : 2)));
					if (d.Mid)
					{
						d.Add(StructureShapes.Rod(b, c, 0.01f, seg, StructureMaterial.Bone, key + i * 4 + (s > 0 ? 1 : 3)));
					}
				}
			}
		}

		/// <summary>The pelvis: a broad flattened bowl of bone.</summary>
		private static void Pelvis(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			d.Add(StructureShapes.Sphere(Vector3.zero, new Vector3(0.14f, 0.09f, 0.06f), d.Seg(8, 5), 4, StructureMaterial.Bone, key).Transform(Matrix4x4.TRS(at, turn, Vector3.one)));
		}

		/// <summary>The bones of a foot, a flat wedge, turned <paramref name="yaw"/> about +y.</summary>
		private static void Foot(StructureDraft d, Vector3 ankle, float yaw, int key)
		{
			if (!d.Mid)
			{
				return;
			}
			Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
			d.Add(StructureShapes.Block(ankle + turn * new Vector3(0f, -0.015f, -0.08f), new Vector3(0.07f, 0.03f, 0.17f), turn, StructureMaterial.Bone, key));
		}

		/// <summary>
		/// A sword, its blade along +x from the guard, lying flat (y up): rusted iron, rust bloomed on the blade, the grip's
		/// leather long gone to a bare tang.
		/// </summary>
		private static void Sword(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one);
			d.Add(StructureShapes.Prism(new[] { new Vector2(-0.38f, -0.025f), new Vector2(0.36f, -0.022f), new Vector2(0.44f, 0f), new Vector2(0.36f, 0.022f), new Vector2(-0.38f, 0.025f) },
				-0.005f, 0.005f, StructureMaterial.Iron, key).Transform(m * Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f))));
			d.Add(StructureShapes.Box(new Vector3(-0.4f, 0f, 0f), new Vector3(0.03f, 0.022f, 0.19f), StructureMaterial.Iron, key + 1).Transform(m));
			d.Add(StructureShapes.Rod(new Vector3(-0.41f, 0f, 0f), new Vector3(-0.54f, 0f, 0f), 0.014f, d.Seg(6, 4), StructureMaterial.Iron, key + 2).Transform(m));
			d.Add(StructureShapes.Sphere(new Vector3(-0.565f, 0f, 0f), new Vector3(0.026f, 0.026f, 0.026f), 6, 3, StructureMaterial.Iron, key + 3).Transform(m));
			if (d.Fine)
			{
				for (int i = 0; i < 3; i++)
				{
					float x = -0.25f + 0.22f * i + d.Range(key + 4 + i, 1, -0.05f, 0.05f);
					d.Add(StructureShapes.Box(new Vector3(x, 0.006f, d.Range(key + 4 + i, 2, -0.01f, 0.01f)), new Vector3(d.Range(key + 4 + i, 3, 0.04f, 0.09f), 0.004f, 0.034f),
						StructureMaterial.Earth, key + 4 + i, StructureRole.Detail).Transform(m));
				}
			}
		}

		/// <summary>An iron cap of a helm with its nasal, its crown up +y before <paramref name="turn"/>.</summary>
		private static void Helm(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			var profile = new[] { new Vector2(0.115f, 0f), new Vector2(0.12f, 0.045f), new Vector2(0.105f, 0.11f), new Vector2(0.06f, 0.155f), new Vector2(0f, 0.168f) };
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one);
			d.Add(StructureShapes.Lathe(profile, d.Seg(10, 6), StructureMaterial.Iron, key).Transform(m));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.04f, -0.118f), new Vector3(0.025f, 0.1f, 0.012f), StructureMaterial.Iron, key + 1, StructureRole.Detail).Transform(m));
			}
		}

		/// <summary>A round shield of boards with an iron boss, lying face up.</summary>
		private static void Shield(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one);
			d.Add(StructureShapes.Cylinder(0.3f, -0.012f, 0.012f, d.Seg(14, 8), StructureMaterial.Timber, key).Transform(m));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 0.015f, 0f), new Vector3(0.07f, 0.05f, 0.07f), d.Seg(8, 5), 3, StructureMaterial.Iron, key + 1).Transform(m));
		}

		/// <summary>A lantern long gone out: iron base, posts and cap, its panes black with soot; a small one is a miner's lamp.</summary>
		private static void ColdLantern(StructureDraft d, Vector3 at, Quaternion turn, int key, bool small = false)
		{
			float k = small ? 0.75f : 1f;
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one * k);
			d.Add(StructureShapes.Box(new Vector3(0f, 0.015f, 0f), new Vector3(0.13f, 0.03f, 0.13f), StructureMaterial.Iron, key).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.14f, 0.1f), StructureMaterial.Ash, key + 1).Transform(m));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.075f, 0.17f), new Vector2(0.075f, 0.185f), new Vector2(0f, 0.24f) }, d.Seg(6, 4), StructureMaterial.Iron, key + 2, false).Transform(m));
			if (d.Fine)
			{
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(sx * 0.055f, 0.1f, sz * 0.055f), new Vector3(0.014f, 0.15f, 0.014f), StructureMaterial.Iron, key + 3 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0), StructureRole.Detail).Transform(m));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.27f, 0f), new Vector3(0.07f, 0.012f, 0.012f), StructureMaterial.Iron, key + 8, StructureRole.Detail).Transform(m));
			}
		}

		/// <summary>A miner's pick lying flat: an ash haft along +x, the iron head across its end, both points curved.</summary>
		private static void Pick(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(at, turn, Vector3.one);
			d.Add(StructureShapes.Rod(new Vector3(-0.42f, 0.02f, 0f), new Vector3(0.42f, 0.02f, 0f), 0.02f, d.Seg(6, 4), StructureMaterial.Timber, key, StructureRole.Structure).Transform(m));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Block(new Vector3(0.44f, 0.03f, s * 0.13f), new Vector3(0.04f, 0.035f, 0.26f), Quaternion.Euler(0f, s * 12f, 0f), StructureMaterial.Iron, key + 1 + (s > 0 ? 1 : 0)).Transform(m));
			}
		}

		// ── The carvings ──────────────────────────────────────────────

		private static void CaveCarving(StructureDraft d)
		{
			Vector2 face = CaveCarvingFaces[Mathf.Clamp(d.Variant, 0, CaveCarvingFaces.Length - 1)];
			// The worked face: an irregular patch of smoothed rock, its root deep in the wall behind it.
			int points = d.Fine ? 18 : d.Mid ? 12 : 8;
			var outline = new Vector2[points];
			for (int i = 0; i < points; i++)
			{
				float a = 2f * Mathf.PI * i / points;
				// A rectangle's outline with its corners worn round and its edge broken.
				float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
				float square = 1f / Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy));
				float r = Mathf.Lerp(1f, square, d.Variant == 1 ? 0.8f : 0.55f) * d.Range(i, 7, 0.95f, 1.01f);
				outline[i] = new Vector2(cx * r * face.x * 0.5f, face.y * 0.5f + cy * r * face.y * 0.5f);
			}
			if (d.Variant == 1)
			{
				// A stele's face: square below, its top arched.
				for (int i = 0; i < points; i++)
				{
					Vector2 p = outline[i];
					float a = 2f * Mathf.PI * i / points;
					if (Mathf.Sin(a) > 0.2f)
					{
						float t = Mathf.Cos(a);
						p = new Vector2(t * face.x * 0.5f, face.y - 0.5f * face.x * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t))) * 0.6f);
					}
					outline[i] = p;
				}
			}
			d.Add(StructureShapes.Prism(outline, -0.04f, CaveCarvingRoot, StructureMaterial.Monolith, 1));
			switch (d.Variant)
			{
				case 0:
					Frieze(d, face);
					break;
				case 1:
					RuneStele(d, face);
					break;
				default:
					Paintings(d, face);
					break;
			}
		}

		/// <summary>The face's front, where relief stands out from: just proud of the patch's own face (z −0.04).</summary>
		private const float ReliefZ = -0.055f, ReliefDepth = 0.03f;

		/// <summary>A frieze: a cut border, a sun and a spiral, and a procession of hunters driving a stag and a boar.</summary>
		private static void Frieze(StructureDraft d, Vector2 face)
		{
			float w = face.x * 0.5f - 0.18f, top = face.y - 0.14f, low = 0.14f;
			// The border: a groove-edged band along the top and the bottom.
			d.Add(Wares.Stroke(new Vector3(0f, 0f, ReliefZ), new Vector2(-w, top), new Vector2(w, top), 0.035f, ReliefDepth, StructureMaterial.Monolith, 10));
			d.Add(Wares.Stroke(new Vector3(0f, 0f, ReliefZ), new Vector2(-w, low), new Vector2(w, low), 0.035f, ReliefDepth, StructureMaterial.Monolith, 11));
			var c = new Vector3(0f, 0f, ReliefZ);
			// The sun high at the left, its rays round it; a spiral at the right.
			Vector2 sun = new Vector2(-w + 0.2f, top - 0.24f);
			d.Add(Wares.Disc(c + (Vector3)sun, 0.09f, ReliefDepth, d.Seg(12, 6), StructureMaterial.Monolith, 12));
			int rays = d.Fine ? 8 : d.Mid ? 6 : 0;
			for (int i = 0; i < rays; i++)
			{
				float a = 2f * Mathf.PI * i / rays;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				d.Add(Wares.Stroke(c, sun + dir * 0.12f, sun + dir * 0.19f, 0.022f, ReliefDepth, StructureMaterial.Monolith, 13 + i));
			}
			if (d.Mid)
			{
				Spiral(d, c + new Vector3(w - 0.2f, top - 0.26f, 0f), 0.15f, d.Fine ? 14 : 8, 30);
			}
			// The procession, left to right: two hunters with spears, the stag, a hunter, the boar.
			float y = low + 0.06f, s = 0.62f;
			Hunter(d, c + new Vector3(-w + 0.55f, y, 0f), s, 50, spear: true);
			Hunter(d, c + new Vector3(-w + 0.85f, y, 0f), s * 0.95f, 60, spear: true);
			Beast(d, c + new Vector3(-0.05f, y + 0.12f, 0f), 0.85f, 70, antlers: true, StructureMaterial.Monolith);
			Hunter(d, c + new Vector3(0.42f, y, 0f), s, 90, spear: false);
			Beast(d, c + new Vector3(w - 0.3f, y + 0.06f, 0f), 0.7f, 100, antlers: false, StructureMaterial.Monolith);
			if (d.Fine)
			{
				// What is left of the ochre they were painted with, in the stag's flank and the sun.
				d.Add(Wares.Disc(c + (Vector3)sun + new Vector3(0f, 0f, -ReliefDepth * 0.5f), 0.06f, 0.01f, 10, StructureMaterial.Earth, 120));
				d.Add(Wares.Disc(c + new Vector3(-0.08f, y + 0.24f, -ReliefDepth * 0.5f), 0.07f, 0.01f, 10, StructureMaterial.Earth, 121));
			}
		}

		/// <summary>A stele: a sun-wheel cut in the arched top, rows of runes under it, a band at the foot.</summary>
		private static void RuneStele(StructureDraft d, Vector2 face)
		{
			var c = new Vector3(0f, 0f, ReliefZ);
			var wheel = new Vector3(0f, face.y - 0.42f, ReliefZ);
			d.Add(Wares.Ring(wheel, 0.22f, 0.17f, ReliefDepth, d.Seg(16, 8), Quaternion.Euler(90f, 0f, 0f), StructureMaterial.Monolith, 10));
			int spokes = d.Mid ? 4 : 2;
			for (int i = 0; i < spokes; i++)
			{
				float a = Mathf.PI * i / spokes;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				d.Add(Wares.Stroke(wheel, -dir * 0.18f, dir * 0.18f, 0.03f, ReliefDepth, StructureMaterial.Monolith, 11 + i));
			}
			int rows = d.Fine ? 5 : d.Mid ? 4 : 2, perRow = d.Mid ? 4 : 3;
			float rowTop = face.y - 0.82f, pitch = 0.2f, colW = (face.x - 0.32f) / perRow;
			for (int r = 0; r < rows; r++)
			{
				for (int k = 0; k < perRow; k++)
				{
					var at = new Vector2(-0.5f * (face.x - 0.32f) + colW * (k + 0.5f), rowTop - r * pitch);
					Glyph(d, c, at, 0.075f, 200 + r * 16 + k * 4);
				}
				if (d.Mid)
				{
					// The line each row was cut on.
					d.Add(Wares.Stroke(c, new Vector2(-0.5f * face.x + 0.14f, rowTop - r * pitch - 0.1f), new Vector2(0.5f * face.x - 0.14f, rowTop - r * pitch - 0.1f), 0.012f, ReliefDepth * 0.6f, StructureMaterial.Monolith, 300 + r));
				}
			}
			d.Add(Wares.Stroke(c, new Vector2(-0.5f * face.x + 0.1f, 0.16f), new Vector2(0.5f * face.x - 0.1f, 0.16f), 0.05f, ReliefDepth, StructureMaterial.Monolith, 320));
		}

		/// <summary>Hands stencilled in ochre and beasts drawn in charcoal, on a pale patch of flowstone, with rows of dots.</summary>
		private static void Paintings(StructureDraft d, Vector2 face)
		{
			// The paint lies on the face itself: thin as a skin.
			var c = new Vector3(0f, 0f, -0.045f);
			const float paint = 0.008f;
			int hands = d.Fine ? 4 : d.Mid ? 3 : 2;
			for (int i = 0; i < hands; i++)
			{
				var at = new Vector2(-0.5f * face.x + 0.3f + i * 0.2f + d.Range(i, 1, -0.04f, 0.04f), face.y - 0.3f - 0.15f * (i % 2) + d.Range(i, 2, -0.05f, 0.05f));
				Hand(d, c, at, d.Range(i, 3, -25f, 25f), paint, 10 + i * 8);
			}
			// Two beasts in outline, charcoal, a horse and a bison.
			Beast(d, c + new Vector3(0.25f, face.y * 0.42f, 0f), 0.85f, 60, antlers: false, StructureMaterial.Ash, outline: true, thick: paint);
			if (d.Mid)
			{
				Beast(d, c + new Vector3(-0.35f, face.y * 0.2f, 0f), 0.6f, 100, antlers: true, StructureMaterial.Ash, outline: true, thick: paint);
			}
			int dots = d.Fine ? 9 : d.Mid ? 6 : 0;
			for (int i = 0; i < dots; i++)
			{
				var at = new Vector3(0.5f * face.x - 0.55f + i * 0.055f, face.y - 0.2f + 0.03f * Mathf.Sin(i * 1.3f), 0f);
				d.Add(Wares.Disc(c + at, 0.018f, paint, 6, StructureMaterial.Earth, 140 + i));
			}
		}

		/// <summary>A hand in ochre: the palm and five fingers spread, turned <paramref name="angle"/> degrees.</summary>
		private static void Hand(StructureDraft d, Vector3 c, Vector2 at, float angle, float thick, int key)
		{
			Quaternion turn = Quaternion.Euler(0f, 0f, angle);
			Vector2 R(Vector2 p) => at + (Vector2)(turn * (Vector3)p);
			var palm = new Vector2[8];
			for (int i = 0; i < 8; i++)
			{
				float a = 2f * Mathf.PI * i / 8;
				palm[i] = R(new Vector2(Mathf.Cos(a) * 0.05f, Mathf.Sin(a) * 0.06f));
			}
			d.Add(Wares.Shape(c, palm, thick, StructureMaterial.Earth, key));
			int fingers = d.Mid ? 5 : 3;
			for (int f = 0; f < fingers; f++)
			{
				float a = Mathf.Lerp(-55f, 30f, f / 4f) * Mathf.Deg2Rad;
				var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
				float len = f == 0 ? 0.05f : f == 2 ? 0.08f : 0.07f;
				d.Add(Wares.Stroke(c, R(dir * 0.05f), R(dir * (0.05f + len)), 0.018f, thick, StructureMaterial.Earth, key + 1 + f));
			}
		}

		/// <summary>A hunter, a stick figure in relief <paramref name="s"/> metres tall, a spear raised or a bow drawn.</summary>
		private static void Hunter(StructureDraft d, Vector3 c, float s, int key, bool spear)
		{
			float w = 0.035f * s / 0.62f;
			d.Add(Wares.Disc(c + new Vector3(0f, 0.86f * s, 0f), 0.07f * s, ReliefDepth, d.Seg(10, 6), StructureMaterial.Monolith, key));
			d.Add(Wares.Stroke(c, new Vector2(0f, 0.38f * s), new Vector2(0.02f * s, 0.76f * s), w, ReliefDepth, StructureMaterial.Monolith, key + 1));
			d.Add(Wares.Stroke(c, new Vector2(0f, 0.38f * s), new Vector2(-0.14f * s, 0f), w, ReliefDepth, StructureMaterial.Monolith, key + 2));
			d.Add(Wares.Stroke(c, new Vector2(0f, 0.38f * s), new Vector2(0.16f * s, 0.02f * s), w, ReliefDepth, StructureMaterial.Monolith, key + 3));
			if (!d.Mid)
			{
				return;
			}
			d.Add(Wares.Stroke(c, new Vector2(0.02f * s, 0.7f * s), new Vector2(0.2f * s, 0.6f * s), w * 0.85f, ReliefDepth, StructureMaterial.Monolith, key + 4));
			d.Add(Wares.Stroke(c, new Vector2(0.02f * s, 0.7f * s), new Vector2(-0.14f * s, 0.52f * s), w * 0.85f, ReliefDepth, StructureMaterial.Monolith, key + 5));
			if (spear)
			{
				d.Add(Wares.Stroke(c, new Vector2(-0.1f * s, 0.3f * s), new Vector2(0.42f * s, 0.98f * s), w * 0.6f, ReliefDepth, StructureMaterial.Monolith, key + 6));
				if (d.Fine)
				{
					d.Add(Wares.Taper(c, new Vector2(0.42f * s, 0.98f * s), new Vector2(0.5f * s, 1.09f * s), 0.05f * s, 0.005f, ReliefDepth, StructureMaterial.Monolith, key + 7));
				}
			}
			else if (d.Fine)
			{
				// A bow drawn: its stave an arc of three strokes before him.
				Vector2 b0 = new Vector2(0.26f * s, 0.36f * s), b1 = new Vector2(0.33f * s, 0.62f * s), b2 = new Vector2(0.26f * s, 0.88f * s);
				d.Add(Wares.Stroke(c, b0, b1, w * 0.7f, ReliefDepth, StructureMaterial.Monolith, key + 6));
				d.Add(Wares.Stroke(c, b1, b2, w * 0.7f, ReliefDepth, StructureMaterial.Monolith, key + 7));
				d.Add(Wares.Stroke(c, b0, b2, w * 0.3f, ReliefDepth, StructureMaterial.Monolith, key + 8));
			}
		}

		/// <summary>
		/// A beast facing +x, <paramref name="s"/> metres long: its body, neck and head, four legs, antlers or a boar's back; in
		/// relief (a solid body) or, <paramref name="outline"/>, drawn round its body in strokes.
		/// </summary>
		private static void Beast(StructureDraft d, Vector3 c, float s, int key, bool antlers, StructureMaterial m, bool outline = false, float thick = ReliefDepth)
		{
			int points = d.Fine ? 12 : 8;
			var body = new Vector2[points];
			for (int i = 0; i < points; i++)
			{
				float a = 2f * Mathf.PI * i / points;
				// Fuller at the shoulder than the haunch.
				float k = 1f + 0.15f * Mathf.Cos(a);
				body[i] = new Vector2(Mathf.Cos(a) * 0.3f * s, Mathf.Sin(a) * 0.12f * s * k);
			}
			float w = 0.03f * s;
			if (outline)
			{
				for (int i = 0; i < points; i++)
				{
					d.Add(Wares.Stroke(c, body[i], body[(i + 1) % points], w * 0.8f, thick, m, key + i));
				}
			}
			else
			{
				d.Add(Wares.Shape(c, body, thick, m, key));
			}
			Vector2 neck = new Vector2(0.26f * s, 0.06f * s), head = new Vector2(0.42f * s, 0.2f * s);
			d.Add(Wares.Stroke(c, neck, head, 0.07f * s, thick, m, key + 20));
			d.Add(Wares.Stroke(c, head, head + new Vector2(0.1f * s, -0.04f * s), 0.05f * s, thick, m, key + 21));
			for (int l = 0; l < 4; l++)
			{
				if (!d.Mid && (l == 1 || l == 2))
				{
					continue;
				}
				float x = (l < 2 ? -0.2f : 0.16f) * s + (l % 2) * 0.05f * s;
				float swing = (l % 2 == 0 ? 1f : -1f) * 0.04f * s;
				d.Add(Wares.Stroke(c, new Vector2(x, -0.06f * s), new Vector2(x + swing, -0.3f * s), w, thick, m, key + 22 + l));
			}
			if (!d.Mid)
			{
				return;
			}
			if (antlers)
			{
				Vector2 crown = head + new Vector2(-0.02f * s, 0.05f * s);
				Vector2 beam = crown + new Vector2(-0.12f * s, 0.2f * s);
				d.Add(Wares.Stroke(c, crown, beam, w * 0.6f, thick, m, key + 30));
				if (d.Fine)
				{
					for (int t = 0; t < 3; t++)
					{
						Vector2 at = Vector2.Lerp(crown, beam, 0.35f + 0.3f * t);
						d.Add(Wares.Stroke(c, at, at + new Vector2(0.07f * s, 0.06f * s), w * 0.45f, thick, m, key + 31 + t));
					}
				}
			}
			else
			{
				// A boar's bristled back, or a horse's mane: a ridge over the shoulders.
				d.Add(Wares.Stroke(c, new Vector2(-0.1f * s, 0.13f * s), new Vector2(0.26f * s, 0.16f * s), w * 0.8f, thick, m, key + 30));
			}
			if (d.Fine)
			{
				d.Add(Wares.Stroke(c, new Vector2(-0.3f * s, 0f), new Vector2(-0.38f * s, -0.1f * s), w * 0.5f, thick, m, key + 35));
			}
		}

		/// <summary>A spiral cut in strokes, <paramref name="radius"/> at its widest, <paramref name="segments"/> of them.</summary>
		private static void Spiral(StructureDraft d, Vector3 c, float radius, int segments, int key)
		{
			Vector2 last = Vector2.zero;
			for (int i = 1; i <= segments; i++)
			{
				float t = i / (float)segments;
				float a = t * 2.6f * Mathf.PI;
				var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius * t);
				d.Add(Wares.Stroke(c, last, p, 0.022f, ReliefDepth, StructureMaterial.Monolith, key + i));
				last = p;
			}
		}

		/// <summary>A rune: two or three strokes inside a box <paramref name="size"/> across, drawn from the key.</summary>
		private static void Glyph(StructureDraft d, Vector3 c, Vector2 at, float size, int key)
		{
			// A stave, and a twig or two off it (the shapes runes are cut in, never curves).
			float h = size, half = size * 0.45f;
			d.Add(Wares.Stroke(c, at + new Vector2(0f, -h), at + new Vector2(0f, h), 0.018f, ReliefDepth, StructureMaterial.Monolith, key));
			int twigs = 1 + Mathf.FloorToInt(d.Rand(key, 1) * 2.99f);
			for (int t = 0; t < twigs && (d.Mid || t == 0); t++)
			{
				float y0 = Mathf.Lerp(-h, h, d.Rand(key, 2 + t)), y1 = y0 + Mathf.Lerp(-h, h, d.Rand(key, 5 + t)) * 0.8f;
				float side = d.Rand(key, 8 + t) < 0.5f ? -1f : 1f;
				d.Add(Wares.Stroke(c, at + new Vector2(0f, y0), at + new Vector2(side * half, Mathf.Clamp(y1, -h, h)), 0.016f, ReliefDepth, StructureMaterial.Monolith, key + 1 + t));
			}
		}

		// ── The cave-in ───────────────────────────────────────────────

		/// <summary>
		/// A timber set the roof came down on, for a 4 m drift (scaled to the bore as a MineSet is): v0 across the drift, the
		/// left post leaning, the right one snapped, the cap fallen aslant from the left post's head to the floor; v1 thrown
		/// against the wall (+x), a snapped post leant on it and a split cap and boards along its foot.
		/// </summary>
		private static void CaveInTimbers(StructureDraft d)
		{
			float w = MineSetHalfWidth;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(-(w + 0.06f), -0.1f, 0f), new Vector3(-(w - 0.12f), MineSetHeight - 0.15f, 0.18f), 0.2f, 0.2f, StructureMaterial.Timber, 1));
				// The right post: its stump in the floor, its upper half fallen across the way.
				d.Add(StructureShapes.Beam(new Vector3(w + 0.06f, -0.1f, 0f), new Vector3(w + 0.02f, 0.85f, 0.02f), 0.2f, 0.2f, StructureMaterial.Timber, 2));
				d.Add(StructureShapes.Beam(new Vector3(w - 0.15f, 0.12f, -0.55f), new Vector3(0.15f, 0.16f, -1.45f), 0.2f, 0.2f, StructureMaterial.Timber, 3));
				// The cap, down from the left post's head to the rubble at the right.
				d.Add(StructureShapes.Beam(new Vector3(-(w - 0.1f), MineSetHeight - 0.05f, 0.12f), new Vector3(w + 0.1f, 0.25f, 0.55f), 0.24f, 0.22f, StructureMaterial.Timber, 4));
				if (d.Mid)
				{
					// Lagging boards slid off the cap, leant in the rubble.
					int boards = d.Fine ? 5 : 3;
					for (int b = 0; b < boards; b++)
					{
						float x = -w * 0.6f + 1.4f * w * b / Mathf.Max(1, boards - 1);
						d.Add(StructureShapes.Block(new Vector3(x, 0.45f + 0.15f * d.Rand(b, 1), 0.35f + 0.3f * d.Rand(b, 2)), new Vector3(0.18f, 0.04f, 1.2f + 0.3f * d.Rand(b, 3)),
							Quaternion.Euler(-35f - 20f * d.Rand(b, 4), d.Range(b, 5, -20f, 20f), d.Range(b, 6, -10f, 10f)), StructureMaterial.Timber, 10 + b, StructureRole.Detail)).Grain = Vector3.forward;
					}
				}
				if (d.Fine)
				{
					// Splinters at the break.
					for (int i = 0; i < 3; i++)
					{
						d.Add(StructureShapes.Block(new Vector3(w + 0.02f + d.Range(i, 7, -0.06f, 0.06f), 0.92f, d.Range(i, 8, -0.06f, 0.06f)), new Vector3(0.04f, 0.2f, 0.03f),
							Quaternion.Euler(d.Range(i, 9, -25f, 25f), 0f, d.Range(i, 10, -25f, 25f)), StructureMaterial.Timber, 20 + i, StructureRole.Detail));
					}
				}
				return;
			}
			// Against the wall at +x: the snapped post leant on it, the split cap along its foot, boards fallen across them.
			d.Add(StructureShapes.Beam(new Vector3(0.1f, -0.05f, -0.7f), new Vector3(0.62f, 1.75f, -0.35f), 0.2f, 0.2f, StructureMaterial.Timber, 1));
			d.Add(StructureShapes.Beam(new Vector3(0.45f, 0.12f, -1.15f), new Vector3(0.55f, 0.14f, 0.9f), 0.22f, 0.2f, StructureMaterial.Timber, 2));
			d.Add(StructureShapes.Beam(new Vector3(0.2f, 0.1f, 0.4f), new Vector3(0.62f, 0.55f, 1.05f), 0.2f, 0.2f, StructureMaterial.Timber, 3));
			if (d.Mid)
			{
				int boards = d.Fine ? 4 : 2;
				for (int b = 0; b < boards; b++)
				{
					d.Add(StructureShapes.Block(new Vector3(0.35f + 0.1f * d.Rand(b, 1), 0.28f + 0.1f * b, -0.6f + 0.45f * b), new Vector3(0.18f, 0.04f, 1.1f),
						Quaternion.Euler(d.Range(b, 2, -15f, 15f), 70f + d.Range(b, 3, -25f, 25f), d.Range(b, 4, -20f, 20f)), StructureMaterial.Timber, 10 + b, StructureRole.Detail)).Grain = Vector3.forward;
				}
			}
		}

		// ── The cache ─────────────────────────────────────────────────

		/// <summary>
		/// A strongbox big enough to hold the lootable chest the cache spawns (<see cref="CacheCofferInside"/>) out of sight: v0 an
		/// iron-bound coffer, its lid rounded, a hasp and lock plate at the front (−z); v1 a squat box, its lid flat, iron at
		/// its corners.
		/// </summary>
		private static void CacheCoffer(StructureDraft d)
		{
			const float w = 1.0f, depth = 0.68f;
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.31f, 0f), new Vector3(w, 0.62f, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.65f, 0f), new Vector3(w + 0.04f, 0.07f, depth + 0.04f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
				if (d.Mid)
				{
					for (int sx = -1; sx <= 1; sx += 2)
					{
						for (int sz = -1; sz <= 1; sz += 2)
						{
							d.Add(StructureShapes.Box(new Vector3(sx * (w * 0.5f - 0.03f), 0.34f, sz * (depth * 0.5f - 0.03f)), new Vector3(0.08f, 0.66f, 0.08f), StructureMaterial.Iron, 3 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
					d.Add(StructureShapes.Box(new Vector3(0f, 0.5f, -depth * 0.5f - 0.012f), new Vector3(0.1f, 0.12f, 0.025f), StructureMaterial.Iron, 8, StructureRole.Detail));
				}
				return;
			}
			const float body = 0.56f, rise = 0.18f;
			d.Add(StructureShapes.Box(new Vector3(0f, body * 0.5f, 0f), new Vector3(w, body, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			int arc = d.Seg(10, 4);
			d.Add(LidArc(depth * 0.5f + 0.01f, rise, -w * 0.5f - 0.01f, w * 0.5f + 0.01f, body, arc, StructureMaterial.Timber, 2)).Grain = Vector3.right;
			if (d.Mid)
			{
				// Three iron bands over the lid and down the body.
				foreach (float x in new[] { -0.36f, 0f, 0.36f })
				{
					int k = 3 + Mathf.RoundToInt((x + 0.36f) / 0.36f) * 2;
					d.Add(LidArc(depth * 0.5f + 0.025f, rise + 0.015f, x - 0.03f, x + 0.03f, body, arc, StructureMaterial.Iron, k));
					d.Add(StructureShapes.Box(new Vector3(x, body * 0.5f, 0f), new Vector3(0.06f, body, depth + 0.03f), StructureMaterial.Iron, k + 1, StructureRole.Detail));
				}
				// The hasp and lock plate.
				d.Add(StructureShapes.Box(new Vector3(0f, body - 0.08f, -depth * 0.5f - 0.03f), new Vector3(0.12f, 0.14f, 0.03f), StructureMaterial.Iron, 12, StructureRole.Detail));
			}
			if (d.Fine)
			{
				// Handles at the ends.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(Wares.Torus(new Vector3(s * (w * 0.5f + 0.02f), body * 0.62f, 0f), 0.06f, 0.01f, 8, 3, Quaternion.Euler(0f, 0f, 90f), StructureMaterial.Iron, 13 + (s > 0 ? 1 : 0)));
				}
			}
		}

		/// <summary>A rounded lid: a half-ellipse (half-width <paramref name="half"/>, rise <paramref name="rise"/>) across z, run along x from x0 to x1, its foot at <paramref name="y"/>.</summary>
		private static StructureSolid LidArc(float half, float rise, float x0, float x1, float y, int segments, StructureMaterial m, int key)
		{
			var outline = new Vector2[segments + 1];
			for (int i = 0; i <= segments; i++)
			{
				float a = Mathf.PI * i / segments;
				outline[i] = new Vector2(Mathf.Cos(a) * half, Mathf.Sin(a) * rise);
			}
			// The outline in (z, y), extruded along x: built in (x, y) along z, then turned a quarter about +y (z to x).
			return StructureShapes.Prism(outline, x0, x1, m, key).Transform(Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one));
		}
	}
}
#endif
