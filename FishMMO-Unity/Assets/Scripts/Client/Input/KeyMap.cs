using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Represents a mapping between a custom virtual key name and its corresponding Unity <see cref="KeyCode"/>.
	/// This struct provides a lightweight way to associate a user-friendly name with a specific physical key,
	/// and includes convenience methods to check the current state of the mapped key using Unity's Input system.
	/// Being a struct, it is a value type, which can offer minor performance benefits for small, frequently
	/// copied data, avoiding heap allocations.
	/// </summary>
	public struct KeyMap
	{
		/// <summary>
		/// Gets the custom string name for this virtual key (e.g., "Jump", "Interact", "Hotkey 1").
		/// This name is used to retrieve the key mapping from the <see cref="InputManager"/>.
		/// This property is read-only after initialization.
		/// </summary>
		public string VirtualKey { get; private set; }

		/// <summary>
		/// Gets or sets the Unity <see cref="KeyCode"/> that is mapped to this virtual key.
		/// This allows for runtime key rebinding by changing the underlying physical key.
		/// </summary>
		public KeyCode Key { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="KeyMap"/> struct.
		/// </summary>
		/// <param name="virtualKey">The custom string name that identifies this key mapping.</param>
		/// <param name="key">The Unity <see cref="KeyCode"/> to which this virtual key is mapped.</param>
		public KeyMap(string virtualKey, KeyCode key)
		{
			VirtualKey = virtualKey; // Assign the virtual key name.
			Key = key;               // Assign the physical key code.
		}

		/// <summary>
		/// Checks if the mapped physical key is currently being held down.
		/// This method efficiently wraps <see cref="Input.GetKey(KeyCode)"/>.
		/// </summary>
		/// <returns>
		/// <c>true</c> if the <see cref="Key"/> is currently held down; otherwise, <c>false</c>.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)] // Hint to the JIT compiler to inline this method call for performance.
		public bool GetKey()
		{
			return Input.GetKey(Key);
		}

		/// <summary>
		/// Checks if the mapped physical key was pressed down in the current frame.
		/// This method efficiently wraps <see cref="Input.GetKeyDown(KeyCode)"/>.
		/// </summary>
		/// <returns>
		/// <c>true</c> if the <see cref="Key"/> was pressed down in this frame; otherwise, <c>false</c>.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetKeyDown()
		{
			return Input.GetKeyDown(Key);
		}

		/// <summary>
		/// Checks if the mapped physical key was released in the current frame.
		/// This method efficiently wraps <see cref="Input.GetKeyUp(KeyCode)"/>.
		/// </summary>
		/// <returns>
		/// <c>true</c> if the <see cref="Key"/> was released in this frame; otherwise, <c>false</c>.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool GetKeyUp()
		{
			return Input.GetKeyUp(Key);
		}
	}
}