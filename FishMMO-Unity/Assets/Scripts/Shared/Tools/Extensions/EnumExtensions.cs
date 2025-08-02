using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for working with enums.
	/// </summary>
	public static class EnumExtensions
	{
		/// <summary>
		/// Returns an array of all values of the specified enum type.
		/// </summary>
		/// <typeparam name="T">The enum type.</typeparam>
		/// <returns>Array of all enum values.</returns>
		public static T[] ToArray<T>() where T : Enum
		{
			return (T[])Enum.GetValues(typeof(T));
		}
	}
}