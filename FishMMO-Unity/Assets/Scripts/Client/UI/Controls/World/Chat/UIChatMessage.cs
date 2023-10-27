using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIChatMessage : MonoBehaviour//, IPointerEnterHandler, IPointerExitHandler
	{
		public ChatChannel Channel;
		public TMP_Text CharacterName;
		public TMP_Text Text;
	}
}