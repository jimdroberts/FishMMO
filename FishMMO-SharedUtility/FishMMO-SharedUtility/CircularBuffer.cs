using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// A circular doubly linked list designed for high-performance addition/removal of reference types.
	/// </summary>
	/// <typeparam name="T">Must be a reference type.</typeparam>
	public class CircularBuffer<T> where T : class
	{
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

		public Node? Head => head;
		public Node? Tail => tail;

		/// <summary>
		/// Adds an item to the end (tail) of the buffer.
		/// </summary>
		public Node Add(T item, Action<Node>? onAddCallback = null, Action? onRemoveCallback = null)
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

			return newNode;
		}

		/// <summary>
		/// Removes a specific node from the circle and repairs the links.
		/// </summary>
		public void Remove(Node? node)
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
		}

		/// <summary>
		/// Removes and returns the value of the Tail node.
		/// </summary>
		public T? Pop()
		{
			if (tail == null) return null;

			T val = tail.Value;
			Remove(tail); // Use the centralized Remove logic to ensure pointers and OnRemove are handled
			return val;
		}

		/// <summary>
		/// Safely checks if the head contains a value.
		/// </summary>
		public bool Peek()
		{
			return head?.Value != null;
		}

		public bool Empty() => head == null;

		/// <summary>
		/// Enumerates the values once from Head to Tail.
		/// </summary>
		public IEnumerable<T> GetValues()
		{
			if (head == null) yield break;

			Node current = head!;
			do
			{
				yield return current.Value;
				current = current.Next!;
			} while (current != head);
		}
	}
}