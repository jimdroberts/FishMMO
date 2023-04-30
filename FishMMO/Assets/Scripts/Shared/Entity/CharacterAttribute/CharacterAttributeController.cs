using System.Collections.Generic;
using FishNet.Object;

public class CharacterAttributeController : NetworkBehaviour
{
	public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

	public readonly Dictionary<string, CharacterAttribute> attributes = new Dictionary<string, CharacterAttribute>();
	public readonly Dictionary<string, CharacterResourceAttribute> resourceAttributes = new Dictionary<string, CharacterResourceAttribute>();

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
					resourceAttributes.Add(resource.Template.Name, resource);
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
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
			ClientManager.UnregisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);
		}
	}

	public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
	{
		return attributes.TryGetValue(template.name, out attribute);
	}

	public bool TryGetAttribute(string name, out CharacterAttribute attribute)
	{
		return attributes.TryGetValue(name, out attribute);
	}

	public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
	{
		return resourceAttributes.TryGetValue(template.name, out attribute);
	}

	public bool TryGetResourceAttribute(string name, out CharacterResourceAttribute attribute)
	{
		return resourceAttributes.TryGetValue(name, out attribute);
	}

	public void AddAttribute(CharacterAttribute instance)
	{
		if (!attributes.ContainsKey(instance.Template.Name))
		{
			attributes.Add(instance.Template.Name, instance);

			foreach (CharacterAttributeTemplate parent in instance.Template.ParentTypes)
			{
				CharacterAttribute parentInstance;
				if (attributes.TryGetValue(parent.Name, out parentInstance))
				{
					parentInstance.AddChild(instance);
				}
			}

			foreach (CharacterAttributeTemplate child in instance.Template.ChildTypes)
			{
				CharacterAttribute childInstance;
				if (attributes.TryGetValue(child.Name, out childInstance))
				{
					instance.AddChild(childInstance);
				}
			}

			foreach (CharacterAttributeTemplate dependant in instance.Template.DependantTypes)
			{
				CharacterAttribute dependantInstance;
				if (attributes.TryGetValue(dependant.Name, out dependantInstance))
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
		if (CharacterAttributeTemplate.Cache.TryGetValue(msg.templateID, out CharacterAttributeTemplate template) &&
			attributes.TryGetValue(template.Name, out CharacterAttribute attribute))
		{
			attribute.SetModifier(msg.modifier);
			attribute.SetValue(msg.baseValue);
		}
	}

	/// <summary>
	/// Server sent a multiple attribute update broadcasts.
	/// </summary>
	private void OnClientCharacterAttributeUpdateMultipleBroadcastReceived(CharacterAttributeUpdateMultipleBroadcast msg)
	{
		foreach (CharacterAttributeUpdateBroadcast subMsg in msg.attributes)
		{
			if (CharacterAttributeTemplate.Cache.TryGetValue(subMsg.templateID, out CharacterAttributeTemplate template) &&
				attributes.TryGetValue(template.Name, out CharacterAttribute attribute))
			{
				attribute.SetModifier(subMsg.modifier);
				attribute.SetValue(subMsg.baseValue);
			}
		}
	}
}