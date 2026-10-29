#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class StructurePieces
	{
		/// <summary>
		/// The fantasy wave's Cold pack: its new pieces (2026-10-27, poi-fantasy-wave/WAVE.md). The frozen north's halls and relics (a frost
		/// giant's ice-block longhouse hung with mammoth tusks, a dragon asleep in the ice, the lich's frost-bound black spire, the
		/// ice court's hall and its spires, a soldier of the frozen army), the longship drawn up on a strand, and the sea folk's coral
		/// halls and shell houses. All are laid by id (=IceBlockHall, =IceEncasedDragon, =LichSpire, =IceSpireHall, =IceSpire,
		/// =FrozenSoldier, =BeachedLongship, =CoralHall, =ShellHouse): their tags are ones no template query draws.
		/// </summary>
		/// <remarks>
		/// Ice is the palette's IceBlock surface, built in directly: it is no kit material of any style, so the solids play no palette
		/// part and a biome never re-lays them in stone. The lich's spire is the Keeps pack's DarkSpire (black slate) with the frost
		/// laid over it, not a second tower.
		/// </remarks>
		private static void AddFantasyCold(List<StructurePiece> list)
		{
			Piece(list, "IceBlockHall", StructureStyle.Stone, StructureSize.Building,
				"A frost giant's longhouse, 8 x 22 m: walls and a steep roof of great ice blocks on a fieldstone plinth, a door under an arch of two mammoth tusks, crossed tusks at both gables, a row of bones along the ridge; v0 an ice roof, v1 taller, with a roof of hides over the ice laid with antlers.",
				IceBlockHall, "building");
			Piece(list, "IceEncasedDragon", StructureStyle.Stone, StructureSize.Building,
				"A dragon asleep in the ice, 17 m: a hill of faceted ice with the beast's skull breaking out of one end, its ribs standing in a crown along the back and one wing's bones raised out of the ice, the tail's spikes at the other end; v0 the wing raised, jaws shut; v1 the wing folded and the jaws open.",
				IceEncasedDragon, "dead", "building");
			Piece(list, "LichSpire", StructureStyle.Stone, StructureSize.Building,
				"The lich's tower: the black spire (DarkSpire, v0 horned, v1 stepped) bound in frost, a ring of ice shards driven round its plinth and skulls on iron spikes between them, a cold blue light on its crown.",
				LichSpire, "building", "tower", "wizard");
			Piece(list, "IceSpireHall", StructureStyle.Stone, StructureSize.Building,
				"The ice court's hall, 11 x 22 m: a steep ice-block hall behind an iron-bound door, a tower of ice at each corner capped with a spire, a great needle rising behind the roof, runes lit pale blue in the towers' slits; v0 four corner towers, v1 two tall front towers and a taller needle.",
				IceSpireHall, "building");
			Piece(list, "IceSpire", StructureStyle.Stone, StructureSize.Medium,
				"A spire of clear ice, 11 m: a faceted needle splintering at its foot, shards leaning against it; v1 a cluster of three, the tallest 10 m.",
				IceSpire, "building");
			Piece(list, "FrozenSoldier", StructureStyle.Stone, StructureSize.Medium,
				"One of the frozen host: a mailed soldier half sunk in a block of ice, his spear raised and rime on his helm; v1 a standard-bearer, his banner stiff and torn.",
				FrozenSoldier, "dead");
			StructurePiece landing = Piece(list, "BeachedLongship", StructureStyle.Timber, StructureSize.Building,
				"A longship drawn up on its strand, 20 m: the clinker hull on log rollers, heeled a little, the dragon head high over the beach, oars shipped and the sail furled; v1 with a canvas awning pitched over the middle on a ridge-pole.",
				BeachedLongship, "ship");
			landing.Anchor = StructureAnchor.Ground;
			Piece(list, "CoralHall", StructureStyle.Stone, StructureSize.Building,
				"The sea folk's hall, 14 m across: a low drum of white shell-stone under a ribbed dome, an open arch for a door, horns of coral standing round it and fans of branch coral on the roof, sea-lamps burning green-gold; v1 a tall conch tower with its spiral rib and an arch at its foot.",
				CoralHall, "building", "underwater");
			Piece(list, "ShellHouse", StructureStyle.Stone, StructureSize.Medium,
				"A sea-folk house, 5 m: v0 a scallop dome of white shell with radial ribs and a round door, a coral chimney tube and a sea-lamp; v1 a giant clam, its two valves ajar over a pearl.",
				ShellHouse, "building", "underwater");
		}

		// ── Shared bits ───────────────────────────────────────────────

		/// <summary>A bent rod (a tusk, a rib, a coral branch, a tail spike): a quadratic curve a-b-c in steps, thinning from r0 to r1, its last step pointed.</summary>
		private static void ColdBend(StructureDraft d, Vector3 a, Vector3 b, Vector3 c, float r0, float r1, StructureMaterial material, int key, StructureRole role, int steps)
		{
			steps = Mathf.Max(2, steps);
			Vector3 prev = a;
			for (int i = 1; i <= steps; i++)
			{
				float t = i / (float)steps;
				Vector3 p = (1f - t) * (1f - t) * a + 2f * (1f - t) * t * b + t * t * c;
				float r = Mathf.Lerp(r0, r1, t);
				float length = (p - prev).magnitude;
				d.Add(StructureShapes.Rod(prev, p, r, d.Seg(6, 4), material, key + i, role, i == steps ? length : 0f));
				prev = p;
			}
		}

		private static Vector3 ColdOut(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

		/// <summary>A tower of ice: a faceted shaft with a pointed cap, its foot sunk in the ground; the cap's tip at <paramref name="tip"/> metres.</summary>
		private static void ColdTurret(StructureDraft d, Vector3 at, float radius, float eave, float tip, int key, bool slits)
		{
			int facets = d.Seg(8, 6);
			var profile = new[]
			{
				new Vector2(radius, -FoundationDepth), new Vector2(radius, eave), new Vector2(radius + 0.35f, eave), new Vector2(radius + 0.35f, eave + 0.25f),
				new Vector2(radius * 0.8f, eave + 0.5f), new Vector2(0.05f, tip),
			};
			d.Add(StructureShapes.Lathe(profile, facets, StructureMaterial.IceBlock, key, false).Transform(Matrix4x4.Translate(at)));
			if (slits && d.Mid)
			{
				// Rune-light in the slits, three up the shaft.
				for (int i = 0; i < 3; i++)
				{
					float a = (90f * i + 20f) * Mathf.Deg2Rad;
					float y = eave * (0.45f + 0.18f * i);
					Vector3 n = ColdOut(a);
					d.Add(StructureShapes.Block(at + n * (radius * Mathf.Cos(Mathf.PI / facets) + 0.03f) + Vector3.up * y, new Vector3(0.22f, 0.9f, 0.08f),
						Quaternion.LookRotation(n, Vector3.up), StructureMaterial.Runelight, key + 10 + i, StructureRole.Detail));
				}
			}
		}

		// ── The frost giant's hall ────────────────────────────────────

		private static void IceBlockHall(StructureDraft d)
		{
			bool tall = d.Variant == 1;
			var house = new House
			{
				Width = tall ? 9f : 8.4f, Depth = tall ? 24f : 21f, WallHeight = tall ? 3.4f : 2.9f, Rise = tall ? 5.4f : 4.6f, Overhang = 0.8f, Wall = 0.45f, Plinth = 0.3f,
				WallMaterial = StructureMaterial.IceBlock, RoofMaterial = StructureMaterial.IceBlock, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = false, Chimney = false, Windows = 0, DoorWidth = 1.8f, DoorHeight = 2.5f, DoorX = 0f,
				DoorKind = StructureDoorKind.Iron, RoomKind = StructureRoomKind.Hall, Key = 1,
			};
			HouseRoof roof = BuildHouse(d, house);
			float top = roof.Flat ? roof.WallTop : house.Plinth + house.WallHeight;
			float ridge = top + house.Rise + roof.Shift;
			if (d.Mid)
			{
				// Crossed mammoth tusks at both gable peaks.
				for (int e = -1; e <= 1; e += 2)
				{
					float z = e * (house.Depth * 0.5f + house.Overhang - 0.1f);
					for (int s = -1; s <= 1; s += 2)
					{
						ColdBend(d, new Vector3(-s * 0.7f, ridge - 0.9f, z), new Vector3(-s * 0.2f, ridge + 0.5f, z), new Vector3(s * 1.1f, ridge + 1.5f, z),
							0.2f, 0.07f, StructureMaterial.Bone, 500 + e * 8 + s * 3, StructureRole.Roof, 3);
					}
				}
				// The door's arch: two tusks from the jambs meeting over the lintel.
				float zf = -house.Depth * 0.5f - 0.2f;
				for (int s = -1; s <= 1; s += 2)
				{
					ColdBend(d, new Vector3(s * 1.25f, 0f, zf), new Vector3(s * 1.55f, 3.0f, zf - 0.15f), new Vector3(s * 0.15f, 3.6f, zf - 0.1f),
						0.22f, 0.08f, StructureMaterial.Bone, 540 + s * 4, StructureRole.Detail, 4);
				}
			}
			if (tall)
			{
				// Antlers laid along the hide-and-ice ridge (v1), a rack of them to each side of the gable.
				for (int i = 0; i < (d.Fine ? 5 : 3); i++)
				{
					float z = Mathf.Lerp(-house.Depth * 0.35f, house.Depth * 0.35f, i / 4f);
					d.Add(StructureShapes.Beam(new Vector3(-0.5f, ridge + 0.05f, z), new Vector3(0.5f, ridge + 0.7f, z + 0.25f), 0.1f, 0.1f, StructureMaterial.Bone, 560 + i, StructureRole.Roof));
				}
			}
			else if (d.Mid)
			{
				// A skull on the ridge's middle.
				d.Add(StructureShapes.Sphere(new Vector3(0f, ridge + 0.3f, 0f), new Vector3(0.45f, 0.35f, 0.5f), d.Seg(8, 5), 4, StructureMaterial.Bone, 565, StructureRole.Roof));
			}
		}

		// ── The dragon in the ice ─────────────────────────────────────

		private static void IceEncasedDragon(StructureDraft d)
		{
			bool wingUp = d.Variant == 0;
			// The ice: a faceted hill (three blocks of different heights stacked and tilted), its foot sunk.
			var hill = new[]
			{
				new Vector2(-8f, -0.5f), new Vector2(8f, -0.5f), new Vector2(8.4f, 0.9f), new Vector2(6.2f, 2.6f), new Vector2(2.4f, 3.7f),
				new Vector2(-2.2f, 4.1f), new Vector2(-5.4f, 3.2f), new Vector2(-7.8f, 1.5f),
			};
			d.Add(StructureShapes.Prism(hill, -3.1f, 3.1f, StructureMaterial.IceBlock, 1));
			var spur = new[] { new Vector2(-5f, -0.5f), new Vector2(3f, -0.5f), new Vector2(2f, 4.8f), new Vector2(-3.5f, 5.4f), new Vector2(-5.2f, 3.5f) };
			d.Add(StructureShapes.Prism(spur, -1.9f, 2.2f, StructureMaterial.IceBlock, 2).Transform(Matrix4x4.TRS(new Vector3(0f, 0f, 0.6f), Quaternion.Euler(0f, 8f, 0f), Vector3.one)));
			// Shards of clear ice leaning round its foot.
			int shards = d.Fine ? 9 : d.Mid ? 6 : 4;
			for (int i = 0; i < shards; i++)
			{
				float a = (360f * i / shards + d.Rand(3 + i) * 25f) * Mathf.Deg2Rad;
				float r = 5.4f + d.Rand(20 + i) * 2.4f;
				Vector3 foot = new Vector3(Mathf.Cos(a) * r * 1.35f, -0.4f, Mathf.Sin(a) * r * 0.6f);
				Vector3 lean = new Vector3(-Mathf.Cos(a) * 0.9f, 2.4f + d.Rand(40 + i) * 2.4f, -Mathf.Sin(a) * 0.5f);
				d.Add(StructureShapes.Rod(foot, foot + lean, 0.34f, d.Seg(6, 4), StructureMaterial.IceBlock, 30 + i, StructureRole.Structure, lean.magnitude));
			}

			// The skull, breaking out of the +x end: a long wedge of a head tilted up out of the ice, brow horns swept back, the jaw shut or agape.
			var head = Matrix4x4.TRS(new Vector3(7.4f, 2.5f, 0f), Quaternion.Euler(0f, 0f, 14f), Vector3.one);
			d.Add(StructureShapes.Block(new Vector3(0.9f, 0.1f, 0f), new Vector3(3.2f, 1.2f, 1.5f), Quaternion.identity, StructureMaterial.Bone, 60, StructureRole.Structure, 0.1f).Transform(head));
			d.Add(StructureShapes.Block(new Vector3(-0.7f, 0.65f, 0f), new Vector3(1.8f, 1.1f, 1.8f), Quaternion.identity, StructureMaterial.Bone, 61, StructureRole.Structure, 0.1f).Transform(head));
			float jaw = wingUp ? 0f : 38f;
			d.Add(StructureShapes.Block(new Vector3(1.0f, -0.65f, 0f), new Vector3(3.0f, 0.5f, 1.2f), Quaternion.Euler(0f, 0f, -jaw), StructureMaterial.Bone, 62, StructureRole.Detail, 0.05f)
				.Transform(Matrix4x4.TRS(new Vector3(-0.2f, 0f, 0f), Quaternion.identity, Vector3.one)).Transform(head));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 root = head.MultiplyPoint3x4(new Vector3(-1.2f, 1.1f, s * 0.7f));
					ColdBend(d, root, root + new Vector3(-0.9f, 0.9f, s * 0.7f), root + new Vector3(-2.6f, 1.1f, s * 1.4f), 0.28f, 0.07f, StructureMaterial.Bone, 70 + s * 5, StructureRole.Detail, 3);
					// Teeth in the upper jaw.
					for (int t = 0; t < 4 && d.Fine; t++)
					{
						Vector3 tooth = head.MultiplyPoint3x4(new Vector3(0.1f + t * 0.7f, -0.45f, s * 0.6f));
						d.Add(StructureShapes.Rod(tooth, tooth + new Vector3(0f, -0.5f, 0f), 0.07f, 4, StructureMaterial.Bone, 90 + t * 2 + (s > 0 ? 1 : 0), StructureRole.Detail, 0.5f));
					}
				}
			}
			d.Add(StructureShapes.Sphere(head.MultiplyPoint3x4(new Vector3(-0.1f, 0.85f, 0.45f)), new Vector3(0.22f, 0.22f, 0.22f), d.Seg(8, 5), 4, StructureMaterial.Runelight, 63, StructureRole.Detail));
			d.Emit(head.MultiplyPoint3x4(new Vector3(-0.1f, 0.85f, 0.45f)), SceneEmitterKind.Glow);

			// The ribs: a crown of curved bones standing out of the back.
			int ribs = d.Fine ? 7 : d.Mid ? 5 : 3;
			for (int i = 0; i < ribs; i++)
			{
				float x = Mathf.Lerp(-3.4f, 3.2f, i / (ribs - 1f));
				float h = 3.8f - Mathf.Abs(x - 0.4f) * 0.12f;
				float lift = 1.6f + 0.2f * Mathf.Sin(i * 1.3f);
				ColdBend(d, new Vector3(x, h - 0.2f, -2.2f), new Vector3(x + 0.15f, h + lift + 1.2f, 0f), new Vector3(x - 0.1f, h - 0.2f, 2.2f), 0.2f, 0.12f, StructureMaterial.Bone, 100 + i * 8, StructureRole.Detail, 4);
			}
			// The wing: an arm bone from the shoulder, the finger bones fanned from its elbow (up), or folded flat along the back.
			Vector3 shoulder = new Vector3(2.2f, 4.0f, -1.7f);
			if (wingUp)
			{
				Vector3 elbow = shoulder + new Vector3(-0.8f, 3.4f, -1.2f);
				ColdBend(d, shoulder, shoulder + new Vector3(-0.2f, 2.2f, -0.4f), elbow, 0.26f, 0.2f, StructureMaterial.Bone, 200, StructureRole.Detail, 3);
				for (int f = 0; f < 4; f++)
				{
					Vector3 tip = elbow + new Vector3(-1.6f - f * 1.2f, 1.0f + (f == 1 ? 1.2f : 0f) - f * 0.8f, -2.2f - f * 1.0f);
					ColdBend(d, elbow, Vector3.Lerp(elbow, tip, 0.5f) + new Vector3(0f, 0.7f, 0f), tip, 0.14f, 0.05f, StructureMaterial.Bone, 210 + f * 6, StructureRole.Detail, 3);
				}
			}
			else
			{
				Vector3 elbow = shoulder + new Vector3(-3.4f, 0.5f, -1.6f);
				ColdBend(d, shoulder, shoulder + new Vector3(-1.5f, 1.3f, -0.6f), elbow, 0.26f, 0.2f, StructureMaterial.Bone, 200, StructureRole.Detail, 3);
				for (int f = 0; f < 3; f++)
				{
					Vector3 tip = elbow + new Vector3(-2.6f, -0.2f - f * 0.1f, -0.6f - f * 1.0f);
					d.Add(StructureShapes.Rod(elbow, tip, 0.1f, d.Seg(5, 4), StructureMaterial.Bone, 210 + f * 6, StructureRole.Detail, (tip - elbow).magnitude));
				}
			}
			// The tail's spikes along the −x end.
			int spikes = d.Mid ? 5 : 3;
			for (int i = 0; i < spikes; i++)
			{
				float x = Mathf.Lerp(-6.8f, -2.4f, i / (spikes - 1f));
				float h = Mathf.Lerp(2.0f, 3.8f, i / (spikes - 1f));
				d.Add(StructureShapes.Rod(new Vector3(x, h, 0f), new Vector3(x - 0.4f, h + 1.5f - 0.15f * i, 0f), 0.22f, d.Seg(6, 4), StructureMaterial.Bone, 240 + i, StructureRole.Detail, 1.5f));
			}
		}

		// ── The lich's spire ──────────────────────────────────────────

		private static void LichSpire(StructureDraft d)
		{
			// The black spire itself, then the frost bound round its foot.
			DarkSpire(d);
			int shards = d.Fine ? 10 : d.Mid ? 8 : 6;
			for (int i = 0; i < shards; i++)
			{
				float deg = 360f * i / shards + 13f;
				float a = deg * Mathf.Deg2Rad;
				bool tall = i % 2 == 0;
				// Leave the door's way clear: the shards and skulls skip the sector in front of it (the door faces −z, 270°).
				if (Mathf.Abs(Mathf.DeltaAngle(deg, 270f)) < 26f || Mathf.Abs(Mathf.DeltaAngle(deg + 180f / shards, 270f)) < 22f)
				{
					continue;
				}
				Vector3 foot = ColdOut(a) * 5.4f + Vector3.down * 0.4f;
				Vector3 lean = -ColdOut(a) * (tall ? 1.1f : 0.6f) + Vector3.up * (tall ? 5.6f : 3.4f);
				d.Add(StructureShapes.Rod(foot, foot + lean, tall ? 0.45f : 0.32f, d.Seg(6, 4), StructureMaterial.IceBlock, 900 + i, StructureRole.Structure, lean.magnitude));
				if (!tall && d.Mid)
				{
					// A skull on an iron spike between the shards.
					Vector3 spike = ColdOut(a + Mathf.PI / shards) * 6.5f;
					d.Add(StructureShapes.Rod(spike + Vector3.down * 0.3f, spike + Vector3.up * 2.2f, 0.06f, 4, StructureMaterial.Iron, 930 + i, StructureRole.Detail, 0.5f));
					d.Add(StructureShapes.Sphere(spike + Vector3.up * 2.4f, new Vector3(0.22f, 0.26f, 0.24f), d.Seg(8, 5), 4, StructureMaterial.Bone, 950 + i, StructureRole.Detail));
				}
			}
		}

		// ── The ice court ─────────────────────────────────────────────

		private static void IceSpireHall(StructureDraft d)
		{
			bool front = d.Variant == 1;
			var house = new House
			{
				Width = 11f, Depth = 22f, WallHeight = 5.6f, Rise = 6.4f, Overhang = 0.7f, Wall = 0.55f, Plinth = 0.5f,
				WallMaterial = StructureMaterial.IceBlock, RoofMaterial = StructureMaterial.IceBlock, PlinthMaterial = StructureMaterial.IceBlock,
				Frame = false, RidgeAlongX = false, Chimney = false, Windows = 0, DoorWidth = 2.2f, DoorHeight = 3.4f, DoorX = 0f,
				DoorKind = StructureDoorKind.Iron, RoomKind = StructureRoomKind.Hall, Key = 1,
			};
			HouseRoof roof = BuildHouse(d, house);
			float ridge = house.Plinth + house.WallHeight + house.Rise + roof.Shift;
			float hz = house.Depth * 0.5f + 0.3f;
			// A tower of ice at each corner: the front pair taller in v1.
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					bool frontPair = sz < 0;
					float eave = front && frontPair ? 11f : 8.6f;
					float tip = front && frontPair ? 17.5f : 13.5f;
					float radius = front && frontPair ? 2.2f : 1.9f;
					// Standing at the corner, mostly outside the walls so no tower fills the hall's floor.
					ColdTurret(d, new Vector3(sx * (house.Width * 0.5f + 0.8f * radius), 0f, sz * hz), radius, eave, tip, 20 + (sx > 0 ? 1 : 0) * 40 + (sz > 0 ? 1 : 0) * 80, true);
				}
			}
			// The needle behind the ridge.
			float needle = front ? 21.5f : 17.5f;
			ColdTurret(d, new Vector3(0f, 0f, house.Depth * 0.5f + 1.6f), 2.4f, needle - 6f, needle, 200, d.Fine);
			d.Emit(new Vector3(0f, needle, house.Depth * 0.5f + 1.6f), SceneEmitterKind.Glow);
			if (d.Mid)
			{
				// Icicle fringe along the eaves of the front gable and a pale-fire brazier either side of the door.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Rod(new Vector3(s * 2.2f, 2.1f, -house.Depth * 0.5f - 0.5f), new Vector3(s * 2.2f, 0.4f, -house.Depth * 0.5f - 0.5f), 0.1f, 4, StructureMaterial.IceBlock, 260 + s, StructureRole.Detail, 1.7f));
					d.Add(StructureShapes.Rod(new Vector3(s * 3.4f, ridge - 4.4f, -house.Depth * 0.5f - 0.6f), new Vector3(s * 3.4f, ridge - 5.6f, -house.Depth * 0.5f - 0.6f), 0.08f, 4, StructureMaterial.IceBlock, 270 + s, StructureRole.Detail, 1.2f));
				}
			}
		}

		private static void IceSpire(StructureDraft d)
		{
			bool cluster = d.Variant == 1;
			void Needle(Vector3 at, float r, float h, float lean, int key)
			{
				int facets = d.Seg(8, 5);
				var p = new[] { new Vector2(r, -FoundationDepth), new Vector2(r * 0.95f, 0.8f), new Vector2(r * 0.6f, h * 0.55f), new Vector2(r * 0.28f, h * 0.88f), new Vector2(0.02f, h) };
				d.Add(StructureShapes.Lathe(p, facets, StructureMaterial.IceBlock, key, false).Transform(Matrix4x4.TRS(at, Quaternion.Euler(0f, 0f, lean), Vector3.one)));
			}
			if (cluster)
			{
				Needle(new Vector3(0f, 0f, 0f), 1.0f, 10f, 0f, 1);
				Needle(new Vector3(1.7f, 0f, 0.6f), 0.75f, 7.5f, -8f, 2);
				Needle(new Vector3(-1.5f, 0f, -0.9f), 0.6f, 5.8f, 10f, 3);
			}
			else
			{
				Needle(Vector3.zero, 1.3f, 11f, 0f, 1);
			}
			int shards = d.Fine ? 6 : d.Mid ? 4 : 3;
			for (int i = 0; i < shards; i++)
			{
				float a = (360f * i / shards + 20f) * Mathf.Deg2Rad;
				Vector3 foot = ColdOut(a) * (cluster ? 3.1f : 2.5f) + Vector3.down * 0.4f;
				Vector3 lean = -ColdOut(a) * 0.8f + Vector3.up * (2.2f + 1.6f * d.Rand(7 + i));
				d.Add(StructureShapes.Rod(foot, foot + lean, 0.3f, d.Seg(6, 4), StructureMaterial.IceBlock, 10 + i, StructureRole.Structure, lean.magnitude));
			}
		}

		// ── The frozen host ───────────────────────────────────────────

		private static void FrozenSoldier(StructureDraft d)
		{
			bool bearer = d.Variant == 1;
			// The block he is sunk in, to the thigh.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 1.55f, 1.4f), d.Bevel(0.12f), StructureMaterial.IceBlock, 1));
			d.Add(StructureShapes.Block(new Vector3(0.7f, 0.25f, 0.4f), new Vector3(0.7f, 0.7f, 0.6f), Quaternion.Euler(0f, 25f, 12f), StructureMaterial.IceBlock, 2, StructureRole.Structure, d.Bevel(0.05f)));
			// The man: legs (in the ice), a mail coat, shoulders, a helm with its nasal, a rimed cloak.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.75f, 0f), new Vector3(0.62f, 0.9f, 0.38f), StructureMaterial.Iron, 10));
			d.Add(StructureShapes.Box(new Vector3(0f, 1.38f, 0f), new Vector3(0.72f, 0.18f, 0.42f), StructureMaterial.Canvas, 11));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.4f, 0f), new Vector3(0.24f, 0.28f, 0.26f), d.Seg(10, 6), 5, StructureMaterial.Iron, 12));
			d.Add(StructureShapes.Box(new Vector3(0f, 2.34f, -0.24f), new Vector3(0.06f, 0.24f, 0.04f), StructureMaterial.Iron, 13, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(-0.36f, 2.1f, 0f), new Vector3(-0.5f, 1.45f, -0.05f), 0.15f, 0.15f, StructureMaterial.Iron, 14));
			if (bearer)
			{
				// The standard: a pole up from his fist and a banner torn and stiff in the frost.
				d.Add(StructureShapes.Beam(new Vector3(0.5f, 1.3f, -0.05f), new Vector3(0.5f, 4.2f, -0.05f), 0.08f, 0.08f, StructureMaterial.Timber, 20));
				d.Add(StructureShapes.Beam(new Vector3(0.38f, 1.9f, -0.08f), new Vector3(0.5f, 1.95f, -0.05f), 0.14f, 0.14f, StructureMaterial.Iron, 21, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0.5f, 3.65f, -0.05f), new Vector3(0.05f, 0.9f, 0.1f), StructureMaterial.Dyed, 22, StructureRole.Detail));
				var banner = new[] { new Vector2(0f, 0f), new Vector2(0f, -1.0f), new Vector2(1.1f, -0.8f), new Vector2(1.1f, -0.2f) };
				d.Add(StructureShapes.Prism(banner, -0.025f, 0.025f, StructureMaterial.Dyed, 23, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.56f, 4.1f, -0.05f))));
			}
			else
			{
				// The spear raised, its iron head catching the light; a round shield on the other arm.
				d.Add(StructureShapes.Rod(new Vector3(0.52f, 1.25f, -0.05f), new Vector3(0.5f, 3.9f, -0.05f), 0.045f, 5, StructureMaterial.Timber, 20, StructureRole.Structure));
				d.Add(StructureShapes.Rod(new Vector3(0.5f, 3.9f, -0.05f), new Vector3(0.5f, 4.5f, -0.05f), 0.07f, 5, StructureMaterial.Iron, 21, StructureRole.Detail, 0.6f));
				d.Add(StructureShapes.Cylinder(0.4f, 0f, 0.07f, d.Seg(12, 6), StructureMaterial.Dyed, 22, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(-0.5f, 1.75f, -0.18f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			if (d.Mid)
			{
				// Rime standing off the soldier in little crystals.
				d.Add(StructureShapes.Rod(new Vector3(-0.2f, 2.65f, 0f), new Vector3(-0.28f, 3.0f, 0.05f), 0.04f, 4, StructureMaterial.IceBlock, 30, StructureRole.Detail, 0.35f));
				d.Add(StructureShapes.Rod(new Vector3(0.18f, 2.62f, 0.05f), new Vector3(0.3f, 2.95f, 0f), 0.04f, 4, StructureMaterial.IceBlock, 31, StructureRole.Detail, 0.35f));
			}
		}

		// ── The longship drawn up ─────────────────────────────────────

		private static void BeachedLongship(StructureDraft d)
		{
			// The kit's longship (its oars shipped, its sail furled), lifted so its keel rests on the rollers and heeled a little to one side.
			var hull = new StructureDraft(d.Piece, 1, d.Lod, d.GenerationSeed);
			Longship(hull);
			Matrix4x4 haul = Matrix4x4.TRS(new Vector3(0f, 1.05f, 0f), Quaternion.Euler(5f, 0f, 0f), Vector3.one);
			foreach (StructureSolid s in hull.Solids)
			{
				d.Solids.Add(s.Transform(haul));
			}
			// The log rollers it was dragged up on, across the keel.
			int rollers = d.Mid ? 5 : 3;
			for (int i = 0; i < rollers; i++)
			{
				float x = Mathf.Lerp(-6f, 6f, i / (rollers - 1f));
				d.Add(StructureShapes.Cylinder(0.3f, -2.4f, 2.4f, d.Seg(8, 5), StructureMaterial.Timber, 600 + i, true)
					.Transform(Matrix4x4.TRS(new Vector3(x, 0.18f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			// Props against the hull: stakes either side holding her upright.
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(-3.5f, 0.1f, s * 3.4f), new Vector3(-2.7f, 1.8f, s * 2.0f), 0.18f, 0.18f, StructureMaterial.Timber, 620 + s, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(4.2f, 0.1f, s * 3.2f), new Vector3(3.5f, 1.7f, s * 1.9f), 0.18f, 0.18f, StructureMaterial.Timber, 625 + s, StructureRole.Detail));
				}
			}
			if (d.Variant == 1)
			{
				// An awning over the middle: a ridge-pole on two forks, canvas pitched either side.
				for (int e = -1; e <= 1; e += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(e * 3.2f, 0.9f, 0f), new Vector3(e * 3.2f, 3.5f, 0f), 0.09f, 0.09f, StructureMaterial.Timber, 640 + e, StructureRole.Structure));
				}
				d.Add(StructureShapes.Beam(new Vector3(-3.6f, 3.5f, 0f), new Vector3(3.6f, 3.5f, 0f), 0.08f, 0.08f, StructureMaterial.Timber, 645, StructureRole.Structure));
				for (int s = -1; s <= 1; s += 2)
				{
					// Canvas pitched from the ridge-pole down to the gunwale, 33 degrees either side.
					d.Add(StructureShapes.Block(new Vector3(0f, 2.5f, s * 1.15f), new Vector3(7.4f, 0.06f, 2.9f), Quaternion.Euler(s * 33f, 0f, 0f), StructureMaterial.Canvas, 650 + s, StructureRole.Roof));
				}
			}
		}

		// ── The sea folk's halls ──────────────────────────────────────

		private static void CoralHall(StructureDraft d)
		{
			bool conch = d.Variant == 1;
			int seg = d.Seg(16, 8);
			float doorA = 1.5f * Mathf.PI;
			float outer = conch ? 3.6f : 5.6f, inner = outer - 0.7f, wall = conch ? 4.6f : 3.4f;
			// The footing: a low ring of fieldstone under it all.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.9f, -FoundationDepth), new Vector2(outer + 0.9f, 0.35f), new Vector2(outer + 0.5f, 0.45f) }, seg, StructureMaterial.Fieldstone, 1, false));
			RingWallWithDoor(d, Vector3.zero, inner, outer, 0.4f, wall, seg, doorA, 1.6f, 0.4f + 2.5f, StructureMaterial.Marble, 100);
			if (!conch)
			{
				// A ribbed dome over the drum, its ribs rising from the wall to a coral finial.
				var dome = new[] { new Vector2(outer + 0.15f, wall - 0.1f), new Vector2(outer * 0.88f, wall + 1.2f), new Vector2(outer * 0.62f, wall + 2.3f), new Vector2(outer * 0.3f, wall + 3.0f), new Vector2(0.02f, wall + 3.3f) };
				d.Add(StructureShapes.Lathe(dome, seg, StructureMaterial.Marble, 20, true));
				int ribs = d.Fine ? 8 : 6;
				for (int i = 0; i < ribs; i++)
				{
					float a = 2f * Mathf.PI * i / ribs;
					Vector3 n = ColdOut(a);
					d.Add(StructureShapes.Rod(n * (outer + 0.2f) + Vector3.up * (wall - 0.1f), n * 0.15f + Vector3.up * (wall + 3.45f), 0.11f, 4, StructureMaterial.Fieldstone, 30 + i, StructureRole.Detail));
				}
				// Horns of coral round the hall, standing tall and curving in.
				for (int i = 0; i < 5; i++)
				{
					float a = 2f * Mathf.PI * (i + 0.5f) / 5f + 0.4f;
					Vector3 n = ColdOut(a);
					float h = 6.2f + 2.4f * d.Rand(50 + i);
					ColdBend(d, n * (outer + 1.7f) + Vector3.down * 0.4f, n * (outer + 2.1f) + Vector3.up * h * 0.55f, n * (outer + 0.7f) + Vector3.up * h, 0.5f, 0.12f, StructureMaterial.Marble, 60 + i * 8, StructureRole.Structure, 4);
				}
				// Fans of branch coral on the dome.
				if (d.Mid)
				{
					for (int i = 0; i < 3; i++)
					{
						float a = 2f * Mathf.PI * i / 3f;
						Vector3 b = ColdOut(a) * 2.2f + Vector3.up * (wall + 2.35f);
						for (int k = -1; k <= 1; k++)
						{
							d.Add(StructureShapes.Rod(b, b + ColdOut(a + k * 0.5f) * 0.9f + Vector3.up * 1.3f, 0.08f, 4, StructureMaterial.Fieldstone, 120 + i * 4 + k + 1, StructureRole.Detail, 1.5f));
						}
					}
				}
				d.Add(StructureShapes.Sphere(new Vector3(0f, wall + 3.7f, 0f), new Vector3(0.38f, 0.38f, 0.38f), d.Seg(10, 6), 4, StructureMaterial.Faelight, 40, StructureRole.Detail));
				d.Emit(new Vector3(0f, wall + 3.7f, 0f), SceneEmitterKind.Glow);
			}
			else
			{
				// The conch: a spiral tower tapering from the drum, its rib winding up it, the tip a coral horn.
				var shell = new[] { new Vector2(outer, wall - 0.1f), new Vector2(outer * 1.12f, wall + 1.4f), new Vector2(outer * 0.95f, wall + 3.2f), new Vector2(outer * 0.7f, wall + 4.8f), new Vector2(outer * 0.42f, wall + 6.4f), new Vector2(0.06f, wall + 8.6f) };
				d.Add(StructureShapes.Lathe(shell, seg, StructureMaterial.Marble, 20, true));
				int turns = d.Fine ? 18 : d.Mid ? 12 : 8;
				Vector3 prev = Vector3.zero;
				for (int i = 0; i <= turns; i++)
				{
					float t = i / (float)turns;
					float y = Mathf.Lerp(wall + 0.1f, wall + 8.2f, t);
					float rr = Mathf.Lerp(outer * 1.1f, 0.25f, Mathf.Pow(t, 0.9f)) + 0.12f;
					Vector3 p = ColdOut(t * 2.4f * Mathf.PI) * rr + Vector3.up * y;
					if (i > 0)
					{
						d.Add(StructureShapes.Rod(prev, p, Mathf.Lerp(0.2f, 0.07f, t), 4, StructureMaterial.Fieldstone, 60 + i, StructureRole.Detail));
					}
					prev = p;
				}
				for (int i = 0; i < 3; i++)
				{
					float a = 2f * Mathf.PI * i / 3f + 0.3f;
					Vector3 n = ColdOut(a);
					ColdBend(d, n * (outer + 1.0f) + Vector3.down * 0.4f, n * (outer + 1.5f) + Vector3.up * 2.4f, n * (outer + 0.4f) + Vector3.up * 4.4f, 0.4f, 0.1f, StructureMaterial.Marble, 100 + i * 8, StructureRole.Structure, 3);
				}
				d.Emit(new Vector3(0f, wall + 8.4f, 0f), SceneEmitterKind.Glow);
			}
			// Sea-lamps on posts either side of the arch.
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 post = new Vector3(s * 1.8f, 0f, -(outer + 1.5f));
				d.Add(StructureShapes.Rod(post + Vector3.down * 0.3f, post + Vector3.up * 1.8f, 0.07f, 4, StructureMaterial.Iron, 140 + s, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(post + Vector3.up * 1.95f, new Vector3(0.22f, 0.26f, 0.22f), d.Seg(8, 5), 4, StructureMaterial.Faelight, 145 + s, StructureRole.Detail));
				d.Emit(post + Vector3.up * 1.95f, SceneEmitterKind.Glow);
			}
		}

		private static void ShellHouse(StructureDraft d)
		{
			int seg = d.Seg(14, 8);
			if (d.Variant == 1)
			{
				// A giant clam: the lower valve a deep bowl, the upper propped ajar on a hinge, a pearl in the gape.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(2.2f, -0.4f), new Vector2(2.5f, 0.3f), new Vector2(2.2f, 1.2f), new Vector2(1.5f, 1.6f), new Vector2(0.1f, 1.7f) }, seg, StructureMaterial.Marble, 1, true));
				var valve = StructureShapes.Lathe(new[] { new Vector2(2.2f, 0f), new Vector2(2.4f, 0.5f), new Vector2(1.8f, 1.4f), new Vector2(0.9f, 1.9f), new Vector2(0.05f, 2.0f) }, seg, StructureMaterial.Marble, 2, true)
					.Transform(Matrix4x4.TRS(new Vector3(0f, 1.3f, 1.9f), Quaternion.Euler(34f, 0f, 0f), Vector3.one) * Matrix4x4.Translate(new Vector3(0f, 0f, -1.9f)));
				d.Add(valve);
				d.Add(StructureShapes.Sphere(new Vector3(0f, 1.95f, -0.3f), new Vector3(0.5f, 0.5f, 0.5f), d.Seg(10, 6), 5, StructureMaterial.Faelight, 3, StructureRole.Detail));
				d.Emit(new Vector3(0f, 1.95f, -0.3f), SceneEmitterKind.Glow);
				for (int i = 0; i < (d.Mid ? 7 : 4); i++)
				{
					float a = Mathf.Lerp(-80f, 80f, i / (d.Mid ? 6f : 3f)) * Mathf.Deg2Rad + Mathf.PI;
					d.Add(StructureShapes.Rod(ColdOut(a) * 1.0f + Vector3.up * 1.6f, ColdOut(a) * 2.4f + Vector3.up * 0.7f, 0.06f, 4, StructureMaterial.Fieldstone, 10 + i, StructureRole.Detail));
				}
				return;
			}
			// A scallop: a low dome with radial ribs, a round door cut in its front, a coral chimney tube and a sea-lamp on a stem.
			float doorA = 1.5f * Mathf.PI;
			RingWallWithDoor(d, Vector3.zero, 1.8f, 2.4f, 0.0f, 1.5f, seg, doorA, 1.1f, 1.9f, StructureMaterial.Marble, 1);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(2.55f, -0.4f), new Vector2(2.55f, 0.15f) }, seg, StructureMaterial.Fieldstone, 2, false));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(2.5f, 1.4f), new Vector2(2.25f, 2.1f), new Vector2(1.5f, 2.7f), new Vector2(0.7f, 3.0f), new Vector2(0.02f, 3.1f) }, seg, StructureMaterial.Marble, 3, true));
			int ribs = d.Fine ? 9 : 6;
			for (int i = 0; i < ribs; i++)
			{
				float a = 2f * Mathf.PI * i / ribs + 0.2f;
				Vector3 n = ColdOut(a);
				d.Add(StructureShapes.Rod(n * 2.55f + Vector3.up * 1.4f, n * 0.2f + Vector3.up * 3.15f, 0.09f, 4, StructureMaterial.Fieldstone, 20 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Rod(new Vector3(0.9f, 2.7f, 0.9f), new Vector3(1.0f, 4.1f, 1.0f), 0.18f, d.Seg(6, 4), StructureMaterial.Fieldstone, 40, StructureRole.Detail, 0.4f));
			if (d.Mid)
			{
				Vector3 post = new Vector3(1.2f, 0f, -2.9f);
				d.Add(StructureShapes.Rod(post + Vector3.down * 0.3f, post + Vector3.up * 1.5f, 0.05f, 4, StructureMaterial.Iron, 41, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(post + Vector3.up * 1.62f, new Vector3(0.18f, 0.2f, 0.18f), d.Seg(8, 5), 4, StructureMaterial.Faelight, 42, StructureRole.Detail));
				d.Emit(post + Vector3.up * 1.62f, SceneEmitterKind.Glow);
			}
		}
	}
}
#endif
