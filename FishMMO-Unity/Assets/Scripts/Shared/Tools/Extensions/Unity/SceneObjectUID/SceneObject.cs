using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class SceneObject
	{
		public readonly static Dictionary<long, ISceneObject> Objects = new Dictionary<long, ISceneObject>();

		private static long currentID = 0;

		public static void Register(ISceneObject sceneObject, bool asClient = false)
		{
			if (sceneObject == null)
			{
				return;
			}
			// If this is a client, we don't want to assign an ID, as it will be assigned by the server.
			if (!asClient)
			{
				do
				{
					sceneObject.ID = ++currentID;
				}
				while (Objects.ContainsKey(sceneObject.ID));
			}
			//Log.Debug($"Registering {sceneObject.GameObject.name}:{sceneObject.ID} | {asClient}");

			if (!Objects.ContainsKey(sceneObject.ID))
			{
				Objects.Add(sceneObject.ID, sceneObject);
			}
		}

		public static void Unregister(ISceneObject sceneObject)
		{
			Objects.Remove(sceneObject.ID);
		}
	}
}