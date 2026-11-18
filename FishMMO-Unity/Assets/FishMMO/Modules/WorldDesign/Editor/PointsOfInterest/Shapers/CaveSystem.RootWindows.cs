#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveSystem
	{
		// ── Root windows (2026-10-25, phase I2n-c "drowned galleries", decision 86 (f)) ──

		/*
		 * A small opening up through a thin roof under a wooded, wet host: where the swamp's trees stand on no more than a few metres
		 * of peat and rock over a gallery, the roof has rotted through between their roots, and the daylight comes down in a narrow
		 * shaft screened by the roots hanging into it, onto the water or the mud below. It is a skylight in every way the machinery
		 * knows (CaveMouthKind.Skylight: holed, its lip raised, kept drawn, no way in, the daylight read by the habitat), only small:
		 * its radius is under a skylight's least (SkylightMinRadius), which is how the dressing tells one (IsRootWindow) and hangs its
		 * roots round it. Stored as a skylight is (no format change). Unlike a skylight it opens over water too (a drowned gallery's,
		 * a grotto's pool), as the swamp's sinks do.
		 */

		/// <summary>True for a skylight that is a root window (<see cref="BiomeCaveWaters.IsRootWindowRadius"/>: smaller than any skylight).</summary>
		public static bool IsRootWindow(in CaveMouth mouth) => mouth.Kind == CaveMouthKind.Skylight && BiomeCaveWaters.IsRootWindowRadius(mouth.Radius, SkylightMinRadius);

		/// <summary>The least half-width of a chamber a root window opens over, metres: a gallery's room, not only a great hall.</summary>
		public const float RootWindowMinChamber = 3f;

		/// <summary>
		/// Opens up to <paramref name="most"/> root windows (1 to that many by the seed's roll, <see cref="BiomeCaveWaters.RootWindowsWanted"/>)
		/// in a site's own chambers where the rock over them is <see cref="MinCover"/> to <see cref="BiomeCaveWaters.RootWindowCover"/>
		/// metres, the ground round them not <paramref name="refused"/>, clear of every other mouth: largest chambers first, one a chamber.
		/// Never in a mine, an ice cave or a lava tube, nor under a submerged mouth. Appended to <see cref="Mouths"/>; how many were. Its own
		/// stream: deterministic in its inputs.
		/// </summary>
		public int TryAddRootWindows(CaveGround ground, int siteId, int seed, int most, Func<float, float, bool> refused, out string problem)
		{
			problem = null;
			if (most <= 0 || Mouths.Count == 0 || ground == null || HasSump || !(Form == CaveForm.Cave || Form == CaveForm.Grotto))
			{
				problem = "its form opens no root windows";
				return 0;
			}
			if (segA == null)
			{
				Prepare();
			}
			var rng = new DeterministicRNG(seed);
			int want = BiomeCaveWaters.RootWindowsWanted(most, rng.NextFloat());
			var order = new List<int>();
			for (int c = 0; c < Chambers.Count; c++)
			{
				CaveChamber chamber = Chambers[c];
				if (chamber.SiteId == siteId && chamber.Kind != CaveChamberKind.Alcove && chamber.Kind != CaveChamberKind.Stope
					&& Mathf.Min(chamber.Radii.x, chamber.Radii.z) >= RootWindowMinChamber)
				{
					order.Add(c);
				}
			}
			order.Sort((a, b) =>
			{
				float ra = Mathf.Min(Chambers[a].Radii.x, Chambers[a].Radii.z), rb = Mathf.Min(Chambers[b].Radii.x, Chambers[b].Radii.z);
				return ra != rb ? rb.CompareTo(ra) : a.CompareTo(b);
			});
			if (order.Count == 0)
			{
				problem = $"no chamber is {2f * RootWindowMinChamber:0} m across";
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
				float cy = chamber.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				var across = new Vector3(along.z, 0f, -along.x);
				for (int t = 0; t < SkylightTries; t++)
				{
					// Every draw of a try before any test of it, as the skylights' are.
					float angle = rng.Range(0f, 2f * Mathf.PI), spread = rng.Range(0f, 0.5f), size = rng.NextFloat(), yaw = rng.Range(0f, 360f);
					float radius = Mathf.Lerp(BiomeCaveWaters.RootWindowMinRadius, BiomeCaveWaters.RootWindowMaxRadius, size);
					Vector3 p = chamber.Centre + along * (Mathf.Cos(angle) * spread * chamber.Radii.x) + across * (Mathf.Sin(angle) * spread * chamber.Radii.z);
					if (TrySkylightAt(chamber, p, radius, yaw, siteId, ground, refused, out CaveMouth mouth, out problem,
						BiomeCaveWaters.RootWindowCover, BiomeCaveWaters.RootWindowMinVault))
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
	}
}
#endif
