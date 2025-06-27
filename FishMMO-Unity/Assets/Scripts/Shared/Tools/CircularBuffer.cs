using System;
using System.Collections.Generic; // Added for IEnumerable<T>

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a circular buffer (or doubly linked circular list) that stores elements of a reference type.
	/// This buffer allows for efficient addition and removal of elements, maintaining a circular structure.
	/// </summary>
	/// <typeparam name="T">The type of elements stored in the buffer. Must be a reference type (class).</typeparam>
	public class CircularBuffer<T> where T : class
	{
		/// <summary>
		/// Represents a node within the <see cref="CircularBuffer{T}"/>.
		/// Each node holds a value and references to the next and previous nodes in the circle.
		/// </summary>
		public class Node
		{
			/// <summary>
			/// An action to be invoked when this node is removed from the buffer.
			/// </summary>
			public Action OnRemove;

			/// <summary>
			/// The value stored in this node.
			/// </summary>
			public T Value { get; set; }

			/// <summary>
			/// Gets or sets the next node in the circular buffer.
			/// </summary>
			public Node Next { get; set; }

			/// <summary>
			/// Gets or sets the previous node in the circular buffer.
			/// </summary>
			public Node Previous { get; set; }

			/// <summary>
			/// Initializes a new instance of the <see cref="Node"/> class.
			/// </summary>
			/// <param name="value">The value to store in the node.</param>
			/// <param name="onRemove">An optional action to execute when the node is removed.</param>
			public Node(T value, Action onRemove)
			{
				Value = value;
				this.OnRemove = onRemove;
			}

			/// <summary>
			/// Clears the node's references to its value, next/previous nodes, and the OnRemove action.
			/// This helps in garbage collection and prevents memory leaks, especially with event subscriptions.
			/// </summary>
			public void Clear()
			{
				Value = null;
				Next = null;
				Previous = null;
				OnRemove = null;
			}
		}

		private Node head; // The starting point of the circular buffer.
		private Node tail; // The ending point of the circular buffer (last added element).

		/// <summary>
		/// Adds a new element to the circular buffer. The new element becomes the new tail.
		/// If the buffer is empty, it becomes the first and only node.
		/// </summary>
		/// <param name="item">The item to add to the buffer.</param>
		/// <param name="onAddCallback">An optional callback action to invoke after the node has been added to the buffer.
		/// The <see cref="Node"/> that was added is passed as a parameter.</param>
		/// <param name="onRemoveCallback">An optional callback action to invoke when this specific node is removed from the buffer.</param>
		/// <returns>The newly created <see cref="Node"/> that holds the added item.</returns>
		public Node Add(T item, Action<Node> onAddCallback = null, Action onRemoveCallback = null)
		{
			Node newNode = new Node(item, onRemoveCallback);

			// Invoke the onAdd callback if provided.
			onAddCallback?.Invoke(newNode);

			if (head == null)
			{
				// If the buffer is empty, the new node becomes the head and tail,
				// and points to itself to form a single-node circular list.
				head = newNode;
				tail = newNode;
				newNode.Next = newNode;
				newNode.Previous = newNode;
			}
			else
			{
				// For a non-empty buffer, insert the new node between the current tail and head.
				// The new node's next is the head.
				newNode.Next = head;
				// The new node's previous is the current tail.
				newNode.Previous = tail;
				// The current tail's next points to the new node.
				tail.Next = newNode;
				// The head's previous points to the new node.
				head.Previous = newNode;
				// Update the tail to be the new node.
				tail = newNode;
			}

			return newNode;
		}

		/// <summary>
		/// Removes a specific node from the circular buffer.
		/// Handles cases for single-node buffers and general multi-node removals.
		/// </summary>
		/// <param name="node">The <see cref="Node"/> to remove from the buffer.</param>
		public void Remove(Node node)
		{
			// Do nothing if the node is null.
			if (node == null)
			{
				return;
			}

			// Invoke the OnRemove callback associated with the node before clearing its references.
			node.OnRemove?.Invoke();

			// Check if this is the last element in the buffer.
			if (head == tail)
			{
				// The buffer contains only one element.
				// If the node to remove is indeed the head (and tail), clear and nullify.
				if (head != null && node == head) // Ensure head is not null and it's the target node
				{
					head.Clear();
					head = null;
					tail = null; // Also nullify tail as it's the same node.
				}
			}
			else
			{
				// Adjust the pointers of the surrounding nodes to bypass the node being removed.
				node.Previous.Next = node.Next;
				node.Next.Previous = node.Previous;

				// Update head/tail if the removed node was the head or tail.
				if (node == head)
				{
					head = node.Next;
				}
				if (node == tail)
				{
					tail = node.Previous;
				}
			}

			// Clear the removed node's references to aid garbage collection.
			node.Clear();
		}

		/// <summary>
		/// Removes and returns the last added element (tail) from the circular buffer,
		/// following a Last-In, First-Out (LIFO) behavior.
		/// </summary>
		/// <returns>The value of the removed tail element, or <c>null</c> if the buffer is empty.</returns>
		public T Pop()
		{
			if (head == null)
			{
				// Buffer is empty.
				return null;
			}

			T poppedValue = tail.Value; // Store the value to return.

			// Invoke the OnRemove callback for the tail node before it's removed.
			tail.OnRemove?.Invoke();

			if (head == tail)
			{
				// The buffer contains only one element.
				// Clear and nullify head and tail.
				head.Clear(); // Clear the single node.
				head = null;
				tail = null;
			}
			else
			{
				// Update the pointers to remove the current tail.
				// The new tail's next should point to the head.
				tail.Previous.Next = head;
				// The head's previous should point to the new tail.
				head.Previous = tail.Previous;
				// Move the tail pointer to the previous node.
				tail = tail.Previous;
			}

			return poppedValue;
		}

		/// <summary>
		/// Peeks at the head element of the circular buffer without removing it.
		/// </summary>
		/// <returns><c>true</c> if the head exists and its value is not null; otherwise, <c>false</c>.</returns>
		public bool Peek()
		{
			// Check if head exists and its value is not null.
			return head != null && head.Value != null;
		}

		/// <summary>
		/// Checks if the circular buffer is empty.
		/// </summary>
		/// <returns><c>true</c> if the buffer contains no elements; otherwise, <c>false</c>.</returns>
		public bool Empty()
		{
			// The buffer is empty if either head or tail is null (both should be null if empty).
			return head == null; // tail == null would also suffice as they are always in sync for emptiness.
		}

		/// <summary>
		/// Prints the values of all elements in the circular buffer to the console, starting from the head.
		/// </summary>
		public void Print()
		{
			Node current = head;

			if (current != null)
			{
				do
				{
					Console.Write(current.Value + " ");
					current = current.Next;
				} while (current != head); // Continue until we loop back to the head.
			}

			Console.WriteLine(); // Add a newline at the end for clean output.
		}

		/// <summary>
		/// Gets the current head of the circular buffer.
		/// </summary>
		public Node Head => head;

		/// <summary>
		/// Gets the current tail of the circular buffer.
		/// </summary>
		public Node Tail => tail;

		/// <summary>
		/// Allows iteration through the elements of the circular buffer starting from the head.
		/// </summary>
		/// <returns>An enumerator that can be used to iterate through the collection.</returns>
		public IEnumerable<T> GetValues()
		{
			if (head == null)
			{
				yield break; // If buffer is empty, yield nothing.
			}

			Node current = head;
			do
			{
				yield return current.Value;
				current = current.Next;
			} while (current != head);
		}
	}
}