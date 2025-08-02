using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages registration and tracking of scene objects with unique IDs in FishMMO.
	/// </summary>
	public class SceneObject
	{
		/// <summary>
		/// Dictionary of all registered scene objects, keyed by their unique ID.
		/// </summary>
		public readonly static Dictionary<long, ISceneObject> Objects = new Dictionary<long, ISceneObject>();

		/// <summary>
		/// The current highest assigned scene object ID.
		/// </summary>
		private static long currentID = 0;

		/// <summary>
		/// Registers a scene object, assigning a unique ID if not a client object.
		/// </summary>
		/// <param name="sceneObject">The scene object to register.</param>
		/// <param name="asClient">If true, do not assign an ID (server will assign).</param>
		public static void Register(ISceneObject sceneObject, bool asClient = false)
		{
			if (sceneObject == null)
			{
				return;
			}
			// If this is a client, we don't want to assign an ID, as it will be assigned by the server.
			if (!asClient)
			{
				// Assign a unique ID not already in use
				do
				{
					sceneObject.ID = ++currentID;
				}
				while (Objects.ContainsKey(sceneObject.ID));
			}
			//Log.Debug($"Registering {sceneObject.GameObject.name}:{sceneObject.ID} | {asClient}");

			// Add to dictionary if not already present
			if (!Objects.ContainsKey(sceneObject.ID))
			{
				Objects.Add(sceneObject.ID, sceneObject);
			}
		}

		/// <summary>
		/// Unregisters a scene object, removing it from the dictionary.
		/// </summary>
		/// <param name="sceneObject">The scene object to unregister.</param>
		public static void Unregister(ISceneObject sceneObject)
		{
			Objects.Remove(sceneObject.ID);
		}
	}
}