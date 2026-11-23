namespace FishMMO.Database.Data
{
	/// <summary>
	/// A character's one marked location (the Mark spell, issue #241): where Recall sends it.
	/// </summary>
	/// <remarks>
	/// <para><b>One row per character, replaced on every mark.</b> No version column: a character is resident on one scene
	/// server at a time, and that server's save runs on the character's keyed save lane before the session is released,
	/// so there is no second writer to arbitrate against.</para>
	/// <para><b>The channel is a hint.</b> <see cref="SceneID"/> is the scene row ID (channel) the mark was made in. Recall
	/// prefers it; a row that has since gone (the channel unloaded) costs nothing — the world server routes to any other
	/// channel of <see cref="SceneName"/>, where the position is just as valid.</para>
	/// </remarks>
	public struct CharacterMarkData
	{
		/// <summary>The character that made the mark.</summary>
		public readonly long CharacterID;

		/// <summary>The open-world scene marked.</summary>
		public readonly string SceneName;

		/// <summary>The scene row ID (channel) the mark was made in, or 0.</summary>
		public readonly long SceneID;

		/// <summary>The marked position.</summary>
		public readonly float X, Y, Z;

		/// <summary>The facing at the mark, in degrees about the vertical axis.</summary>
		public readonly float Heading;

		public CharacterMarkData(long characterID, string sceneName, long sceneID, float x, float y, float z, float heading)
		{
			CharacterID = characterID;
			SceneName = sceneName;
			SceneID = sceneID;
			X = x;
			Y = y;
			Z = z;
			Heading = heading;
		}
	}
}
