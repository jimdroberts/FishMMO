using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// A circular doubly linked list designed for high-performance addition/removal of reference types.
	/// All public members are thread-safe.
	/// </summary>
	/// <typeparam name="T">Must be a reference type.</typeparam>
	public class CircularBuffer<T> where T : class
	{
		private readonly object _lock = new object();
		private int _count;

		public class Node
		{
			public Action? OnRemove;
			public T Value { get; set; }
			public Node? Next { get; set; }
			public Node? Previous { get; set; }

			public Node(T value, Action? onRemove)
			{
				Value = value;
				OnRemove = onRemove;
			}

			public void Clear()
			{
				Value = default!;
				Next = null;
				Previous = null;
				OnRemove = null;
			}
		}

		private Node? head;
		private Node? tail;

		public Node? Head
		{
			get { lock (_lock) return head; }
		}

		public Node? Tail
		{
			get { lock (_lock) return tail; }
		}

		/// <summary>
		/// Gets the number of nodes currently in the buffer.
		/// </summary>
		public int Count
		{
			get { lock (_lock) return _count; }
		}

		/// <summary>
		/// Adds an item to the end (tail) of the buffer.
		/// </summary>
		public Node Add(T item, Action<Node>? onAddCallback = null, Action? onRemoveCallback = null)
		{
			lock (_lock)
			{
				Node newNode = new Node(item, onRemoveCallback);
				onAddCallback?.Invoke(newNode);

				if (head == null)
				{
					head = newNode;
					tail = newNode;
					newNode.Next = newNode;
					newNode.Previous = newNode;
				}
				else
				{
					newNode.Next = head;
					newNode.Previous = tail;
					tail!.Next = newNode;
					head!.Previous = newNode;
					tail = newNode;
				}

				_count++;
				return newNode;
			}
		}

		/// <summary>
		/// Removes a specific node from the circle and repairs the links.
		/// </summary>
		public void Remove(Node? node)
		{
			lock (_lock)
			{
				if (node == null || head == null) return;

				node.OnRemove?.Invoke();

				if (head == tail)
				{
					// Only one node exists
					if (node == head)
					{
						head = null;
						tail = null;
					}
				}
				else
				{
					// Link neighbors to each other
					node.Previous!.Next = node.Next;
					node.Next!.Previous = node.Previous;

					if (node == head) head = node.Next;
					if (node == tail) tail = node.Previous;
				}

				node.Clear();
				_count--;
			}
		}

		/// <summary>
		/// Removes and returns the value of the Tail node.
		/// </summary>
		public T? Pop()
		{
			lock (_lock)
			{
				if (tail == null) return null;

				T val = tail.Value;
				Remove(tail); // Use the centralized Remove logic to ensure pointers and OnRemove are handled
				return val;
			}
		}

		/// <summary>
		/// Returns true if the buffer has at least one node, regardless of the head node's value.
		/// Uses the internal count instead of checking head.Value to avoid conflating
		/// an empty buffer with a null-valued node.
		/// </summary>
		public bool Peek()
		{
			lock (_lock)
			{
				return _count > 0;
			}
		}

		/// <summary>
		/// Returns true if the buffer has no nodes.
		/// </summary>
		public bool Empty()
		{
			lock (_lock)
			{
				return head == null;
			}
		}

		/// <summary>
		/// Removes all nodes from the buffer.
		/// </summary>
		public void Clear()
		{
			lock (_lock)
			{
				// Walk the list clearing each node to invoke OnRemove callbacks
				if (head != null)
				{
					Node current = head!;
					do
					{
						Node next = current.Next!;
						current.OnRemove?.Invoke();
						current.Clear();
						current = next;
					} while (current != head);
				}

				head = null;
				tail = null;
				_count = 0;
			}
		}

		/// <summary>
		/// Enumerates the values once from Head to Tail.
		/// Returns a snapshot copy so enumeration is safe outside the lock.
		/// </summary>
		public IEnumerable<T> GetValues()
		{
			List<T> snapshot;
			lock (_lock)
			{
				if (head == null)
				{
					snapshot = new List<T>(0);
				}
				else
				{
					snapshot = new List<T>(_count);
					Node current = head!;
					for (int i = 0; i < _count; i++)
					{
						snapshot.Add(current.Value);
						current = current.Next!;
					}
				}
			}
			return snapshot;
		}
	}
}