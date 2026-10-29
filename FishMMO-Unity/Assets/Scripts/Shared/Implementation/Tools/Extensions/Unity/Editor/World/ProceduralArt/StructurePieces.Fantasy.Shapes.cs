#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class StructurePieces
	{
		/// <summary>
		/// How high a hanging monastery's deck stands over its origin, and how far behind the origin the rock face is at that height
		/// (2026-10-27): FaceSnapShaper sets the site so the face at <see cref="HangingDeckHeight"/> stands <see cref="HangingBackReach"/>
		/// behind it, and the deck's back edge meets the rock there, its beams running on into it.
		/// </summary>
		public const float HangingDeckHeight = 6.5f, HangingBackReach = 1.5f;

		/// <summary>
		/// The fantasy wave's Shapes pieces (2026-10-27, poi-fantasy-wave/WAVE.md): laid by id (=HangingCloister); the one tag,
		/// "monument", no template's query draws, so no older pick moves.
		/// </summary>
		private static void AddFantasyShapes(List<StructurePiece> list)
		{
			Piece(list, "HangingCloister", StructureStyle.Timber, StructureSize.Building,
				"A monastery's hall hung on a sheer rock face, anchored before the face: a plank deck 6.5 m up on great beams run back into the rock, raking struts and tall stilts down to the foot, a stone-footed timber hall under a steep roof, a railed gallery and a ladder up; v0 a long hall with a lantern at its door, v1 a chapel with a bell-cote and a covered stair.",
				HangingCloister, "monument");
		}

		// ── The hanging monastery ───────────────────────────────────────

		private static void HangingCloister(StructureDraft d)
		{
			const float deck = HangingDeckHeight, back = HangingBackReach, front = -6f, rock = 5f;
			bool chapel = d.Variant == 1;
			float width = chapel ? 8f : 9.5f;
			float depth = back - front;
			float mid = 0.5f * (back + front);
			// The deck and the cantilever beams under it, run on back into the rock.
			using (d.Builds(DeckRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, deck - 0.15f, mid), new Vector3(width, 0.3f, depth), StructureMaterial.Timber, 1));
			}
			int beams = d.Mid ? 4 : 3;
			for (int i = 0; i < beams; i++)
			{
				float x = (i / (beams - 1f) - 0.5f) * (width - 1f);
				d.Add(StructureShapes.Beam(new Vector3(x, deck - 0.5f, front - 0.3f), new Vector3(x, deck - 0.5f, rock), 0.34f, 0.4f, StructureMaterial.Timber, 2 + i));
				// A raking strut from under the deck's front down into the face below it.
				d.Add(StructureShapes.Beam(new Vector3(x, deck - 0.6f, front + 0.6f), new Vector3(x, 1.6f, back - 0.4f), 0.24f, 0.24f, StructureMaterial.Timber, 10 + i));
				// A stilt from the deck's front edge to the foot of the face.
				d.Add(StructureShapes.Rod(new Vector3(x, -1f, front + 0.2f), new Vector3(x, deck - 0.3f, front + 0.2f), 0.17f, d.Seg(8, 5), StructureMaterial.Timber, 20 + i));
			}
			if (d.Mid)
			{
				// A ledger beam along the rock, pinned with iron straps.
				d.Add(StructureShapes.Box(new Vector3(0f, deck - 0.45f, back - 0.2f), new Vector3(width, 0.36f, 0.4f), StructureMaterial.Timber, 30));
				for (int i = 0; i < beams; i++)
				{
					float x = (i / (beams - 1f) - 0.5f) * (width - 1f);
					d.Add(StructureShapes.Box(new Vector3(x, deck - 0.5f, front - 0.1f), new Vector3(0.4f, 0.46f, 0.08f), StructureMaterial.Iron, 31 + i, StructureRole.Detail));
				}
			}
			// The rail round the deck's front and sides.
			int posts = d.Fine ? 7 : 4;
			for (int i = 0; i < posts; i++)
			{
				float x = (i / (posts - 1f) - 0.5f) * (width - 0.3f);
				d.Add(StructureShapes.Box(new Vector3(x, deck + 0.5f, front + 0.15f), new Vector3(0.12f, 1f, 0.12f), StructureMaterial.Timber, 40 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, deck + 1.02f, front + 0.15f), new Vector3(width - 0.2f, 0.1f, 0.12f), StructureMaterial.Timber, 50, StructureRole.Detail));
			for (int side = -1; side <= 1; side += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(side * (width * 0.5f - 0.15f), deck + 1.02f, mid), new Vector3(0.12f, 0.1f, depth - 0.3f), StructureMaterial.Timber, 51 + (side > 0 ? 1 : 0), StructureRole.Detail));
			}

			// The hall: a stone footing on the deck, timber-framed plaster walls, a steep gable roof (ridge back to the rock).
			float hallW = chapel ? 5.2f : 6.6f, hallD = chapel ? 5f : 5.6f, hallZ = back - 0.4f - hallD * 0.5f, wallTop = deck + 0.5f + (chapel ? 3.6f : 3f);
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, deck + 0.25f, hallZ), new Vector3(hallW + 0.3f, 0.5f, hallD + 0.3f), d.Bevel(0.04f), StructureMaterial.Stone, 60));
			}
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.5f * (deck + 0.5f + wallTop), hallZ), new Vector3(hallW, wallTop - deck - 0.5f, hallD), StructureMaterial.Plaster, 61));
			}
			float rise = 0.5f * hallW * (chapel ? 1.3f : 1.1f);
			Matrix4x4 roofAt = Matrix4x4.Translate(new Vector3(0f, 0f, hallZ));
			OwnRoofPlan roof = OwnGable(d, hallW, hallD, wallTop, rise, 0.45f, 0.18f, StructureMaterial.Timber, 62, roofAt);
			if (!roof.Flat)
			{
				using (d.Builds(WallRoles))
				{
					Gable(d, hallW, wallTop, roof.Rise, hallZ - hallD * 0.5f + 0.1f, 0.2f, StructureMaterial.Plaster, 64, Matrix4x4.identity);
				}
			}
			// The frame's timbers on the front, the door, the windows.
			float face = hallZ - hallD * 0.5f - 0.05f;
			for (int i = -1; i <= 1; i += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(i * (hallW * 0.5f - 0.1f), 0.5f * (deck + 0.5f + wallTop), face), new Vector3(0.2f, wallTop - deck - 0.5f, 0.12f), StructureMaterial.Timber, 70 + (i > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, wallTop - 0.1f, face), new Vector3(hallW, 0.2f, 0.12f), StructureMaterial.Timber, 72, StructureRole.Detail));
			using (d.Builds(DoorRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, deck + 0.5f + 1.05f, face - 0.02f), new Vector3(1.1f, 2.1f, 0.1f), StructureMaterial.Timber, 73, StructureRole.Detail));
			}
			if (d.Mid)
			{
				for (int i = -1; i <= 1; i += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(i * hallW * 0.3f, deck + 2.3f, face - 0.03f), new Vector3(0.5f, 0.8f, 0.08f), StructureMaterial.Glass, 74 + (i > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			if (!chapel)
			{
				// A lantern on a bracket by the door.
				d.Add(StructureShapes.Box(new Vector3(1.0f, deck + 2.6f, face - 0.35f), new Vector3(0.08f, 0.08f, 0.6f), StructureMaterial.Iron, 80, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(1.0f, deck + 2.3f, face - 0.6f), new Vector3(0.22f, 0.3f, 0.22f), StructureMaterial.Lamplight, 81, StructureRole.Detail));
				d.Emit(new Vector3(1.0f, deck + 2.3f, face - 0.6f), SceneEmitterKind.Lamp);
				// The ladder from the foot to the deck, at its side.
				float lx = width * 0.5f - 0.9f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Rod(new Vector3(lx + s * 0.25f, -0.6f, front - 0.9f), new Vector3(lx + s * 0.25f, deck + 0.9f, front - 0.25f), 0.06f, d.Seg(6, 4), StructureMaterial.Timber, 82 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
				if (d.Mid)
				{
					int rungs = d.Fine ? 16 : 8;
					for (int i = 0; i < rungs; i++)
					{
						float t = (i + 0.5f) / rungs;
						var at = Vector3.Lerp(new Vector3(lx, -0.6f, front - 0.9f), new Vector3(lx, deck + 0.9f, front - 0.25f), t);
						d.Add(StructureShapes.Box(at, new Vector3(0.5f, 0.05f, 0.05f), StructureMaterial.Timber, 90 + i, StructureRole.Detail));
					}
				}
			}
			else
			{
				// The bell-cote over the front gable, its bell hung under a little roof, and a cross-gable porch.
				float top = wallTop + roof.Rise;
				float bz = face + 0.5f;
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.6f, bz), new Vector3(0.9f, 1.4f, 0.7f), StructureMaterial.Timber, 100));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, top + 1.3f), new Vector2(0.75f, top + 1.3f), new Vector2(0f, top + 2.3f) }, 4, StructureMaterial.Timber, 101, true, 0f, false, false, StructureRole.Roof)
					.Transform(Matrix4x4.Translate(new Vector3(0f, 0f, bz)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 45f, 0f))));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, top + 0.35f), new Vector2(0.28f, top + 0.4f), new Vector2(0.18f, top + 0.8f), new Vector2(0f, top + 0.85f) }, d.Seg(8, 5), StructureMaterial.Iron, 102, true, 0f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(new Vector3(0f, 0f, bz - 0.36f))));
				// The covered stair: a timber flight from the foot up the deck's side under a lean-to roof.
				float sx = width * 0.5f + 0.65f;
				d.Add(StructureShapes.Beam(new Vector3(sx, -0.4f, front - 1.8f), new Vector3(sx, deck - 0.1f, front + 3.2f), 1.1f, 0.2f, StructureMaterial.Timber, 110));
				if (d.Mid)
				{
					int steps = d.Fine ? 12 : 7;
					for (int i = 0; i < steps; i++)
					{
						float t = (i + 0.5f) / steps;
						Vector3 at = Vector3.Lerp(new Vector3(sx, -0.25f, front - 1.8f), new Vector3(sx, deck + 0.05f, front + 3.2f), t);
						d.Add(StructureShapes.Box(at, new Vector3(1.1f, 0.08f, 0.3f), StructureMaterial.Timber, 111 + i, StructureRole.Detail));
					}
				}
				d.Add(StructureShapes.Beam(new Vector3(sx, 2.0f, front - 1.8f), new Vector3(sx, deck + 2.3f, front + 3.2f), 1.5f, 0.12f, StructureMaterial.Timber, 130, StructureRole.Roof));
				for (int i = 0; i < 3; i++)
				{
					float t = i / 2f;
					Vector3 foot = Vector3.Lerp(new Vector3(sx + 0.65f, -0.3f, front - 1.6f), new Vector3(sx + 0.65f, deck - 0.1f, front + 3.0f), t);
					d.Add(StructureShapes.Rod(foot, foot + Vector3.up * 2.2f, 0.07f, d.Seg(6, 4), StructureMaterial.Timber, 131 + i, StructureRole.Detail));
				}
				d.Emit(new Vector3(0f, deck + 2.4f, face - 0.4f), SceneEmitterKind.Lamp);
			}
		}
	}
}
#endif
