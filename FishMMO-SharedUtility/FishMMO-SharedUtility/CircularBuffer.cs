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
		private readonly object syncLock = new object();
		private int count;

		/// <summary>
		/// A node in the circular doubly-linked list. Exposed publicly so callers
		/// can hold a reference for O(1) removal via <see cref="Remove"/>.
		/// </summary>
		public class Node
		{
			/// <summary>
			/// Optional callback invoked when this node is removed from its owning list.
			/// Set at construction time via <see cref="Add"/>.
			///
			/// IMPORTANT: This callback is invoked while the list lock is held.
			/// Do NOT call Remove/Pop/Clear on the same list from this callback — it will deadlock.
			/// </summary>
			public Action? OnRemove { get; set; }

			/// <summary>
			/// The value stored in this node.
			/// </summary>
			public T Value { get; set; }

			/// <summary>
			/// The next node in the circular list (never null when the node is linked).
			/// </summary>
			public Node? Next { get; set; }

			/// <summary>
			/// The previous node in the circular list (never null when the node is linked).
			/// </summary>
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

		/// <summary>
		/// Gets a snapshot of the head node. Note that the returned <see cref="Node"/>
		/// reference is only a snapshot taken under the lock. By the time you use it,
		/// the node may have been removed or its <see cref="Node.Value"/> may have been
		/// cleared by another thread. For safe value access use <see cref="TryPeekHead"/> instead.
		/// </summary>
		public Node? Head
		{
			get { lock (syncLock) return head; }
		}

		/// <summary>
		/// Gets a snapshot of the tail node. Note that the returned <see cref="Node"/>
		/// reference is only a snapshot taken under the lock. By the time you use it,
		/// the node may have been removed or its <see cref="Node.Value"/> may have been
		/// cleared by another thread. For safe value access use <see cref="TryPeekTail"/> instead.
		/// </summary>
		public Node? Tail
		{
			get { lock (syncLock) return tail; }
		}

		/// <summary>
		/// Safely reads the head value under the lock.
		/// </summary>
		/// <param name="value">When this method returns, contains the value of the head node, or <c>default</c> if the buffer is empty.</param>
		/// <returns><c>true</c> if the buffer contained at least one node; otherwise <c>false</c>.</returns>
		public bool TryPeekHead(out T value)
		{
			lock (syncLock)
			{
				if (head != null)
				{
					value = head.Value;
					return true;
				}
				value = default!;
				return false;
			}
		}

		/// <summary>
		/// Safely reads the tail value under the lock.
		/// </summary>
		/// <param name="value">When this method returns, contains the value of the tail node, or <c>default</c> if the buffer is empty.</param>
		/// <returns><c>true</c> if the buffer contained at least one node; otherwise <c>false</c>.</returns>
		public bool TryPeekTail(out T value)
		{
			lock (syncLock)
			{
				if (tail != null)
				{
					value = tail.Value;
					return true;
				}
				value = default!;
				return false;
			}
		}

		/// <summary>
		/// Gets the number of nodes currently in the buffer.
		/// </summary>
		public int Count
		{
			get { lock (syncLock) return count; }
		}

		/// <summary>
		/// Adds an item to the end (tail) of the buffer.
		/// </summary>
		public Node Add(T item, Action<Node>? onAddCallback = null, Action? onRemoveCallback = null)
		{
			lock (syncLock)
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

				count++;
				return newNode;
			}
		}

		/// <summary>
		/// Removes a specific node from the circle and repairs the links.
		/// Validates that the node belongs to this list before modifying links.
		/// </summary>
		public void Remove(Node? node)
		{
			lock (syncLock)
			{
				if (node == null || head == null) return;

				// Validate that the node belongs to this list by walking from head.
				bool belongs = false;
				Node current = head;
				do
				{
					if (current == node) { belongs = true; break; }
					current = current.Next!;
				} while (current != head);
				if (!belongs) return;

				try
				{
					node.OnRemove?.Invoke();
				}
				catch (Exception ex)
				{
					// Swallow callback exceptions to prevent list corruption.
					// If the application needs to observe these, it should handle
					// errors inside the callback itself.
					System.Diagnostics.Debug.WriteLine($"[CircularBuffer] Remove callback threw: {ex.Message}");
				}

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
				count--;
			}
		}

		/// <summary>
		/// Removes and returns the value of the Tail node.
		/// </summary>
		public T? Pop()
		{
			lock (syncLock)
			{
				if (tail == null) return null;

				Node node = tail;
				T val = node.Value;

				try
				{
					node.OnRemove?.Invoke();
				}
				catch (Exception ex)
				{
					// Swallow callback exceptions to prevent list corruption.
					System.Diagnostics.Debug.WriteLine($"[CircularBuffer] Pop callback threw: {ex.Message}");
				}

				if (head == tail)
				{
					// Only one node exists
					head = null;
					tail = null;
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
				count--;
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
			lock (syncLock)
			{
				return head != null;
			}
		}

		/// <summary>
		/// Returns true if the buffer has no nodes.
		/// Uses the same underlying check as <see cref="Peek"/> for consistency.
		/// </summary>
		public bool Empty()
		{
			lock (syncLock)
			{
				return head == null;
			}
		}

		/// <summary>
		/// Removes all nodes from the buffer.
		/// </summary>
		public void Clear()
		{
			lock (syncLock)
			{
				// Walk the list clearing each node to invoke OnRemove callbacks
				if (head != null)
				{
					Node current = head!;
					do
					{
						Node next = current.Next!;
						try
				{
					current.OnRemove?.Invoke();
				}
				catch (Exception ex)
				{
					// Swallow callback exceptions to prevent list corruption.
					System.Diagnostics.Debug.WriteLine($"[CircularBuffer] Clear callback threw: {ex.Message}");
				}
						current.Clear();
						current = next;
					} while (current != head);
				}

				head = null;
				tail = null;
				count = 0;
			}
		}

		/// <summary>
		/// Enumerates the values once from Head to Tail.
		/// Returns a snapshot copy so enumeration is safe outside the lock.
		/// </summary>
		public IEnumerable<T> GetValues()
		{
			List<T> snapshot;
			lock (syncLock)
			{
				if (head == null)
				{
					snapshot = new List<T>(0);
				}
				else
				{
					snapshot = new List<T>(count);
					Node current = head!;
					for (int i = 0; i < count; i++)
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