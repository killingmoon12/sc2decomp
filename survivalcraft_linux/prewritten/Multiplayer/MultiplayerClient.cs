using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Xml.Linq;
using Engine;
using Engine.Serialization;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Client side of a multiplayer session. The host owns the world; this class
/// only applies the state it receives and forwards the local player input.
/// </summary>
public sealed class MultiplayerClient
{
	private struct CellChange
	{
		public int X;

		public int Y;

		public int Z;

		public int Value;
	}

	private sealed class EntityState
	{
		public Vector3 Position;

		public Quaternion Rotation;

		public Vector3 Velocity;

		public float CrouchFactor;

		public float Health;

		public float Air;

		public Vector2 LookAngles;

		public bool HasBody;

		public bool HasHealth;

		public bool HasLook;

		public bool HasReceivedState;
	}

	private readonly Project m_project;

	private readonly MultiplayerJoinResult m_joinResult;

	private readonly NetChannel m_channel;

	private readonly string m_hostDescription;

	private readonly ConcurrentQueue<NetChannel.Frame> m_inbox = new ConcurrentQueue<NetChannel.Frame>();

	private readonly Dictionary<int, EntityState> m_states = new Dictionary<int, EntityState>();

	private readonly List<CellChange> m_pendingCellChanges = new List<CellChange>();

	private readonly SubsystemPlayers m_subsystemPlayers;

	private readonly SubsystemTerrain m_subsystemTerrain;

	private readonly SubsystemGameInfo m_subsystemGameInfo;

	private readonly SubsystemTimeOfDay m_subsystemTimeOfDay;

	private readonly SubsystemWeather m_subsystemWeather;

	private readonly SubsystemPickables m_subsystemPickables;

	private readonly SubsystemParticles m_subsystemParticles;

	private PlayerInput m_lastInput;

	private bool m_hasInput;

	private double m_lastInputSendTime;

	private double m_lastInventorySyncTime;

	private readonly List<BlockInventoryRecord> m_inventoryRecords = new List<BlockInventoryRecord>();

	private int[] m_lastLocalInventoryValues;

	private int[] m_lastLocalInventoryCounts;

	private int m_lastLocalInventoryActive;

	private double m_lastPlayerInventorySendTime;

	private int[] m_pendingLocalInventoryValues;

	private int[] m_pendingLocalInventoryCounts;

	private int m_pendingLocalInventoryActive;

	private double m_pendingLocalInventoryTime;

	private bool m_hasPendingLocalInventory;

	private bool m_disconnectHandled;

	private bool m_ready;

	public int PlayerCount { get; private set; }

	public string HostDescription => m_hostDescription;

	public MultiplayerClient(Project project, MultiplayerJoinResult joinResult, string playerName)
	{
		m_project = project;
		m_joinResult = joinResult;
		m_channel = joinResult.Channel;
		m_hostDescription = joinResult.HostDescription;
		m_subsystemPlayers = project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
		m_subsystemTerrain = project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
		m_subsystemGameInfo = project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
		m_subsystemTimeOfDay = project.FindSubsystem<SubsystemTimeOfDay>(throwOnError: true);
		m_subsystemWeather = project.FindSubsystem<SubsystemWeather>(throwOnError: true);
		m_subsystemPickables = project.FindSubsystem<SubsystemPickables>(throwOnError: true);
		m_subsystemParticles = project.FindSubsystem<SubsystemParticles>(throwOnError: true);
		PlayerCount = m_subsystemPlayers.PlayersData.Count;
	}

	public void Start()
	{
		m_channel.MessageReceived += delegate(NetChannel.Frame frame)
		{
			m_inbox.Enqueue(frame);
		};
		m_channel.Start();
		m_channel.Send(MultiplayerMessage.Ready, null);
		m_ready = true;
		ShowMessage("Connected to " + m_hostDescription);
	}

	public void Stop(string reason)
	{
		m_channel?.Close(reason);
	}

