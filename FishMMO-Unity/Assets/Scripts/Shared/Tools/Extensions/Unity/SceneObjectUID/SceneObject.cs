using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class SceneObject
	{
		public readonly static Dictionary<long, ISceneObject> Objects = new Dictionary<long, ISceneObject>();

		private static long currentID = 0;

		public static void Register(ISceneObject sceneObject, long id = -1)
		{
			if (id < currentID)
			{
				do
				{
					sceneObject.ID = ++currentID;
				}
				while (Objects.ContainsKey(sceneObject.ID));
			}

			Objects.Add(sceneObject.ID, sceneObject);
		}

		public static void Unregister(ISceneObject sceneObject)
		{
			Objects.Remove(sceneObject.ID);
		}
	}
}