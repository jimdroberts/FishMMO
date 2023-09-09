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

			foreach (CharacterAttribute attribute in existingCharacter.AttributeController.attributes.Values)
			{
				if (attribute.Template.IsResourceAttribute)
				{
					continue;
				}
				if (!attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
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
			foreach (CharacterResourceAttribute attribute in existingCharacter.AttributeController.resourceAttributes.Values)
			{
				if (!attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
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
					existingCharacter.AttributeController.SetAttribute(attribute.TemplateID, attribute.BaseValue, attribute.Modifier);
				}
			}
		}
	}
}