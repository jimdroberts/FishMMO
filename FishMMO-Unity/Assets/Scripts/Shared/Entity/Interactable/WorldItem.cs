using FishMMO.Server.Core.World.SceneServer;
using UnityEngine;
using FishNet.Connection;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an item that exists in the world and can be interacted with or picked up by players.
	/// </summary>
	public class WorldItem : Interactable, IWorldItem
	{
		// Change to a property with a private setter or public set
		[SerializeField]
		private BaseItemTemplate template;
		public BaseItemTemplate Template
		{
			get => template;
			set { template = value; OnTemplateChanged(); }
		}

		public uint Amount;

		/// <summary>
		/// Achievement to increment when a player picks up this world item.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		uint IWorldItem.Amount { get => Amount; set => Amount = value; }

		/// <inheritdoc />
		AchievementTemplate IWorldItem.AchievementTemplate => AchievementTemplate;

		public override string Title => template != null ? template.Name : "";

		// This handles the visual swap when a template is injected
		private void OnTemplateChanged()
		{
#if !UNITY_SERVER
			if (template == null) return;

			MeshFilter mf = GetComponentInChildren<MeshFilter>();
			if (mf != null && template.Mesh != null)
			{
				mf.sharedMesh = template.Mesh;
			}
			// Add logic here for Materials or Icons if needed
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			// Write the Template ID so clients know which data to look up
			writer.WriteInt32(template != null ? template.ID : -1);
			writer.WriteUInt32(Amount);
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			int templateId = reader.ReadInt32();
			Amount = reader.ReadUInt32();

			// Use your existing Cache system to find the ScriptableObject by ID
			if (templateId != -1)
			{
				Template = BaseItemTemplate.Get<BaseItemTemplate>(templateId);
			}
		}
	}
}