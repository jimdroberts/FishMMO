using System;

namespace FishMMO.Shared
{
	public static class RandomExtensions
	{
		public static ulong NextULong(this System.Random random)
		{
			byte[] bytes = new byte[8];
			random.NextBytes(bytes);
			return BitConverter.ToUInt64(bytes, 0);
		}

		public static long Next(this System.Random random, long min, long max)
		{
			if (min > max)
			{
				long tmp = max;
				max = min;
				min = tmp;
			}
			ulong range = (ulong)(max - min);
			if (range == 0)
			{
				return max;
			}
			ulong result;
			ulong limit = ulong.MaxValue - (((ulong.MaxValue % range) + 1) % range);
			do
			{
				result = NextULong(random);
			} while (result > limit);

			return (long)(result % range + (ulong)min);
		}
	}
}