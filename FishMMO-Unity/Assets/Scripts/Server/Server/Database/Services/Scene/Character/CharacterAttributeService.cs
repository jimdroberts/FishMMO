using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class CharacterAttributeService
	{
		/// <summary>
		/// Save a character attributes to the database.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character)
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
					dbAttribute.CurrentValue = 0;
				}
				else
				{
					dbContext.CharacterAttributes.Add(new CharacterAttributeEntity()
					{
						CharacterID = character.ID,
						TemplateID = attribute.Template.ID,
						Value = attribute.Value,
						CurrentValue = 0,
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
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
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
		/// Load character attributes from the database.
		/// </summary>
		public static void Load(NpgsqlDbContext dbContext, Character character)
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