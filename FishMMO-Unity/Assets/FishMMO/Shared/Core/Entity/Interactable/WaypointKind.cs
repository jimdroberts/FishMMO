namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What a <see cref="IWaypoint"/> is (issue #241). Serialized on the scene object and baked into the world scene details:
	/// never renumber.
	/// </summary>
	public enum WaypointKind : byte
	{
		/// <summary>A discoverable fast-travel point.</summary>
		Waypoint = 0,

		/// <summary>
		/// A Gate Stone: discovered like a waypoint (same index space, same record), the destination the Gate spell opens a
		/// portal to, and the way home for its travellers while a gate to it is open. Not a fast-travel point.
		/// </summary>
		GateStone = 1,
	}
}
