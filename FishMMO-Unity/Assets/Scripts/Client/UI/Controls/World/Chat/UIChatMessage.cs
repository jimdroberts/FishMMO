using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying a single chat message, including channel, character name, and text.
	/// </summary>
	public class UIChatMessage : MonoBehaviour//, IPointerEnterHandler, IPointerExitHandler
	{
		/// <summary>
		/// The chat channel this message belongs to (e.g., System, Local, Party).
		/// </summary>
		public ChatChannel Channel;

		/// <summary>
		/// The label displaying the character name who sent the message.
		/// </summary>
		public TMP_Text CharacterName;

		/// <summary>
		/// The label displaying the chat message text.
		/// </summary>
		public TMP_Text Text;
	}
}