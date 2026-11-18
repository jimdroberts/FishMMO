#if UNITY_EDITOR
namespace FishMMO.Shared.WorldDesign
{
	/// <summary>A binary min-heap of cells by height, ties broken by cell index.</summary>
	internal sealed class CellHeap
	{
		private readonly float[] keys;
		private readonly int[] cells;
		public int Count;

		public CellHeap(int capacity)
		{
			keys = new float[capacity];
			cells = new int[capacity];
		}

		private bool Less(int a, int b) => keys[a] < keys[b] || (keys[a] == keys[b] && cells[a] < cells[b]);

		public void Push(float key, int cell)
		{
			int i = Count++;
			keys[i] = key;
			cells[i] = cell;
			while (i > 0)
			{
				int parent = (i - 1) >> 1;
				if (!Less(i, parent))
				{
					break;
				}
				Swap(i, parent);
				i = parent;
			}
		}

		public int Pop()
		{
			int top = cells[0];
			Count--;
			keys[0] = keys[Count];
			cells[0] = cells[Count];
			int i = 0;
			while (true)
			{
				int left = 2 * i + 1, right = left + 1, smallest = i;
				if (left < Count && Less(left, smallest))
				{
					smallest = left;
				}
				if (right < Count && Less(right, smallest))
				{
					smallest = right;
				}
				if (smallest == i)
				{
					break;
				}
				Swap(i, smallest);
				i = smallest;
			}
			return top;
		}

		private void Swap(int a, int b)
		{
			(keys[a], keys[b]) = (keys[b], keys[a]);
			(cells[a], cells[b]) = (cells[b], cells[a]);
		}
	}
}
#endif