	public void SubmitInput(PlayerInput input)
	{
		// Input is only sent a few dozen times per second while look is a
		// per-frame rate, so samples are folded together: continuous values
		// come from the newest sample, one-shot actions are never lost and
		// look deltas are summed.
		if (!m_hasInput)
		{
			m_lastInput = input;
			m_hasInput = true;
			return;
		}
		Vector2 look = m_lastInput.Look + input.Look;
		m_lastInput = MultiplayerSerialization.MergePlayerInput(m_lastInput, input);
		m_lastInput.Look = look;
		// Digging and aiming are held states, so the newest sample wins
		// (merging would keep a released button held forever).
		m_lastInput.Dig = input.Dig;
		m_lastInput.Aim = input.Aim;
	}

	public void OnLocalCellChanged(int x, int y, int z, int value)
	{
		CellChange item = default(CellChange);
		item.X = x;
		item.Y = y;
		item.Z = z;
		item.Value = value;
		m_pendingCellChanges.Add(item);
	}

	public void Update()
	{
		if (!m_ready)
		{
			return;
		}
		ProcessInbox();
		InterpolateEntities();
		SendInput();
		SendLocalCellChanges();
		SendLocalBlockInventories();
		SyncLocalPlayerInventory();
		if (m_channel.IsClosed && !m_disconnectHandled)
		{
			m_disconnectHandled = true;
			MultiplayerManager.ShowDisconnectedMessage(string.IsNullOrEmpty(m_channel.CloseReason) ? "The connection to the host was lost." : m_channel.CloseReason);
		}
	}

	private void ProcessInbox()
	{
		NetChannel.Frame result;
		while (m_inbox.TryDequeue(out result))
		{
			try
			{
				HandleMessage(result);
			}
			catch (Exception ex)
			{
				Log.Error(string.Concat("[Multiplayer] Error applying message ", result.Type, ": ", ex));
			}
		}
	}

	private void HandleMessage(NetChannel.Frame frame)
	{
		using MemoryStream memoryStream = new MemoryStream(frame.Body, writable: false);
		using EngineBinaryReader engineBinaryReader = new EngineBinaryReader(memoryStream);
		switch (frame.Type)
		{
		case MultiplayerMessage.EntityUpdate:
		{
			int num = engineBinaryReader.ReadInt32();
			for (int i = 0; i < num; i++)
			{
				ReadEntityUpdate(engineBinaryReader);
			}
			break;
		}
		case MultiplayerMessage.EntityAdd:
		{
			string text = engineBinaryReader.ReadString();
			AddEntity(XElement.Parse(text));
			break;
		}
		case MultiplayerMessage.EntityRemove:
		{
			int id = engineBinaryReader.ReadInt32();
			RemoveEntity(id);
			break;
		}
		case MultiplayerMessage.CellChange:
		{
			int num2 = engineBinaryReader.ReadInt32();
			for (int j = 0; j < num2; j++)
			{
				int x = engineBinaryReader.ReadInt32();
				int y = engineBinaryReader.ReadInt32();
				int z = engineBinaryReader.ReadInt32();
				int value = engineBinaryReader.ReadInt32();
				MultiplayerManager.ApplyRemoteCell(m_subsystemTerrain, x, y, z, value);
			}
			break;
		}
		case MultiplayerMessage.PlayerState:
			ReadPlayerStates(engineBinaryReader);
			break;
		case MultiplayerMessage.TimeSync:
		{
			double totalElapsedGameTime = engineBinaryReader.ReadDouble();
			double timeOfDayOffset = engineBinaryReader.ReadDouble();
			float timeOfYear = engineBinaryReader.ReadSingle();
			bool flag = engineBinaryReader.ReadBoolean();
			bool flag2 = engineBinaryReader.ReadBoolean();
			m_subsystemGameInfo.SetNetTotalElapsedGameTime(totalElapsedGameTime);
			m_subsystemTimeOfDay.TimeOfDayOffset = timeOfDayOffset;
			m_subsystemGameInfo.WorldSettings.TimeOfYear = timeOfYear;
			if (m_subsystemWeather.IsPrecipitationStarted != flag)
			{
				if (flag)
				{
					m_subsystemWeather.ManualPrecipitationStart();
				}
				else
				{
					m_subsystemWeather.ManualPrecipitationEnd();
				}
			}
			if (m_subsystemWeather.IsFogStarted != flag2)
			{
				if (flag2)
				{
					m_subsystemWeather.ManualFogStart();
				}
				else
				{
					m_subsystemWeather.ManualFogEnd();
				}
			}
			break;
		}
		case MultiplayerMessage.ChatMessage:
		{
			string text2 = engineBinaryReader.ReadString();
			string text3 = engineBinaryReader.ReadString();
			ShowMessage(string.IsNullOrEmpty(text2) ? text3 : (text2 + ": " + text3));
			break;
		}
		case MultiplayerMessage.PlayerJoined:
		{
			// The host sends the complete player list whenever it changes, so
			// this doubles as the reconciliation point for players that joined
			// or left after our snapshot was taken.
			int num3 = engineBinaryReader.ReadInt32();
			HashSet<int> hashSet = new HashSet<int>();
			for (int k = 0; k < num3; k++)
			{
				int playerIndex = engineBinaryReader.ReadInt32();
				string name = engineBinaryReader.ReadString();
				engineBinaryReader.ReadBoolean();
				hashSet.Add(playerIndex);
				EnsurePlayerData(playerIndex, name, -1f);
			}
			PlayerCount = num3;
			RemoveStalePlayers(hashSet);
			break;
		}
		case MultiplayerMessage.Disconnect:
		{
			string text4 = engineBinaryReader.ReadString();
			if (!m_disconnectHandled)
			{
				m_disconnectHandled = true;
				MultiplayerManager.ShowDisconnectedMessage(text4);
			}
			break;
		}
		case MultiplayerMessage.BlockInventory:
			MultiplayerInventorySync.ReadAndApply(m_project, engineBinaryReader);
			break;
		case MultiplayerMessage.MiningState:
			ReadMiningStates(engineBinaryReader);
			break;
		case MultiplayerMessage.Pickables:
			MultiplayerPickables.ReadAndApply(engineBinaryReader, m_subsystemPickables);
			break;
		case MultiplayerMessage.Ping:
			m_channel.Send(MultiplayerMessage.Pong, null);
			break;
		}
	}

