using UnityEngine;

namespace Client
{
	public struct KeyMap
	{
		public string virtualKey;
		public KeyCode key;

		public KeyMap(string virtualKey, KeyCode key)
		{
			this.virtualKey = virtualKey;
			this.key = key;
		}

		public bool GetKey()
		{
			return Input.GetKey(key);
		}

		public bool GetKeyDown()
		{
			return Input.GetKeyDown(key);
		}

		public bool GetKeyUp()
		{
			return Input.GetKeyUp(key);
		}
	}
}