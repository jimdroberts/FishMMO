namespace FishMMO.Shared
{
	public static class StringExtensions
	{
		//Taken from https://stackoverflow.com/questions/5154970/how-do-i-create-a-hashcode-in-net-c-for-a-string-that-is-safe-to-store-in-a
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