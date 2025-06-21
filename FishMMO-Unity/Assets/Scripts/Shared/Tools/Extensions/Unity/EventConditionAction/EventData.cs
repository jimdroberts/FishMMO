using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class EventData
	{
		public ICharacter Initiator { get; }
		public override string ToString() => GetType().Name;

		private readonly Dictionary<Type, EventData> eventDataDictionary = new Dictionary<Type, EventData>();

		public EventData(ICharacter initiator, params EventData[] initialData)
		{
			Initiator = initiator;

			Add(this);

			foreach (var data in initialData)
			{
				if (data != null)
				{
					eventDataDictionary[data.GetType()] = data;
				}
			}
		}

		public void Add(EventData data)
		{
			if (data != null)
			{
				eventDataDictionary[data.GetType()] = data;
			}
		}

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

		public bool Contains<T>() where T : EventData
		{
			return eventDataDictionary.ContainsKey(typeof(T));
		}
	}
}