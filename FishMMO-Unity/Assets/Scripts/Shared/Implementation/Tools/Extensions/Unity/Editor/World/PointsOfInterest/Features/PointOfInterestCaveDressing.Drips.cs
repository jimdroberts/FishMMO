#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>What the drips pass laid: drips, and the puddles among them.</summary>
			public int Dripped, Puddled;

			/// <summary>
			/// Drips from the roof (2026-10-20, caves livened; PointOfInterestCaveDressing.Drips.cs): the stalactites and flowstone the
			/// passes before laid, and the wet patches of the roof (<see cref="CaveHabitat"/>), drip into puddles of their own where the
			/// floor under them holds water, else onto the wet rock or the stalagmite they have built (<see cref="CaveDrips"/>). Each
			/// drip is one emitter (<see cref="SceneEmitterKind.Drip"/>): the client's drops, rings and sound read it. On its own
			/// stream, after every earlier pass, and it lays no props, so nothing laid before it moves.
			/// </summary>
			private void Drips()
			{
				if (context.Points == null)
				{
					return;
				}
				List<CaveDripHang> hangs = HangsLaid();
				CaveDripLayout layout = CaveDrips.Lay(system, habitat ??= CaveHabitat.For(system), hangs, (p, margin) => DripRefused(p, margin));
				context.Points.Emitters ??= new List<SceneEmitter>();
				foreach (CaveDrip drip in layout.Drips)
				{
					context.Points.Emitters.Add(new SceneEmitter { Position = system.Origin + drip.Landing, Kind = SceneEmitterKind.Drip, Seed = drip.Seed, Reach = drip.Height });
				}
				Dripped = layout.Drips.Count;
				Puddled = layout.Puddles;
				if (Dripped > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"cave drips: {Dripped} ({layout.FromStalactites} from stalactites, {layout.FromFlowstone} from flowstone, {layout.FromRoof} from the wet roof), {Puddled} puddles");
				}
			}

			/// <summary>
			/// The stalactites and flowstone the passes before laid (the site's props as laid, by their formation's shape and how they
			/// are turned), each with the top of a stalagmite standing under a stalactite (its drops land on it), system frame.
			/// </summary>
			private List<CaveDripHang> HangsLaid()
			{
				var hangs = new List<CaveDripHang>();
				if (context.Laid == null)
				{
					return hangs;
				}
				string hanging = set.Hanging != null ? "_" + set.Hanging + "_" : null;
				string standing = set.Floor != null ? "_" + set.Floor + "_" : null;
				string flowstone = set.Wall != null ? "_" + set.Wall + "_" : null;
				var tops = new List<(Vector3 at, float width)>();
				foreach (PointOfInterestSiteContext.PlacedProp prop in context.Laid)
				{
					string name = prop.Prefab != null ? prop.Prefab.name : null;
					if (standing == null || name == null || !name.StartsWith("Formation_", StringComparison.Ordinal) || !name.Contains(standing))
					{
						continue;
					}
					if ((prop.Rotation * Vector3.up).y > 0.5f)
					{
						Vector3 local = prop.Position - system.Origin;
						tops.Add((local + Vector3.up * (Mathf.Abs(prop.Scale.y) * set.FloorHeight), Mathf.Abs(prop.Scale.x)));
					}
				}
				foreach (PointOfInterestSiteContext.PlacedProp prop in context.Laid)
				{
					string name = prop.Prefab != null ? prop.Prefab.name : null;
					if (name == null || !name.StartsWith("Formation_", StringComparison.Ordinal))
					{
						continue;
					}
					Vector3 local = prop.Position - system.Origin;
					float up = (prop.Rotation * Vector3.up).y;
					if (hanging != null && name.Contains(hanging) && up < -0.5f)
					{
						// A stalactite hangs from the roof by its pivot, its tip its height under it.
						float width = Mathf.Abs(prop.Scale.x);
						Vector3 tip = local - Vector3.up * (Mathf.Abs(prop.Scale.y) * set.FloorHeight);
						float landOn = float.NaN;
						foreach ((Vector3 at, float topWidth) in tops)
						{
							float reach = 0.3f * Mathf.Max(width, topWidth) + 0.15f;
							if (at.y < tip.y && new Vector2(at.x - tip.x, at.z - tip.z).sqrMagnitude < reach * reach && (float.IsNaN(landOn) || at.y > landOn))
							{
								landOn = at.y;
							}
						}
						hangs.Add(new CaveDripHang { Tip = tip, Roof = local.y, LandOn = landOn });
					}
					else if (flowstone != null && name.Contains(flowstone) && up > 0.5f)
					{
						// Flowstone drips off the lip of its lower lobes, a little out from the wall it coats.
						Vector3 facing = prop.Rotation * Vector3.forward;
						facing.y = 0f;
						facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
						float s = Mathf.Abs(prop.Scale.y);
						Vector3 lip = local + facing * (0.25f + 0.3f * s) + Vector3.up * (0.55f * set.WallHeight * s);
						hangs.Add(new CaveDripHang { Tip = lip, Roof = float.NaN, LandOn = float.NaN, Flowstone = true, Facing = facing });
					}
				}
				return hangs;
			}

			/// <summary>
			/// Where a drip may not land (a point on a floor, system frame, and the puddle's reach round it): under the cave's own water,
			/// its streams and pools or the scene's, in or beside molten lava.
			/// </summary>
			private bool DripRefused(Vector3 p, float margin)
			{
				if (Submerged(p) || InLava(p))
				{
					return true;
				}
				if (LavaPlan.Active && CaveLavaDressing.DistanceToLava(LavaPlan, p, 4f) < margin + 1.5f)
				{
					return true;
				}
				// The cave's own streams and pools (CaveStreams.Plan; the scene's hydrology leaves them out of its plan view): a puddle
				// beside one would stand in it.
				if (StreamPlan.Active && CaveStreamDressing.InWater(StreamPlan, p, margin + 0.3f, 0.3f))
				{
					return true;
				}
				if (context.Water != null)
				{
					Vector3 world = system.Origin + p;
					float surface = context.Water.SurfaceAt(world.x, world.z);
					// A stream's or pool's surface just over this floor (a body far over it is another level's).
					if (!float.IsNegativeInfinity(surface) && surface > world.y - 0.1f && surface < world.y + 6f)
					{
						return true;
					}
				}
				return false;
			}
		}
	}

	/// <summary>Where a drip may come from that the dressing laid: a stalactite's tip, or the lip of a flowstone sheet (system frame).</summary>
	public struct CaveDripHang
	{
		/// <summary>Where the drops let go: a stalactite's tip, a flowstone's lip.</summary>
		public Vector3 Tip;
		/// <summary>A stalactite's roof (its pivot's height), where its wetness is read; NaN for flowstone.</summary>
		public float Roof;
		/// <summary>The top of the stalagmite under a stalactite, its drops' landing; NaN where they fall to the floor.</summary>
		public float LandOn;
		/// <summary>A flowstone's lip, not a stalactite: wet as its wall, facing <see cref="Facing"/> out of it.</summary>
		public bool Flowstone;
		public Vector3 Facing;
	}

	/// <summary>Where a drip comes from.</summary>
	public enum CaveDripSource : byte
	{
		Roof,
		Stalactite,
		Flowstone,
	}

	/// <summary>One drip (<see cref="CaveDrips.Lay"/>), system frame.</summary>
	public struct CaveDrip
	{
		/// <summary>Where its drops land: its puddle's surface, the floor, or a stalagmite's tip.</summary>
		public Vector3 Landing;
		/// <summary>How far they fall, metres.</summary>
		public float Height;
		/// <summary>Seconds between drops on the even beat (as the seed stores it).</summary>
		public float Interval;
		/// <summary>Its puddle's radius, metres; 0 where it has none.</summary>
		public float Puddle;
		public SceneDripLanding Lands;
		public CaveDripSource Source;
		/// <summary>The emitter's seed (<see cref="SceneDrip.Seed"/>).</summary>
		public uint Seed;
	}

	/// <summary>What <see cref="CaveDrips.Lay"/> lays: the drips, and how many came from where.</summary>
	public sealed class CaveDripLayout
	{
		public readonly List<CaveDrip> Drips = new List<CaveDrip>();
		public int FromRoof, FromStalactites, FromFlowstone, Puddles;
	}

	/// <summary>
	/// Where a cave drips (2026-10-20, caves livened: "drips from the roof … and the puddles they fall in"): the drip sources and the
	/// puddles under them, laid from the system's field and its habitat.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where water comes through.</b> Water seeps down through the rock along its joints and drips where it reaches the roof: off
	/// the tips of the stalactites it has built (most of them still dripping where the roof is wet), off the lower lobes of flowstone,
	/// and from the bare roof where it is wet (<see cref="CaveHabitat"/>'s Wet, read on the roof with its normal down: the same field
	/// that darkens the rock and grows the moss, so the drips fall where the rock is darkest). A dry lava tube, an ice cave's frozen
	/// reach and a dry mine drip little or not at all, by the habitat alone.
	/// </para>
	/// <para>
	/// <b>How often.</b> A wetter roof drips faster: from about every six seconds on a damp one to under a second on a streaming one,
	/// each drip its own pace; never so fast that more than three of its drops are in the air (<see cref="SceneDrip.InFlight"/>).
	/// </para>
	/// <para>
	/// <b>Where they land.</b> The floor under the drip (or the top of the stalagmite under a stalactite). Where the floor holds water
	/// round it the drip has a puddle: a level water surface a few centimetres over the floor where they land, as wide as the floor
	/// round it stays within a few centimetres under that level and none of it more (water would run off). The floor that rises
	/// through the level shapes the puddle's edge (the client draws the level, the rock covers it). A puddle never spills over a
	/// lip, never stands in a stream or a pool already there, and never by lava (the caller's refusal). Where the floor slopes the
	/// drops splat on wet rock.
	/// </para>
	/// <para>
	/// <b>Budget.</b> At most <see cref="MostDrips"/> drips a system (<see cref="MostFromRoof"/> from the bare roof), at least
	/// <see cref="Spacing"/> apart and their puddles clear of each other: the client shows the nearest two dozen. Pure: a function of
	/// the system, its habitat, what was laid and what the caller refuses, on a stream of its own; every candidate draws the same
	/// numbers whether it drips or not.
	/// </para>
	/// </remarks>
	public static class CaveDrips
	{
		/// <summary>The most drips a system is given, and the most of them from the bare roof.</summary>
		public const int MostDrips = 60, MostFromRoof = 36;
		/// <summary>The least distance between two drips' landings, metres (and their puddles' radii more).</summary>
		public const float Spacing = 1.6f;
		/// <summary>How deep a puddle stands over the floor where its drops land, metres.</summary>
		public const float PuddleDepth = 0.04f;
		/// <summary>How far under its level the floor may lie anywhere in a puddle, metres: deeper, and the water would run away.</summary>
		public const float PuddleHold = 0.06f;
		/// <summary>The smallest puddle worth one, metres.</summary>
		public const float LeastPuddle = 0.18f;
		/// <summary>The roof's wetness over which it drips at all, and at which it surely does.</summary>
		public const float WetFrom = 0.35f, WetFull = 0.9f;
		/// <summary>The beat of the slowest and the fastest drips, seconds (on the damp roof, on the streaming one).</summary>
		public const float SlowBeat = 6f, FastBeat = 0.8f;

		/// <summary>
		/// The drips of a system: <paramref name="hangs"/> are the stalactites and flowstone already laid (in order), then the wet roof
		/// over its chambers and passages. <paramref name="refused"/> (a landing, its puddle's reach) is true where none may land.
		/// </summary>
		public static CaveDripLayout Lay(CaveSystem system, CaveHabitat habitat, IReadOnlyList<CaveDripHang> hangs, Func<Vector3, float, bool> refused = null)
		{
			var layout = new CaveDripLayout();
			if (system == null || habitat == null)
			{
				return layout;
			}
			new Layer(system, habitat, refused, layout).Run(hangs);
			return layout;
		}

		/// <summary>
		/// The level and radius of the puddle a drip landing on the floor at <paramref name="floor"/> (system frame) has, the largest
		/// of a few that holds (<see cref="PuddleHold"/>, and enough of it under water to be one); false where none does.
		/// </summary>
		public static bool Puddle(CaveSystem system, Vector3 floor, float most, out float level, out float radius)
		{
			level = floor.y + PuddleDepth;
			radius = 0f;
			for (float r = most; r >= LeastPuddle; r *= 0.75f)
			{
				if (Holds(system, floor, level, r))
				{
					radius = r;
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether water at <paramref name="level"/> stands over a disc of radius <paramref name="r"/> round a floor point.</summary>
		private static bool Holds(CaveSystem system, Vector3 floor, float level, float r)
		{
			const int Directions = 12;
			int under = 0, inner = 0;
			for (int ring = 0; ring < 2; ring++)
			{
				float d = ring == 0 ? 0.5f * r : r;
				for (int i = 0; i < Directions; i++)
				{
					float a = (i + 0.5f * ring) * (2f * Mathf.PI / Directions);
					var p = new Vector3(floor.x + Mathf.Cos(a) * d, level, floor.z + Mathf.Sin(a) * d);
					// Rock at the level: the floor (or a wall) rises through the water there, which only shapes its edge.
					if (system.Void(p + Vector3.up * 0.02f) >= 0f)
					{
						continue;
					}
					if (!FloorBelow(system, p + Vector3.up * 0.02f, PuddleHold + 0.3f, out float y) || y < level - PuddleHold)
					{
						return false;
					}
					if (ring == 0)
					{
						inner++;
						if (y < level)
						{
							under++;
						}
					}
				}
			}
			// Enough of the middle under water to be a puddle, not a film on a hump.
			return inner > 0 && under * 2 >= inner;
		}

		/// <summary>The floor under a point in the void within <paramref name="reach"/> (to a few millimetres); false where it is rock or none is found.</summary>
		public static bool FloorBelow(CaveSystem system, Vector3 p, float reach, out float floor)
		{
			floor = p.y;
			if (system.Void(p) >= 0f)
			{
				return false;
			}
			float step = Mathf.Min(0.25f, Mathf.Max(0.05f, 0.25f * reach));
			float y = p.y;
			while (system.Void(new Vector3(p.x, y - step, p.z)) < 0f)
			{
				y -= step;
				if (p.y - y > reach)
				{
					return false;
				}
			}
			float lo = y - step, hi = y;
			for (int k = 0; k < 6; k++)
			{
				float mid = 0.5f * (lo + hi);
				if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
				{
					hi = mid;
				}
				else
				{
					lo = mid;
				}
			}
			floor = 0.5f * (lo + hi);
			return true;
		}

		/// <summary>The roof over a point in the void within <paramref name="reach"/>; false where it is rock or none is found.</summary>
		public static bool RoofAbove(CaveSystem system, Vector3 p, float reach, out float roof)
		{
			roof = p.y;
			if (system.Void(p) >= 0f)
			{
				return false;
			}
			const float Step = 0.5f;
			float y = p.y;
			while (system.Void(new Vector3(p.x, y + Step, p.z)) < 0f)
			{
				y += Step;
				if (y - p.y > reach)
				{
					return false;
				}
			}
			float lo = y, hi = y + Step;
			for (int k = 0; k < 6; k++)
			{
				float mid = 0.5f * (lo + hi);
				if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			roof = 0.5f * (lo + hi);
			return true;
		}

		private sealed class Layer
		{
			private readonly CaveSystem system;
			private readonly CaveHabitat habitat;
			private readonly Func<Vector3, float, bool> refused;
			private readonly CaveDripLayout layout;
			private readonly DeterministicRNG rng;
			private readonly List<(Vector3 at, float radius)> taken = new List<(Vector3, float)>();

			public Layer(CaveSystem system, CaveHabitat habitat, Func<Vector3, float, bool> refused, CaveDripLayout layout)
			{
				this.system = system;
				this.habitat = habitat;
				this.refused = refused;
				this.layout = layout;
				rng = new DeterministicRNG(unchecked(system.Seed * 0x4E1B + 0x7D21));
			}

			public void Run(IReadOnlyList<CaveDripHang> hangs)
			{
				if (hangs != null)
				{
					foreach (CaveDripHang hang in hangs)
					{
						Hang(hang);
					}
				}
				for (int c = 0; c < system.Chambers.Count; c++)
				{
					Chamber(system.Chambers[c]);
				}
				foreach (CavePassage passage in system.Passages)
				{
					Passage(passage);
				}
			}

			private bool Full => layout.Drips.Count >= MostDrips;

			/// <summary>A stalactite's tip or a flowstone's lip: dripping by the wetness of the rock it hangs from.</summary>
			private void Hang(CaveDripHang hang)
			{
				float roll = rng.NextFloat(), pace = rng.NextFloat(), size = rng.NextFloat();
				if (Full)
				{
					return;
				}
				float wet = hang.Flowstone
					? habitat.Sample(hang.Tip - hang.Facing * 0.3f, hang.Facing).Wet
					: habitat.Sample(new Vector3(hang.Tip.x, float.IsNaN(hang.Roof) ? hang.Tip.y : hang.Roof, hang.Tip.z), Vector3.down).Wet;
				// Most stalactites on a wet roof still grow (they drip); on a dry one few do.
				float chance = hang.Flowstone ? 0.15f + 0.6f * wet : 0.2f + 0.7f * wet;
				if (roll >= chance)
				{
					return;
				}
				Vector3 from = hang.Tip - Vector3.up * 0.03f;
				if (!float.IsNaN(hang.LandOn))
				{
					// Onto the stalagmite it has built (a column where the two have met: no drop falls).
					float fall = from.y - hang.LandOn;
					if (fall < SceneDrip.LeastHeight)
					{
						return;
					}
					Add(new Vector3(from.x, hang.LandOn, from.z), fall, 0f, SceneDripLanding.Tip, wet, pace, hang.Flowstone ? CaveDripSource.Flowstone : CaveDripSource.Stalactite);
					return;
				}
				Land(from, wet, pace, size, hang.Flowstone ? CaveDripSource.Flowstone : CaveDripSource.Stalactite);
			}

			/// <summary>Points over a chamber's floor (its outer nine tenths), each dripping where the roof over it is wet.</summary>
			private void Chamber(CaveChamber chamber)
			{
				float cy = chamber.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				var across = new Vector3(along.z, 0f, -along.x);
				float area = Mathf.PI * chamber.Radii.x * chamber.Radii.z;
				int count = Mathf.Clamp(Mathf.RoundToInt(area / 25f), 3, 60);
				for (int i = 0; i < count; i++)
				{
					float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.Range(0f, 0.9f));
					Vector3 p = chamber.Centre + along * (Mathf.Cos(a) * t * chamber.Radii.x) + across * (Mathf.Sin(a) * t * chamber.Radii.z);
					p.y = CaveSystem.FloorOf(chamber, p) + 0.8f;
					Roof(p);
				}
			}

			/// <summary>Points along a passage every 4–8 m, somewhere across its floor, each dripping where the roof over it is wet.</summary>
			private void Passage(CavePassage passage)
			{
				List<CaveNode> nodes = passage.Nodes;
				if (nodes.Count < 2)
				{
					return;
				}
				float end = nodes[nodes.Count - 1].Along;
				for (float along = nodes[0].Along + rng.Range(2f, 6f); along < end; along += rng.Range(4f, 8f))
				{
					int i = 1;
					while (i < nodes.Count - 1 && nodes[i].Along < along)
					{
						i++;
					}
					CaveNode a = nodes[i - 1], b = nodes[i];
					float t = Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along));
					Vector3 centre = Vector3.Lerp(a.Centre, b.Centre, t);
					Vector3 dir = b.Centre - a.Centre;
					dir.y = 0f;
					dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
					var side = new Vector3(dir.z, 0f, -dir.x);
					float radius = Mathf.Lerp(a.Radius, b.Radius, t);
					Vector3 p = centre + side * (rng.Range(-0.7f, 0.7f) * radius);
					p.y = Mathf.Lerp(a.Floor, b.Floor, t) + 0.8f;
					Roof(p);
				}
			}

			/// <summary>The roof over a point: a drip where it is wet enough, by chance.</summary>
			private void Roof(Vector3 p)
			{
				float roll = rng.NextFloat(), pace = rng.NextFloat(), size = rng.NextFloat();
				if (Full || layout.FromRoof >= MostFromRoof || !RoofAbove(system, p, SceneDrip.MostHeight, out float roof))
				{
					return;
				}
				float wet = habitat.Sample(new Vector3(p.x, roof, p.z), Vector3.down).Wet;
				float chance = 0.6f * Smooth(WetFrom, WetFull, wet);
				if (roll >= chance)
				{
					return;
				}
				Land(new Vector3(p.x, roof - 0.03f, p.z), wet, pace, size, CaveDripSource.Roof);
			}

			/// <summary>A drop let go at <paramref name="from"/> falls to the floor: into a puddle there where it holds, else onto the rock.</summary>
			private void Land(Vector3 from, float wet, float pace, float size, CaveDripSource source)
			{
				if (!FloorBelow(system, from, SceneDrip.MostHeight, out float floor) || from.y - floor < SceneDrip.LeastHeight)
				{
					return;
				}
				var landing = new Vector3(from.x, floor, from.z);
				// Wetter rock, and a drip that has run longer, wets a wider puddle.
				float most = Mathf.Lerp(0.35f, 1.1f, Mathf.Clamp01(0.5f * wet + 0.6f * size));
				if (Puddle(system, landing, most, out float level, out float radius) && !Crowded(landing, radius) && !Refused(landing, radius))
				{
					Add(new Vector3(from.x, level, from.z), from.y - level, radius, SceneDripLanding.Puddle, wet, pace, source);
					return;
				}
				Add(landing, from.y - floor, 0f, SceneDripLanding.Rock, wet, pace, source);
			}

			private void Add(Vector3 landing, float height, float puddle, SceneDripLanding lands, float wet, float pace, CaveDripSource source)
			{
				if (height > SceneDrip.MostHeight || Crowded(landing, puddle) || Refused(landing, puddle))
				{
					return;
				}
				// Wetter rock drips faster; each drip its own pace round that; never so fast its drops crowd the air.
				float beat = Mathf.Lerp(SlowBeat, FastBeat, Smooth(WetFrom, 1f, wet)) * Mathf.Lerp(0.7f, 1.4f, pace);
				float interval = Mathf.Clamp(Mathf.Max(beat, SceneDrip.LeastIntervalFor(height)), SceneDrip.LeastInterval, SceneDrip.LeastInterval + 255f * SceneDrip.IntervalStep);
				// Stored in its steps, rounded up so the stored beat still keeps the drops apart.
				interval = SceneDrip.LeastInterval + Mathf.Ceil((interval - SceneDrip.LeastInterval) / SceneDrip.IntervalStep - 1e-3f) * SceneDrip.IntervalStep;
				uint hash = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(landing.x * 10f), Mathf.RoundToInt(landing.z * 10f), 0xD1);
				uint seed = SceneDrip.Seed(hash, interval, puddle, lands);
				layout.Drips.Add(new CaveDrip
				{
					Landing = landing,
					Height = height,
					Interval = SceneDrip.IntervalOf(seed),
					Puddle = SceneDrip.PuddleOf(seed),
					Lands = lands,
					Source = source,
					Seed = seed,
				});
				taken.Add((landing, SceneDrip.PuddleOf(seed)));
				if (lands == SceneDripLanding.Puddle)
				{
					layout.Puddles++;
				}
				switch (source)
				{
					case CaveDripSource.Stalactite:
						layout.FromStalactites++;
						break;
					case CaveDripSource.Flowstone:
						layout.FromFlowstone++;
						break;
					default:
						layout.FromRoof++;
						break;
				}
			}

			/// <summary>Within <see cref="Spacing"/> (and both puddles' radii) of a drip already laid, on the same level.</summary>
			private bool Crowded(Vector3 p, float radius)
			{
				foreach ((Vector3 at, float r) in taken)
				{
					float reach = Spacing + radius + r;
					if (Mathf.Abs(at.y - p.y) < 2.5f && new Vector2(at.x - p.x, at.z - p.z).sqrMagnitude < reach * reach)
					{
						return true;
					}
				}
				return false;
			}

			private bool Refused(Vector3 p, float margin) => refused != null && refused(p, margin);

			private static float Smooth(float a, float b, float x)
			{
				float t = Mathf.Clamp01((x - a) / (b - a));
				return t * t * (3f - 2f * t);
			}
		}
	}
}
#endif
