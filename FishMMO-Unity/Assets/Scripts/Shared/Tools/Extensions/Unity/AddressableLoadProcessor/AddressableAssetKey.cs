using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared
{
	[Serializable]
	public class AddressableAssetKey
	{
		public List<string> Keys;
		public Addressables.MergeMode MergeMode = Addressables.MergeMode.None;

		public AddressableAssetKey(string key, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			Keys = new List<string>() { key } ?? throw new ArgumentNullException(nameof(key));
			MergeMode = mergeMode;
		}

		public AddressableAssetKey(List<string> keys, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			Keys = keys ?? throw new ArgumentNullException(nameof(keys));
			MergeMode = mergeMode;
		}

		public override bool Equals(object obj)
		{
			if (obj is AddressableAssetKey other)
			{
				return Keys.SequenceEqual(other.Keys);
			}
			return false;
		}

		public override int GetHashCode()
		{
			// Combine hash codes of all elements in the list
			return Keys.Aggregate(0, (hash, item) => hash ^ item.GetHashCode());
		}

		public override string ToString()
		{
			return string.Join(", ", Keys);
		}
	}
}