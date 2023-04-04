public static class ByteArrayExtensions
{
	public static bool Compare(this byte[] first, byte[] second)
	{
		if (first.Length != second.Length) return false;
		int length = first.Length;
		for (int index = 0; index < length; ++index) if (first[index] != second[index]) return false;
		return true;
	}
}