using UnityEngine;
using FishMMO.Shared.Core;

// Every player carries its one marked location (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.TravelMarkController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// A player's one marked location: where the Mark spell put it and the Recall spell sends it (issue #241).
	/// </summary>
	/// <remarks>
	/// <para>Server state only. Nothing about the mark is sent to clients yet; the spells answer with toasts.</para>
	/// <para><b>Saved when dirty.</b> <see cref="SetMark"/> dirties it; the save participant (Travel.Server) captures it on
	/// the next save and confirms it with <see cref="MarkPersisted"/>, which only clears the dirty flag when the mark is still
	/// the one that was written — a mark made while a save is in flight stays dirty for the next.</para>
	/// </remarks>
	public class TravelMarkController : CharacterPart, ITravelMarkController
	{
		/// <summary>Whether the character has marked a location.</summary>
		public bool HasMark { get; private set; }

		/// <summary>The marked open-world scene.</summary>
		public string SceneName { get; private set; }

		/// <summary>The scene row ID (channel) the mark was made in, or 0.</summary>
		public long SceneID { get; private set; }

		/// <summary>The marked position.</summary>
		public Vector3 Position { get; private set; }

		/// <summary>The facing at the mark, degrees about the vertical axis.</summary>
		public float Heading { get; private set; }

		/// <summary>Whether the mark has changed since it was last saved.</summary>
		public bool PersistenceDirty { get; private set; }

		/// <summary>The facing as a rotation.</summary>
		public Quaternion Rotation => Quaternion.Euler(0.0f, Heading, 0.0f);

		/// <summary>Server: records a new mark, replacing the old one.</summary>
		public void SetMark(string sceneName, long sceneID, Vector3 position, float heading)
		{
			HasMark = !string.IsNullOrEmpty(sceneName);
			SceneName = sceneName;
			SceneID = sceneID;
			Position = position;
			Heading = Mathf.Repeat(heading, 360.0f);
			PersistenceDirty = true;
		}

		/// <summary>Server: restores the saved mark at load. Not a change: nothing is dirtied.</summary>
		public void Restore(string sceneName, long sceneID, Vector3 position, float heading)
		{
			HasMark = !string.IsNullOrEmpty(sceneName);
			SceneName = sceneName;
			SceneID = sceneID;
			Position = position;
			Heading = heading;
			PersistenceDirty = false;
		}

		/// <summary>
		/// Server: the save of this mark landed. Clears the dirty flag only when the mark is still exactly what was written.
		/// </summary>
		public void MarkPersisted(string sceneName, long sceneID, Vector3 position, float heading)
		{
			if (PersistenceDirty &&
				string.Equals(SceneName, sceneName, System.StringComparison.Ordinal) &&
				SceneID == sceneID &&
				Position == position &&
				Mathf.Approximately(Heading, heading))
			{
				PersistenceDirty = false;
			}
		}

		/// <inheritdoc/>
		public override void ResetState(bool asServer)
		{
			HasMark = false;
			SceneName = null;
			SceneID = 0;
			Position = Vector3.zero;
			Heading = 0.0f;
			PersistenceDirty = false;
		}
	}
}
