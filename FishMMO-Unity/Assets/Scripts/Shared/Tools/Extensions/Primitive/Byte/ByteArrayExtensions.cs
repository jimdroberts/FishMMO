namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for byte arrays, providing comparison functionality.
	/// </summary>
	public static class ByteArrayExtensions
	{
		/// <summary>
		/// Compares two byte arrays for equality by checking length and each element.
		/// </summary>
		/// <param name="first">First byte array.</param>
		/// <param name="second">Second byte array.</param>
		/// <returns>True if arrays are equal in length and content, otherwise false.</returns>
		public static bool Compare(this byte[] first, byte[] second)
		{
			if (first.Length != second.Length) return false;
			int length = first.Length;
			for (int index = 0; index < length; ++index) if (first[index] != second[index]) return false;
			return true;
		}
	}
}