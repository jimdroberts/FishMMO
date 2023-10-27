using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public static class IListExtensions
	{
		internal static readonly object LockObj = new object();
		internal static Random Random = new Random();

		internal static int GetNext(int min, int max)
		{
			lock (LockObj)
			{
				return Random.Next(min, max);
			}
		}

		public static void Shuffle<T>(this IList<T> list)
		{
			int i = list.Count - 1;
			while (i > 1)
			{
				--i;
				int j = GetNext(0, i + 1);
				T random = list[j];
				list[j] = list[i];
				list[i] = random;
			}
		}

		public static void Randomize<T>(this IList<T> list)
		{
			for (int i = 0; i < list.Count; ++i)
			{
				int j = GetNext(0, list.Count - 1);
				T element = list[j];
				list[j] = list[i];
				list[i] = element;
			}
		}

		public static T GetRandom<T>(this IList<T> list)
		{
			return list[GetNext(0, list.Count - 1)];
		}
	}
}