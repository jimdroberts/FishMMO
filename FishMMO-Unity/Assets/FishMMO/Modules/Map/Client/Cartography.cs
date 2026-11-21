using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Supplies the local character's Cartography skill level to the map subsystem.
	/// </summary>
	/// <remarks>
	/// An interface with one property, because the thing behind it does not exist yet. When the
	/// profession system lands it implements this and registers itself with
	/// <see cref="Cartography"/>; until then the default implementation answers with a constant
	/// and every map feature that scales with skill reads the same seam it always will.
	/// </remarks>
	public interface ICartographyProvider
	{
		/// <summary>
		/// The local character's Cartography tier, from zero upwards.
		/// </summary>
		int DetailTier { get; }
	}

	/// <summary>
	/// The one place the map subsystem asks how good the player's maps are.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a seam rather than a number.</b> Cartography is going to scale half a dozen
	/// unrelated things — how far the fog lifts as you walk, how much of the world map you can see
	/// at once, whether region names and coordinates are shown, how many notes you may keep. Each
	/// of those is a different formula in a different file, and hard-coding today's constants into
	/// them means finding all six again later. Every one of them reads a property here instead, so
	/// wiring up the real skill is a single registration.</para>
	///
	/// <para><b>Why the default is the maximum.</b> The skill does not exist, so there is nothing
	/// for a player to level and no way to earn a better map. Shipping a crippled map that unlocks
	/// against a system nobody can interact with would be a bug, not a preview. When the
	/// profession arrives, the starting tier becomes whatever it grants a new character, and that
	/// change belongs in the provider — not here.</para>
	///
	/// <para><b>Where the skill must be earned.</b> Not from the fog file. See
	/// <see cref="FogOfWarStore"/>: exploration progress is on the player's disk and is forgeable,
	/// so Cartography experience has to be awarded by the server from the positions it already
	/// receives. The fog file decides what the map looks like and nothing else.</para>
	/// </remarks>
	public static class Cartography
	{
		/// <summary>The highest tier the map subsystem understands.</summary>
		public const int MaximumDetailTier = 4;

		/// <summary>The provider currently answering for the local character.</summary>
		private static ICartographyProvider provider;

		/// <summary>
		/// Installs the provider that answers for the local character.
		/// </summary>
		/// <param name="value">The provider, or null to fall back to the default.</param>
		public static void SetProvider(ICartographyProvider value)
		{
			provider = value;
		}

		/// <summary>The local character's Cartography tier, clamped into range.</summary>
		public static int DetailTier => provider == null
			? MaximumDetailTier
			: Mathf.Clamp(provider.DetailTier, 0, MaximumDetailTier);

		/// <summary>The tier as a fraction of the maximum, for anything that scales smoothly.</summary>
		public static float DetailFraction => DetailTier / (float)MaximumDetailTier;

		/// <summary>
		/// The detail tier above which authored region labels and landmarks stay hidden.
		/// </summary>
		/// <remarks>
		/// Authored content carries its own tier — continents at zero, hamlets at three — so this
		/// is the cut-off the world map applies against it. A novice sees the provinces; an expert
		/// sees the farmsteads.
		/// </remarks>
		public static int VisibleContentTier => DetailTier;

		/// <summary>
		/// The largest fraction of a scene the world map will show at once.
		/// </summary>
		/// <remarks>
		/// One at full skill: the whole zone fits on screen. Below that the map is a window that
		/// has to be panned, which is the difference between owning a map of the province and
		/// owning a sketch of the road you are on.
		/// </remarks>
		public static float MaximumWorldMapExtent => Mathf.Lerp(0.35f, 1.0f, DetailFraction);

		/// <summary>Whether the maps show numeric coordinates.</summary>
		public static bool ShowsCoordinates => DetailTier >= 1;

		/// <summary>Whether the world map draws a measured grid over the terrain.</summary>
		public static bool ShowsGrid => DetailTier >= 2;

		/// <summary>How many notes the player may keep per scene.</summary>
		/// <remarks>
		/// A cap at all because notes are written to disk and drawn every frame the map is open;
		/// a cap that grows because "room for more annotations" is a natural reward for a
		/// map-making profession.
		/// </remarks>
		public static int NoteCapacity => 8 + (DetailTier * 8);

		/// <summary>
		/// The edge length, in pixels, the minimap's render texture is created at.
		/// </summary>
		/// <remarks>
		/// Sharper maps at higher skill, and a real cost saved at low skill: the texture is
		/// re-rendered thirty times a second, so a quarter of the pixels is a quarter of the fill.
		/// Powers of two because the renderer rounds to one anyway.
		/// </remarks>
		public static int MinimapResolution => DetailTier >= 3 ? 512 : 256;
	}
}
