namespace FishMMO.Database.Data
{
	/// <summary>
	/// One page of the portals a character has activated for themselves in one scene: a 64-bit
	/// mask where bit N is portal <c>page * 64 + N</c>.
	/// </summary>
	/// <remarks>
	/// <para>The same shape as <see cref="CharacterWaypointData"/>, for the same reasons: an
	/// activation is a bit, set once and never cleared, read together on login and merged on save.
	/// Only portals scoped per character land here; a portal opened for the whole world is a row
	/// of <see cref="WorldPortalStateData"/> instead.</para>
	/// <para><b>Merge, not replace.</b> The write is <c>mask | EXCLUDED.mask</c>, so retries and two
	/// scene servers writing during a transfer converge on the union, and there is no version
	/// column to reject a stale write with.</para>
	/// </remarks>
	public struct CharacterPortalData
	{
		/// <summary>Character that activated the portals.</summary>
		public readonly long CharacterID;

		/// <summary>The scene the portals stand in.</summary>
		public readonly string SceneName;

		/// <summary>Which 64-portal page of the scene this row holds.</summary>
		public readonly short Page;

		/// <summary>The activated bits of that page.</summary>
		public readonly ulong Mask;

		public CharacterPortalData(long characterID, string sceneName, short page, ulong mask)
		{
			CharacterID = characterID;
			SceneName = sceneName;
			Page = page;
			Mask = mask;
		}
	}
}
