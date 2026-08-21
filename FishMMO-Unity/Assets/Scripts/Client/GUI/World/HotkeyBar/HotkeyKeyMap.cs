namespace FishMMO.Client
{
	/// <summary>
	/// Maps hotkey slot indices to the input names bound to them.
	/// </summary>
	/// <remarks>
	/// Previously a static method on the uGUI <c>UIHotkeyBar</c>. It is not a rendering concern —
	/// it names input bindings — and the UI Toolkit hotkey bar needs it, so it moved out of the
	/// uGUI tree rather than being duplicated into it.
	/// </remarks>
	public static class HotkeyKeyMap
	{
		/// <summary>
		/// Gets the key mapping string for a hotkey index.
		/// </summary>
		/// <param name="hotkeyIndex">The hotkey index.</param>
		/// <returns>The key mapping string, or empty when the index is not bound.</returns>
		public static string Get(int hotkeyIndex)
		{
			switch (hotkeyIndex)
			{
				case 0:
					return "Left Mouse";
				case 1:
					return "Right Mouse";
				case 2:
					return "Hotkey 1";
				case 3:
					return "Hotkey 2";
				case 4:
					return "Hotkey 3";
				case 5:
					return "Hotkey 4";
				case 6:
					return "Hotkey 5";
				case 7:
					return "Hotkey 6";
				case 8:
					return "Hotkey 7";
				case 9:
					return "Hotkey 8";
				case 10:
					return "Hotkey 9";
				case 11:
					return "Hotkey 0";
				default:
					return "";
			}
		}
	}
}
