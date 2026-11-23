using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A player's one marked location (issue #241), as <see cref="ICharacter.TryGet{T}"/> finds it: a character's parts are
	/// looked up by interface only (a class throws "must be an interface").
	/// </summary>
	public interface ITravelMarkController : ICharacterBehaviour
	{
		/// <summary>Whether the character has marked a location.</summary>
		bool HasMark { get; }

		/// <summary>The marked open-world scene.</summary>
		string SceneName { get; }

		/// <summary>The scene row ID (channel) the mark was made in, or 0.</summary>
		long SceneID { get; }

		/// <summary>The marked position.</summary>
		Vector3 Position { get; }

		/// <summary>The facing at the mark, degrees about the vertical axis.</summary>
		float Heading { get; }

		/// <summary>The facing as a rotation.</summary>
		Quaternion Rotation { get; }

		/// <summary>Whether the mark has changed since it was last saved.</summary>
		bool PersistenceDirty { get; }

		/// <summary>Server: records a new mark, replacing the old one.</summary>
		void SetMark(string sceneName, long sceneID, Vector3 position, float heading);

		/// <summary>Server: restores the saved mark at load. Not a change: nothing is dirtied.</summary>
		void Restore(string sceneName, long sceneID, Vector3 position, float heading);

		/// <summary>Server: the save of this mark landed; clears the dirty flag only when the mark is still the one written.</summary>
		void MarkPersisted(string sceneName, long sceneID, Vector3 position, float heading);
	}
}
