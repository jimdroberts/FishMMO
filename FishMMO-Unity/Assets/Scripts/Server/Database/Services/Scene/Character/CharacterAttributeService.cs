using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing a character's attributes, including saving, deleting, and loading attribute data from the database.
		/// </summary>
		public class CharacterAttributeService
	{
		/// <summary>
		/// Saves a character's attributes to the database.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character whose attributes will be saved.</param>
		public static void Save(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			var attributes = dbContext.CharacterAttributes.Where(c => c.CharacterID == character.ID)
														  .ToDictionary(k => k.TemplateID);

			foreach (CharacterAttribute attribute in attributeController.Attributes.Values)
			{
				if (attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
				{
					dbAttribute.CharacterID = character.ID;
					dbAttribute.TemplateID = attribute.Template.ID;
					dbAttribute.Value = attribute.Value;
					dbAttribute.CurrentValue = 0.0f;
				}
				else
				{
					dbContext.CharacterAttributes.Add(new CharacterAttributeEntity()
					{
						CharacterID = character.ID,
						TemplateID = attribute.Template.ID,
						Value = attribute.Value,
						CurrentValue = 0.0f,
					});
				}
			}
			foreach (CharacterResourceAttribute attribute in attributeController.ResourceAttributes.Values)
			{
				if (attributes.TryGetValue(attribute.Template.ID, out CharacterAttributeEntity dbAttribute))
				{
					dbAttribute.CharacterID = character.ID;
					dbAttribute.TemplateID = attribute.Template.ID;
					dbAttribute.Value = attribute.Value;
					dbAttribute.CurrentValue = attribute.CurrentValue;
				}
				else
				{
					dbContext.CharacterAttributes.Add(new CharacterAttributeEntity()
					{
						CharacterID = character.ID,
						TemplateID = attribute.Template.ID,
						Value = attribute.Value,
						CurrentValue = attribute.CurrentValue,
					});
				}
			}
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes all attributes for a character from the database. If keepData is false, the entries are removed.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="characterID">The character ID.</param>
		/// <param name="keepData">Whether to keep the data (currently not implemented).</param>
		public static void Delete(NpgsqlDbContext dbContext, long characterID, bool keepData = true)
		{
			if (characterID == 0)
			{
				return;
			}
			if (!keepData)
			{
				var attributes = dbContext.CharacterAttributes.Where(c => c.CharacterID == characterID);
				if (attributes != null)
				{
					dbContext.CharacterAttributes.RemoveRange(attributes);
					dbContext.SaveChanges();
				}
			}
		}

		/// <summary>
		/// Loads a character's attributes from the database and assigns them to the character's attribute controller.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="character">The player character to load attributes for.</param>
		public static void Load(NpgsqlDbContext dbContext, IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			var attributes = dbContext.CharacterAttributes.Where(c => c.CharacterID == character.ID);
			foreach (CharacterAttributeEntity attribute in attributes)
			{
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(attribute.TemplateID);
				if (template != null)
				{
					if (template.IsResourceAttribute)
					{
						attributeController.SetResourceAttribute(template.ID, attribute.Value, attribute.CurrentValue);
					}
					else
					{
						attributeController.SetAttribute(template.ID, attribute.Value);
					}
				}
			};
		}
	}
}