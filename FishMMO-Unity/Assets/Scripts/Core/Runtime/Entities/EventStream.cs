using System;
using System.Threading;

namespace FishMMO.Core
{
	/// <summary>A number per event type, assigned on first use; indexes the world's stream table.</summary>
	public static class EventTypeIds
	{
		private static int next = -1;

		internal static int Next() => Interlocked.Increment(ref next);
	}

	/// <summary>The id of event type <typeparamref name="T"/>.</summary>
	public static class EventType<T> where T : struct
	{
		/// <summary>This type's id; the same for the whole process.</summary>
		public static readonly int Id = EventTypeIds.Next();
	}

	/// <summary>What the world needs from every event stream.</summary>
	public interface IEventStream
	{
		/// <summary>How many events are waiting.</summary>
		int Count { get; }

		/// <summary>Drops every event.</summary>
		void Clear();
	}

	/// <summary>
	/// Facts of one type raised during a tick (an entity died, a modifier expired, an item was gained), for the
	/// systems that care to read. An event lives from when it is raised to the end of its tick, so systems in
	/// later phases see what earlier ones raised; nothing carries over to the next tick.
	/// </summary>
	public sealed class EventStream<T> : IEventStream where T : struct
	{
		private T[] items = new T[16];
		private int count;

		/// <inheritdoc />
		public int Count => count;

		/// <summary>Every event raised so far this tick, in order.</summary>
		public ReadOnlySpan<T> Current => new ReadOnlySpan<T>(items, 0, count);

		/// <summary>Raises an event.</summary>
		public void Raise(in T item)
		{
			if (count == items.Length)
			{
				Array.Resize(ref items, count * 2);
			}
			items[count++] = item;
		}

		/// <inheritdoc />
		public void Clear()
		{
			Array.Clear(items, 0, count);
			count = 0;
		}
	}
}
