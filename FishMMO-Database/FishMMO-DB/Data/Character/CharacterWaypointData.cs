namespace FishMMO.Database.Data
{
	/// <summary>
	/// One page of a character's discovered waypoints in one scene: a 64-bit mask where bit N is
	/// waypoint <c>page * 64 + N</c>.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a mask.</b> A waypoint is a bit — discovered or not — and a scene has tens of
	/// them, all read together on login and written together on save. A row per waypoint would
	/// spend roughly forty bytes on each bit and have the load path fold them back into the set
	/// the game actually wants. The mask stores the set as it is used, exactly as
	/// <see cref="CharacterDialogueChoiceData"/> does for dialogue choices; pages keep it
	/// unbounded rather than capped at 64 per scene.</para>
	///
	/// <para><b>Merge, not replace.</b> Bits are only ever set, so the write is
	/// <c>mask | EXCLUDED.mask</c>: idempotent under retries, and two scene servers writing during
	/// a transfer converge on the union rather than one discarding the other's discoveries. That
	/// is also why there is no version column — there is no stale write to reject.</para>
	///
	/// <para><b>When scenes change.</b> The bit index is the waypoint's authored index, which is
	/// stable by contract (see <c>Waypoint.WaypointIndex</c>). A renamed scene orphans its rows;
	/// they cost eight bytes each and are never read. A removed waypoint leaves a bit nothing
	/// consults.</para>
	/// </remarks>
	public struct CharacterWaypointData
	{
		/// <summary>Character that discovered the waypoints.</summary>
		public readonly long CharacterID;

		/// <summary>The scene the waypoints stand in.</summary>
		public readonly string SceneName;

		/// <summary>Which 64-waypoint page of the scene this row holds.</summary>
		public readonly short Page;

		/// <summary>The discovered bits of that page.</summary>
		public readonly ulong Mask;

		public CharacterWaypointData(long characterID, string sceneName, short page, ulong mask)
		{
			CharacterID = characterID;
			SceneName = sceneName;
			Page = page;
			Mask = mask;
		}
	}
}
