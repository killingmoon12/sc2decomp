using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using Engine.Serialization;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Entity helpers shared by the host and its clients.
///
/// Remote players must never be created through
/// <see cref="DatabaseManager.CreateEntity(Project,string,ValuesDictionary,bool)"/>
/// with overrides, because that method mutates the shared entity template
/// dictionary. We deep-copy the template first and apply our own values to the
/// copy instead.
/// </summary>
public static class MultiplayerEntities
{
	public static ValuesDictionary CloneValuesDictionary(ValuesDictionary source)
	{
		ValuesDictionary valuesDictionary = new ValuesDictionary();
		valuesDictionary.DatabaseObject = source.DatabaseObject;
		foreach (KeyValuePair<string, object> item in source)
		{
			if (item.Value is ValuesDictionary source2)
			{
				valuesDictionary.SetValue(item.Key, CloneValuesDictionary(source2));
			}
			else
			{
				valuesDictionary.SetValue(item.Key, item.Value);
			}
		}
		return valuesDictionary;
	}

	/// <summary>Creates a player entity from a private copy of its template.</summary>
	public static Entity CreatePlayerEntity(Project project, PlayerData playerData, Vector3 position, Quaternion rotation)
	{
		ValuesDictionary valuesDictionary = CloneValuesDictionary(DatabaseManager.FindEntityValuesDictionary(playerData.GetEntityTemplateName(), throwIfNotFound: true));
		valuesDictionary.GetValue<ValuesDictionary>("Player").SetValue("PlayerIndex", playerData.PlayerIndex);
		ValuesDictionary valuesDictionary2 = valuesDictionary.GetValue<ValuesDictionary>("Intro", null);
		if (valuesDictionary2 != null)
		{
			valuesDictionary2.SetValue("PlayIntro", false);
		}
		Entity entity = project.CreateEntity(valuesDictionary);
		ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
		if (componentBody != null)
		{
			componentBody.Position = position;
			componentBody.Rotation = rotation;
		}
		return entity;
	}

	/// <summary>
	/// Serializes a single entity (including its <c>MultiplayerNetId</c>) so it can
	/// be recreated on a client.
	/// </summary>
	public static XElement SaveEntityXml(Project project, Entity entity)
	{
		EntityDataList entityDataList = project.SaveEntities(new Entity[1] { entity });
		if (entityDataList.EntitiesData.Count != 1)
		{
			throw new InvalidOperationException("Cannot serialize entity \"" + entity.ValuesDictionary.DatabaseObject.Name + "\" because it owns child entities.");
		}
		XElement xElement = new XElement("Entity");
		entityDataList.EntitiesData[0].Save(xElement);
		return xElement;
	}

	/// <summary>Recreates an entity previously serialized with <see cref="SaveEntityXml"/>.</summary>
	public static Entity LoadEntityXml(Project project, XElement node)
	{
		EntityData entityData = new EntityData(project.GameDatabase, node);
		Entity entity = project.CreateEntity(entityData.ValuesDictionary);
		project.AddEntity(entity);
		return entity;
	}

	public static Vector3 GetPosition(Entity entity)
	{
		ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
		return componentBody?.Position ?? Vector3.Zero;
	}

	public static float GetYaw(Quaternion rotation)
	{
		return MathUtils.Atan2(2f * rotation.Y * rotation.W - 2f * rotation.X * rotation.Z, 1f - 2f * rotation.Y * rotation.Y - 2f * rotation.Z * rotation.Z);
	}

	public static Quaternion RotationFromYaw(float yaw)
	{
		return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
	}
}