	private void ReadEntityUpdate(EngineBinaryReader reader)
	{
		int key = reader.ReadInt32();
		byte b = reader.ReadByte();
		EntityState value;
		if (!m_states.TryGetValue(key, out value))
		{
			value = new EntityState();
			m_states[key] = value;
		}
		value.HasBody = (b & 1) != 0;
		value.HasHealth = (b & 2) != 0;
		value.HasLook = (b & 4) != 0;
		if (value.HasBody)
		{
			value.Position = reader.ReadVector3();
			value.Rotation = reader.ReadQuaternion();
			value.Velocity = reader.ReadVector3();
			value.CrouchFactor = reader.ReadSingle();
		}
		if (value.HasHealth)
		{
			value.Health = reader.ReadSingle();
			value.Air = reader.ReadSingle();
		}
		if (value.HasLook)
		{
			value.LookAngles = reader.ReadVector2();
		}
		Entity entity = EntityNetRegistry.Find(key);
		if (entity == null)
		{
			return;
		}
		if (!value.HasReceivedState)
		{
			value.HasReceivedState = true;
			ApplyEntityStateImmediate(entity, value, snap: true);
			return;
		}
		if (entity.FindComponent<ComponentPlayer>(throwOnError: false) != null)
		{
			// Player bodies are moved by InterpolateEntities (the local one is
			// also predicted locally), but health and the head look must still
			// track the host every update.
			ApplyPlayerState(entity, value);
			return;
		}
		ApplyEntityStateImmediate(entity, value, snap: false);
	}

	/// <summary>Applies the non-transform parts of a player state on a client.</summary>
	private void ApplyPlayerState(Entity entity, EntityState state)
	{
		ComponentHealth componentHealth = entity.FindComponent<ComponentHealth>(throwOnError: false);
		if (componentHealth != null && state.HasHealth)
		{
			componentHealth.SetNetHealth(state.Health, state.Air);
		}
		// The local player's look is predicted from the local mouse, so the
		// host's (slightly older) angles must not be applied on top of it.
		if (state.HasLook && !IsLocalPlayerEntity(entity))
		{
			ComponentCreature componentCreature = entity.FindComponent<ComponentCreature>(throwOnError: false);
			componentCreature?.ComponentLocomotion?.SetLookAngles(state.LookAngles);
		}
	}

