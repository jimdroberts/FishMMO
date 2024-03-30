using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;

namespace FishMMO.Shared
{
	public class CharacterAttributeController : CharacterBehaviour, ICharacterAttributeController
	{
		public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

		private readonly Dictionary<int, CharacterAttribute> attributes = new Dictionary<int, CharacterAttribute>();
		private readonly Dictionary<int, CharacterResourceAttribute> resourceAttributes = new Dictionary<int, CharacterResourceAttribute>();

		public Dictionary<int, CharacterAttribute> Attributes { get { return attributes; } }
		public Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get { return resourceAttributes; } }

		public override void OnAwake()
		{
			if (CharacterAttributeDatabase != null)
			{
				foreach (CharacterAttributeTemplate attribute in CharacterAttributeDatabase.Attributes.Values)
				{
					if (attribute.IsResourceAttribute)
					{
						CharacterResourceAttribute resource = new CharacterResourceAttribute(attribute.ID, attribute.InitialValue, attribute.InitialValue, 0);
						AddAttribute(resource);
						ResourceAttributes.Add(resource.Template.ID, resource);
					}
					else
					{
						AddAttribute(new CharacterAttribute(attribute.ID, attribute.InitialValue, 0));
					}
				}
			}
		}

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int attributeCount = reader.ReadInt32();
			if (attributeCount > 0)
			{
				for (int i = 0; i < attributeCount; ++i)
				{
					int templateID = reader.ReadInt32();
					int value = reader.ReadInt32();

					SetAttribute(templateID, value);
				}
			}

			int resourceAttributeCount = reader.ReadInt32();
			if (resourceAttributeCount > 0)
			{
				for (int i = 0; i < resourceAttributeCount; ++i)
				{
					int templateID = reader.ReadInt32();
					int value = reader.ReadInt32();
					int currentValue = reader.ReadInt32();

					SetResourceAttribute(templateID, value, currentValue);
				}
			}
		}

		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			writer.WriteInt32(Attributes.Count);
			foreach (CharacterAttribute attribute in Attributes.Values)
			{
				writer.WriteInt32(attribute.Template.ID);
				writer.WriteInt32(attribute.Value);
			}

			writer.WriteInt32(ResourceAttributes.Count);
			foreach (CharacterResourceAttribute resourceAttribute in ResourceAttributes.Values)
			{
				writer.WriteInt32(resourceAttribute.Template.ID);
				writer.WriteInt32(resourceAttribute.Value);
				writer.WriteInt32(resourceAttribute.CurrentValue);
			}
		}

		public void SetAttribute(int id, int value)
		{
			if (Attributes.TryGetValue(id, out CharacterAttribute attribute))
			{
				attribute.SetValue(value);
			}
		}

		public void SetResourceAttribute(int id, int value, int currentValue)
		{
			if (ResourceAttributes.TryGetValue(id, out CharacterResourceAttribute attribute))
			{
				attribute.SetValue(value);
				attribute.SetCurrentValue(currentValue);
			}
		}

		public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
		{
			return Attributes.TryGetValue(template.ID, out attribute);
		}

		public bool TryGetAttribute(int id, out CharacterAttribute attribute)
		{
			return Attributes.TryGetValue(id, out attribute);
		}

		public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
		{
			return ResourceAttributes.TryGetValue(template.ID, out attribute);
		}

		public float GetResourceAttributeCurrentPercentage(CharacterAttributeTemplate template)
		{
			if (ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
			{
				return attribute.FinalValue / attribute.CurrentValue;
			}
			return 0.0f;
		}

		public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute)
		{
			return ResourceAttributes.TryGetValue(id, out attribute);
		}

		public void AddAttribute(CharacterAttribute instance)
		{
			if (!Attributes.ContainsKey(instance.Template.ID))
			{
				Attributes.Add(instance.Template.ID, instance);

				foreach (CharacterAttributeTemplate parent in instance.Template.ParentTypes)
				{
					CharacterAttribute parentInstance;
					if (Attributes.TryGetValue(parent.ID, out parentInstance))
					{
						parentInstance.AddChild(instance);
					}
				}

				foreach (CharacterAttributeTemplate child in instance.Template.ChildTypes)
				{
					CharacterAttribute childInstance;
					if (Attributes.TryGetValue(child.ID, out childInstance))
					{
						instance.AddChild(childInstance);
					}
				}

				foreach (CharacterAttributeTemplate dependant in instance.Template.DependantTypes)
				{
					CharacterAttribute dependantInstance;
					if (Attributes.TryGetValue(dependant.ID, out dependantInstance))
					{
						instance.AddDependant(dependantInstance);
					}
				}
			}
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);

			ClientManager.RegisterBroadcast<CharacterResourceAttributeUpdateBroadcast>(OnClientCharacterResourceAttributeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterResourceAttributeUpdateMultipleBroadcast>(OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived);
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);

				ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateBroadcast>(OnClientCharacterResourceAttributeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateMultipleBroadcast>(OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateBroadcastReceived(CharacterAttributeUpdateBroadcast msg, Channel channel)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.templateID);
			if (template != null &&
				Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
			{
				attribute.SetValue(msg.value);
			}
		}

		/// <summary>
		/// Server sent a multiple attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateMultipleBroadcastReceived(CharacterAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (CharacterAttributeUpdateBroadcast subMsg in msg.attributes)
			{
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.templateID);
				if (template != null &&
					Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
				{
					attribute.SetValue(subMsg.value);
				}
			}
		}

		/// <summary>
		/// Server sent a resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateBroadcastReceived(CharacterResourceAttributeUpdateBroadcast msg, Channel channel)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.templateID);
			if (template != null &&
				ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
			{
				attribute.SetCurrentValue(msg.currentValue);
				attribute.SetValue(msg.value);
			}
		}

		/// <summary>
		/// Server sent a multiple resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived(CharacterResourceAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (CharacterResourceAttributeUpdateBroadcast subMsg in msg.attributes)
			{
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.templateID);
				if (template != null &&
					ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
				{
					attribute.SetCurrentValue(subMsg.currentValue);
					attribute.SetValue(subMsg.value);
				}
			}
		}
#endif
	}
}