#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What gathers round lived-in buildings and camps (2026-10-11: points of interest looked dated): woodpiles, carts,
	/// hay, a washing line, lanterns, signposts, benches, water butts; and at a camp, a cooking tripod, a drying rack and a
	/// weapon rack. Tagged "clutter" with where it belongs ("town", "camp"); PropsFeature leans it against the buildings.
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddClutter(List<StructurePiece> list)
		{
			Piece(list, "Woodpile", StructureStyle.Timber, StructureSize.Small, "Split logs stacked against a wall under a plank lid, 1.5 m; v1 a loose heap with a chopping block.",
				Woodpile, "clutter", "town", "camp");
			Piece(list, "Cart", StructureStyle.Timber, StructureSize.Medium, "A two-wheeled cart, shafts down; v1 loaded with sacks.",
				Cart, "clutter", "town");
			Piece(list, "HayBales", StructureStyle.Timber, StructureSize.Small, "Three bales of hay, two stacked; v1 a loose rick.",
				HayBales, "clutter", "town", "farm");
			Piece(list, "WashingLine", StructureStyle.Timber, StructureSize.Small, "Two posts, a line and the washing on it, 4 m along x; v1 fewer clothes.",
				WashingLine, "clutter", "town");
			Piece(list, "LanternPost", StructureStyle.Timber, StructureSize.Small, "A post with an iron lantern on a bracket; v1 a lantern on a hook by a door, low.",
				LanternPost, "clutter", "town", "light");
			Piece(list, "Signpost", StructureStyle.Timber, StructureSize.Small, "A signpost with two arms; v1 three.",
				Signpost, "clutter", "town");
			Piece(list, "Bench", StructureStyle.Timber, StructureSize.Small, "A plank bench, 1.6 m; v1 a bench and a little table.",
				Bench, "clutter", "town");
			Piece(list, "WaterButt", StructureStyle.Timber, StructureSize.Small, "A lidded water butt with a dipper; v1 two barrels and a bucket.",
				WaterButt, "clutter", "town", "camp");
			Piece(list, "CookingTripod", StructureStyle.Timber, StructureSize.Small, "Three poles over a fire ring and a hanging pot; v1 a spit.",
				CookingTripod, "clutter", "camp", "fire");
			Piece(list, "DryingRack", StructureStyle.Timber, StructureSize.Small, "A pole frame with hides stretched on it; v1 fish hung to dry.",
				DryingRack, "clutter", "camp");
			Piece(list, "WeaponRack", StructureStyle.Timber, StructureSize.Small, "A rack of spears and shields; v1 axes and a target.",
				WeaponRack, "clutter", "camp", "military");
		}

		private static void Woodpile(StructureDraft d)
		{
			int seg = d.Seg(7, 5);
			if (d.Variant == 0)
			{
				// Rows of logs end-on, a plank lid over them: 28 logs at the finest level, six-sided, inside the small budget.
				int rows = d.Fine ? 4 : 3, cols = d.Fine ? 7 : 5;
				seg = d.Seg(6, 5);
				for (int r = 0; r < rows; r++)
				{
					for (int c = 0; c < cols; c++)
					{
						float x = -0.65f + c * (1.3f / (cols - 1)) + (r % 2) * 0.09f, y = 0.1f + r * 0.19f;
						Log(d, new Vector3(x, y, -0.3f), new Vector3(x + d.Range(10 + r * 20 + c, 1, -0.03f, 0.03f), y, 0.3f), 0.09f, 10 + r * 20 + c, 0f, StructureMaterial.Timber, seg);
					}
				}
				d.Add(StructureShapes.Block(new Vector3(0.05f, 0.1f + rows * 0.19f + 0.04f, 0.05f), new Vector3(1.65f, 0.05f, 0.8f), Quaternion.Euler(-8f, 0f, 0f), StructureMaterial.Timber, 200, StructureRole.Roof));
			}
			else
			{
				for (int i = 0; i < (d.Fine ? 9 : 5); i++)
				{
					float a = d.Range(300 + i, 1, 0f, 360f);
					Vector3 c = new Vector3(d.Range(300 + i, 2, -0.6f, 0.6f), 0.08f + (i % 3) * 0.12f, d.Range(300 + i, 3, -0.4f, 0.4f));
					Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.3f;
					Log(d, c - dir, c + dir, 0.08f, 300 + i, 0f, StructureMaterial.Timber, seg);
				}
				d.Add(StructureShapes.Cylinder(0.28f, -0.1f, 0.45f, seg, StructureMaterial.Timber, 320, true).Transform(Matrix4x4.Translate(new Vector3(0.95f, 0f, 0.1f))));
			}
		}

		private static void Cart(StructureDraft d)
		{
			// The bed on its axle, two spoked wheels (as discs), shafts resting on the ground ahead.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.85f, 0f), new Vector3(1.3f, 0.08f, 2f), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.66f, 1.05f, 0f), new Vector3(0.06f, 0.35f, 2f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.forward;
				var wheel = StructureShapes.Cylinder(0.55f, -0.05f, 0.05f, d.Seg(14, 8), StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0));
				d.Add(wheel.Transform(Matrix4x4.TRS(new Vector3(s * 0.78f, 0.55f, 0.1f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				if (d.Mid)
				{
					var hub = StructureShapes.Cylinder(0.12f, -0.09f, 0.09f, d.Seg(8, 6), StructureMaterial.Iron, 6 + (s > 0 ? 1 : 0), false, StructureRole.Detail);
					d.Add(hub.Transform(Matrix4x4.TRS(new Vector3(s * 0.78f, 0.55f, 0.1f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
				}
				d.Add(StructureShapes.Beam(new Vector3(s * 0.4f, 0.82f, -0.95f), new Vector3(s * 0.32f, 0.05f, -2.6f), 0.08f, 0.08f, StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-0.85f, 0.55f, 0.1f), new Vector3(0.85f, 0.55f, 0.1f), 0.09f, 0.09f, StructureMaterial.Timber, 10));
			if (d.Variant == 1 && d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					var at = new Vector3(-0.3f + (i % 2) * 0.6f, 1.08f + (i / 2) * 0.28f, -0.4f + (i % 3) * 0.4f);
					d.Add(StructureShapes.Sphere(at, new Vector3(0.28f, 0.18f, 0.38f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 20 + i));
				}
			}
		}

		private static void HayBales(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				var at = new[] { new Vector3(-0.55f, 0.27f, 0f), new Vector3(0.55f, 0.27f, 0.05f), new Vector3(0f, 0.81f, 0.02f) };
				for (int i = 0; i < at.Length; i++)
				{
					d.Add(StructureShapes.ChamferBox(at[i], new Vector3(1.05f, 0.55f, 0.6f), d.Bevel(0.06f), StructureMaterial.Thatch, 1 + i)).Grain = Vector3.right;
				}
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(1.3f, -0.1f), new Vector2(1.25f, 0.9f), new Vector2(0.7f, 1.8f), new Vector2(0f, 2.1f) },
					d.Seg(12, 7), StructureMaterial.Thatch, 1, true, 0f, false, true));
			}
		}

		private static void WashingLine(StructureDraft d)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 2f, -0.3f, 0f), new Vector3(s * 2f, 1.9f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-2f, 1.8f, 0f), new Vector3(2f, 1.72f, 0f), 0.015f, 0.015f, StructureMaterial.Canvas, 3, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			int items = d.Variant == 0 ? 5 : 3;
			for (int i = 0; i < items; i++)
			{
				float x = -1.6f + i * (3.2f / Mathf.Max(1, items - 1));
				float w = d.Range(10 + i, 1, 0.35f, 0.7f), h = d.Range(10 + i, 2, 0.45f, 0.8f);
				StructureMaterial cloth = i % 2 == 0 ? StructureMaterial.Canvas : StructureMaterial.Dyed;
				d.Add(StructureShapes.Block(new Vector3(x, 1.74f - h * 0.5f, 0f), new Vector3(w, h, 0.02f), Quaternion.Euler(d.Range(10 + i, 3, -6f, 6f), 0f, 0f), cloth, 10 + i, StructureRole.Detail));
			}
		}

		private static void LanternPost(StructureDraft d)
		{
			bool post = d.Variant == 0;
			float height = post ? 2.6f : 1.9f;
			if (post)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0f), new Vector3(0f, height, 0f), 0.14f, 0.14f, StructureMaterial.Timber, 1));
				d.Add(StructureShapes.Beam(new Vector3(0f, height - 0.15f, 0f), new Vector3(0f, height - 0.15f, -0.55f), 0.07f, 0.07f, StructureMaterial.Iron, 2, StructureRole.Detail));
			}
			// The lantern: an iron cage round a glowing pane (Glass: lit at night, StructureSurfaces), a cap.
			Vector3 at = post ? new Vector3(0f, height - 0.5f, -0.55f) : new Vector3(0f, height, 0f);
			d.Add(StructureShapes.Box(at, new Vector3(0.18f, 0.26f, 0.18f), StructureMaterial.Glass, 3, StructureRole.Detail));
			d.Emit(at, SceneEmitterKind.Lamp);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, at.y + 0.13f), new Vector2(0f, at.y + 0.28f) }, 4, StructureMaterial.Iron, 4, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
				.Transform(Matrix4x4.Translate(new Vector3(at.x, 0f, at.z))));
			d.Add(StructureShapes.Box(at + Vector3.down * 0.15f, new Vector3(0.22f, 0.04f, 0.22f), StructureMaterial.Iron, 5, StructureRole.Detail));
		}

		private static void Signpost(StructureDraft d)
		{
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0f), new Vector3(0f, 2.3f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 1));
			int arms = d.Variant == 0 ? 2 : 3;
			for (int i = 0; i < arms; i++)
			{
				float yaw = d.Range(10 + i, 1, 0f, 360f);
				Quaternion q = Quaternion.Euler(0f, yaw, 0f);
				Vector3 c = new Vector3(0f, 2.05f - i * 0.28f, 0f) + q * new Vector3(0.45f, 0f, 0f);
				d.Add(StructureShapes.Block(c, new Vector3(0.8f, 0.18f, 0.04f), q, StructureMaterial.Timber, 10 + i, StructureRole.Detail));
			}
		}

		private static void Bench(StructureDraft d)
		{
			d.Add(StructureShapes.Box(new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.07f, 0.34f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.62f, 0.21f, 0f), new Vector3(0.08f, 0.45f, 0.3f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0)));
			}
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.72f, -0.8f), new Vector3(1f, 0.06f, 0.7f), StructureMaterial.Timber, 4)).Grain = Vector3.right;
				for (int i = 0; i < 4; i++)
				{
					d.Add(StructureShapes.Box(new Vector3((i % 2 == 0 ? -1 : 1) * 0.42f, 0.35f, -0.8f + (i < 2 ? -0.28f : 0.28f)), new Vector3(0.07f, 0.72f, 0.07f), StructureMaterial.Timber, 5 + i));
				}
			}
		}

		private static void WaterButt(StructureDraft d)
		{
			var profile = new List<Vector2> { new Vector2(0.36f, 0f), new Vector2(0.42f, 0.55f), new Vector2(0.36f, 1.1f) };
			d.Add(StructureShapes.Lathe(profile, d.Seg(14, 8), StructureMaterial.Timber, 1, true, 0f, false, true));
			d.Add(StructureShapes.Cylinder(0.37f, 1.1f, 1.14f, d.Seg(14, 8), StructureMaterial.Timber, 2, false, StructureRole.Detail));
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Lathe(profile, d.Seg(14, 8), StructureMaterial.Timber, 3, true, 0f, false, true).Transform(Matrix4x4.Translate(new Vector3(0.85f, 0f, 0.1f))));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 0f), new Vector2(0.17f, 0.32f) }, d.Seg(10, 6), StructureMaterial.Timber, 4, true, 0f, false, true)
					.Transform(Matrix4x4.Translate(new Vector3(0.4f, 0f, -0.55f))));
			}
		}

		private static void CookingTripod(StructureDraft d)
		{
			// A ring of stones round ash, three poles meeting over it, a pot on a chain (or a spit on two forks).
			int stones = d.Fine ? 8 : 5;
			for (int i = 0; i < stones; i++)
			{
				float a = i * Mathf.PI * 2f / stones;
				d.Add(RoughStone(d, new Vector3(Mathf.Cos(a) * 0.55f, 0.05f, Mathf.Sin(a) * 0.55f), new Vector3(0.22f, 0.16f, 0.2f), a * Mathf.Rad2Deg, 10 + i));
			}
			d.Add(StructureShapes.Cylinder(0.45f, -0.05f, 0.03f, d.Seg(10, 6), StructureMaterial.Ash, 30, false, StructureRole.Detail));
			d.Emit(new Vector3(0f, 0.12f, 0f), SceneEmitterKind.Fire);
			if (d.Variant == 0)
			{
				var top = new Vector3(0f, 1.6f, 0f);
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					Log(d, new Vector3(Mathf.Cos(a) * 0.9f, -0.1f, Mathf.Sin(a) * 0.9f), top + new Vector3(Mathf.Cos(a) * 0.08f, 0.15f, Mathf.Sin(a) * 0.08f), 0.04f, 40 + i, 0f, StructureMaterial.Timber, 5);
				}
				d.Add(StructureShapes.Beam(top, top + Vector3.down * 0.8f, 0.02f, 0.02f, StructureMaterial.Iron, 45, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 0.55f), new Vector2(0.22f, 0.65f), new Vector2(0.2f, 0.82f), new Vector2(0.17f, 0.84f) },
					d.Seg(10, 6), StructureMaterial.Iron, 46, true));
			}
			else
			{
				for (int s = -1; s <= 1; s += 2)
				{
					Log(d, new Vector3(s * 0.75f, -0.2f, 0f), new Vector3(s * 0.75f, 0.95f, 0f), 0.04f, 50 + (s > 0 ? 1 : 0), 0f, StructureMaterial.Timber, 5);
				}
				Log(d, new Vector3(-0.95f, 0.9f, 0f), new Vector3(0.95f, 0.9f, 0f), 0.025f, 52, 0f, StructureMaterial.Timber, 5);
			}
		}

		private static void DryingRack(StructureDraft d)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * 1.1f, -0.3f, 0f), new Vector3(s * 1.1f, 2f, 0f), 0.065f, 1 + (s > 0 ? 1 : 0), 0f, StructureMaterial.Timber, 5);
			}
			Log(d, new Vector3(-1.2f, 1.85f, 0f), new Vector3(1.2f, 1.85f, 0f), 0.04f, 3, 0f, StructureMaterial.Timber, 5);
			Log(d, new Vector3(-1.2f, 0.45f, 0f), new Vector3(1.2f, 0.45f, 0f), 0.04f, 4, 0f, StructureMaterial.Timber, 5);
			if (!d.Mid)
			{
				return;
			}
			if (d.Variant == 0)
			{
				for (int i = 0; i < 2; i++)
				{
					d.Add(StructureShapes.Block(new Vector3(-0.5f + i * 1f, 1.15f, 0f), new Vector3(0.85f, 1.25f, 0.03f), Quaternion.identity, StructureMaterial.Canvas, 10 + i, StructureRole.Detail));
				}
			}
			else
			{
				for (int i = 0; i < 6; i++)
				{
					float x = -0.9f + i * 0.36f;
					d.Add(StructureShapes.Block(new Vector3(x, 1.55f, 0f), new Vector3(0.12f, 0.5f, 0.05f), Quaternion.Euler(0f, 0f, d.Range(20 + i, 1, -5f, 5f)), StructureMaterial.Bone, 20 + i, StructureRole.Detail));
				}
			}
		}

		private static void WeaponRack(StructureDraft d)
		{
			d.Add(StructureShapes.Beam(new Vector3(-0.9f, 0.3f, 0f), new Vector3(0.9f, 0.3f, 0f), 0.1f, 0.25f, StructureMaterial.Timber, 1));
			d.Add(StructureShapes.Beam(new Vector3(-0.9f, 1.2f, 0.12f), new Vector3(0.9f, 1.2f, 0.12f), 0.08f, 0.08f, StructureMaterial.Timber, 2));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.85f, -0.2f, 0.05f), new Vector3(s * 0.85f, 1.3f, 0.12f), 0.08f, 0.08f, StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
			}
			if (!d.Mid)
			{
				return;
			}
			int count = 5;
			for (int i = 0; i < count; i++)
			{
				float x = -0.6f + i * 0.3f;
				if (d.Variant == 0)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0.32f, -0.02f), new Vector3(x + 0.04f, 2.1f, 0.14f), 0.03f, 0.03f, StructureMaterial.Timber, 10 + i, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(x + 0.045f, 2.18f, 0.145f), new Vector3(0.05f, 0.18f, 0.02f), Quaternion.identity, StructureMaterial.Iron, 20 + i, StructureRole.Detail));
				}
				else
				{
					d.Add(StructureShapes.Beam(new Vector3(x, 0.32f, -0.02f), new Vector3(x + 0.02f, 1.25f, 0.13f), 0.035f, 0.035f, StructureMaterial.Timber, 10 + i, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(x + 0.08f, 1.12f, 0.1f), new Vector3(0.14f, 0.16f, 0.02f), Quaternion.identity, StructureMaterial.Iron, 20 + i, StructureRole.Detail));
				}
			}
			if (d.Variant == 0)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					var shield = StructureShapes.Cylinder(0.32f, -0.025f, 0.025f, d.Seg(12, 8), StructureMaterial.Dyed, 30 + (s > 0 ? 1 : 0), false, StructureRole.Detail);
					d.Add(shield.Transform(Matrix4x4.TRS(new Vector3(s * 1.15f, 0.55f, 0.1f), Quaternion.Euler(80f, 0f, 0f), Vector3.one)));
				}
			}
		}
	}
}
#endif
