using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Container for event-related data in FishMMO. Supports storing and retrieving multiple event data types.
	/// </summary>
	public class EventData
	{
		/// <summary>
		/// The character that initiated the event.
		/// </summary>
		public ICharacter Initiator { get; }

		/// <summary>
		/// Returns the type name of this event data instance.
		/// </summary>
		/// <returns>Type name string.</returns>
		public override string ToString() => GetType().Name;

		/// <summary>
		/// Dictionary storing event data by their type.
		/// </summary>
		private readonly Dictionary<Type, EventData> eventDataDictionary = new Dictionary<Type, EventData>();

		/// <summary>
		/// Constructs a new EventData container, optionally with initial event data objects.
		/// </summary>
		/// <param name="initiator">The character that initiated the event.</param>
		/// <param name="initialData">Optional initial event data objects to add.</param>
		public EventData(ICharacter initiator, params EventData[] initialData)
		{
			Initiator = initiator;

			// Add this instance to the dictionary
			Add(this);

			// Add any initial event data objects
			foreach (var data in initialData)
			{
				if (data != null)
				{
					eventDataDictionary[data.GetType()] = data;
				}
			}
		}

		/// <summary>
		/// Adds an event data object to the dictionary, keyed by its type.
		/// </summary>
		/// <param name="data">The event data object to add.</param>
		public void Add(EventData data)
		{
			if (data != null)
			{
				eventDataDictionary[data.GetType()] = data;
			}
		}

		/// <summary>
		/// Attempts to retrieve an event data object of type T from the dictionary.
		/// </summary>
		/// <typeparam name="T">The type of event data to retrieve.</typeparam>
		/// <param name="data">The retrieved event data object, or default if not found.</param>
		/// <returns>True if found; otherwise, false.</returns>
		public bool TryGet<T>(out T data) where T : EventData
		{
			if (eventDataDictionary.TryGetValue(typeof(T), out EventData foundData))
			{
				data = foundData as T;
				return data != null;
			}
			data = default(T);
			return false;
		}

		/// <summary>
		/// Checks if an event data object of type T exists in the dictionary.
		/// </summary>
		/// <typeparam name="T">The type of event data to check for.</typeparam>
		/// <returns>True if found; otherwise, false.</returns>
		public bool Contains<T>() where T : EventData
		{
			return eventDataDictionary.ContainsKey(typeof(T));
		}
	}
}