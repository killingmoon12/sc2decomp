using System;
using System.Collections.Generic;

namespace GameEntitySystem
{
	/// <summary>
	/// Stable per-entity identifiers used by the multiplayer layer.
	///
	/// Ids are written into saved entity data (and therefore into world
	/// snapshots) so a client can match the entities it loaded against the
	/// transform updates the host keeps sending after the snapshot.
	///
	/// The registry lives in the entity-system assembly so that
	/// <see cref="Entity"/> and <see cref="Project"/> can use it without
	/// depending on the game assembly.
	/// </summary>
	public static class EntityNetRegistry
	{
		private static readonly Dictionary<Entity, int> m_entityToId = new Dictionary<Entity, int>();
		private static readonly Dictionary<int, Entity> m_idToEntity = new Dictionary<int, Entity>();
		private static int m_nextId = 1;

		/// <summary>
		/// When true, saving an entity writes its network id into the saved
		/// values dictionary. Only enabled while hosting, so ordinary
		/// single-player worlds are not polluted with multiplayer data.
		/// </summary>
		public static bool Enabled { get; set; }

		/// <summary>
		/// Optional predicate used by Project.SaveEntities to omit entities
		/// from a snapshot (remote player avatars must not be baked into the
		/// host save the client downloads).
		/// </summary>
		public static Func<Entity, bool> SaveFilter { get; set; }

		public static int Count => m_idToEntity.Count;

		public static int GetId(Entity entity)
		{
			if (entity != null && m_entityToId.TryGetValue(entity, out int id))
			{
				return id;
			}
			return 0;
		}

		public static int GetOrCreateId(Entity entity)
		{
			if (entity == null)
			{
				return 0;
			}
			if (m_entityToId.TryGetValue(entity, out int existing))
			{
				return existing;
			}
			int id = m_nextId++;
			m_entityToId[entity] = id;
			m_idToEntity[id] = entity;
			return id;
		}

		public static Entity Find(int id)
		{
			if (id != 0 && m_idToEntity.TryGetValue(id, out Entity entity))
			{
				return entity;
			}
			return null;
		}

		public static void Register(Entity entity, int id)
		{
			if (entity == null || id == 0)
			{
				return;
			}
			if (m_entityToId.TryGetValue(entity, out int oldId) && oldId != id)
			{
				m_idToEntity.Remove(oldId);
			}
			if (m_idToEntity.TryGetValue(id, out Entity oldEntity) && oldEntity != entity)
			{
				m_entityToId.Remove(oldEntity);
			}
			m_entityToId[entity] = id;
			m_idToEntity[id] = entity;
			if (id >= m_nextId)
			{
				m_nextId = id + 1;
			}
		}

		public static void Unregister(Entity entity)
		{
			if (entity != null && m_entityToId.TryGetValue(entity, out int id))
			{
				m_entityToId.Remove(entity);
				m_idToEntity.Remove(id);
			}
		}

		public static void Reset()
		{
			m_entityToId.Clear();
			m_idToEntity.Clear();
			m_nextId = 1;
			Enabled = false;
			SaveFilter = null;
		}
	}
}