	private void ApplyEntityStateImmediate(Entity entity, EntityState state, bool snap)
	{
		ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
		if (componentBody != null && state.HasBody)
		{
			bool flag = IsLocalPlayerEntity(entity);
			if (snap || Vector3.DistanceSquared(componentBody.Position, state.Position) > 64f)
			{
				componentBody.Position = state.Position;
			}
			if (!flag)
			{
				componentBody.Rotation = state.Rotation;
			}
			componentBody.Velocity = state.Velocity;
			componentBody.TargetCrouchFactor = state.CrouchFactor;
		}
		ComponentHealth componentHealth = entity.FindComponent<ComponentHealth>(throwOnError: false);
		if (componentHealth != null && state.HasHealth)
		{
			componentHealth.SetNetHealth(state.Health, state.Air);
		}
		ComponentCreature componentCreature = entity.FindComponent<ComponentCreature>(throwOnError: false);
		if (componentCreature != null && componentCreature.ComponentLocomotion != null && state.HasLook && !IsLocalPlayerEntity(entity))
		{
			componentCreature.ComponentLocomotion.SetLookAngles(state.LookAngles);
		}
	}

	private bool IsLocalPlayerEntity(Entity entity)
	{
		ComponentPlayer componentPlayer = entity.FindComponent<ComponentPlayer>(throwOnError: false);
		return componentPlayer != null && componentPlayer.PlayerData != null && componentPlayer.PlayerData.PlayerIndex == MultiplayerManager.LocalPlayerIndex;
	}

	/// <summary>Smoothly moves replicated bodies towards the newest host transform.</summary>
	private void InterpolateEntities()
	{
		float num = MathUtils.Saturate(Time.FrameDuration * 16f);
		foreach (KeyValuePair<int, EntityState> state in m_states)
		{
			EntityState value = state.Value;
			if (!value.HasReceivedState || !value.HasBody)
			{
				continue;
			}
			Entity entity = EntityNetRegistry.Find(state.Key);
			if (entity == null)
			{
				continue;
			}
			ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
			if (componentBody == null)
			{
				continue;
			}
			if (IsLocalPlayerEntity(entity))
			{
				Vector3 position = componentBody.Position;
				if (Vector3.DistanceSquared(position, value.Position) < 256f)
				{
					componentBody.Position = Vector3.Lerp(position, value.Position, num);
				}
				else
				{
					componentBody.Position = value.Position;
				}
			}
			else
			{
				componentBody.Position = Vector3.Lerp(componentBody.Position, value.Position, num);
				componentBody.Rotation = Quaternion.Slerp(componentBody.Rotation, value.Rotation, num);
			}
		}
	}

	private void AddEntity(XElement node)
	{
		EntityData entityData = new EntityData(m_project.GameDatabase, node);
		int num = entityData.ValuesDictionary.GetValue("MultiplayerNetId", 0);
		if (num != 0 && EntityNetRegistry.Find(num) != null)
		{
			return;
		}
		// A player entity can arrive before the host's player list update.
		// ComponentPlayer.Load looks its PlayerData up by index, so it has to
		// exist before the entity is created.
		ValuesDictionary playerValues = entityData.ValuesDictionary.GetValue<ValuesDictionary>("Player", null);
		if (playerValues != null)
		{
			EnsurePlayerData(playerValues.GetValue<int>("PlayerIndex"), null, -1f);
		}
		Entity entity = m_project.CreateEntity(entityData.ValuesDictionary);
		m_project.AddEntity(entity);
		if (num != 0)
		{
			EntityNetRegistry.Register(entity, num);
		}
	}

	private void RemoveEntity(int netId)
	{
		Entity entity = EntityNetRegistry.Find(netId);
		if (entity != null)
		{
			m_states.Remove(netId);
			m_project.RemoveEntity(entity, disposeEntity: true);
		}
	}

