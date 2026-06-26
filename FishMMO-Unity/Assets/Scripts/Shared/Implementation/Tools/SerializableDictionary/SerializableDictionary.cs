using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Base class for SerializableDictionaryBase implementation.</summary>
	public abstract class SerializableDictionaryBase
	{
		/// <summary>Base class for Storage implementation.</summary>
		public abstract class Storage { }

		/// <summary>Dictionary class definition.</summary>
		protected class Dictionary<TKey, TValue> : System.Collections.Generic.Dictionary<TKey, TValue>
		{
			/// <summary>Dictionary method.</summary>
			/// <returns>A dictionary of cached objects.</returns>
			public Dictionary() { }
			/// <summary>Dictionary method.</summary>
			/// <param name="dict">The dict parameter.</param>
			/// <returns>A dictionary of cached objects.</returns>
			public Dictionary(IDictionary<TKey, TValue> dict) : base(dict) { }
			/// <summary>Dictionary method.</summary>
			/// <param name="info">The info parameter.</param>
			/// <param name="context">The context parameter.</param>
			/// <returns>A dictionary of cached objects.</returns>
			public Dictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }
		}
	}

	[Serializable]
	/// <summary>Base class for SerializableDictionaryBase implementation.</summary>
	public abstract class SerializableDictionaryBase<TKey, TValue, TValueStorage> : SerializableDictionaryBase, IDictionary<TKey, TValue>, IDictionary, ISerializationCallbackReceiver, IDeserializationCallback, System.Runtime.Serialization.ISerializable
	{
		Dictionary<TKey, TValue> m_dict;
		[SerializeField]
		TKey[] m_keys;
		[SerializeField]
		TValueStorage[] m_values;

		/// <summary>SerializableDictionaryBase method.</summary>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionaryBase()
		{
			m_dict = new Dictionary<TKey, TValue>();
		}

		/// <summary>SerializableDictionaryBase method.</summary>
		/// <param name="dict">The dict parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionaryBase(IDictionary<TKey, TValue> dict)
		{
			m_dict = new Dictionary<TKey, TValue>(dict);
		}

		/// <summary>Sets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <param name="value">The value parameter.</param>
		protected abstract void SetValue(TValueStorage[] storage, int i, TValue value);
		/// <summary>Gets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <returns>An integer result.</returns>
		protected abstract TValue GetValue(TValueStorage[] storage, int i);

		/// <summary>Copies data from another collection into this one.</summary>
		/// <param name="dict">The dict parameter.</param>
		public void CopyFrom(IDictionary<TKey, TValue> dict)
		{
			m_dict.Clear();
			foreach (var kvp in dict)
			{
				m_dict[kvp.Key] = kvp.Value;
			}
		}

		/// <summary>Called after deserialization to reconstruct internal state.</summary>
		public void OnAfterDeserialize()
		{
			if (m_keys != null && m_values != null && m_keys.Length == m_values.Length)
			{
				m_dict.Clear();
				int n = m_keys.Length;
				for (int i = 0; i < n; ++i)
				{
					m_dict[m_keys[i]] = GetValue(m_values, i);
				}

				m_keys = null;
				m_values = null;
			}
		}

		/// <summary>Called before serialization to prepare data for storage.</summary>
		public void OnBeforeSerialize()
		{
			int n = m_dict.Count;
			m_keys = new TKey[n];
			m_values = new TValueStorage[n];

			int i = 0;
			foreach (var kvp in m_dict)
			{
				m_keys[i] = kvp.Key;
				SetValue(m_values, i, kvp.Value);
				++i;
			}
		}

		#region IDictionary<TKey, TValue>

		/// <summary>Gets the collection of keys.</summary>
		public ICollection<TKey> Keys { get { return ((IDictionary<TKey, TValue>)m_dict).Keys; } }
		/// <summary>Gets the collection of values.</summary>
		public ICollection<TValue> Values { get { return ((IDictionary<TKey, TValue>)m_dict).Values; } }
		/// <summary>Gets or sets the Count value.</summary>
		public int Count { get { return ((IDictionary<TKey, TValue>)m_dict).Count; } }
		/// <summary>Gets a value indicating whether the readonly condition is met.</summary>
		public bool IsReadOnly { get { return ((IDictionary<TKey, TValue>)m_dict).IsReadOnly; } }

		public TValue this[TKey key]
		{
			get { return ((IDictionary<TKey, TValue>)m_dict)[key]; }
			set { ((IDictionary<TKey, TValue>)m_dict)[key] = value; }
		}

		/// <summary>Adds an item to the collection.</summary>
		/// <param name="key">The key parameter.</param>
		/// <param name="value">The value parameter.</param>
		public void Add(TKey key, TValue value)
		{
			((IDictionary<TKey, TValue>)m_dict).Add(key, value);
		}

		/// <summary>Checks whether the ntainskey condition is met.</summary>
		/// <param name="key">The key parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool ContainsKey(TKey key)
		{
			return ((IDictionary<TKey, TValue>)m_dict).ContainsKey(key);
		}

		/// <summary>Removes an item from the collection.</summary>
		/// <param name="key">The key parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool Remove(TKey key)
		{
			return ((IDictionary<TKey, TValue>)m_dict).Remove(key);
		}

		/// <summary>Attempts to perform the getvalue operation.</summary>
		/// <param name="key">The key parameter.</param>
		/// <returns>True if successful, false otherwise.</returns>
		public bool TryGetValue(TKey key, out TValue value)
		{
			return ((IDictionary<TKey, TValue>)m_dict).TryGetValue(key, out value);
		}

		/// <summary>Adds an item to the collection.</summary>
		/// <param name="item">The item parameter.</param>
		public void Add(KeyValuePair<TKey, TValue> item)
		{
			((IDictionary<TKey, TValue>)m_dict).Add(item);
		}

		/// <summary>Clears all items from the collection.</summary>
		public void Clear()
		{
			((IDictionary<TKey, TValue>)m_dict).Clear();
		}

		/// <summary>Checks whether the ntains condition is met.</summary>
		/// <param name="item">The item parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool Contains(KeyValuePair<TKey, TValue> item)
		{
			return ((IDictionary<TKey, TValue>)m_dict).Contains(item);
		}

		/// <summary>Copies the collection elements to an array.</summary>
		/// <param name="array">The array parameter.</param>
		/// <param name="arrayIndex">The arrayIndex parameter.</param>
		public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
		{
			((IDictionary<TKey, TValue>)m_dict).CopyTo(array, arrayIndex);
		}

		/// <summary>Removes an item from the collection.</summary>
		/// <param name="item">The item parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool Remove(KeyValuePair<TKey, TValue> item)
		{
			return ((IDictionary<TKey, TValue>)m_dict).Remove(item);
		}

		/// <summary>Gets the enumerator value.</summary>
		/// <returns>An enumerator for iterating through the collection.</returns>
		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
		{
			return ((IDictionary<TKey, TValue>)m_dict).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IDictionary<TKey, TValue>)m_dict).GetEnumerator();
		}

		#endregion

		#region IDictionary

		/// <summary>Gets a value indicating whether the fixedsize condition is met.</summary>
		public bool IsFixedSize { get { return ((IDictionary)m_dict).IsFixedSize; } }
		ICollection IDictionary.Keys { get { return ((IDictionary)m_dict).Keys; } }
		ICollection IDictionary.Values { get { return ((IDictionary)m_dict).Values; } }
		/// <summary>Gets a value indicating whether the synchronized condition is met.</summary>
		public bool IsSynchronized { get { return ((IDictionary)m_dict).IsSynchronized; } }
		/// <summary>Gets or sets the SyncRoot value.</summary>
		public object SyncRoot { get { return ((IDictionary)m_dict).SyncRoot; } }

		public object this[object key]
		{
			get { return ((IDictionary)m_dict)[key]; }
			set { ((IDictionary)m_dict)[key] = value; }
		}

		/// <summary>Adds an item to the collection.</summary>
		/// <param name="key">The key parameter.</param>
		/// <param name="value">The value parameter.</param>
		public void Add(object key, object value)
		{
			((IDictionary)m_dict).Add(key, value);
		}

		/// <summary>Checks whether the ntains condition is met.</summary>
		/// <param name="key">The key parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool Contains(object key)
		{
			return ((IDictionary)m_dict).Contains(key);
		}

		IDictionaryEnumerator IDictionary.GetEnumerator()
		{
			return ((IDictionary)m_dict).GetEnumerator();
		}

		/// <summary>Removes an item from the collection.</summary>
		/// <param name="key">The key parameter.</param>
		public void Remove(object key)
		{
			((IDictionary)m_dict).Remove(key);
		}

		/// <summary>Copies the collection elements to an array.</summary>
		/// <param name="array">The array parameter.</param>
		/// <param name="index">The index parameter.</param>
		public void CopyTo(Array array, int index)
		{
			((IDictionary)m_dict).CopyTo(array, index);
		}

		#endregion

		#region IDeserializationCallback

		/// <summary>Called when deserialization is complete.</summary>
		/// <param name="sender">The sender parameter.</param>
		public void OnDeserialization(object sender)
		{
			((IDeserializationCallback)m_dict).OnDeserialization(sender);
		}

		#endregion

		#region ISerializable

		/// <summary>SerializableDictionaryBase method.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		protected SerializableDictionaryBase(SerializationInfo info, StreamingContext context)
		{
			m_dict = new Dictionary<TKey, TValue>(info, context);
		}

		/// <summary>Gets the objectdata value.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			((System.Runtime.Serialization.ISerializable)m_dict).GetObjectData(info, context);
		}

		#endregion
	}

	/// <summary>SerializableDictionary class definition.</summary>
	public static class SerializableDictionary
	{
		/// <summary>Base class for Storage implementation.</summary>
		public class Storage<T> : SerializableDictionaryBase.Storage
		{
			/// <summary>The data value.</summary>
			public T data;
		}
	}

	/// <summary>SerializableDictionary class definition.</summary>
	public class SerializableDictionary<TKey, TValue> : SerializableDictionaryBase<TKey, TValue, TValue>
	{
		/// <summary>SerializableDictionary method.</summary>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionary() { }
		/// <summary>SerializableDictionary method.</summary>
		/// <param name="dict">The dict parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionary(IDictionary<TKey, TValue> dict) : base(dict) { }
		/// <summary>SerializableDictionary method.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		protected SerializableDictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }

		/// <summary>Gets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <returns>An integer result.</returns>
		protected override TValue GetValue(TValue[] storage, int i)
		{
			return storage[i];
		}

		/// <summary>Sets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <param name="value">The value parameter.</param>
		protected override void SetValue(TValue[] storage, int i, TValue value)
		{
			storage[i] = value;
		}
	}

	/// <summary>SerializableDictionary class definition.</summary>
	public class SerializableDictionary<TKey, TValue, TValueStorage> : SerializableDictionaryBase<TKey, TValue, TValueStorage> where TValueStorage : SerializableDictionary.Storage<TValue>, new()
	{
		/// <summary>SerializableDictionary method.</summary>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionary() { }
		/// <summary>SerializableDictionary method.</summary>
		/// <param name="dict">The dict parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		public SerializableDictionary(IDictionary<TKey, TValue> dict) : base(dict) { }
		/// <summary>SerializableDictionary method.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		/// <returns>A dictionary of cached objects.</returns>
		protected SerializableDictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }

		/// <summary>Gets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <returns>An integer result.</returns>
		protected override TValue GetValue(TValueStorage[] storage, int i)
		{
			return storage[i].data;
		}

		/// <summary>Sets the value value.</summary>
		/// <param name="storage">The storage parameter.</param>
		/// <param name="i">The i parameter.</param>
		/// <param name="value">The value parameter.</param>
		protected override void SetValue(TValueStorage[] storage, int i, TValue value)
		{
			storage[i] = new TValueStorage();
			storage[i].data = value;
		}
	}
}