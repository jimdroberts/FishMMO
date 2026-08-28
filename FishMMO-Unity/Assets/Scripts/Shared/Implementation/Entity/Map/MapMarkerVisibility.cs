namespace FishMMO.Shared
{
	/// <summary>
	/// The rule that decides whether a marker is drawn for the local player.
	/// </summary>
	/// <remarks>
	/// <para>This is the map system's anti-radar surface, so the rule is authored on the marker
	/// rather than inferred at draw time. A modified client can of course ignore any of this; the
	/// point is that the rule chooses <b>what the honest client draws</b>, and the throttled tier
	/// additionally decides <b>what position data the map subsystem ever holds</b>, so a client
	/// that lies about the rule still has nothing better than a stale, coarse position to lie
	/// with.</para>
	/// <para>The real guarantee lives one layer down and is not negotiable from here: an entity
	/// the server has not streamed to this connection has no GameObject, so it has no marker at
	/// any visibility setting. See <c>ObserverStreamingPolicy</c>.</para>
	/// </remarks>
	public enum MapMarkerVisibility : byte
	{
		/// <summary>
		/// Drawn whenever the object exists. For world fixtures — vendors, nodes, doors,
		/// teleporters — whose positions are public knowledge and do not move.
		/// </summary>
		Always = 0,

		/// <summary>
		/// Drawn only for the local player's own character.
		/// </summary>
		SelfOnly,

		/// <summary>
		/// Drawn at full fidelity for party and guild members, and not at all for anyone else.
		/// The group already shares positions through the party frames, so nothing is leaked.
		/// </summary>
		PartyOrGuild,

		/// <summary>
		/// Drawn only while inside the local detection radius, at a reduced refresh rate and
		/// without a name. The default for hostile and neutral player characters.
		/// </summary>
		/// <remarks>
		/// The detection radius is deliberately smaller than the observer range that put the
		/// object on this client in the first place, so the map is strictly less informative than
		/// the network stream it is drawn from — a client that removes the check gains a radius
		/// it already had, not a new one.
		/// </remarks>
		Detection,

		/// <summary>
		/// Drawn only once the fog of war has revealed the cell the marker stands in. For
		/// landmarks and points of interest that are a reward for exploring.
		/// </summary>
		Discovered,
	}
}
