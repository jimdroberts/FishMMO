using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Banker : Interactable
	{
		public override string Title { get { return "Banker"; } }
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.goldenrod); } }
	}
}