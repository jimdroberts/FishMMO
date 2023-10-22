using System.Collections.Generic;
using FishNet.Object;

public class CharacterAttributeController : NetworkBehaviour
{
	public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

	public readonly Dictionary<int, CharacterAttribute> Attributes = new Dictionary<int, CharacterAttribute>();
	public readonly Dictionary<int, CharacterResourceAttribute> ResourceAttributes = new Dictionary<int, CharacterResourceAttribute>();

	protected void Awake()
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

	public override void OnStartClient()
	{
		base.OnStartClient();

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

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
			ClientManager.UnregisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);

			ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateBroadcast>(OnClientCharacterResourceAttributeUpdateBroadcastReceived);
			ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateMultipleBroadcast>(OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived);
		}
	}

	public void SetAttribute(int id, int baseValue, int modifier)
	{
		if (Attributes.TryGetValue(id, out CharacterAttribute attribute))
		{
			attribute.SetValue(baseValue);
			attribute.SetModifier(modifier);
		}
	}

	public void SetResourceAttribute(int id, int baseValue, int modifier, int currentValue)
	{
		if (ResourceAttributes.TryGetValue(id, out CharacterResourceAttribute attribute))
		{
			attribute.SetValue(baseValue);
			attribute.SetModifier(modifier);
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

	/// <summary>
	/// Server sent an attribute update broadcast.
	/// </summary>
	private void OnClientCharacterAttributeUpdateBroadcastReceived(CharacterAttributeUpdateBroadcast msg)
	{
		CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.templateID);
		if (template != null &&
			Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
		{
			attribute.SetFinal(msg.value);
		}
	}

	/// <summary>
	/// Server sent a multiple attribute update broadcast.
	/// </summary>
	private void OnClientCharacterAttributeUpdateMultipleBroadcastReceived(CharacterAttributeUpdateMultipleBroadcast msg)
	{
		foreach (CharacterAttributeUpdateBroadcast subMsg in msg.attributes)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.templateID);
			if (template != null &&
				Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
			{
				attribute.SetFinal(subMsg.value);
			}
		}
	}

	/// <summary>
	/// Server sent a resource attribute update broadcast.
	/// </summary>
	private void OnClientCharacterResourceAttributeUpdateBroadcastReceived(CharacterResourceAttributeUpdateBroadcast msg)
	{
		CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.templateID);
		if (template != null &&
			ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
		{
			attribute.SetCurrentValue(msg.value);
			attribute.SetFinal(msg.max);
		}
	}

	/// <summary>
	/// Server sent a multiple resource attribute update broadcast.
	/// </summary>
	private void OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived(CharacterResourceAttributeUpdateMultipleBroadcast msg)
	{
		foreach (CharacterResourceAttributeUpdateBroadcast subMsg in msg.attributes)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.templateID);
			if (template != null &&
				ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
			{
				attribute.SetCurrentValue(subMsg.value);
				attribute.SetFinal(subMsg.max);
			}
		}
	}
}