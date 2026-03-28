using FishMMO.Shared.Core;
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
		[SerializeField, TemplateReference(typeof(BaseItemTemplate))]
		private int templateID;

		private BaseItemTemplate cachedTemplate;
		public BaseItemTemplate Template
		{
			get
			{
				if (cachedTemplate == null && templateID != 0)
				{
					cachedTemplate = BaseItemTemplate.Get<BaseItemTemplate>(templateID);
				}
				return cachedTemplate;
			}
			set
			{
				cachedTemplate = value;
				templateID = value != null ? value.ID : 0;
				OnTemplateChanged();
			}
		}

		public uint Amount;

		/// <summary>
		/// Achievement template ID to increment when a player picks up this world item.
		/// </summary>
		[TemplateReference(typeof(AchievementTemplate))]
		public int AchievementTemplateID;

		/// <inheritdoc />
		uint IWorldItem.Amount { get => Amount; set => Amount = value; }

		/// <inheritdoc />
		int IWorldItem.AchievementTemplateID => AchievementTemplateID;

		public override string Title => Template != null ? Template.Name : "";

		// This handles the visual swap when a template is injected
		private void OnTemplateChanged()
		{
#if !UNITY_SERVER
			if (cachedTemplate == null) return;

			MeshFilter mf = GetComponentInChildren<MeshFilter>();
			if (mf != null && cachedTemplate.Mesh != null)
			{
				mf.sharedMesh = cachedTemplate.Mesh;
			}
			// Add logic here for Materials or Icons if needed
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			// Write the Template ID so clients know which data to look up
			writer.WriteInt32(templateID != 0 ? templateID : -1);
			writer.WriteUInt32(Amount);
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			int readTemplateId = reader.ReadInt32();
			Amount = reader.ReadUInt32();

			// Use your existing Cache system to find the ScriptableObject by ID
			if (readTemplateId != -1)
			{
				Template = BaseItemTemplate.Get<BaseItemTemplate>(readTemplateId);
			}
		}
	}
}