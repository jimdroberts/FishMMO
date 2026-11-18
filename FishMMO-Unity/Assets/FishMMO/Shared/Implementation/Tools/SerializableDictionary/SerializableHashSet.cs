using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Base class for SerializableHashSetBase implementation.</summary>
	public abstract class SerializableHashSetBase
	{
		/// <summary>Base class for Storage implementation.</summary>
		public abstract class Storage { }

		/// <summary>HashSet class definition.</summary>
		protected class HashSet<TValue> : System.Collections.Generic.HashSet<TValue>
		{
			/// <summary>Checks whether the shset condition is met.</summary>
			/// <returns>True if the condition is met; otherwise, false.</returns>
			public HashSet() { }
			/// <summary>Checks whether the shset condition is met.</summary>
			/// <param name="set">The set parameter.</param>
			/// <returns>True if the condition is met; otherwise, false.</returns>
			public HashSet(ISet<TValue> set) : base(set) { }
			/// <summary>Checks whether the shset condition is met.</summary>
			/// <param name="info">The info parameter.</param>
			/// <param name="context">The context parameter.</param>
			/// <returns>True if the condition is met; otherwise, false.</returns>
			public HashSet(SerializationInfo info, StreamingContext context) : base(info, context) { }
		}
	}

	[Serializable]
	/// <summary>SerializableHashSet class definition.</summary>
	public abstract class SerializableHashSet<T> : SerializableHashSetBase, ISet<T>, ISerializationCallbackReceiver, IDeserializationCallback, System.Runtime.Serialization.ISerializable
	{
		HashSet<T> m_hashSet;
		[SerializeField]
		T[] m_keys;

		/// <summary>SerializableHashSet method.</summary>
		/// <returns>The result of the operation.</returns>
		public SerializableHashSet()
		{
			m_hashSet = new HashSet<T>();
		}

		/// <summary>SerializableHashSet method.</summary>
		/// <param name="set">The set parameter.</param>
		/// <returns>The result of the operation.</returns>
		public SerializableHashSet(ISet<T> set)
		{
			m_hashSet = new HashSet<T>(set);
		}

		/// <summary>Copies data from another collection into this one.</summary>
		/// <param name="set">The set parameter.</param>
		public void CopyFrom(ISet<T> set)
		{
			m_hashSet.Clear();
			foreach (var value in set)
			{
				m_hashSet.Add(value);
			}
		}

		/// <summary>Called after deserialization to reconstruct internal state.</summary>
		public void OnAfterDeserialize()
		{
			if (m_keys != null)
			{
				m_hashSet.Clear();
				int n = m_keys.Length;
				for (int i = 0; i < n; ++i)
				{
					m_hashSet.Add(m_keys[i]);
				}

				m_keys = null;
			}
		}

		/// <summary>Called before serialization to prepare data for storage.</summary>
		public void OnBeforeSerialize()
		{
			int n = m_hashSet.Count;
			m_keys = new T[n];

			int i = 0;
			foreach (var value in m_hashSet)
			{
				m_keys[i] = value;
				++i;
			}
		}

		#region ISet<TValue>

		/// <summary>Gets or sets the Count value.</summary>
		public int Count { get { return ((ISet<T>)m_hashSet).Count; } }
		/// <summary>Gets a value indicating whether the readonly condition is met.</summary>
		public bool IsReadOnly { get { return ((ISet<T>)m_hashSet).IsReadOnly; } }

		/// <summary>Adds an item to the collection.</summary>
		/// <param name="item">The item parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool Add(T item)
		{
			return ((ISet<T>)m_hashSet).Add(item);
		}

		/// <summary>ExceptWith method.</summary>
		/// <param name="other">The other parameter.</param>
		public void ExceptWith(IEnumerable<T> other)
		{
			((ISet<T>)m_hashSet).ExceptWith(other);
		}

		/// <summary>IntersectWith method.</summary>
		/// <param name="other">The other parameter.</param>
		public void IntersectWith(IEnumerable<T> other)
		{
			((ISet<T>)m_hashSet).IntersectWith(other);
		}

		/// <summary>Checks whether the propersubsetof condition is met.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool IsProperSubsetOf(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).IsProperSubsetOf(other);
		}

		/// <summary>Checks whether the propersupersetof condition is met.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool IsProperSupersetOf(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).IsProperSupersetOf(other);
		}

		/// <summary>Checks whether the subsetof condition is met.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool IsSubsetOf(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).IsSubsetOf(other);
		}

		/// <summary>Checks whether the supersetof condition is met.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool IsSupersetOf(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).IsSupersetOf(other);
		}

		/// <summary>Overlaps method.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool Overlaps(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).Overlaps(other);
		}

		/// <summary>Sets the equals value.</summary>
		/// <param name="other">The other parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool SetEquals(IEnumerable<T> other)
		{
			return ((ISet<T>)m_hashSet).SetEquals(other);
		}

		/// <summary>SymmetricExceptWith method.</summary>
		/// <param name="other">The other parameter.</param>
		public void SymmetricExceptWith(IEnumerable<T> other)
		{
			((ISet<T>)m_hashSet).SymmetricExceptWith(other);
		}

		/// <summary>UnionWith method.</summary>
		/// <param name="other">The other parameter.</param>
		public void UnionWith(IEnumerable<T> other)
		{
			((ISet<T>)m_hashSet).UnionWith(other);
		}

		/// <summary>Adds an item to the collection.</summary>
		/// <param name="item">The item parameter.</param>
		void ICollection<T>.Add(T item)
		{
			((ISet<T>)m_hashSet).Add(item);
		}

		/// <summary>Clears all items from the collection.</summary>
		public void Clear()
		{
			((ISet<T>)m_hashSet).Clear();
		}

		/// <summary>Checks whether the ntains condition is met.</summary>
		/// <param name="item">The item parameter.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public bool Contains(T item)
		{
			return ((ISet<T>)m_hashSet).Contains(item);
		}

		/// <summary>Copies the collection elements to an array.</summary>
		/// <param name="array">The array parameter.</param>
		/// <param name="arrayIndex">The arrayIndex parameter.</param>
		public void CopyTo(T[] array, int arrayIndex)
		{
			((ISet<T>)m_hashSet).CopyTo(array, arrayIndex);
		}

		/// <summary>Removes an item from the collection.</summary>
		/// <param name="item">The item parameter.</param>
		/// <returns>True if successful; otherwise, false.</returns>
		public bool Remove(T item)
		{
			return ((ISet<T>)m_hashSet).Remove(item);
		}

		/// <summary>Gets the enumerator value.</summary>
		/// <returns>An enumerator for iterating through the collection.</returns>
		public IEnumerator<T> GetEnumerator()
		{
			return ((ISet<T>)m_hashSet).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((ISet<T>)m_hashSet).GetEnumerator();
		}

		#endregion

		#region IDeserializationCallback

		/// <summary>Called when deserialization is complete.</summary>
		/// <param name="sender">The sender parameter.</param>
		public void OnDeserialization(object sender)
		{
			((IDeserializationCallback)m_hashSet).OnDeserialization(sender);
		}

		#endregion

		#region ISerializable

		/// <summary>SerializableHashSet method.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		/// <returns>The result of the operation.</returns>
		protected SerializableHashSet(SerializationInfo info, StreamingContext context)
		{
			m_hashSet = new HashSet<T>(info, context);
		}

		/// <summary>Gets the objectdata value.</summary>
		/// <param name="info">The info parameter.</param>
		/// <param name="context">The context parameter.</param>
		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			((System.Runtime.Serialization.ISerializable)m_hashSet).GetObjectData(info, context);
		}

		#endregion
	}
}