namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// A max-heap of scene instance handles ordered by remaining capacity.
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
		private (int handle, int capacity)[] _heap;
		private int _count;

		/// <summary>Number of entries currently in the heap.</summary>
		public int Count => _count;

		/// <summary>
		/// Creates a new heap with pre-allocated backing storage.
		/// </summary>
		/// <param name="capacity">Initial backing array size (avoids resizing when known).</param>
		public InstanceCapacityHeap(int capacity)
		{
			_heap = new (int, int)[capacity > 0 ? capacity : 4];
			_count = 0;
		}

		/// <summary>
		/// Pushes an instance handle with its remaining capacity onto the heap. O(log N).
		/// </summary>
		public void Push(int handle, int remainingCapacity)
		{
			if (remainingCapacity <= 0)
			{
				return;
			}

			EnsureCapacity(_count + 1);
			_heap[_count] = (handle, remainingCapacity);
			SiftUp(_count);
			_count++;
		}

		/// <summary>
		/// Assigns the caller to the instance with the most remaining capacity.
		/// Decrements that instance's capacity and re-heapifies. If the instance
		/// reaches zero capacity it is removed from the heap.
		/// </summary>
		/// <param name="handle">The assigned scene handle, or 0 if the heap is empty.</param>
		/// <returns><c>true</c> if an instance was available; <c>false</c> if the heap is empty.</returns>
		public bool TryAssignFromTop(out int handle)
		{
			if (_count == 0)
			{
				handle = 0;
				return false;
			}

			handle = _heap[0].handle;
			int newCap = _heap[0].capacity - 1;

			if (newCap <= 0)
			{
				// Remove root: swap with last, shrink, sift down
				_count--;
				if (_count > 0)
				{
					_heap[0] = _heap[_count];
					SiftDown(0);
				}
			}
			else
			{
				// Decrease root capacity and sift down
				_heap[0].capacity = newCap;
				SiftDown(0);
			}

			return true;
		}

		private void SiftUp(int index)
		{
			while (index > 0)
			{
				int parent = (index - 1) >> 1;
				if (_heap[index].capacity <= _heap[parent].capacity)
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

				if (left < _count && _heap[left].capacity > _heap[largest].capacity)
				{
					largest = left;
				}
				if (right < _count && _heap[right].capacity > _heap[largest].capacity)
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
			var tmp = _heap[a];
			_heap[a] = _heap[b];
			_heap[b] = tmp;
		}

		private void EnsureCapacity(int required)
		{
			if (_heap != null && _heap.Length >= required)
			{
				return;
			}
			int newLen = _heap == null ? 4 : _heap.Length * 2;
			if (newLen < required)
			{
				newLen = required;
			}
			var newArr = new (int, int)[newLen];
			if (_heap != null && _count > 0)
			{
				System.Array.Copy(_heap, newArr, _count);
			}
			_heap = newArr;
		}
	}
}
