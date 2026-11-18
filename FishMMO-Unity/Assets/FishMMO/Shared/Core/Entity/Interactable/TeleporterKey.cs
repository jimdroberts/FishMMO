namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The one spelling of a teleporter's lookup key.
	/// </summary>
	/// <remarks>
	/// <para>A cross-scene teleport is resolved by name: the world scene details cache stores each
	/// teleporter under its GameObject name, and the runtime sends the GameObject name of the
	/// object the player used. Those two names were produced by different code. The bake
	/// trimmed whitespace; the runtime did not. The client stripped Unity's <c>(Clone)</c> suffix
	/// in <c>Interactable.Awake</c>; the server build did not, because that strip sat behind
	/// <c>!UNITY_SERVER</c>. A key that depends on which process computed it is not a key, so
	/// every producer and consumer goes through here.</para>
	/// </remarks>
	public static class TeleporterKey
	{
		private const string CloneSuffix = "(Clone)";

		/// <summary>
		/// Normalises a GameObject name into the teleporter key: whitespace trimmed and every
		/// <c>(Clone)</c> suffix removed. Null and empty come back as empty.
		/// </summary>
		public static string Normalize(string gameObjectName)
		{
			if (string.IsNullOrEmpty(gameObjectName))
			{
				return string.Empty;
			}

			// Trim first so a trailing space cannot hide the suffix, and again after each strip
			// so "X (Clone)" comes out as "X". Unity appends one suffix per Instantiate of an
			// instance, so strip repeatedly.
			string key = gameObjectName.Trim();
			while (key.EndsWith(CloneSuffix, System.StringComparison.Ordinal))
			{
				key = key.Substring(0, key.Length - CloneSuffix.Length).Trim();
			}
			return key;
		}
	}
}
