using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Client
{
	public struct KeyMap
	{
		public string VirtualKey;
		public KeyCode Key;

		public KeyMap(string virtualKey, KeyCode key)
		{
			VirtualKey = virtualKey;
			Key = key;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetKey()
		{
			return Input.GetKey(Key);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetKeyDown()
		{
			return Input.GetKeyDown(Key);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetKeyUp()
		{
			return Input.GetKeyUp(Key);
		}
	}
}