	/// <summary>
	/// Applies the host's digging/animation state. The client never runs
	/// <see cref="ComponentMiner.Update"/>, so this is what makes the crack
	/// overlay and the hand swing visible.
	/// </summary>
	private void ReadMiningStates(EngineBinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 0 || num > MultiplayerManager.MaxPlayers * 8)
		{
			throw new InvalidOperationException("Invalid mining state count " + num + ".");
		}
		for (int i = 0; i < num; i++)
		{
			int num2 = reader.ReadInt32();
			if (!reader.ReadBoolean())
			{
				continue;
			}
			CellFace? cellFace = null;
			if (reader.ReadBoolean())
			{
				cellFace = new CellFace(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
			}
			float digProgress = reader.ReadSingle();
			float digTime = reader.ReadSingle();
			float pokingPhase = reader.ReadSingle();
			PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => p.PlayerIndex == num2);
			ComponentPlayer componentPlayer = playerData?.ComponentPlayer;
			ComponentMiner componentMiner = componentPlayer?.ComponentMiner;
			if (componentMiner == null)
			{
				continue;
			}
			// ComponentMiner.Dig spawns a debris burst every time the hand
			// passes the middle of its swing. MiningState carries the same
			// phase, so the client can mirror those chips without extra
			// messages (it never runs the miner itself).
			float pokingPhase2 = componentMiner.PokingPhase;
			componentMiner.SetNetDigState(cellFace, digProgress, digTime, pokingPhase);
			if (cellFace.HasValue && pokingPhase2 <= 0.5f && pokingPhase > 0.5f)
			{
				SpawnDigDebrisParticles(cellFace.Value);
			}
		}
	}

	/// <summary>
	/// Mirrors the block chips the host's miner creates when the hand hits the
	/// block. The client has the same terrain, so it can build the particle
	/// system from the replicated cell face.
	/// </summary>
	private void SpawnDigDebrisParticles(CellFace cellFace)
	{
		try
		{
			int cellValue = m_subsystemTerrain.Terrain.GetCellValue(cellFace.X, cellFace.Y, cellFace.Z);
			if (cellValue == 0)
			{
				return;
			}
			Block block = BlocksManager.Blocks[Terrain.ExtractContents(cellValue)];
			Vector3 position = new Vector3(cellFace.X + 0.5f, cellFace.Y + 0.5f, cellFace.Z + 0.5f) + 0.5f * CellFace.FaceToVector3(cellFace.Face);
			m_subsystemParticles.AddParticleSystem(block.CreateDebrisParticleSystem(m_subsystemTerrain, position, cellValue, 0.35f));
		}
		catch (Exception ex)
		{
			Log.Warning("[Multiplayer] Could not create dig particles: " + ex.Message);
		}
	}

	private void ReadPlayerStates(EngineBinaryReader reader)
	{
		int num = reader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			int num2 = reader.ReadInt32();
			string name = reader.ReadString();
			float level = reader.ReadSingle();
			// Host-side remote flag; unused on a client, but part of the frame.
			// The host writes it as an int, so it must be consumed as one, or
			// every following field is read three bytes early.
			_ = reader.ReadInt32();
			bool hasPlayer = reader.ReadBoolean();
			PlayerData playerData = EnsurePlayerData(num2, name, level);
			if (!hasPlayer)
			{
				continue;
			}
			float health = reader.ReadSingle();
			float air = reader.ReadSingle();
			float food = reader.ReadSingle();
			float temperature = reader.ReadSingle();
			float wetness = reader.ReadSingle();
			int slotCount = reader.ReadInt32();
			int activeSlot = -1;
			if (slotCount > 0)
			{
				activeSlot = reader.ReadInt32();
			}
			int[] values = new int[slotCount];
			int[] counts = new int[slotCount];
			// The payload is always consumed in full, even when this player or
			// its entity is not present locally yet, otherwise every following
			// player entry is read at the wrong offset.
			for (int j = 0; j < slotCount; j++)
			{
				values[j] = reader.ReadInt32();
				counts[j] = reader.ReadInt32();
			}
			ComponentPlayer componentPlayer = playerData.ComponentPlayer;
			ComponentInventoryBase inventory = componentPlayer?.ComponentMiner.Inventory as ComponentInventoryBase;
			if (componentPlayer == null)
			{
				continue;
			}
			if (inventory != null && slotCount > 0 && ShouldApplyLocalInventory(num2, values, counts, activeSlot))
			{
				int num3 = MathUtils.Min(slotCount, inventory.SlotsCount);
				for (int k = 0; k < num3; k++)
				{
					inventory.SetSlotNet(k, values[k], counts[k]);
				}
				if (activeSlot >= -1 && activeSlot < inventory.SlotsCount)
				{
					inventory.ActiveSlotIndex = activeSlot;
				}
				if (num2 == MultiplayerManager.LocalPlayerIndex)
				{
					RememberLocalPlayerInventory(inventory);
				}
			}
			componentPlayer.ComponentHealth.SetNetHealth(health, air);
			componentPlayer.ComponentVitalStats.SetNetVitals(food, temperature, wetness);
		}
	}

	/// <summary>
	/// A local inventory edit is forwarded to the host, but the host keeps
	/// sending its own (slightly older) view at 5 Hz. Applying that view
	/// immediately would undo the edit before the host has seen it, so local
	/// state wins until the host echoes it back (or a short grace period ends).
	/// </summary>
	private bool ShouldApplyLocalInventory(int playerIndex, int[] values, int[] counts, int activeSlot)
	{
		if (playerIndex != MultiplayerManager.LocalPlayerIndex || !m_hasPendingLocalInventory)
		{
			return true;
		}
		if (activeSlot == m_pendingLocalInventoryActive
			&& EqualSlots(values, counts, m_pendingLocalInventoryValues, m_pendingLocalInventoryCounts))
		{
			m_hasPendingLocalInventory = false;
			return true;
		}
		if (Time.RealTime - m_pendingLocalInventoryTime < 1.0)
		{
			return false;
		}
		m_hasPendingLocalInventory = false;
		return true;
	}

	private static bool EqualSlots(int[] values, int[] counts, int[] otherValues, int[] otherCounts)
	{
		if (values.Length != otherValues.Length)
		{
			return false;
		}
		for (int i = 0; i < values.Length; i++)
		{
			if (values[i] != otherValues[i] || counts[i] != otherCounts[i])
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Returns the local mirror of a host player, creating it when the player
	/// joined after our snapshot was downloaded.
	/// </summary>
	private PlayerData EnsurePlayerData(int playerIndex, string name, float level)
	{
		PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => p.PlayerIndex == playerIndex);
		if (playerData == null)
		{
			playerData = m_subsystemPlayers.AddPlayerDataAt(playerIndex, playerIndex != MultiplayerManager.LocalPlayerIndex);
			Log.Information("[Multiplayer] Replicating player {0}", playerIndex);
		}
		if (!string.IsNullOrEmpty(name))
		{
			playerData.Name = name;
		}
		if (level > 0f)
		{
			playerData.Level = level;
		}
		return playerData;
	}

	/// <summary>Drops mirrors of players the host no longer reports.</summary>
	private void RemoveStalePlayers(HashSet<int> present)
	{
		List<PlayerData> list = new List<PlayerData>();
		foreach (PlayerData playerData in m_subsystemPlayers.PlayersData)
		{
			if (playerData.PlayerIndex != MultiplayerManager.LocalPlayerIndex && !present.Contains(playerData.PlayerIndex))
			{
				list.Add(playerData);
			}
		}
		foreach (PlayerData playerData in list)
		{
			Log.Information("[Multiplayer] Player {0} left", playerData.PlayerIndex);
			try
			{
				m_subsystemPlayers.RemovePlayerData(playerData);
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Could not remove player: " + ex.Message);
			}
		}
	}

	private void SendInput()
	{
		double realTime = Time.RealTime;
		if (!m_hasInput || realTime - m_lastInputSendTime < MultiplayerManager.InputSendInterval)
		{
			return;
		}
		m_lastInputSendTime = realTime;
		m_hasInput = false;
		PlayerInput input = m_lastInput;
		m_lastInput = default(PlayerInput);
		m_channel.Send(MultiplayerMessage.Input, delegate(EngineBinaryWriter writer)
		{
			writer.WritePlayerInput(input);
		});
	}

	private void SendLocalCellChanges()
	{
		if (m_pendingCellChanges.Count == 0)
		{
			return;
		}
		List<CellChange> list = new List<CellChange>(m_pendingCellChanges);
		m_pendingCellChanges.Clear();
		m_channel.Send(MultiplayerMessage.CellChange, delegate(EngineBinaryWriter writer)
		{
			writer.Write(list.Count);
			foreach (CellChange item in list)
			{
				writer.Write(item.X);
				writer.Write(item.Y);
				writer.Write(item.Z);
				writer.Write(item.Value);
			}
		});
	}

	private ComponentInventoryBase GetLocalPlayerInventory()
	{
		PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => p.PlayerIndex == MultiplayerManager.LocalPlayerIndex);
		return playerData?.ComponentPlayer?.ComponentMiner.Inventory as ComponentInventoryBase;
	}

	/// <summary>
	/// The inventory screen is rendered locally, so item moves made there only
	/// exist on this machine until they are forwarded to the host.
	/// </summary>
	private void SyncLocalPlayerInventory()
	{
		ComponentInventoryBase inventory = GetLocalPlayerInventory();
		if (inventory == null)
		{
			return;
		}
		int count = inventory.SlotsCount;
		bool flag = m_lastLocalInventoryValues == null || m_lastLocalInventoryValues.Length != count;
		if (!flag)
		{
			flag = inventory.ActiveSlotIndex != m_lastLocalInventoryActive;
			for (int i = 0; i < count && !flag; i++)
			{
				flag = inventory.GetSlotValue(i) != m_lastLocalInventoryValues[i] || inventory.GetSlotCount(i) != m_lastLocalInventoryCounts[i];
			}
		}
		if (!flag)
		{
			return;
		}
		double realTime = Time.RealTime;
		if (realTime - m_lastPlayerInventorySendTime < 0.05)
		{
			return;
		}
		m_lastPlayerInventorySendTime = realTime;
		RememberLocalPlayerInventory(inventory);
		int[] values = new int[count];
		int[] counts = new int[count];
		for (int j = 0; j < count; j++)
		{
			values[j] = inventory.GetSlotValue(j);
			counts[j] = inventory.GetSlotCount(j);
		}
		int activeSlot = inventory.ActiveSlotIndex;
		m_pendingLocalInventoryValues = values;
		m_pendingLocalInventoryCounts = counts;
		m_pendingLocalInventoryActive = activeSlot;
		m_pendingLocalInventoryTime = realTime;
		m_hasPendingLocalInventory = true;
		m_channel.Send(MultiplayerMessage.PlayerInventory, delegate(EngineBinaryWriter writer)
		{
			writer.Write(MultiplayerManager.LocalPlayerIndex);
			writer.Write(activeSlot);
			writer.Write(count);
			for (int k = 0; k < count; k++)
			{
				writer.Write(values[k]);
				writer.Write(counts[k]);
			}
		});
	}

	private void RememberLocalPlayerInventory(ComponentInventoryBase inventory)
	{
		int count = inventory.SlotsCount;
		m_lastLocalInventoryValues = new int[count];
		m_lastLocalInventoryCounts = new int[count];
		for (int i = 0; i < count; i++)
		{
			m_lastLocalInventoryValues[i] = inventory.GetSlotValue(i);
			m_lastLocalInventoryCounts[i] = inventory.GetSlotCount(i);
		}
		m_lastLocalInventoryActive = inventory.ActiveSlotIndex;
	}

	private void SendLocalBlockInventories()
	{
		double realTime = Time.RealTime;
		if (realTime - m_lastInventorySyncTime < MultiplayerManager.InventorySyncInterval)
		{
			return;
		}
		m_lastInventorySyncTime = realTime;
		MultiplayerInventorySync.Collect(m_project, m_inventoryRecords);
		if (m_inventoryRecords.Count == 0)
		{
			return;
		}
		List<BlockInventoryRecord> records = new List<BlockInventoryRecord>(m_inventoryRecords);
		m_channel.Send(MultiplayerMessage.BlockInventory, delegate(EngineBinaryWriter writer)
		{
			MultiplayerInventorySync.Write(writer, records);
		});
	}

	private void ShowMessage(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => p.PlayerIndex == MultiplayerManager.LocalPlayerIndex);
		ComponentPlayer componentPlayer = playerData?.ComponentPlayer;
		if (componentPlayer != null && componentPlayer.ComponentGui != null)
		{
			componentPlayer.ComponentGui.DisplaySmallMessage(text, Color.White, blinking: false, playNotificationSound: false);
		}
	}

	/// <summary>
	/// Performs the blocking join handshake: connect, download the world snapshot
	/// and import it as a normal world. Called from a background task.
	/// </summary>
	public static MultiplayerJoinResult ConnectAndDownload(string address, int port, string playerName)
	{
		TcpClient tcpClient = new TcpClient();
		try
		{
			if (!tcpClient.ConnectAsync(address, port).Wait(5000))
			{
				return MultiplayerJoinResult.Failed("Could not reach " + address + ":" + port + " (timeout).");
			}
		}
		catch (Exception ex)
		{
			return MultiplayerJoinResult.Failed("Could not connect to " + address + ":" + port + ": " + ex.Message);
		}
		MemoryStream snapshot = null;
		int num = -1;
		string text = string.Empty;
		string text2 = string.Empty;
		try
		{
			NetworkStream stream = tcpClient.GetStream();
			stream.ReadTimeout = 120000;
			NetChannel.WriteFrame(stream, MultiplayerMessage.Hello, delegate(EngineBinaryWriter writer)
			{
				writer.Write(MultiplayerProtocol.Version);
				writer.Write(playerName ?? "Player");
			});
			while (true)
			{
				NetChannel.Frame frame = NetChannel.ReadFrame(stream);
				using MemoryStream memoryStream = new MemoryStream(frame.Body, writable: false);
				using EngineBinaryReader engineBinaryReader = new EngineBinaryReader(memoryStream);
				if (frame.Type == MultiplayerMessage.Welcome)
				{
					engineBinaryReader.ReadInt32();
					num = engineBinaryReader.ReadInt32();
					text2 = engineBinaryReader.ReadString();
					text = engineBinaryReader.ReadString();
					continue;
				}
				if (frame.Type == MultiplayerMessage.Snapshot)
				{
					int num2 = engineBinaryReader.ReadInt32();
					byte[] array = engineBinaryReader.ReadBytes(num2);
					snapshot = new MemoryStream(array, writable: false);
					break;
				}
				if (frame.Type == MultiplayerMessage.Disconnect)
				{
					string error = engineBinaryReader.ReadString();
					return MultiplayerJoinResult.Failed(error);
				}
			}
			Log.Information("[Multiplayer] Downloaded world snapshot from {0} ({1} KiB)", address, snapshot.Length / 1024);
			string text3 = WorldsManager.ImportWorld(snapshot);
			// Remember that this world is only a cache: if the game crashes
			// while playing, the next world-list refresh deletes it.
			MultiplayerManager.MarkClientWorldCache(text3);
			WorldsManager.UpdateWorldsList();
			WorldInfo worldInfo = WorldsManager.GetWorldInfo(text3);
			if (worldInfo == null)
			{
				try
				{
					WorldsManager.DeleteWorld(text3);
				}
				catch (Exception)
				{
				}
				return MultiplayerJoinResult.Failed("The downloaded world could not be read.");
			}
			NetChannel netChannel = new NetChannel(tcpClient.Client, "Multiplayer Host");
			return new MultiplayerJoinResult
			{
				Success = true,
				WorldDirectory = text3,
				WorldInfo = worldInfo,
				PlayerIndex = num,
				Channel = netChannel,
				HostDescription = address + ":" + port
			};
		}
		catch (Exception ex2)
		{
			return MultiplayerJoinResult.Failed("Join failed: " + ex2.Message);
		}
		finally
		{
			snapshot?.Dispose();
		}
	}
}
