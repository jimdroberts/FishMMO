using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Client
{
	public class ServerDetailsButton : MonoBehaviour
	{
		public Button serverButton;
		public TMP_Text serverLabel;
		public TMP_Text serverStatusLabel;
		public WorldServerDetails details;

		public delegate void ServerSelectEvent(ServerDetailsButton button);
		public event ServerSelectEvent OnServerSelected;

		public void Initialize(WorldServerDetails details)
		{
			this.details = details;
			serverLabel.text = (details.locked ? "[Locked] " : " ") + details.name;
			serverStatusLabel.text = details.characterCount.ToString();
		}

		public void OnClick_ServerButton()
		{
			OnServerSelected?.Invoke(this);
		}

		public void SetLabelColors(Color color)
		{
			serverLabel.color = color;
			serverStatusLabel.color = color;
		}
	}
}