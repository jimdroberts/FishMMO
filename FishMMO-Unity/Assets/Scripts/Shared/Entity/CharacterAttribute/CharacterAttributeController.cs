using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;

namespace FishMMO.Shared
{
	public class CharacterAttributeController : CharacterBehaviour, ICharacterAttributeController
	{
		public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

		public CharacterAttributeTemplate HealthResourceTemplate;
		public CharacterAttributeTemplate HealthRegenerationTemplate;
		public CharacterAttributeTemplate ManaResourceTemplate;
		public CharacterAttributeTemplate ManaRegenerationTemplate;
		public CharacterAttributeTemplate StaminaResourceTemplate;
		public CharacterAttributeTemplate StaminaRegenerationTemplate;

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
						AddResourceAttribute(new CharacterResourceAttribute(attribute.ID, attribute.InitialValue, attribute.InitialValue, 0));
					}
					else
					{
						AddAttribute(new CharacterAttribute(attribute.ID, attribute.InitialValue, 0));
					}
				}

				InitializeAttributeDependents();
				InitializeResourceAttributeDependents();
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
			}
		}

		private void AddDependents(CharacterAttribute instance)
		{
			foreach (CharacterAttributeTemplate parent in instance.Template.ParentTypes)
			{
				CharacterAttribute parentInstance;
				if (parent.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(parent.ID, out CharacterResourceAttribute parentResourceInstance))
					{
						instance.AddChild(parentResourceInstance);
					}
				}
				else if (Attributes.TryGetValue(parent.ID, out parentInstance))
				{
					instance.AddChild(parentInstance);
				}
			}

			foreach (CharacterAttributeTemplate child in instance.Template.ChildTypes)
			{
				CharacterAttribute childInstance;
				if (child.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(child.ID, out CharacterResourceAttribute childResourceInstance))
					{
						instance.AddChild(childResourceInstance);
					}
				}
				else if (Attributes.TryGetValue(child.ID, out childInstance))
				{
					instance.AddChild(childInstance);
				}
			}

			foreach (CharacterAttributeTemplate dependant in instance.Template.DependantTypes)
			{
				CharacterAttribute dependantInstance;
				if (dependant.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(dependant.ID, out CharacterResourceAttribute dependantResourceInstance))
					{
						instance.AddDependant(dependantResourceInstance);
					}
				}
				else if (Attributes.TryGetValue(dependant.ID, out dependantInstance))
				{
					instance.AddDependant(dependantInstance);
				}
			}
		}

		public void InitializeAttributeDependents()
		{
			foreach (CharacterAttribute instance in attributes.Values)
			{
				AddDependents(instance);
			}
		}

		public void AddResourceAttribute(CharacterResourceAttribute instance)
		{
			if (!ResourceAttributes.ContainsKey(instance.Template.ID))
			{
				ResourceAttributes.Add(instance.Template.ID, instance);
			}
		}

		public void InitializeResourceAttributeDependents()
		{
			foreach (CharacterResourceAttribute instance in resourceAttributes.Values)
			{
				AddDependents(instance);
			}
		}

		private float accumulatedRegenDelta = 0.0f;

		public void Regenerate(float deltaTime)
		{
			const float REGEN_TICK_RATE = 5.0f;

			accumulatedRegenDelta += deltaTime;

			// Check if accumulatedDelta has reached or exceeded REGEN_TICK_RATE seconds
			if (accumulatedRegenDelta >= REGEN_TICK_RATE)
			{
				// Calculate how many 5-second intervals have passed
				int intervals = (int)(accumulatedRegenDelta / REGEN_TICK_RATE);

				// Reduce accumulatedDelta by the total duration of processed intervals
				accumulatedRegenDelta -= intervals * REGEN_TICK_RATE;

				// Regenerate health, mana, and stamina
				RegenerateResource(HealthResourceTemplate, HealthRegenerationTemplate, intervals);
				RegenerateResource(ManaResourceTemplate, ManaRegenerationTemplate, intervals);
				RegenerateResource(StaminaResourceTemplate, StaminaRegenerationTemplate, intervals);
			}
		}

		private void RegenerateResource(CharacterAttributeTemplate resourceTemplate, CharacterAttributeTemplate regenerationTemplate, int intervals)
		{
			if (resourceTemplate != null &&
				regenerationTemplate != null &&
				resourceAttributes.TryGetValue(resourceTemplate.ID, out CharacterResourceAttribute resource))
			{
				int regenAmountPerInterval = resource.GetDependantFinalValue(regenerationTemplate.Name);
				int totalRegenAmount = regenAmountPerInterval * intervals;
				resource.Gain(totalRegenAmount);
			}
		}

		public void ApplyResourceState(CharacterAttributeResourceState resourceState)
		{
			if (resourceAttributes.TryGetValue(HealthResourceTemplate.ID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplate.ID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out CharacterResourceAttribute stamina))
			{
				accumulatedRegenDelta = resourceState.RegenDelta;
				health.SetCurrentValue(resourceState.Health);
				mana.SetCurrentValue(resourceState.Mana);
				stamina.SetCurrentValue(resourceState.Stamina);
			}
		}

		public CharacterAttributeResourceState GetResourceState()
		{
			if (resourceAttributes.TryGetValue(HealthResourceTemplate.ID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplate.ID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out CharacterResourceAttribute stamina))
			{
				return new CharacterAttributeResourceState()
				{
					RegenDelta = accumulatedRegenDelta,
					Health = health.CurrentValue,
					Mana = mana.CurrentValue,
					Stamina = stamina.CurrentValue,
				};
			}
			return default;
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