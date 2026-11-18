using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class representing a key or set of keys for Unity Addressable assets, with merge mode support.
	/// </summary>
	[Serializable]
	public class AddressableAssetKey
	{
		/// <summary>List of addressable asset keys.</summary>
		public List<string> Keys;
		/// <summary>Merge mode for combining addressable keys.</summary>
		public Addressables.MergeMode MergeMode = Addressables.MergeMode.None;

		/// <summary>
		/// Constructs an AddressableAssetKey with a single key and optional merge mode.
		/// </summary>
		/// <param name="key">Single addressable asset key.</param>
		/// <param name="mergeMode">Merge mode for combining keys.</param>
		public AddressableAssetKey(string key, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			Keys = new List<string>() { key } ?? throw new ArgumentNullException(nameof(key));
			MergeMode = mergeMode;
		}

		/// <summary>
		/// Constructs an AddressableAssetKey with a list of keys and optional merge mode.
		/// </summary>
		/// <param name="keys">List of addressable asset keys.</param>
		/// <param name="mergeMode">Merge mode for combining keys.</param>
		public AddressableAssetKey(List<string> keys, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			Keys = keys ?? throw new ArgumentNullException(nameof(keys));
			MergeMode = mergeMode;
		}

		/// <summary>
		/// Determines whether the specified object is equal to the current AddressableAssetKey.
		/// Compares the sequence of keys.
		/// </summary>
		/// <param name="obj">Object to compare.</param>
		/// <returns>True if the keys are equal, otherwise false.</returns>
		public override bool Equals(object obj)
		{
			if (obj is AddressableAssetKey other)
			{
				return Keys.SequenceEqual(other.Keys);
			}
			return false;
		}

		/// <summary>
		/// Returns a hash code for the AddressableAssetKey, combining hash codes of all keys.
		/// </summary>
		/// <returns>Combined hash code of all keys.</returns>
		public override int GetHashCode()
		{
			// Combine hash codes of all elements in the list
			return Keys.Aggregate(0, (hash, item) => hash ^ item.GetHashCode());
		}

		/// <summary>
		/// Returns a string representation of the AddressableAssetKey, joining all keys with commas.
		/// </summary>
		/// <returns>Comma-separated string of all keys.</returns>
		public override string ToString()
		{
			return string.Join(", ", Keys);
		}
	}
}