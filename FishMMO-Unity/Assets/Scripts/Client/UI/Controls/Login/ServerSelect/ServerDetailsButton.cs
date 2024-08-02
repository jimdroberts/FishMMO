using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ServerDetailsButton : MonoBehaviour
	{
		public Button ServerButton;
		public TMP_Text ServerLabel;
		public TMP_Text ServerStatusLabel;

		public WorldServerDetails Details;
		public delegate void ServerSelectEvent(ServerDetailsButton button);
		public event ServerSelectEvent OnServerSelected;

		private Color labelColor;

		public void Initialize(WorldServerDetails details)
		{
			Details = details;
			ServerLabel.text = (details.Locked ? "[Locked] " : " ") + details.Name;
			ServerStatusLabel.text = details.CharacterCount.ToString();
			labelColor = ServerLabel.color;
			gameObject.SetActive(true);
		}

		public void OnClick_ServerButton()
		{
			OnServerSelected?.Invoke(this);
		}

		public void ResetLabelColor()
		{
			ServerLabel.color = labelColor;
			ServerStatusLabel.color = labelColor;
		}

		public void SetLabelColors(Color color)
		{
			labelColor = ServerLabel.color;

			ServerLabel.color = color;
			ServerStatusLabel.color = color;
		}
	}
}