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

		public bool GetKey()
		{
			return Input.GetKey(Key);
		}

		public bool GetKeyDown()
		{
			return Input.GetKeyDown(Key);
		}

		public bool GetKeyUp()
		{
			return Input.GetKeyUp(Key);
		}
	}
}