#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The fields laid so far in one placing of a scene's sites (2026-10-18, phase I2k, decision 32), so a settlement's fields
	/// never lie over another's: each site's fields (PointOfInterestFields) claim their ground once they are laid, and the next
	/// site's keep clear of every claim. Pure: one is made for each Place (PointOfInterestStage) and handed to every site.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>First laid, first kept.</b> The sites are built in the plan's order, which a cut, a repaint and Regenerate POIs all keep,
	/// so which field gives way is the same every run. A field of a later site that would lie over a claim is SHRUNK about its own
	/// middle (<see cref="ShrinkSteps"/>) until it clears, staying inside the ground its site already found fit, or left out when
	/// even the smallest step does not clear or would be too small a field (<see cref="MinimumSize"/>).
	/// </para>
	/// <para>
	/// <b>Nothing else moves.</b> The test is made after the site has drawn every field it wanted, as it always drew them, so its
	/// other fields lie where they did, and its random sequence goes on as before (PointOfInterestFields keeps the fences' draws in
	/// step). Fields are kept as far apart as a site keeps its own (<see cref="Gap"/>): room for both fences and a way between.
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestFieldClaims
	{
		/// <summary>How far apart two fields are kept, metres: each grown by this before they are tested, as a site's own fields are.</summary>
		public const float Gap = 6f;

		/// <summary>The sizes a field that lies over a claim is tried at, as shares of its own, largest first.</summary>
		public static readonly float[] ShrinkSteps = { 0.8f, 0.64f };

		/// <summary>The smallest field kept, metres (its long side, its short side).</summary>
		public static readonly Vector2 MinimumSize = new Vector2(16f, 12f);

		/// <summary>One laid field's ground.</summary>
		public readonly struct Claim
		{
			public readonly int Site;
			public readonly Vector2 Centre;
			public readonly float Yaw;
			public readonly Vector2 Size;

			public Claim(int site, Vector2 centre, float yaw, Vector2 size)
			{
				Site = site;
				Centre = centre;
				Yaw = yaw;
				Size = size;
			}
		}

		private readonly List<Claim> claims = new List<Claim>();

		/// <summary>Every field claimed so far, in the order laid.</summary>
		public IReadOnlyList<Claim> All => claims;

		/// <summary>Claims a laid field's ground for <paramref name="site"/>.</summary>
		public void Add(int site, Vector2 centre, float yaw, Vector2 size) => claims.Add(new Claim(site, centre, yaw, size));

		/// <summary>True when a field of <paramref name="site"/> there lies over no other site's claim (each kept <see cref="Gap"/> apart).</summary>
		public bool Clear(int site, Vector2 centre, float yaw, Vector2 size)
		{
			Vector2 grown = size + Vector2.one * Gap;
			foreach (Claim claim in claims)
			{
				if (claim.Site != site && PointOfInterestLayouts.Overlaps(centre, yaw, grown, claim.Centre, claim.Yaw, claim.Size + Vector2.one * Gap))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The size a field of <paramref name="site"/> may be laid at (pure): its own where it is <see cref="Clear"/>, else the first
		/// of <see cref="ShrinkSteps"/> about its middle that is and is no smaller than <see cref="MinimumSize"/>; false (and its own
		/// size) when none is, when it is left out. Always true with no claims (null).
		/// </summary>
		public static bool Fit(PointOfInterestFieldClaims claims, int site, Vector2 centre, float yaw, Vector2 size, out Vector2 fitted)
		{
			fitted = size;
			if (claims == null || claims.Clear(site, centre, yaw, size))
			{
				return true;
			}
			foreach (float step in ShrinkSteps)
			{
				Vector2 smaller = size * step;
				if (Mathf.Max(smaller.x, smaller.y) < MinimumSize.x || Mathf.Min(smaller.x, smaller.y) < MinimumSize.y)
				{
					break;
				}
				if (claims.Clear(site, centre, yaw, smaller))
				{
					fitted = smaller;
					return true;
				}
			}
			return false;
		}
	}
}
#endif
