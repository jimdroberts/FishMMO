#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveSystem
	{
		// ── Skylights (2026-10-20, caves livened) ─────────────────

		/*
		 * An opening in a great chamber's roof up to the ground over it, where the roof was thin enough to fall in: the daylight
		 * comes down through it in a shaft onto a lit, green patch of floor, and from the hill over it the cave shows as a pit
		 * with a broken lip. It is a mouth of its own kind (CaveMouthKind.Skylight), so the mouth machinery holes the terrain
		 * over it (HoleCells, CutHoles, the cut read back by UseCutHoles), raises the collar's lip round it and keeps its pieces
		 * drawn and casting shadows (the mesher's NearMouth); but it is no way in: no approach is graded to it, no way ends at
		 * it (LandingOf false), it is not one of a site's own mouths for the paths or the map's entries (CaveShaper.OwnMouths,
		 * PointOfInterestEntrances.IsWayIn), and the mouths' growth, boulders, vines and bat flights pass it by. What the
		 * daylight reaches inside (CaveHabitat, the dressing's sunlit floor and roots) reads it as any mouth.
		 *
		 * Stored in the mouth's own values (no format change): Floor the floor under the opening's middle, Radius the
		 * opening's, Zone its radius (a skylight's thin roof is the throat itself), Landing the opening's middle on the ground,
		 * and Water the vault's top over the floor, where the throat leaves the chamber (both in the system's frame).
		 */

		/// <summary>Bucket ids at and under this are skylights' throats (SkylightId − the mouth's index): between the chambers and the hollows.</summary>
		private const int SkylightId = -900000;
		/// <summary>
		/// How high a skylight's lip stands over the ground round it, metres, and how far from the throat's bare wall it runs before
		/// it lies flush: a broken edge a stride or two wide, knee to thigh high, seen before it is walked into (the collar's
		/// blends round its top off by about half a metre, so it stands about a metre).
		/// </summary>
		public const float SkylightRim = 1.6f, SkylightLipBand = 3.5f;
		/// <summary>How far over the ground the throat's void runs, metres: the shell is cut off at the ground, so it only needs to pierce it.</summary>
		public const float SkylightOverTop = 6f;
		/// <summary>How much wider than the opening the throat is where it leaves the vault (1 + this), easing to the opening over <see cref="SkylightFlareRun"/>.</summary>
		public const float SkylightFlare = 0.35f, SkylightFlareRun = 6f;
		/// <summary>The opening's radius, metres, at least and at most.</summary>
		public const float SkylightMinRadius = 2.5f, SkylightMaxRadius = 8f;
		/// <summary>The narrowest a chamber may be (its lesser half-width, metres) to open one: a great chamber's roof.</summary>
		public const float SkylightMinChamber = 8f;
		/// <summary>The least headroom over the floor where it opens, metres: a vault, not a crawl.</summary>
		public const float SkylightMinVault = 5f;
		/// <summary>The most rock and soil over the vault there, metres: a thin roof falls in (a doline, a tube's collapse), a thick one stands.</summary>
		public const float SkylightMaxCover = 45f;
		/// <summary>How far in plan a skylight keeps from every other mouth and its way in, and from another skylight, metres.</summary>
		public const float SkylightClear = 40f;
		/// <summary>The most the ground round an opening may rise or fall across it, rise over run: a pit in a slope, not a cliff's face.</summary>
		public const float SkylightSteep = 0.6f;
		/// <summary>Places tried in each chamber.</summary>
		private const int SkylightTries = 6;

		/// <summary>
		/// The share of systems of a form that open any skylight: limestone caves often (a doline's fallen roof), lava tubes most
		/// (a tube's roof collapses into a skylight as it cools), ice caves now and then (a glacier's roof melted through);
		/// never a mine, a grotto or a sea cave.
		/// </summary>
		public static float SkylightShare(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.Cave: return 0.45f;
				case CaveForm.LavaTube: return 0.6f;
				case CaveForm.IceCave: return 0.25f;
				default: return 0f;
			}
		}

		/// <summary>The most skylights a system of a size opens.</summary>
		public static int MostSkylights(int size) => size >= 2 ? 3 : size == 1 ? 2 : 1;

		/// <summary>Whether a mouth is an opening in a roof (<see cref="CaveMouthKind.Skylight"/>), not a way in.</summary>
		public static bool IsSkylight(in CaveMouth mouth) => mouth.Kind == CaveMouthKind.Skylight;

		/// <summary>A skylight's opening, its middle on the ground (the system's frame).</summary>
		public static Vector3 SkylightOpening(in CaveMouth mouth) => mouth.Landing;

		/// <summary>The vault's top over a skylight's floor, where its throat leaves the chamber (the system's frame y).</summary>
		public static float SkylightVault(in CaveMouth mouth) => mouth.Water;

		/// <summary>
		/// How far a point is from a mouth, metres, for the shell's thickness and the collar round it and for the mesher's
		/// pieces kept and lit at a mouth: from its floor's point, or for a skylight from its throat's axis between the vault
		/// and the opening (so the rock all the way up the throat and round its lip is a mouth's).
		/// </summary>
		public static float MouthDistance(in CaveMouth mouth, Vector3 p)
		{
			if (mouth.Kind != CaveMouthKind.Skylight)
			{
				return (p - mouth.Floor).magnitude;
			}
			float bottom = Mathf.Min(mouth.Water, mouth.Landing.y);
			float y = Mathf.Clamp(p.y, bottom, mouth.Landing.y);
			float dx = p.x - mouth.Floor.x, dy = p.y - y, dz = p.z - mouth.Floor.z;
			return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		/// <summary>A skylight's opening and vault moved with the rest of a system taken into another's frame (<see cref="Absorb"/>).</summary>
		private static void ShiftSkylight(ref CaveMouth mouth, Vector3 shift)
		{
			if (mouth.Kind == CaveMouthKind.Skylight)
			{
				mouth.Landing += shift;
				mouth.Water += shift.y;
			}
		}

		/// <summary>A skylight's throat (negative inside): a round shaft from a metre under the vault's top to over the ground, flared where it leaves the vault.</summary>
		private float SkylightField(int index, Vector3 p, out float radius, out float floor)
		{
			CaveMouth mouth = Mouths[index];
			float bottom = mouth.Water - 1f, top = mouth.Landing.y + SkylightOverTop;
			float dx = p.x - mouth.Floor.x, dz = p.z - mouth.Floor.z;
			float s = 1f - Mathf.Clamp01((p.y - bottom) / SkylightFlareRun);
			radius = mouth.Radius * (1f + SkylightFlare * s * s);
			// The floor the wall noise is kept off is the chamber's under it: the throat's walls are all wall.
			floor = mouth.Floor.y;
			return Mathf.Max(Mathf.Sqrt(dx * dx + dz * dz) - radius, Mathf.Max(bottom - p.y, p.y - top));
		}

		/// <summary>Each skylight's throat in every bucket its reach touches (called from <see cref="PrepareField"/>).</summary>
		private void AddSkylightsToBuckets(float pad)
		{
			for (int m = 0; m < Mouths.Count; m++)
			{
				CaveMouth mouth = Mouths[m];
				if (mouth.Kind != CaveMouthKind.Skylight)
				{
					continue;
				}
				float r = mouth.Radius * (1f + SkylightFlare) + pad;
				AddToBuckets(new Vector3(mouth.Floor.x - r, mouth.Water - 1f - pad, mouth.Floor.z - r),
					new Vector3(mouth.Floor.x + r, mouth.Landing.y + SkylightOverTop + pad, mouth.Floor.z + r), SkylightId - m);
			}
		}

		/// <summary>Grows a box by the skylights' throats (for <see cref="Extent"/>).</summary>
		private void EncapsulateSkylights(ref Bounds b, float grow)
		{
			foreach (CaveMouth mouth in Mouths)
			{
				if (mouth.Kind != CaveMouthKind.Skylight)
				{
					continue;
				}
				float r = mouth.Radius * (1f + SkylightFlare) + grow;
				b.Encapsulate(new Vector3(mouth.Floor.x - r, mouth.Water - 1f - grow, mouth.Floor.z - r));
				b.Encapsulate(new Vector3(mouth.Floor.x + r, mouth.Landing.y + SkylightOverTop + grow, mouth.Floor.z + r));
			}
		}

		/// <summary>
		/// Opens skylights in the great chambers of a site's own system where the rock over their vaults is thin
		/// (<see cref="MinCover"/> to <see cref="SkylightMaxCover"/>): on ground not too steep and not <paramref name="refused"/>
		/// (water, another site), clear of every other mouth and its way in, and with no other passage or chamber in the throat's
		/// way up. A share of systems by form (<see cref="SkylightShare"/>), one to <see cref="MostSkylights"/> each, largest
		/// chambers first. Each is appended to <see cref="Mouths"/>; how many were. Deterministic in its inputs, its own stream.
		/// </summary>
		/// <param name="ground">The ground over the system (world heights), as the plan sampled it.</param>
		/// <param name="refused">Ground an opening may not break out on; null for none.</param>
		public int TryAddSkylights(CaveGround ground, int siteId, int seed, Func<float, float, bool> refused, out string problem)
		{
			problem = null;
			float share = SkylightShare(Form);
			if (share <= 0f || Mouths.Count == 0 || ground == null || !float.IsNaN(WaterLevel) || HasSump)
			{
				problem = "its form opens no skylights";
				return 0;
			}
			if (Unit(unchecked((uint)seed), 0x5C17u) >= share)
			{
				problem = "its roofs stood";
				return 0;
			}
			if (segA == null)
			{
				Prepare();
			}
			var rng = new DeterministicRNG(seed);
			int want = rng.Range(1, MostSkylights(SizeClass) + 1);
			var order = new List<int>();
			for (int c = 0; c < Chambers.Count; c++)
			{
				CaveChamber chamber = Chambers[c];
				bool great = chamber.Kind == CaveChamberKind.Cavern || chamber.Kind == CaveChamberKind.Hall || chamber.Kind == CaveChamberKind.Room;
				if (great && chamber.SiteId == siteId && Mathf.Min(chamber.Radii.x, chamber.Radii.z) >= SkylightMinChamber)
				{
					order.Add(c);
				}
			}
			// The greatest first; equal ones in their planned order.
			order.Sort((a, b) =>
			{
				float ra = Mathf.Min(Chambers[a].Radii.x, Chambers[a].Radii.z), rb = Mathf.Min(Chambers[b].Radii.x, Chambers[b].Radii.z);
				return ra != rb ? rb.CompareTo(ra) : a.CompareTo(b);
			});
			if (order.Count == 0)
			{
				problem = $"no chamber is {2f * SkylightMinChamber:0} m across";
				return 0;
			}
			int added = 0;
			foreach (int c in order)
			{
				if (added >= want)
				{
					break;
				}
				CaveChamber chamber = Chambers[c];
				float half = Mathf.Min(chamber.Radii.x, chamber.Radii.z);
				float cy = chamber.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				var across = new Vector3(along.z, 0f, -along.x);
				for (int t = 0; t < SkylightTries; t++)
				{
					// Every draw of a try before any test of it: a refused place never shifts the next one's.
					float angle = rng.Range(0f, 2f * Mathf.PI), spread = rng.Range(0f, 0.4f), size = rng.NextFloat(), yaw = rng.Range(0f, 360f);
					float least = Mathf.Max(SkylightMinRadius, 1.5f * Amp);
					float most = Mathf.Max(least, Mathf.Min(SkylightMaxRadius, 0.35f * half));
					float radius = Mathf.Lerp(least, most, size);
					Vector3 p = chamber.Centre + along * (Mathf.Cos(angle) * spread * chamber.Radii.x) + across * (Mathf.Sin(angle) * spread * chamber.Radii.z);
					if (TrySkylightAt(chamber, p, radius, yaw, siteId, ground, refused, out CaveMouth mouth, out problem))
					{
						Mouths.Add(mouth);
						Prepare();
						added++;
						break;
					}
				}
			}
			return added;
		}

		/// <summary>A skylight over a point of a chamber's floor, if one fits there (see <see cref="TryAddSkylights"/>).</summary>
		private bool TrySkylightAt(in CaveChamber chamber, Vector3 p, float radius, float yaw, int siteId, CaveGround ground, Func<float, float, bool> refused,
			out CaveMouth mouth, out string problem)
		{
			mouth = default;
			float nominal = FloorOf(chamber, p);
			if (!Column(p.x, p.z, nominal + 1f, out float floor, out float vault))
			{
				problem = "no open floor under the place";
				return false;
			}
			if (vault - floor < SkylightMinVault)
			{
				problem = $"only {vault - floor:0.#} m of headroom where it would open";
				return false;
			}
			float wx = Origin.x + p.x, wz = Origin.z + p.z;
			float reach = radius + 2f;
			if (!ground.Contains(wx, wz, reach + 10f))
			{
				problem = "it would open off the sampled ground";
				return false;
			}
			float top = ground.At(wx, wz) - Origin.y;
			float cover = top - vault;
			if (cover < MinCover || cover > SkylightMaxCover)
			{
				problem = $"{cover:0.#} m of rock over the vault";
				return false;
			}
			if (refused != null && refused(wx, wz))
			{
				problem = "the ground over it is water or another site's";
				return false;
			}
			// The ground round the opening: not too steep, not refused; and the vault's highest under that ring, so the throat's
			// way up is tested above all of the chamber's own void.
			float lo = top, hi = top, vaultTop = vault;
			float ring = radius * (1f + SkylightFlare) + 1.5f;
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI * 0.25f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
				float gx = wx + ca * reach, gz = wz + sa * reach;
				float g = ground.At(gx, gz) - Origin.y;
				lo = Mathf.Min(lo, g);
				hi = Mathf.Max(hi, g);
				if (refused != null && refused(gx, gz))
				{
					problem = "the ground round it is water or another site's";
					return false;
				}
				if (Column(p.x + ca * ring, p.z + sa * ring, nominal + 1f, out _, out float side))
				{
					vaultTop = Mathf.Max(vaultTop, side);
				}
			}
			if (hi - lo > SkylightSteep * 2f * reach)
			{
				problem = $"the ground falls {hi - lo:0.#} m across it";
				return false;
			}
			// Clear of every other mouth (its zone and the way in graded before it) and of the other skylights.
			for (int m = 0; m < Mouths.Count; m++)
			{
				CaveMouth other = Mouths[m];
				float dx = p.x - other.Floor.x, dz = p.z - other.Floor.z;
				float keep = other.Kind == CaveMouthKind.Skylight ? SkylightClear + radius + other.Radius : SkylightClear + other.Zone + radius;
				if (dx * dx + dz * dz < keep * keep)
				{
					problem = "too near another mouth";
					return false;
				}
				if (other.Kind == CaveMouthKind.Ground && GradesApproach)
				{
					Rect approach = ApproachArea(m);
					if (wx > approach.xMin - reach && wx < approach.xMax + reach && wz > approach.yMin - reach && wz < approach.yMax + reach)
					{
						problem = "over another mouth's way in";
						return false;
					}
				}
			}
			// Nothing else of the system in the throat's way up: no passage or chamber over the vault within a margin of it.
			for (float y = vaultTop + 2f; y <= top + 1f; y += 1f)
			{
				for (int k = 0; k <= 8; k++)
				{
					float a = k * Mathf.PI * 0.25f;
					float d = k == 8 ? 0f : ring;
					if (Smooth(new Vector3(p.x + Mathf.Cos(a) * d, y, p.z + Mathf.Sin(a) * d)) < 1.5f)
					{
						problem = "another passage runs through its way up";
						return false;
					}
				}
			}
			mouth = new CaveMouth
			{
				Floor = new Vector3(p.x, floor, p.z),
				Yaw = yaw,
				SiteId = siteId,
				Radius = radius,
				Zone = radius,
				Collar = 1.5f,
				CollarLow = 1.5f,
				Shell = 3f,
				Kind = CaveMouthKind.Skylight,
				Water = vault,
				Landing = new Vector3(p.x, top, p.z),
			};
			problem = null;
			return true;
		}

		/// <summary>
		/// The open column at (x, z) through a point in the void (system frame): its floor under and its roof over, by marching
		/// the field; false where the point is rock.
		/// </summary>
		private bool Column(float x, float z, float from, out float floor, out float roof)
		{
			floor = roof = from;
			if (Void(new Vector3(x, from, z)) >= 0f)
			{
				return false;
			}
			float y = from;
			while (y > from - 30f && Void(new Vector3(x, y - 0.1f, z)) < 0f)
			{
				y -= 0.1f;
			}
			floor = y;
			y = from;
			while (y < from + 250f && Void(new Vector3(x, y + 0.25f, z)) < 0f)
			{
				y += 0.25f;
			}
			roof = y;
			return true;
		}

		/// <summary>The system's skylights' indices in <see cref="Mouths"/>.</summary>
		public List<int> Skylights()
		{
			var found = new List<int>();
			for (int m = 0; m < Mouths.Count; m++)
			{
				if (Mouths[m].Kind == CaveMouthKind.Skylight)
				{
					found.Add(m);
				}
			}
			return found;
		}
	}
}
#endif
