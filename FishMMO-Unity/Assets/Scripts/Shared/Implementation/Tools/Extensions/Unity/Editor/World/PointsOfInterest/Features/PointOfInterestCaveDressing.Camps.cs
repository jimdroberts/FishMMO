#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Those who came this way before (2026-10-13: "Torches, lanterns, hanging lanterns, encampments"): now and then an
	/// explorers' camp in a great room off the ways (its fire, its bedrolls, its stores, a lantern on a stand), and lanterns
	/// on stands at a bridge's or a climb's head, marking an adventurers' route. A mine's miners' camp is pitched the same.
	/// </summary>
	/// <remarks>
	/// Never in a site's own chamber (its camp or den is there already), never on a way, never in a climb's step-off. A
	/// quarter of the rooms that could take a camp have one: an underground camp is a find, not a feature of every room.
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The share of the rooms large enough that have a camp in them.</summary>
			private const float CampChance = 0.25f;

			/// <summary>The camps and the route's lanterns of a natural cave.</summary>
			private void Encampments()
			{
				var random = new DeterministicRNG(unchecked(system.Seed * 0x7C35 + 0x0E4B));
				foreach (CaveChamber c in system.Chambers)
				{
					bool pitch = random.NextFloat() < CampChance;
					if (!pitch || c.Kind == CaveChamberKind.Alcove || c.Kind == CaveChamberKind.Stope || Mathf.Min(c.Radii.x, c.Radii.z) < 6.5f || IsOwn(c))
					{
						continue;
					}
					for (int tries = 0; tries < 10; tries++)
					{
						Vector3 p = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.35f, 0.72f));
						if (CampGround(p, 2.4f, 2.6f, out Vector3 at) && Camp(random, at, 2.2f, miners: false))
						{
							break;
						}
					}
				}
				RouteLanterns(random);
			}

			/// <summary>
			/// Whether a spot near a point is fit for a camp: the floor found, near level round it out to
			/// <paramref name="radius"/>, headroom over it, and clear of every lane and way by <paramref name="margin"/>.
			/// </summary>
			private bool CampGround(Vector3 near, float radius, float margin, out Vector3 at)
			{
				at = near;
				if (!Ground(near + Vector3.up * 1.2f, 3f, out float floor))
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				if (!Clear(at, margin) || system.Void(at + Vector3.up * 3f) >= 0f)
				{
					return false;
				}
				for (int k = 0; k < 6; k++)
				{
					Vector3 q = at + Dir(k * 60f) * radius;
					if (!Ground(q + Vector3.up * 1f, 1.6f, out float f) || Mathf.Abs(f - floor) > 0.35f)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>
			/// A camp round a fire at a spot on the floor: its fire (a cooking fire, or a ring of stones), one to three beds
			/// with their feet to it, its stores, a lantern on a stand leaning in over it. Each piece is set on the floor where
			/// it stands and dropped where it would lie on a way; false (nothing laid) where the fire itself cannot be.
			/// </summary>
			private bool Camp(DeterministicRNG random, Vector3 at, float ring, bool miners)
			{
				bool cooking = miners || random.NextFloat() < 0.5f;
				int fireVariant = random.Range(0, 2);
				int beds = miners ? 2 : random.Range(1, 4);
				float a0 = random.Range(0f, 360f), fireYaw = random.Range(0f, 360f);
				if (!Clear(at, 0.6f))
				{
					return false;
				}
				if (!Lay(cooking ? CaveArt.CookingFire : "CampfireRing", fireVariant, at, Quaternion.Euler(0f, fireYaw, 0f), Vector3.one))
				{
					return false;
				}
				Camps++;
				// The beds on one side of the fire, the stores and the lantern on the other.
				float spread = 360f / (beds + 3);
				for (int b = 0; b < beds; b++)
				{
					float a = a0 + b * spread + random.Range(-10f, 10f);
					bool straw = miners || random.NextFloat() < 0.4f;
					int v = random.Range(0, 2);
					Vector3 outward = Dir(a);
					if (CampPiece(at + outward * (ring + 0.1f), at.y, out Vector3 bed))
					{
						// Head away from the fire (+z), feet to it.
						Lay(straw ? CaveArt.StrawBed : "Bedroll", straw ? v : 0, bed, Quaternion.LookRotation(outward, Vector3.up), Vector3.one);
					}
				}
				float rest = a0 + beds * spread + 0.5f * spread;
				Vector3 store = Dir(rest + random.Range(0f, 0.5f * spread));
				bool barrel = random.NextFloat() < 0.5f;
				float storeYaw = random.Range(0f, 360f);
				if (CampPiece(at + store * (ring + 0.3f), at.y, out Vector3 storeAt))
				{
					Lay(barrel ? "Barrel" : "Crate", random.NextFloat() < 0.25f ? 1 : 0, storeAt, Quaternion.Euler(0f, storeYaw, 0f), Vector3.one);
				}
				if (miners)
				{
					Vector3 more = Dir(rest + spread);
					if (CampPiece(at + more * (ring + 0.4f), at.y, out Vector3 moreAt))
					{
						Lay(barrel ? "Crate" : "Barrel", 0, moreAt, Quaternion.Euler(0f, storeYaw + 30f, 0f), Vector3.one);
					}
					Vector3 water = Dir(rest - 0.5f * spread);
					if (CampPiece(at + water * (ring - 0.3f), at.y, out Vector3 waterAt))
					{
						Lay(CaveArt.WaterBucket, 0, waterAt, Quaternion.Euler(0f, storeYaw, 0f), Vector3.one);
					}
				}
				// The lantern's stand outward of the fire, its arm (−z) leaning in over the camp.
				Vector3 stand = Dir(rest + 1.4f * spread);
				int standVariant = random.Range(0, 2);
				if (CampPiece(at + stand * (ring - 0.2f), at.y, out Vector3 standAt) && system.Void(standAt + Vector3.up * 2.4f) < 0f
					&& Lay(CaveArt.LanternStand, standVariant, standAt, Quaternion.LookRotation(stand, Vector3.up), Vector3.one))
				{
					Lamps++;
				}
				return true;
			}

			/// <summary>A camp piece's spot: on the floor near the camp's level, off every lane and way, with room over it.</summary>
			private bool CampPiece(Vector3 near, float level, out Vector3 at)
			{
				at = near;
				if (!Ground(new Vector3(near.x, level + 1f, near.z), 1.8f, out float floor) || Mathf.Abs(floor - level) > 0.45f)
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				return Clear(at, 0f) && system.Void(at + Vector3.up * 1.3f) < 0f;
			}

			/// <summary>
			/// An adventurers' route marked now and then: a lantern on a stand beside each end of a bridge or a crossing, beside
			/// a climb's head, its arm leaning in over the way, always out of the step-off.
			/// </summary>
			private void RouteLanterns(DeterministicRNG random)
			{
				var ends = new List<(Vector3 end, Vector3 toward, float half)>();
				foreach (CaveSolid s in system.Solids)
				{
					if (s.Kind == CaveSolidKind.Bridge && random.NextFloat() < 0.4f)
					{
						ends.Add((s.A, Flat(s.B - s.A), s.Width + 0.6f));
						ends.Add((s.B, Flat(s.A - s.B), s.Width + 0.6f));
					}
				}
				foreach (CaveCrossing c in system.Crossings)
				{
					if (random.NextFloat() < 0.4f)
					{
						// Beside the rope walk's posts (0.85 m out) and its landing.
						ends.Add((c.A, Flat(c.B - c.A), c.HalfWidth + 1f));
						ends.Add((c.B, Flat(c.A - c.B), c.HalfWidth + 1f));
					}
				}
				foreach ((Vector3 end, Vector3 toward, float half) in ends)
				{
					Vector3 across = new Vector3(toward.z, 0f, -toward.x) * (random.NextFloat() < 0.5f ? 1f : -1f);
					int variant = random.Range(0, 2);
					for (int k = 0; k < 2; k++, across = -across)
					{
						Vector3 p = end - toward * 0.8f + across * (half + 0.6f);
						if (RouteSpot(p, end.y, out Vector3 at))
						{
							if (Lay(CaveArt.LanternStand, variant, at, Quaternion.LookRotation(across, Vector3.up), Vector3.one))
							{
								Lamps++;
							}
							break;
						}
					}
				}
				foreach (CaveClimb climb in system.Climbs)
				{
					bool mark = random.NextFloat() < 0.35f;
					int variant = random.Range(0, 2);
					float hand = random.NextFloat() < 0.5f ? 1f : -1f;
					if (!mark)
					{
						continue;
					}
					Vector3 exit = Flat(climb.TopExit);
					Vector3 across = new Vector3(exit.z, 0f, -exit.x);
					bool laid = false;
					foreach (float forward in new[] { 1f, 1.8f })
					{
						for (int k = 0; k < 2 && !laid; k++)
						{
							Vector3 side = across * (k == 0 ? hand : -hand);
							Vector3 p = climb.Top + exit * forward + side * 1.7f;
							// Never in the step-off: from the top a pace along the exit, and well to its side.
							if (NearLine(p, climb.Top, climb.Top + exit * 2.5f, 1.4f, 2f) || !RouteSpot(p, climb.Top.y, out Vector3 at))
							{
								continue;
							}
							laid = Lay(CaveArt.LanternStand, variant, at, Quaternion.LookRotation(side, Vector3.up), Vector3.one);
							Lamps += laid ? 1 : 0;
						}
						if (laid)
						{
							break;
						}
					}
				}
			}

			/// <summary>A lantern stand's spot near a way's end: its floor at the end's level, clear of every way, with room over it.</summary>
			private bool RouteSpot(Vector3 near, float level, out Vector3 at)
			{
				at = near;
				if (!Ground(new Vector3(near.x, level + 1.2f, near.z), 2f, out float floor) || Mathf.Abs(floor - level) > 0.6f)
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				return Clear(at, 0f) && system.Void(at + Vector3.up * 2.4f) < 0f;
			}
		}
	}
}
#endif
