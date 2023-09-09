using System.Collections.Generic;
using FishNet.Object;

public class CharacterAttributeController : NetworkBehaviour
{
	public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

	public readonly Dictionary<int, CharacterAttribute> attributes = new Dictionary<int, CharacterAttribute>();
	public readonly Dictionary<int, CharacterResourceAttribute> resourceAttributes = new Dictionary<int, CharacterResourceAttribute>();

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
					resourceAttributes.Add(resource.Template.ID, resource);
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
		
	}

	public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
	{
		return attributes.TryGetValue(template.ID, out attribute);
	}

	public bool TryGetAttribute(int id, out CharacterAttribute attribute)
	{
		return attributes.TryGetValue(id, out attribute);
	}

	public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
	{
		return resourceAttributes.TryGetValue(template.ID, out attribute);
	}

	public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute)
	{
		return resourceAttributes.TryGetValue(id, out attribute);
	}

	public void AddAttribute(CharacterAttribute instance)
	{
		if (!attributes.ContainsKey(instance.Template.ID))
		{
			attributes.Add(instance.Template.ID, instance);

			foreach (CharacterAttributeTemplate parent in instance.Template.ParentTypes)
			{
				CharacterAttribute parentInstance;
				if (attributes.TryGetValue(parent.ID, out parentInstance))
				{
					parentInstance.AddChild(instance);
				}
			}

			foreach (CharacterAttributeTemplate child in instance.Template.ChildTypes)
			{
				CharacterAttribute childInstance;
				if (attributes.TryGetValue(child.ID, out childInstance))
				{
					instance.AddChild(childInstance);
				}
			}

			foreach (CharacterAttributeTemplate dependant in instance.Template.DependantTypes)
			{
				CharacterAttribute dependantInstance;
				if (attributes.TryGetValue(dependant.ID, out dependantInstance))
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
			attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
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
				attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
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
			resourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
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
				resourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
			{
				attribute.SetCurrentValue(subMsg.value);
				attribute.SetFinal(subMsg.max);
			}
		}
	}
}