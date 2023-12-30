using System;

namespace FishMMO.Shared
{
	public class CircularBuffer<T> where T : class
	{
		public class Node
		{
			public Action OnRemove;
			public T Value { get; set; }
			public Node Next { get; set; }
			public Node Previous { get; set; }

			public Node(T value, Action onRemove)
			{
				Value = value;
				this.OnRemove = onRemove;
			}

			public void Clear()
			{
				Value = null;
				Next = null;
				Previous = null;
				OnRemove = null;
			}
		}

		private Node head;
		private Node tail;

		// Add a new element to the circular buffer
		public Node Add(T item, Action<Node> onAddCallback = null, Action onRemoveCallback = null)
		{
			Node newNode = new Node(item, onRemoveCallback);
			if (onAddCallback != null)
			{
				onAddCallback(newNode);
			}
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
				tail.Next = newNode;
				head.Previous = newNode;
				tail = newNode;
			}

			return newNode;
		}

		// Remove an element from the circular buffer
		public void Remove(Node node)
		{
			if (node == null)
			{
				return;
			}
			node?.OnRemove();

			if (head == tail)
			{
				// The buffer contains only one element
				if (head != null)
				{
					head.Clear();
					head = null;
				}
				if (tail != null)
				{
					tail.Clear();
					tail = null;
				}
			}
			else
			{
				node.Previous.Next = node.Next;
				node.Next.Previous = node.Previous;

				if (node == head)
					head = node.Next;
				if (node == tail)
					tail = node.Previous;
			}
		}

		// Remove and return the last added element (FILO behavior)
		public T Pop()
		{
			if (head == null)
			{
				return null;
			}

			T poppedValue = tail.Value;
			tail?.OnRemove();

			if (head == tail)
			{
				// The buffer contains only one element
				if (head != null)
				{
					head.Clear();
					head = null;
				}
				if (tail != null)
				{
					tail.Clear();
					tail = null;
				}
			}
			else
			{
				tail.Previous.Next = head;
				head.Previous = tail.Previous;
				tail = tail.Previous;
			}

			return poppedValue;
		}

		// Print the elements in the circular buffer
		public void Print()
		{
			Node current = head;

			if (current != null)
			{
				do
				{
					Console.Write(current.Value + " ");
					current = current.Next;
				} while (current != head);
			}

			Console.WriteLine();
		}
	}
}