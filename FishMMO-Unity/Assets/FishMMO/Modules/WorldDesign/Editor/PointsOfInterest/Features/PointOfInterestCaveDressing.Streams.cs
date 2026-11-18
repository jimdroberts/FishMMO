#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The system's water (<see cref="CaveStreams.Plan"/>), worked out once when first asked.</summary>
			private CaveStreamPlan streamPlan;
			/// <summary>What the water's pass laid: pieces (stones, rimstone, flowstone, glow) and the pools' and falls' lights.</summary>
			public int StreamPieces, StreamLights;

			private CaveStreamPlan StreamPlan => streamPlan ??= CaveStreams.Plan(system) ?? CaveStreamPlan.Dry;

			/// <summary>
			/// Whether a prop of an earlier pass would stand in the cave's water (its foot in a body's footprint, under its surface):
			/// <see cref="Place"/> refuses it, after the pass has drawn all it draws for it (no stream moves). Rock is let stand in
			/// it (a stalagmite in a pool, a block fallen into a stream); never true in a dry or frozen cave.
			/// </summary>
			private bool InStream(GameObject prefab, Vector3 local)
			{
				CaveStreamPlan plan = StreamPlan;
				return plan.Active && !plan.Frozen && !prefab.name.StartsWith("Formation_", System.StringComparison.Ordinal)
					&& CaveStreamDressing.InWater(plan, local);
			}

			/// <summary>
			/// A wet cave's water, dressed (2026-10-20, caves livened; after every earlier pass, on its own stream, so nothing laid
			/// before it moves): rounded stones in the streams' beds and on the pools' floors, flowstone and wet-loving glow on the
			/// walls under each spring, rimstone and luminous fungus round the pools, boulders round the plunge pools, and a soft
			/// light over the larger pools and at the falls' feet, in the cave's own glow (<see cref="CaveStreamDressing"/>).
			/// Nothing in a dry cave; an ice cave's frozen water is the ice pass's.
			/// </summary>
			private void Streams()
			{
				CaveStreamPlan plan = StreamPlan;
				if (!plan.Active || plan.Frozen || plan.Bodies.Count == 0)
				{
					return;
				}
				var random = new DeterministicRNG(unchecked(system.Seed * 0x5EA7 + 0x57EA));
				CaveGlowPalette colours = Colours();
				bool limestone = set.Wall == "Flowstone";
				int lights = 0;
				for (int b = 0; b < plan.Bodies.Count && StreamPieces < CaveStreamDressing.MaxPieces; b++)
				{
					CaveStreamBody body = plan.Bodies[b];
					if (body.Kind == CaveStreamKind.Pool)
					{
						Pool(random, body, colours, limestone, ref lights);
					}
					else
					{
						Stream(random, body, colours, limestone, ref lights);
					}
				}
				PointOfInterestFeatureKit.Note(context, $"cave water dressed: {plan.Bodies.Count} bodies, {StreamPieces} pieces, {StreamLights} lights");
			}

			/// <summary>A stream's dressing: stones down its bed, glow on the wall side of its channel, a spring's flowstone and glow, a light at its falls' feet.</summary>
			private void Stream(DeterministicRNG random, CaveStreamBody body, in CaveGlowPalette colours, bool limestone, ref int lights)
			{
				Vector3[] path = body.Path;
				int n = path.Length;
				for (int k = 0; k + 1 < n; k++)
				{
					bool falling = body.Falls != null && body.Falls[k + 1];
					if (falling)
					{
						// A spring's crack: flowstone poured down the wall under it, and glow where the spray keeps the wall wet.
						if (k == 0)
						{
							Spring(random, path[0], path[2], colours, limestone);
						}
						// A fall's foot: a light where the spray catches it, at the end of its run of falling points.
						int foot = k + 1;
						while (foot + 1 < n && body.Falls[foot + 1])
						{
							foot++;
						}
						if (foot - k >= 1 && path[k].y - path[foot].y >= CaveStreams.MinFall && lights < CaveStreamDressing.MaxLights)
						{
							lights += WaterLight(path[foot] + Vector3.up * 1.2f, colours, 7f + 0.5f * (path[k].y - path[foot].y)) ? 1 : 0;
						}
						k = foot - 1;
						continue;
					}
					Vector3 a = path[k], c = path[k + 1];
					float run = new Vector2(c.x - a.x, c.z - a.z).magnitude;
					// Every few metres along it: a stone in the bed, now and then glow on the wall side of its channel.
					float u = random.NextFloat(), v = random.NextFloat(), w = random.NextFloat(), size = random.NextFloat(), turn = random.Range(0f, 360f);
					if (run < 0.05f || float.IsNaN(body.Banks[k]) || u > CaveStreamDressing.BedStoneShare || StreamPieces >= CaveStreamDressing.MaxPieces)
					{
						continue;
					}
					Vector3 along = new Vector3(c.x - a.x, 0f, c.z - a.z) / run;
					var across = new Vector3(along.z, 0f, -along.x);
					Vector3 at = Vector3.Lerp(a, c, v) + across * ((w - 0.5f) * 0.6f * body.Width);
					at.y -= body.Depth + 0.05f;
					BedStone(at, Mathf.Lerp(0.12f, 0.32f, size), turn);
					// The wall's side: away from the lane its channel runs beside.
					if (size < 0.3f && body.Passage >= 0)
					{
						Vector3 side = WallSide(body.Passage, a, across);
						Vector3 bank = a + side * (0.5f * body.Width + 0.5f);
						bank.y = body.Banks[k] + 0.6f;
						if (system.Void(bank) < 0f && FloorAndRoof(bank, out float floor, out _) && !InLane(new Vector3(bank.x, floor, bank.z), 0.3f))
						{
							if (FloorGlow(random, CaveGlowKind.Fungus, colours.Fungus, new Vector3(bank.x, floor, bank.z), Mathf.Lerp(0.7f, 1.2f, w), 5f, false))
							{
								StreamPieces++;
							}
						}
					}
				}
			}

			/// <summary>
			/// Under a spring's crack: flowstone poured down the wall (a limestone cave's: calcite the water laid as it ran) and a
			/// shelf of wet-loving glow fungus beside it.
			/// </summary>
			private void Spring(DeterministicRNG random, Vector3 lip, Vector3 landing, in CaveGlowPalette colours, bool limestone)
			{
				Vector3 into = landing - lip;
				into.y = 0f;
				into = into.sqrMagnitude > 1e-6f ? -into.normalized : Vector3.forward;
				float scale = random.Range(0.5f, 0.9f), yaw = Mathf.Atan2(-into.x, -into.z) * Mathf.Rad2Deg;
				float side = random.NextFloat() < 0.5f ? -1f : 1f;
				GameObject flowstone = limestone ? StreamFormation(random, set.Wall) : null;
				// Down a wall to its foot (a crack a few metres up it, not an aven's in a roof over a way), off the lanes.
				if (flowstone != null && FloorAndRoof(lip - into * 0.2f, out float floor, out _) && lip.y - floor <= 3.6f)
				{
					var foot = new Vector3(lip.x, floor, lip.z) + into * 0.25f;
					if (!InLane(foot, 0.3f) && Place(flowstone, foot, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
					{
						StreamPieces++;
					}
				}
				if (colours.Fungi)
				{
					var across = new Vector3(into.z, 0f, -into.x) * side;
					if (WallGlow(random, CaveGlowKind.ShelfFungus, colours.Fungus, lip - into * 0.6f + across * 0.9f + Vector3.down * 0.5f, into, 2f, random.Range(0.6f, 1f), false))
					{
						StreamPieces++;
					}
				}
			}

			/// <summary>
			/// A pool's dressing: rimstone round part of its rim (a limestone cave's), luminous fungus and stones on the rest, cobbles
			/// on its floor, boulders round a plunge pool; a soft light over a larger one, as if the water itself glowed.
			/// </summary>
			private void Pool(DeterministicRNG random, CaveStreamBody body, in CaveGlowPalette colours, bool limestone, ref int lights)
			{
				Vector3 c = body.Path[0];
				float r = body.Width;
				bool plunge = body.Depth >= CaveStreams.PoolDepthMax - 1e-3f && body.Banks != null;
				int stations = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * r / 1.6f), 4, 28);
				float turn = random.Range(0f, 360f);
				for (int s = 0; s < stations; s++)
				{
					float pick = random.NextFloat(), size = random.NextFloat(), spin = random.Range(0f, 360f);
					Vector3 dir = Dir(turn + 360f * s / stations);
					Vector3 rim = c + dir * (r + 0.35f);
					rim.y = c.y + 0.8f;
					if (system.Void(rim) >= 0f || !FloorAndRoof(rim, out float floor, out float roof) || floor < c.y - 0.05f)
					{
						continue;
					}
					var at = new Vector3(rim.x, floor, rim.z);
					if (InLane(at, 0.4f) || NearMouth(at))
					{
						continue;
					}
					if (plunge && pick < 0.3f)
					{
						BankStone(at + dir * 0.3f, Mathf.Lerp(0.35f, 0.75f, size), spin);
					}
					else if (limestone && set.Pool != null && pick < 0.45f)
					{
						GameObject gour = StreamFormation(random, set.Pool);
						if (gour != null && roof - floor > 1f)
						{
							context.AddProp(gour, system.Origin + at - Vector3.up * 0.05f, Quaternion.Euler(0f, spin, 0f), Vector3.one * Mathf.Lerp(0.35f, 0.6f, size));
							Placed++;
							StreamPieces++;
						}
					}
					else if (colours.Fungi && pick < 0.7f)
					{
						if (FloorGlow(random, CaveGlowKind.Fungus, size < 0.25f ? colours.FungusAccent : colours.Fungus, at + dir * 0.2f, Mathf.Lerp(0.8f, 1.4f, size), 5f, false))
						{
							StreamPieces++;
						}
					}
					else if (pick < 0.85f)
					{
						BankStone(at + dir * 0.25f, Mathf.Lerp(0.15f, 0.35f, size), spin);
					}
				}
				// Cobbles on its floor, seen through the water.
				int cobbles = Mathf.Clamp(Mathf.RoundToInt(r * 1.5f), 1, 8);
				for (int k = 0; k < cobbles; k++)
				{
					float a = random.Range(0f, 360f), d = Mathf.Sqrt(random.NextFloat()) * 0.75f * r, size = random.NextFloat(), spin = random.Range(0f, 360f);
					Vector3 at = c + Dir(a) * d;
					at.y = c.y - 0.1f;
					if (FloorAndRoof(at, out float bed, out _) && bed < c.y - 0.15f)
					{
						BedStone(new Vector3(at.x, bed, at.z), Mathf.Lerp(0.15f, 0.4f, size), spin);
					}
				}
				if (r >= 1.5f && lights < CaveStreamDressing.MaxLights && body.Banks != null)
				{
					lights += WaterLight(c + Vector3.up * 0.9f, colours, 6f + 1.5f * r) ? 1 : 0;
				}
			}

			/// <summary>The side of a stream's channel toward the wall: away from its passage's line.</summary>
			private Vector3 WallSide(int passage, Vector3 p, Vector3 across)
			{
				List<CaveNode> nodes = system.Passages[passage].Nodes;
				float best = float.MaxValue;
				Vector3 nearest = p;
				for (int i = 0; i + 1 < nodes.Count; i++)
				{
					Vector3 a = nodes[i].Centre, b = nodes[i + 1].Centre;
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float d = (ap - ab * t).sqrMagnitude;
					if (d < best)
					{
						best = d;
						nearest = Vector3.Lerp(a, b, t);
					}
				}
				var away = new Vector3(p.x - nearest.x, 0f, p.z - nearest.z);
				return Vector3.Dot(away, across) >= 0f ? across : -across;
			}

			/// <summary>A water-worn stone on a stream's or pool's bed (under the water: not through <see cref="Place"/>).</summary>
			private void BedStone(Vector3 at, float scale, float spin)
			{
				GameObject stone = StreamFormation(null, "Boulder", spin);
				if (stone == null)
				{
					return;
				}
				context.AddProp(stone, system.Origin + at, Quaternion.Euler(0f, spin, 0f), new Vector3(scale * 1.2f, scale * 0.6f, scale));
				Placed++;
				StreamPieces++;
			}

			/// <summary>A stone on a bank (through <see cref="Place"/>: rock may stand at the water's edge).</summary>
			private void BankStone(Vector3 at, float scale, float spin)
			{
				if (FloorAndRoof(at + Vector3.up * 0.5f, out float floor, out float roof) && roof - floor > scale + 0.5f
					&& Place(StreamFormation(null, "Boulder", spin), new Vector3(at.x, floor - 0.05f * scale, at.z), Quaternion.Euler(0f, spin, 0f), new Vector3(scale, scale * 0.7f, scale * 1.1f)))
				{
					StreamPieces++;
				}
			}

			/// <summary>
			/// A formation of the cave's rock for the water's dressing: its variant drawn from <paramref name="random"/> (never the
			/// dressing's own stream), or from a hash of <paramref name="salt"/> where none is given; null while its art is missing.
			/// </summary>
			private GameObject StreamFormation(DeterministicRNG random, string shape, float salt = 0f)
			{
				int variant = random != null ? random.Range(0, RockTypes.VariantCount)
					: (int)(PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(salt * 10f), shape.Length, 0x57E) % (uint)RockTypes.VariantCount);
				(string rock, string artShape) = FormationArt(set.Rock, shape, system.Form);
				string name = RockArtNames.FormationPrefab(rock, artShape, variant);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				if (prefab == null)
				{
					Missing.Add($"{rock} {artShape}");
				}
				return prefab;
			}

			/// <summary>
			/// A soft light over the water in the cave's own glow (its luminous fungus's hue), where none of the system's lights is
			/// within <see cref="CaveStreamDressing.LightSpacing"/>; false where none is laid.
			/// </summary>
			private bool WaterLight(Vector3 local, in CaveGlowPalette colours, float reach)
			{
				if (!CaveArt.TryGlow(colours.Fungi ? CaveGlowKind.Fungus : CaveGlowKind.Crystal, colours.Fungi ? colours.Fungus : colours.Crystal, 0, out CaveGlowArt art)
					|| art.EmitterHue < 0 || system.Void(local) >= 0f)
				{
					return false;
				}
				foreach (Vector3 lit in glowLights)
				{
					if ((lit - local).sqrMagnitude < CaveStreamDressing.LightSpacing * CaveStreamDressing.LightSpacing)
					{
						return false;
					}
				}
				uint seed = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(local.x * 10f), Mathf.RoundToInt(local.z * 10f), 0x57C7);
				Emit(local, SceneEmitterKind.Crystal, CaveArt.EmitterSeed(seed, art.EmitterHue), Mathf.Clamp(reach, 6f, 14f));
				StreamLights++;
				return true;
			}
		}
	}

	/// <summary>What the water's dressing needs to know of a cave's water, pure (<see cref="CaveStreams.Plan"/>).</summary>
	public static class CaveStreamDressing
	{
		/// <summary>The most lights a system's water is given (over its pools and at its falls' feet).</summary>
		public const int MaxLights = 6;
		/// <summary>The most pieces a system's water is dressed with (stones, rimstone, flowstone, glow).</summary>
		public const int MaxPieces = 400;
		/// <summary>The share of a stream's metres a stone is laid in its bed at (a stone every three metres or so).</summary>
		public const float BedStoneShare = 0.3f;
		/// <summary>The least distance from a water light to any other light of the system, metres (the glow's own spacing).</summary>
		public const float LightSpacing = 9f;

		/// <summary>
		/// Whether a point (system frame) lies in a plan's water: inside a body's footprint (a stream's half-width, a pool's radius,
		/// and <paramref name="margin"/> more) and under its surface there (and <paramref name="above"/> more), down to 30 m under
		/// it (a level below is not the water's). A stream's falls are left out (a fall's foot is its pool's). False for a dry or
		/// frozen plan.
		/// </summary>
		public static bool InWater(CaveStreamPlan plan, Vector3 p, float margin = 0.2f, float above = 0.1f)
		{
			if (plan == null || !plan.Active || plan.Frozen)
			{
				return false;
			}
			foreach (CaveStreamBody body in plan.Bodies)
			{
				Vector3[] path = body.Path;
				if (path == null || path.Length == 0)
				{
					continue;
				}
				if (body.Kind == CaveStreamKind.Pool)
				{
					Vector3 c = path[0];
					float r = body.Width + margin;
					if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude < r * r && p.y < c.y + above && p.y > c.y - 30f)
					{
						return true;
					}
					continue;
				}
				float reach = 0.5f * body.Width + margin;
				for (int i = 0; i + 1 < path.Length; i++)
				{
					if (body.Falls != null && body.Falls[i + 1])
					{
						continue;
					}
					Vector3 a = path[i], b = path[i + 1];
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float y = Mathf.Lerp(a.y, b.y, t);
					if ((ap - ab * t).sqrMagnitude < reach * reach && p.y < y + above && p.y > y - 30f)
					{
						return true;
					}
				}
			}
			return false;
		}
	}
}
#endif
