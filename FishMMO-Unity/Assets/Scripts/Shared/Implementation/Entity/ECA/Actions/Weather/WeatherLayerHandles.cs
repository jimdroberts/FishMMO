using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Remembers which server-side layer handle an <see cref="AddWeatherLayerAction"/> got, under
	/// the name its author gave it, so a <see cref="RemoveWeatherLayerAction"/> can take that exact
	/// layer away again.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists at all.</b> A layer is removed by a handle the server allocated at
	/// runtime, and ECA actions have no way to pass a value from one to the next — each is executed
	/// independently against the event. Naming the layer at authoring time and looking the handle up
	/// by that name is what closes the gap.
	/// </para>
	/// <para>
	/// <b>Server-side only, and small on purpose.</b> Only the server ever reaches this, because
	/// only the server passes the gate in front of it. Entries are dropped when the layer is removed
	/// and when a character is forgotten, so the table holds one entry per storm somebody has
	/// running rather than growing with every trigger ever fired.
	/// </para>
	/// </remarks>
	public static class WeatherLayerHandles
	{
		private static readonly Dictionary<long, Dictionary<string, ushort>> byCharacter = new Dictionary<long, Dictionary<string, ushort>>();

		/// <summary>Records a handle under a name for a character.</summary>
		public static void Remember(ICharacter character, string name, ushort handle)
		{
			if (character == null || string.IsNullOrWhiteSpace(name) || handle == 0)
			{
				return;
			}
			if (!byCharacter.TryGetValue(character.ID, out Dictionary<string, ushort> handles))
			{
				byCharacter[character.ID] = handles = new Dictionary<string, ushort>();
			}
			handles[name] = handle;
		}

		/// <summary>
		/// Takes the handle recorded under that name, removing the record. False when there is none.
		/// </summary>
		/// <remarks>
		/// Taking rather than reading: a handle is good for exactly one removal, and a record left
		/// behind would have a second trigger try to remove a layer the server has already forgotten
		/// — or, worse, one whose number has since been handed to a different layer.
		/// </remarks>
		public static bool TryTake(ICharacter character, string name, out ushort handle)
		{
			handle = 0;
			if (character == null || string.IsNullOrWhiteSpace(name))
			{
				return false;
			}
			if (!byCharacter.TryGetValue(character.ID, out Dictionary<string, ushort> handles) ||
				!handles.TryGetValue(name, out handle))
			{
				return false;
			}
			handles.Remove(name);
			if (handles.Count == 0)
			{
				byCharacter.Remove(character.ID);
			}
			return true;
		}

		/// <summary>Drops every handle recorded for a character.</summary>
		public static void Forget(ICharacter character)
		{
			if (character != null)
			{
				byCharacter.Remove(character.ID);
			}
		}

		/// <summary>Drops everything. For a server teardown, and for tests.</summary>
		public static void Clear() => byCharacter.Clear();

		/// <summary>How many characters have a handle recorded. For tests.</summary>
		public static int TrackedCharacters => byCharacter.Count;
	}
}
