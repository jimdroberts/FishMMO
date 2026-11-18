namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// A max-heap of scene instances, keyed by scene row ID, ordered by remaining capacity.
	/// Used for O(log N) fallback instance selection in open-world routing.
	/// <para>
	/// The root always holds the instance with the most remaining capacity.
	/// <see cref="TryAssignFromTop"/> peeks the root, decrements its capacity,
	/// and re-heapifies — yielding O(log N) per assignment instead of O(N) linear scan.
	/// If the root reaches zero capacity it is removed automatically.
	/// </para>
	/// </summary>
	public struct InstanceCapacityHeap
	{
		private (long handle, int capacity)[] heap;
		private int count;

		/// <summary>Number of entries currently in the heap.</summary>
		public int Count => count;

		/// <summary>
		/// Creates a new heap with pre-allocated backing storage.
		/// </summary>
		/// <param name="capacity">Initial backing array size (avoids resizing when known).</param>
		public InstanceCapacityHeap(int capacity)
		{
			heap = new (long, int)[capacity > 0 ? capacity : 4];
			count = 0;
		}

		/// <summary>
		/// Pushes an instance (identified by its scene row ID) with its remaining capacity onto the heap. O(log N).
		/// </summary>
		public void Push(long handle, int remainingCapacity)
		{
			if (remainingCapacity <= 0)
			{
				return;
			}

			EnsureCapacity(count + 1);
			heap[count] = (handle, remainingCapacity);
			SiftUp(count);
			count++;
		}

		/// <summary>
		/// Assigns the caller to the instance with the most remaining capacity.
		/// Decrements that instance's capacity and re-heapifies. If the instance
		/// reaches zero capacity it is removed from the heap.
		/// </summary>
		/// <param name="handle">The assigned scene row ID, or 0 if the heap is empty.</param>
		/// <returns><c>true</c> if an instance was available; <c>false</c> if the heap is empty.</returns>
		public bool TryAssignFromTop(out long handle)
		{
			if (count == 0)
			{
				handle = 0;
				return false;
			}

			handle = heap[0].handle;
			int newCap = heap[0].capacity - 1;

			if (newCap <= 0)
			{
				// Remove root: swap with last, shrink, sift down
				count--;
				if (count > 0)
				{
					heap[0] = heap[count];
					SiftDown(0);
				}
			}
			else
			{
				// Decrease root capacity and sift down
				heap[0].capacity = newCap;
				SiftDown(0);
			}

			return true;
		}

		private void SiftUp(int index)
		{
			while (index > 0)
			{
				int parent = (index - 1) >> 1;
				if (heap[index].capacity <= heap[parent].capacity)
				{
					break;
				}
				Swap(index, parent);
				index = parent;
			}
		}

		private void SiftDown(int index)
		{
			while (true)
			{
				int left = (index << 1) + 1;
				int right = left + 1;
				int largest = index;

				if (left < count && heap[left].capacity > heap[largest].capacity)
				{
					largest = left;
				}
				if (right < count && heap[right].capacity > heap[largest].capacity)
				{
					largest = right;
				}
				if (largest == index)
				{
					break;
				}
				Swap(index, largest);
				index = largest;
			}
		}

		private void Swap(int a, int b)
		{
			var tmp = heap[a];
			heap[a] = heap[b];
			heap[b] = tmp;
		}

		private void EnsureCapacity(int required)
		{
			if (heap != null && heap.Length >= required)
			{
				return;
			}
			int newLen = heap == null ? 4 : heap.Length * 2;
			if (newLen < required)
			{
				newLen = required;
			}
			var newArr = new (long, int)[newLen];
			if (heap != null && count > 0)
			{
				System.Array.Copy(heap, newArr, count);
			}
			heap = newArr;
		}
	}
}
