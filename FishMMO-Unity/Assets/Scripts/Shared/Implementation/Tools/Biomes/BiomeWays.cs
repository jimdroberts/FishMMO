using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a biome's ways bridge its rivers (<see cref="BiomeTemplate.CrossingStyle"/>, 2026-10-26, phase I2l-b; decisions 49, 52, 59, 61,
	/// 78, 79): the kit's timber spans everywhere, or the country bridge the biome's draft names. Stored by number: append only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A bridge belongs to its ends</b> (decision 35 (i)): the style is the biome's whose banks the bridge joins
	/// (PathCrossings.BridgeBiome), and each style bridges only the spans it can (PathCrossings.StyleFits): a liana bridge is not thrown
	/// over a 200 m river, nor a cantilever over a brook; outside its spans, or over a gorge (whose crossing is one stone arch or rope
	/// bridge lip to lip), the way takes the kit's own bridge as before.
	/// </para>
	/// <para>Until 2026-10-26 every river bridge was the timber span; <see cref="Default"/> (every biome's default) keeps it so.</para>
	/// </remarks>
	public enum BiomeCrossingStyle : byte
	{
		/// <summary>The kit's bridge: railed timber spans on trestles (and a gorge's stone arch or rope bridge).</summary>
		Default = 0,
		/// <summary>A liana bridge (Jungle, decision 52): a walk of lashed poles hung from twisted vine cables, V-ties up to vine hand lines, between tree-post anchors.</summary>
		Liana = 1,
		/// <summary>A corbelled laterite bridge (Jungle Temple, decision 59): a flat stone deck over narrow openings whose sides step in course by course, on block piers.</summary>
		CorbelledLaterite = 2,
		/// <summary>A covered wind-and-rain bridge (Karst, decision 61): a timber gallery under a tiled roof on stone piers, open at the sides between its posts.</summary>
		WindAndRain = 3,
		/// <summary>A cantilever bridge (Rocky Terrain, decision 78): log cribs on each bank, each course of beams reaching out past the one below, a level deck over the gap.</summary>
		Cantilever = 4,
		/// <summary>The Goblins' Bridge (Scree, decision 79): great rough slabs laid across boulder piers, a crooked clapper walk.</summary>
		GoblinsBridge = 5,
		/// <summary>A snow-bridge crossing (Glacier, decision 49): a hump of hard-packed snow over a narrow channel, its underside hung with ice.</summary>
		SnowBridge = 6,
	}

	/// <summary>
	/// What a biome's straight roads are (<see cref="BiomeStraightRoads.Form"/>, 2026-10-26, phase I2l-b; decisions 50, 56, 87, 89). Stored by
	/// number: append only.
	/// </summary>
	public enum BiomeStraightRoadForm : byte
	{
		/// <summary>No straight roads: every way is routed round the ground, as before.</summary>
		None = 0,
		/// <summary>Dead-straight roads (High Desert, the Pilgrims' Road of the Runaway Greenhouse Plain): cut and filled to their grade, stairs cut where a cliff stands across them.</summary>
		Straight = 1,
		/// <summary>Straight roads raised on an embankment above the ground (the sacbe of Jungle Ruins, Jungle Temple's royal roads).</summary>
		Raised = 2,
		/// <summary>Straight roads, two of them sunk deep into the ground between sheer cut walls (the vie cave of Swamp Ruins).</summary>
		Cut = 3,
	}

	/// <summary>
	/// A biome's straight-road network (<see cref="BiomeTemplate.StraightRoads"/>, 2026-10-26, phase I2l-b): the dead-straight way class of
	/// decision 50, with its raised (56) and cut (87) profiles and the royal road's ox-cart ruts (59). One network a scene at most, laid
	/// over the trunk ways joining the settlements of the biomes that set it (PathStraightRoads).
	/// </summary>
	[Serializable]
	public struct BiomeStraightRoads
	{
		/// <summary>The roads' form; <see cref="BiomeStraightRoadForm.None"/> for none.</summary>
		public BiomeStraightRoadForm Form;
		/// <summary>Worn into two wheel ruts with grass between them (a royal road's ox-cart ruts), not paved.</summary>
		public bool Ruts;

		/// <summary>No straight roads (every biome's default).</summary>
		public static BiomeStraightRoads None => default;

		public BiomeStraightRoads(BiomeStraightRoadForm form, bool ruts = false)
		{
			Form = form;
			Ruts = ruts;
		}

		/// <summary>True when the biome lays straight roads.</summary>
		public bool IsSet => Form != BiomeStraightRoadForm.None;

		/// <summary>The authoring mark: empty for none, else the form and "+Ruts".</summary>
		public string Signature() => IsSet ? Form + (Ruts ? "+Ruts" : string.Empty) : string.Empty;

		public override string ToString() => IsSet ? Signature() : "None";
	}
}
