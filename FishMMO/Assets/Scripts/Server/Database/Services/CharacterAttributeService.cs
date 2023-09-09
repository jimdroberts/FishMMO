using System.Linq;
using FishMMO_DB;
using FishMMO_DB.Entities;

namespace FishMMO.Server.Services
{
	public class CharacterAttributeService
	{
		/// <summary>
		/// Save a character attributes to the database.
		/// </summary>
		public static void SaveCharacterAttributes(ServerDbContext dbContext, Character existingCharacter)
		{
			if (existingCharacter == null)
			{
				return;
			}

			var attributes = dbContext.Attributes
				.Where(c => c.CharacterId == existingCharacter.ID)
				.ToDictionary(k => k.TemplateID);

			foreach (CharacterAttribute attribute in existingCharacter.AttributeController.Attributes.Values)
			{
				// is looping resources separately faster than boxing?
				if (attribute.Template.IsResourceAttribute)
				{
					continue;
				}
				if (attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
				{
					dbAttribute.CharacterId = existingCharacter.ID;
					dbAttribute.TemplateID = attribute.Template.ID;
					dbAttribute.BaseValue = attribute.BaseValue;
					dbAttribute.Modifier = attribute.Modifier;
					dbAttribute.CurrentValue = 0;
				}
				else
				{
					dbContext.Attributes.Add(new CharacterAttributeEntity()
					{
						CharacterId = existingCharacter.ID,
						TemplateID = attribute.Template.ID,
						BaseValue = attribute.BaseValue,
						Modifier = attribute.Modifier,
						CurrentValue = 0,
					});
				}
			}
			// is looping resources separately faster than boxing?
			foreach (CharacterResourceAttribute attribute in existingCharacter.AttributeController.ResourceAttributes.Values)
			{
				if (attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
				{
					dbAttribute.CharacterId = existingCharacter.ID;
					dbAttribute.TemplateID = attribute.Template.ID;
					dbAttribute.BaseValue = attribute.BaseValue;
					dbAttribute.Modifier = attribute.Modifier;
					dbAttribute.CurrentValue = attribute.CurrentValue;
				}
				else
				{
					dbContext.Attributes.Add(new CharacterAttributeEntity()
					{
						CharacterId = existingCharacter.ID,
						TemplateID = attribute.Template.ID,
						BaseValue = attribute.BaseValue,
						Modifier = attribute.Modifier,
						CurrentValue = attribute.CurrentValue,
					});
				}
			}
		}

		/// <summary>
		/// Load character attributes from the database.
		/// </summary>
		public static void LoadCharacterAttributes(ServerDbContext dbContext, Character existingCharacter)
		{
			var attributes = dbContext.Attributes
				.Where(c => c.CharacterId == existingCharacter.ID)
				.ToList();

			if (attributes != null)
			{
				foreach (var attribute in attributes)
				{
					CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(attribute.TemplateID);
					if (template != null)
					{
						if (template.IsResourceAttribute)
						{
							existingCharacter.AttributeController.SetResourceAttribute(template.ID, attribute.BaseValue, attribute.Modifier, attribute.CurrentValue);
						}
						else
						{
							existingCharacter.AttributeController.SetAttribute(template.ID, attribute.BaseValue, attribute.Modifier);
						}
					}
				}
			}
		}
	}
}