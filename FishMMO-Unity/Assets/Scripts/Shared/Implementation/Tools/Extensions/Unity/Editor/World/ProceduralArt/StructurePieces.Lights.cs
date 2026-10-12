#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Medieval light sources (2026-10-12: "We need torches, light posts, braziers, and other light sources"): a torch
	/// on a pole, a torch in an iron sconce for a wall, and a brazier. Each gives off its flame (SceneEmitterKind.Torch for a
	/// torch, Fire for a brazier's bowl), which the client lights (SiteLightPool) and burns (SiteEmbers' flames).
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>A wall torch's flame stands this far out from the wall and this high over its fixing point, metres.</summary>
		public const float WallTorchReach = 0.32f, WallTorchRise = 0.42f;

		private static void AddLights(List<StructurePiece> list)
		{
			Piece(list, "TorchPost", StructureStyle.Timber, StructureSize.Small,
				"A torch on a pole, 2.4 m: v0 a pitch-wrapped torch in an iron cup, v1 an iron cresset basket of burning wood.",
				TorchPost, "light", "torch", "camp", "town");
			Piece(list, "WallTorch", StructureStyle.Iron, StructureSize.Small,
				"A torch in an iron sconce, its back plate at z = 0 against a wall, the torch leaning out along −z; v1 a ring bracket.",
				WallTorch, "light", "torch").Collides = false;
			Piece(list, "Brazier", StructureStyle.Iron, StructureSize.Small,
				"A fire bowl: v0 an iron brazier on three splayed legs, v1 a stone bowl on a pedestal.",
				Brazier, "light", "military", "sacred");
		}

		private static void TorchPost(StructureDraft d)
		{
			const float height = 2.4f;
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.35f, 0f), new Vector3(0f, height, 0f), 0.11f, 0.11f, StructureMaterial.Timber, 1)).Grain = Vector3.up;
			if (d.Variant == 1)
			{
				// A cresset: an iron basket of four staves and two hoops, wood burning in it.
				int seg = d.Seg(8, 4);
				for (int i = 0; i < 4; i++)
				{
					float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
					var foot = new Vector3(Mathf.Cos(a) * 0.08f, height - 0.02f, Mathf.Sin(a) * 0.08f);
					var lip = new Vector3(Mathf.Cos(a) * 0.2f, height + 0.32f, Mathf.Sin(a) * 0.2f);
					d.Add(StructureShapes.Beam(foot, lip, 0.025f, 0.025f, StructureMaterial.Iron, 2 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Cylinder(0.205f, height + 0.27f, height + 0.31f, seg, StructureMaterial.Iron, 8, true, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.14f, height + 0.1f, height + 0.13f, seg, StructureMaterial.Iron, 9, true, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.13f, height + 0.05f, height + 0.22f, seg, StructureMaterial.Ash, 10, false, StructureRole.Detail));
				d.Emit(new Vector3(0f, height + 0.22f, 0f), SceneEmitterKind.Torch);
				return;
			}
			// The cup and the torch in it, its head bound in pitch-soaked rag.
			d.Add(StructureShapes.Cylinder(0.07f, height - 0.05f, height + 0.08f, d.Seg(8, 4), StructureMaterial.Iron, 2, false, StructureRole.Detail));
			Torch(d, new Vector3(0f, height - 0.1f, 0f), Vector3.up, 3);
		}

		private static void WallTorch(StructureDraft d) => MountedTorch(d, Matrix4x4.identity, d.Variant == 1, 1);

		/// <summary>
		/// A torch in an iron sconce, built in the frame <paramref name="place"/> (its back plate at the origin against a wall
		/// facing −z): the wall torch piece's, and a building's own where it has torches of its own (a gatehouse's).
		/// </summary>
		internal static void MountedTorch(StructureDraft d, Matrix4x4 place, bool ring, int key)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, 0f, 0.01f), new Vector3(0.12f, 0.24f, 0.02f), StructureMaterial.Iron, key).Transform(place));
			var ringAt = new Vector3(0f, ring ? 0.02f : 0.05f, -0.16f);
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.08f, 0f), ringAt + Vector3.down * 0.05f, 0.03f, 0.03f, StructureMaterial.Iron, key + 1).Transform(place));
			if (!ring)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.09f, 0f), ringAt + Vector3.up * 0.03f, 0.025f, 0.025f, StructureMaterial.Iron, key + 2, StructureRole.Detail).Transform(place));
			}
			d.Add(StructureShapes.Cylinder(0.05f, ringAt.y - 0.03f, ringAt.y + 0.03f, d.Seg(8, 4), StructureMaterial.Iron, key + 3, true)
				.Transform(place * Matrix4x4.Translate(new Vector3(0f, 0f, ringAt.z))));
			// The torch from below the ring up and out to its head.
			Vector3 lean = new Vector3(0f, 0.93f, -0.37f).normalized;
			Vector3 foot = ringAt - lean * 0.2f, head = foot + lean * 0.48f;
			d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, key + 4).Transform(place)).Grain = place.MultiplyVector(lean);
			d.Add(StructureShapes.Beam(head - lean * 0.02f, head + lean * 0.13f, 0.075f, 0.075f, StructureMaterial.Canvas, key + 5, StructureRole.Detail).Transform(place));
			d.Emit(place.MultiplyPoint3x4(head + lean * 0.14f), SceneEmitterKind.Torch);
		}

		/// <summary>A torch: a shaft along <paramref name="up"/> from its foot, a rag-bound head, and its flame given off above it.</summary>
		private static void Torch(StructureDraft d, Vector3 foot, Vector3 up, int key)
		{
			Vector3 head = foot + up * 0.48f;
			d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, key)).Grain = up;
			d.Add(StructureShapes.Beam(head - up * 0.02f, head + up * 0.13f, 0.075f, 0.075f, StructureMaterial.Canvas, key + 1, StructureRole.Detail));
			d.Emit(head + up * 0.14f, SceneEmitterKind.Torch);
		}

		private static void Brazier(StructureDraft d)
		{
			int seg = d.Seg(14, 6);
			if (d.Variant == 1)
			{
				// A stone fire bowl on a pedestal: the shaft, the bowl, the coals.
				d.Add(StructureShapes.Cylinder(0.24f, -0.2f, 0.12f, seg, StructureMaterial.Stone, 1, true));
				d.Add(StructureShapes.Cylinder(0.13f, 0.12f, 0.75f, seg, StructureMaterial.Stone, 2, false));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.13f, 0.72f), new Vector2(0.42f, 0.98f), new Vector2(0.46f, 1.05f), new Vector2(0.38f, 1.05f), new Vector2(0.12f, 0.86f) },
					seg, StructureMaterial.Stone, 3, true, 0f, true, false));
				d.Add(StructureShapes.Cylinder(0.36f, 0.94f, 1.0f, seg, StructureMaterial.Ash, 4, true, StructureRole.Detail));
				d.Emit(new Vector3(0f, 1.0f, 0f), SceneEmitterKind.Fire);
				return;
			}
			// An iron bowl on three splayed legs, a ring tying them.
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				var foot = new Vector3(Mathf.Cos(a) * 0.38f, -0.05f, Mathf.Sin(a) * 0.38f);
				var top = new Vector3(Mathf.Cos(a) * 0.22f, 0.78f, Mathf.Sin(a) * 0.22f);
				d.Add(StructureShapes.Beam(foot, top, 0.04f, 0.04f, StructureMaterial.Iron, 1 + i));
			}
			d.Add(StructureShapes.Cylinder(0.3f, 0.4f, 0.44f, seg, StructureMaterial.Iron, 5, true, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.05f, 0.74f), new Vector2(0.3f, 0.8f), new Vector2(0.4f, 0.98f), new Vector2(0.35f, 0.98f), new Vector2(0.05f, 0.8f) },
				seg, StructureMaterial.Iron, 6, true, 0f, true, false));
			d.Add(StructureShapes.Cylinder(0.32f, 0.88f, 0.94f, seg, StructureMaterial.Ash, 7, true, StructureRole.Detail));
			d.Emit(new Vector3(0f, 0.94f, 0f), SceneEmitterKind.Fire);
		}
	}
}
#endif
