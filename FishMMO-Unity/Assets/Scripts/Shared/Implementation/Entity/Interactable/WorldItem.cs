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

		/// <summary>
		/// Writes the scene object ID, then the template and stack size.
		/// </summary>
		/// <remarks>
		/// <see cref="Interactable.WritePayload"/> is what sends the scene object ID, and this
		/// override used to skip it. The consequence was total: a client's copy of a world item
		/// kept ID 0 and was never entered into <see cref="SceneObject.Objects"/>, so the interact
		/// key sent <c>InteractableID = 0</c> — an ID the server never issues, because scene object
		/// IDs count down from zero and are always negative. Every ground drop in the game was
		/// therefore impossible to pick up, and the refusal path is silent, so it looked like a
		/// dead keypress rather than a bug.
		/// </remarks>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);

			// Write the Template ID so clients know which data to look up
			writer.WriteInt32(templateID != 0 ? templateID : -1);
			writer.WriteUInt32(Amount);
		}

		/// <inheritdoc />
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			int readTemplateId = reader.ReadInt32();
			Amount = reader.ReadUInt32();

			// Use your existing Cache system to find the ScriptableObject by ID
			if (readTemplateId != -1)
			{
				Template = BaseItemTemplate.Get<BaseItemTemplate>(readTemplateId);
			}
		}

		/// <summary>
		/// Clears the rolled stack when this instance returns to the pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Per-life state. A pooled world item that is not reset comes back out of the pool still
		/// carrying the amount its previous life was spawned with, and
		/// <see cref="ItemSpawnableSettings"/> only overwrites it when the object is respawned
		/// through a spawner — an item dropped by script would keep the old number.
		/// </para>
		/// <para>
		/// The template is deliberately left alone: <c>templateID</c> is a serialized field a
		/// designer may have authored on the prefab, and zeroing it here would strip that on the
		/// first despawn. Amount is the half that is always injected at spawn.
		/// </para>
		/// </remarks>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Amount = 0;
		}
	}
}