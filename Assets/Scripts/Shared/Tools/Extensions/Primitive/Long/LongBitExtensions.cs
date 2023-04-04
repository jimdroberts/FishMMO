using System;

public static class LongBitExtensions
{
	public static bool IsFlagged(this long flag, int bitPosition)
	{
		return ((flag >> bitPosition) & 1) == 1;
	}

	public static bool IsFlagged<T>(this long flag, T bitPosition)
	{
		int pos = Convert.ToInt32(bitPosition);
		return ((flag >> pos) & 1) == 1;
	}

	public static void DisableBit<T>(this ref long flag, T bitPosition)
	{
		int pos = Convert.ToInt32(bitPosition);
		flag &= ~(1 << pos);
	}

	public static void DisableBit(this ref long flag, int bitPosition)
	{
		flag &= ~(1 << bitPosition);
	}

	public static void EnableBit<T>(this ref long flag, T bitPosition)
	{
		flag |= (long)(1 << Convert.ToInt32(bitPosition));
	}

	public static void EnableBit(this ref long flag, int bitPosition)
	{
		flag |= (long)(1 << bitPosition);
	}
}