namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for string, including deterministic hash code generation.
	/// </summary>
	public static class StringExtensions
	{
		/// <summary>
		/// Computes a deterministic hash code for a string, suitable for storage and consistent across .NET versions.
		/// </summary>
		/// <param name="text">The string to hash.</param>
		/// <returns>A deterministic integer hash code.</returns>
		/// <remarks>
		/// Taken from https://stackoverflow.com/questions/5154970/how-do-i-create-a-hashcode-in-net-c-for-a-string-that-is-safe-to-store-in-a
		/// </remarks>
		public static int GetDeterministicHashCode(this string text)
		{
			unchecked
			{
				int hash = 23;
				foreach (char c in text)
				{
					hash = hash * 31 + c;
				}
				return hash;
			}
		}
	}
}