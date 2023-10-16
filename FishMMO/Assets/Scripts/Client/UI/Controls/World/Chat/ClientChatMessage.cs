using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class ClientChatMessage : MonoBehaviour//, IPointerEnterHandler, IPointerExitHandler
	{
		public ChatChannel Channel;
		public TMP_Text name;
		public TMP_Text text;
	}
}