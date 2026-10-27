using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Engine;
using Engine.Serialization;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Host side of a multiplayer session. Simulates nothing itself: it relies on
/// the normal single-player simulation of the host world and mirrors the
/// results to the connected clients.
/// </summary>
public sealed class MultiplayerServer : IDisposable
{
	private struct CellChange
	{
		public int X;

		public int Y;

		public int Z;

		public int Value;
	}

	private sealed class ClientSlot
	{
		public NetChannel Channel;

		public string Name = "Player";

		public int PlayerIndex = -1;

		public PlayerData PlayerData;

		public bool SnapshotSent;

		public bool Ready;

		public bool Disconnected;

		public double ConnectTime;

		public double LastInputTime;

		public PlayerInput PendingInput;

		public bool HasPendingInput;

		public List<CellChange> PendingCells = new List<CellChange>();

		/// <summary>Set when a still-loading client falls too far behind.</summary>
		public bool PendingCellsOverflow;

		public HashSet<int> KnownEntities = new HashSet<int>();

		/// <summary>
		/// Net ids of the entities contained in the snapshot this client
		/// downloaded. They must not be sent again as EntityAdd.
		/// </summary>
		public HashSet<int> SnapshotEntityIds;
	}

	private readonly Project m_project;

	private readonly SubsystemPlayers m_subsystemPlayers;

	private readonly SubsystemTerrain m_subsystemTerrain;

	private readonly SubsystemGameInfo m_subsystemGameInfo;

	private readonly SubsystemTimeOfDay m_subsystemTimeOfDay;

	private readonly SubsystemWeather m_subsystemWeather;

	private readonly SubsystemPickables m_subsystemPickables;

	private readonly string m_hostName;

	private readonly ConcurrentQueue<KeyValuePair<ClientSlot, NetChannel.Frame>> m_inbox = new ConcurrentQueue<KeyValuePair<ClientSlot, NetChannel.Frame>>();

	private readonly List<ClientSlot> m_clients = new List<ClientSlot>();

	private readonly List<CellChange> m_pendingCells = new List<CellChange>();

	private readonly List<BlockInventoryRecord> m_inventoryRecords = new List<BlockInventoryRecord>();

	/// <summary>
	/// Net ids of entities that could not be serialized. Stored as ids, not
	/// entity references, so a removed entity cannot keep its whole object
	/// graph alive just because it was once logged.
	/// </summary>
	private readonly HashSet<int> m_unserializableEntities = new HashSet<int>();

	/// <summary>Frames a client may queue before it is considered abusive.</summary>
	private const int MaxInboxFrames = 4096;

	/// <summary>Cell changes buffered for a client that is still loading.</summary>
	private const int MaxPendingCells = 32768;

	/// <summary>How long a connection may sit without starting the handshake.</summary>
	private const double HandshakeTimeout = 30.0;

	private TcpListener m_listener;

	private Thread m_acceptThread;

	private volatile bool m_running;

	private double m_lastEntitySyncTime;

	private double m_lastPlayerStateTime;

	private double m_lastTimeSyncTime;

	private double m_lastInventorySyncTime;

	private double m_lastMiningStateTime;

	private double m_lastPickableSyncTime;

	private int m_port;

	public int Port => m_port;

	public string HostName => m_hostName;

	public string WorldName => m_subsystemGameInfo?.WorldSettings?.Name ?? "World";

	public bool IsRunning => m_running;

	public int ConnectedPlayerCount
	{
		get
		{
			int num = 0;
			foreach (ClientSlot client in SnapshotClients())
			{
				if (client.Ready && !client.Disconnected)
				{
					num++;
				}
			}
			return num;
		}
	}

	public MultiplayerServer(Project project, int port, string hostName)
	{
		m_project = project;
		m_hostName = hostName;
		m_port = port;
		m_subsystemPlayers = project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
		m_subsystemTerrain = project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
		m_subsystemGameInfo = project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
		m_subsystemTimeOfDay = project.FindSubsystem<SubsystemTimeOfDay>(throwOnError: true);
		m_subsystemWeather = project.FindSubsystem<SubsystemWeather>(throwOnError: true);
		m_subsystemPickables = project.FindSubsystem<SubsystemPickables>(throwOnError: true);
	}

	public void Start()
	{
		Exception lastError = null;
		for (int i = 0; i < 10; i++)
		{
			try
			{
				int port = MultiplayerProtocol.DefaultPort + i;
				TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
				tcpListener.Start();
				m_listener = tcpListener;
				m_port = port;
				lastError = null;
				break;
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}
		if (m_listener == null)
		{
			throw new InvalidOperationException("Could not bind a TCP port for multiplayer: " + lastError?.Message);
		}
		m_running = true;
		m_acceptThread = new Thread(AcceptLoop)
		{
			IsBackground = true,
			Name = "Multiplayer Server Accept"
		};
		m_acceptThread.Start();
	}

	private void AcceptLoop()
	{
		while (m_running)
		{
			try
			{
				Socket socket = m_listener.AcceptSocket();
				ClientSlot clientSlot = new ClientSlot();
				clientSlot.ConnectTime = Time.RealTime;
				clientSlot.LastInputTime = Time.RealTime;
				clientSlot.Channel = new NetChannel(socket, "Multiplayer Client");
				clientSlot.Channel.MessageReceived += delegate(NetChannel.Frame frame)
				{
					// A client that floods the host must not grow this queue
					// without bound while the game thread is busy.
					if (m_inbox.Count >= MaxInboxFrames)
					{
						clientSlot.Channel.Close("Too many messages queued.");
						return;
					}
					m_inbox.Enqueue(new KeyValuePair<ClientSlot, NetChannel.Frame>(clientSlot, frame));
				};
				clientSlot.Channel.Start();
				lock (m_clients)
				{
					m_clients.Add(clientSlot);
				}
				Log.Information("[Multiplayer] Incoming connection from {0}", socket.RemoteEndPoint);
			}
			catch (SocketException)
			{
				if (!m_running)
				{
					break;
				}
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Accept error: " + ex.Message);
				Thread.Sleep(100);
			}
		}
	}

	private List<ClientSlot> SnapshotClients()
	{
		lock (m_clients)
		{
			return new List<ClientSlot>(m_clients);
		}
	}

	/// <summary>Called from the game thread before the world is updated.</summary>
	public void Update()
	{
		if (!m_running)
		{
			return;
		}
		ProcessInbox();
		RemoveLostClients();
		ApplyClientInputs();
		CheckTimeouts();
		UpdateRemotePlayerTerrain();
		double realTime = Time.RealTime;
		if (realTime - m_lastEntitySyncTime >= MultiplayerManager.EntitySyncInterval)
		{
			m_lastEntitySyncTime = realTime;
			FlushCellChanges();
			SyncEntities();
		}
		if (realTime - m_lastPlayerStateTime >= MultiplayerManager.PlayerStateInterval)
		{
			m_lastPlayerStateTime = realTime;
			SyncPlayerStates();
		}
		if (realTime - m_lastTimeSyncTime >= MultiplayerManager.TimeSyncInterval)
		{
			m_lastTimeSyncTime = realTime;
			SyncWorldState();
		}
		if (realTime - m_lastInventorySyncTime >= MultiplayerManager.InventorySyncInterval)
		{
			m_lastInventorySyncTime = realTime;
			SyncBlockInventories();
		}
		if (realTime - m_lastMiningStateTime >= MultiplayerManager.MiningStateInterval)
		{
			m_lastMiningStateTime = realTime;
			SyncMiningStates();
		}
		if (realTime - m_lastPickableSyncTime >= MultiplayerManager.PickableSyncInterval)
		{
			m_lastPickableSyncTime = realTime;
			SyncPickables();
		}
	}

	/// <summary>
	/// Terrain is only generated around cameras that are being drawn. Remote
	/// players have no drawn camera, so the host has to ask the terrain updater
	/// to keep their surroundings loaded explicitly.
	/// </summary>
	private void UpdateRemotePlayerTerrain()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || client.PlayerData == null)
			{
				continue;
			}
			ComponentPlayer componentPlayer = client.PlayerData.ComponentPlayer;
			if (componentPlayer == null)
			{
				continue;
			}
			m_subsystemTerrain.TerrainUpdater.SetUpdateLocation(client.PlayerData.PlayerIndex, componentPlayer.ComponentBody.Position.XZ, 64f, 64f);
		}
	}

	private void SyncBlockInventories()
	{
		MultiplayerInventorySync.Collect(m_project, m_inventoryRecords);
		if (m_inventoryRecords.Count == 0)
		{
			return;
		}
		BroadcastBlockInventories(m_inventoryRecords, null);
	}

	private void BroadcastBlockInventories(List<BlockInventoryRecord> records, ClientSlot except)
	{
		if (records.Count == 0)
		{
			return;
		}
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready || client == except)
			{
				continue;
			}
			client.Channel.Send(MultiplayerMessage.BlockInventory, delegate(EngineBinaryWriter writer)
			{
				MultiplayerInventorySync.Write(writer, records);
			});
		}
	}

	private void SyncMiningStates()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			client.Channel.Send(MultiplayerMessage.MiningState, delegate(EngineBinaryWriter writer)
			{
				writer.Write(m_subsystemPlayers.PlayersData.Count);
				foreach (PlayerData playersDatum in m_subsystemPlayers.PlayersData)
				{
					writer.Write(playersDatum.PlayerIndex);
					ComponentPlayer componentPlayer = playersDatum.ComponentPlayer;
					if (componentPlayer == null)
					{
						writer.Write(false);
						continue;
					}
					writer.Write(true);
					ComponentMiner componentMiner = componentPlayer.ComponentMiner;
					CellFace? digCellFace = componentMiner.DigCellFace;
					writer.Write(digCellFace.HasValue);
					if (digCellFace.HasValue)
					{
						writer.Write(digCellFace.Value.X);
						writer.Write(digCellFace.Value.Y);
						writer.Write(digCellFace.Value.Z);
						writer.Write(digCellFace.Value.Face);
					}
					writer.Write(componentMiner.DigProgress);
					writer.Write(componentMiner.DigTime);
					writer.Write(componentMiner.PokingPhase);
				}
			});
		}
	}

	/// <summary>
	/// Pickables are not entities, so they cannot ride on the entity sync. The
	/// host publishes the item stacks near each client and the client replaces
	/// its own list with it.
	/// </summary>
	private void SyncPickables()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			Vector3 clientCenter = GetClientCenter(client);
			client.Channel.Send(MultiplayerMessage.Pickables, delegate(EngineBinaryWriter writer)
			{
				MultiplayerPickables.Write(writer, m_subsystemPickables, clientCenter, MultiplayerManager.PickableSyncDistanceSquared);
			});
		}
	}

	private void ProcessInbox()
	{
		KeyValuePair<ClientSlot, NetChannel.Frame> result;
		while (m_inbox.TryDequeue(out result))
		{
			ClientSlot key = result.Key;
			NetChannel.Frame value = result.Value;
			if (key.Disconnected)
			{
				continue;
			}
			try
			{
				HandleMessage(key, value);
			}
			catch (Exception ex)
			{
				Log.Error(string.Concat("[Multiplayer] Error handling message ", value.Type, ": ", ex));
				DisconnectClient(key, "Protocol error.");
			}
		}
	}

	private void HandleMessage(ClientSlot slot, NetChannel.Frame frame)
	{
		using MemoryStream memoryStream = new MemoryStream(frame.Body, writable: false);
		using EngineBinaryReader engineBinaryReader = new EngineBinaryReader(memoryStream);
		switch (frame.Type)
		{
		case MultiplayerMessage.Hello:
		{
			int num = engineBinaryReader.ReadInt32();
			string text = engineBinaryReader.ReadString();
			if (num != MultiplayerProtocol.Version)
			{
				slot.Channel.Send(MultiplayerMessage.Disconnect, delegate(EngineBinaryWriter writer)
				{
					writer.Write("Protocol mismatch: host speaks version " + MultiplayerProtocol.Version + ".");
				});
				DisconnectClient(slot, "Protocol mismatch.");
				return;
			}
			slot.Name = SanitizeName(text);
			AcceptClient(slot);
			break;
		}
		case MultiplayerMessage.Input:
		{
			PlayerInput playerInput = engineBinaryReader.ReadPlayerInput();
			slot.PendingInput = slot.HasPendingInput ? MultiplayerSerialization.MergePlayerInput(slot.PendingInput, playerInput) : playerInput;
			slot.HasPendingInput = true;
			slot.LastInputTime = Time.RealTime;
			break;
		}
		case MultiplayerMessage.CellChange:
		{
			int x = engineBinaryReader.ReadInt32();
			int y = engineBinaryReader.ReadInt32();
			int z = engineBinaryReader.ReadInt32();
			int value = engineBinaryReader.ReadInt32();
			if (slot.Ready)
			{
				MultiplayerManager.ApplyRemoteCell(m_subsystemTerrain, x, y, z, value);
			}
			break;
		}
		case MultiplayerMessage.Ready:
			CompleteJoin(slot);
			break;
		case MultiplayerMessage.ChatMessage:
		{
			string text2 = engineBinaryReader.ReadString();
			BroadcastChat(slot.Name, text2);
			break;
		}
		case MultiplayerMessage.BlockInventory:
		{
			// Apply the client's edit and relay it to everybody else: the
			// diff-based sync below only notices local changes.
			m_inventoryRecords.Clear();
			MultiplayerInventorySync.Read(engineBinaryReader, m_inventoryRecords);
			MultiplayerInventorySync.Apply(m_project, m_inventoryRecords);
			BroadcastBlockInventories(m_inventoryRecords, slot);
			break;
		}
		case MultiplayerMessage.PlayerInventory:
		{
			int playerIndex = engineBinaryReader.ReadInt32();
			int activeSlot = engineBinaryReader.ReadInt32();
			int slotCount = engineBinaryReader.ReadInt32();
			PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => p.PlayerIndex == playerIndex);
			ComponentPlayer componentPlayer = playerData?.ComponentPlayer;
			ComponentInventoryBase componentInventoryBase = componentPlayer?.ComponentMiner.Inventory as ComponentInventoryBase;
			for (int i = 0; i < slotCount; i++)
			{
				int value = engineBinaryReader.ReadInt32();
				int count = engineBinaryReader.ReadInt32();
				if (componentInventoryBase != null && i < componentInventoryBase.SlotsCount)
				{
					componentInventoryBase.SetSlotNet(i, value, count);
				}
			}
			if (componentInventoryBase != null && activeSlot >= -1 && activeSlot < componentInventoryBase.SlotsCount)
			{
				componentInventoryBase.ActiveSlotIndex = activeSlot;
			}
			break;
		}
		case MultiplayerMessage.Ping:
			slot.Channel.Send(MultiplayerMessage.Pong, null);
			break;
		}
	}

	private static string SanitizeName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "Player";
		}
		name = name.Trim();
		if (!PlayerData.VerifyName(name))
		{
			string text = new string(name.Where(char.IsLetterOrDigit).ToArray());
			name = text.Length < 2 ? "Player" : text.Substring(0, MathUtils.Min(text.Length, 14));
		}
		return name;
	}

	private void AcceptClient(ClientSlot slot)
	{
		if (m_subsystemPlayers.PlayersData.Count >= MultiplayerManager.MaxPlayers)
		{
			slot.Channel.Send(MultiplayerMessage.Disconnect, delegate(EngineBinaryWriter writer)
			{
				writer.Write("The host world is full.");
			});
			DisconnectClient(slot, "World full.");
			return;
		}
		PlayerData playerData = new PlayerData(m_project)
		{
			IsRemote = true,
			Name = slot.Name
		};
		m_subsystemPlayers.AddPlayerData(playerData);
		slot.PlayerData = playerData;
		slot.PlayerIndex = playerData.PlayerIndex;
		Vector3 spawnPosition = FindSpawnPosition();
		Entity entity = MultiplayerEntities.CreatePlayerEntity(m_project, playerData, spawnPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtils.DegToRad(45f)));
		m_project.AddEntity(entity);
		Log.Information("[Multiplayer] {0} joined as player {1}", slot.Name, playerData.PlayerIndex);
		SendSnapshot(slot);
		BroadcastChat(null, slot.Name + " joined the game");
		BroadcastPlayerList();
	}

	private Vector3 FindSpawnPosition()
	{
		PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => !p.IsRemote);
		Vector3 vector = playerData?.ComponentPlayer?.ComponentBody.Position ?? m_subsystemPlayers.GlobalSpawnPosition;
		if (vector == Vector3.Zero)
		{
			vector = m_subsystemTerrain.TerrainContentsGenerator.FindCoarseSpawnPosition();
		}
		int num = Terrain.ToCell(vector.X);
		int num2 = Terrain.ToCell(vector.Z);
		int num3 = m_subsystemTerrain.Terrain.CalculateTopmostCellHeight(num, num2) + 1;
		return new Vector3((float)num + 0.5f, (float)num3 + 0.01f, (float)num2 + 0.5f);
	}

	private void SendSnapshot(ClientSlot slot)
	{
		byte[] buffer;
		int length;
		MultiplayerManager.BeginSnapshotSave();
		try
		{
			GameManager.SaveProject(waitForCompletion: true, showErrorDialog: false);
			using MemoryStream memoryStream = new MemoryStream();
			WorldsManager.ExportWorld(GameManager.WorldInfo.DirectoryName, memoryStream);
			// Send from the stream buffer directly; ToArray() would allocate a
			// second copy of the whole (possibly very large) snapshot.
			buffer = memoryStream.GetBuffer();
			length = (int)memoryStream.Length;
		}
		finally
		{
			MultiplayerManager.EndSnapshotSave();
		}
		// Record what the snapshot contains so it is not streamed back in
		// full as individual EntityAdd messages once the client is ready.
		slot.SnapshotEntityIds = new HashSet<int>();
		foreach (Entity entity2 in m_project.Entities)
		{
			slot.SnapshotEntityIds.Add(EntityNetRegistry.GetOrCreateId(entity2));
		}
		slot.Channel.Send(MultiplayerMessage.Welcome, delegate(EngineBinaryWriter writer)
		{
			writer.Write(MultiplayerProtocol.Version);
			writer.Write(slot.PlayerIndex);
			writer.Write(slot.Name);
			writer.Write(WorldName);
		});
		slot.Channel.SendWorldSnapshot(buffer, length);
		slot.SnapshotSent = true;
		Log.Information("[Multiplayer] Sent world snapshot ({0} KiB) to {1}", length / 1024, slot.Name);
	}

	private void CompleteJoin(ClientSlot slot)
	{
		if (slot.Ready || !slot.SnapshotSent)
		{
			return;
		}
		slot.Ready = true;
		slot.LastInputTime = Time.RealTime;
		foreach (CellChange pendingCell in slot.PendingCells)
		{
			SendCellChange(slot, pendingCell);
		}
		slot.PendingCells.Clear();
		if (slot.SnapshotEntityIds != null)
		{
			foreach (int snapshotEntityId in slot.SnapshotEntityIds)
			{
				slot.KnownEntities.Add(snapshotEntityId);
			}
		}
		HashSet<int> snapshotIds = slot.SnapshotEntityIds;
		// The snapshot list is only needed until the client is ready; keeping
		// it for the rest of the session is pure overhead.
		slot.SnapshotEntityIds = null;
		foreach (Entity entity in m_project.Entities)
		{
			int orCreateId = EntityNetRegistry.GetOrCreateId(entity);
			if (snapshotIds != null && snapshotIds.Contains(orCreateId))
			{
				continue;
			}
			// Created while the snapshot was being written or while the
			// client was loading: the client has never seen it.
			slot.KnownEntities.Add(orCreateId);
			SendEntityAdd(slot, entity);
		}
		BroadcastChat(null, slot.Name + " joined the game");
		Log.Information("[Multiplayer] {0} finished loading the world", slot.Name);
	}

	private void ApplyClientInputs()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || client.PlayerData == null)
			{
				continue;
			}
			ComponentPlayer componentPlayer = client.PlayerData.ComponentPlayer;
			if (componentPlayer == null)
			{
				continue;
			}
			if (client.HasPendingInput)
			{
				componentPlayer.ComponentInput.SetPlayerInput(client.PendingInput);
				client.HasPendingInput = false;
				client.PendingInput = default(PlayerInput);
			}
		}
	}

	private void CheckTimeouts()
	{
		double realTime = Time.RealTime;
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected)
			{
				continue;
			}
			if (!client.Ready)
			{
				if (client.PendingCellsOverflow)
				{
					DisconnectClient(client, "Could not keep up while loading the world.");
				}
				else if (!client.SnapshotSent)
				{
					// A connection that never even says hello used to stay
					// forever, holding a socket and two threads per attempt.
					if (realTime - client.ConnectTime > HandshakeTimeout)
					{
						DisconnectClient(client, "Timed out during the join handshake.");
					}
				}
				else if (realTime - client.ConnectTime > 120.0)
				{
					DisconnectClient(client, "Timed out while loading the world.");
				}
			}
			else if (realTime - client.LastInputTime > MultiplayerManager.ClientInputTimeout)
			{
				// A crashed or killed client never closes its connection. The
				// input stream is the liveness signal instead.
				DisconnectClient(client, "Timed out (no input from client).");
			}
		}
	}

	private void RemoveLostClients()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected)
			{
				continue;
			}
			if (client.Channel.IsClosed)
			{
				DisconnectClient(client, client.Channel.CloseReason);
			}
		}
	}

	public void OnLocalCellChanged(int x, int y, int z, int value)
	{
		CellChange item = default(CellChange);
		item.X = x;
		item.Y = y;
		item.Z = z;
		item.Value = value;
		m_pendingCells.Add(item);
		foreach (ClientSlot client in SnapshotClients())
		{
			if (!client.Disconnected && !client.Ready && client.SnapshotSent)
			{
				if (client.PendingCells.Count >= MaxPendingCells)
				{
					// Do not buffer an unbounded amount of world edits for a
					// client that may never finish loading.
					client.PendingCellsOverflow = true;
				}
				else
				{
					client.PendingCells.Add(item);
				}
			}
		}
	}

	private void FlushCellChanges()
	{
		if (m_pendingCells.Count == 0)
		{
			return;
		}
		List<CellChange> list = new List<CellChange>(m_pendingCells);
		m_pendingCells.Clear();
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			client.Channel.Send(MultiplayerMessage.CellChange, delegate(EngineBinaryWriter writer)
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
	}

	private void SendCellChange(ClientSlot slot, CellChange change)
	{
		slot.Channel.Send(MultiplayerMessage.CellChange, delegate(EngineBinaryWriter writer)
		{
			writer.Write(1);
			writer.Write(change.X);
			writer.Write(change.Y);
			writer.Write(change.Z);
			writer.Write(change.Value);
		});
	}

	private void SyncEntities()
	{
		List<Entity> list = new List<Entity>();
		foreach (Entity entity in m_project.Entities)
		{
			list.Add(entity);
		}
		Dictionary<int, Entity> dictionary = new Dictionary<int, Entity>();
		foreach (Entity item in list)
		{
			dictionary[EntityNetRegistry.GetOrCreateId(item)] = item;
		}
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			Vector3 clientCenter = GetClientCenter(client);
			HashSet<int> hashSet = new HashSet<int>();
			List<KeyValuePair<int, Entity>> list2 = new List<KeyValuePair<int, Entity>>();
			foreach (KeyValuePair<int, Entity> item2 in dictionary)
			{
				if (!IsEntityRelevant(item2.Value, clientCenter))
				{
					continue;
				}
				hashSet.Add(item2.Key);
				list2.Add(item2);
				if (!client.KnownEntities.Contains(item2.Key))
				{
					SendEntityAdd(client, item2.Value);
				}
			}
			List<int> list3 = new List<int>();
			foreach (int knownEntity in client.KnownEntities)
			{
				if (!hashSet.Contains(knownEntity))
				{
					list3.Add(knownEntity);
				}
			}
			foreach (int item3 in list3)
			{
				client.Channel.Send(MultiplayerMessage.EntityRemove, delegate(EngineBinaryWriter writer)
				{
					writer.Write(item3);
				});
			}
			client.KnownEntities.Clear();
			foreach (int item4 in hashSet)
			{
				client.KnownEntities.Add(item4);
			}
			if (list2.Count > 0)
			{
				client.Channel.Send(MultiplayerMessage.EntityUpdate, delegate(EngineBinaryWriter writer)
				{
					writer.Write(list2.Count);
					foreach (KeyValuePair<int, Entity> item5 in list2)
					{
						WriteEntityUpdate(writer, item5.Key, item5.Value);
					}
				});
			}
		}
	}

	private static bool IsEntityRelevant(Entity entity, Vector3 center)
	{
		ComponentPlayer componentPlayer = entity.FindComponent<ComponentPlayer>(throwOnError: false);
		if (componentPlayer != null)
		{
			return true;
		}
		ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
		if (componentBody != null)
		{
			return Vector3.DistanceSquared(componentBody.Position, center) < 147456f;
		}
		// Block entities (chests, furnaces, dispensers...) have no body but do
		// carry state that must be mirrored, so keep them within range too.
		ComponentBlockEntity componentBlockEntity = entity.FindComponent<ComponentBlockEntity>(throwOnError: false);
		if (componentBlockEntity != null)
		{
			return Vector3.DistanceSquared(new Vector3(componentBlockEntity.Coordinates.X, componentBlockEntity.Coordinates.Y, componentBlockEntity.Coordinates.Z), center) < 147456f;
		}
		return false;
	}

	private static void WriteEntityUpdate(EngineBinaryWriter writer, int netId, Entity entity)
	{
		writer.Write(netId);
		ComponentBody componentBody = entity.FindComponent<ComponentBody>(throwOnError: false);
		ComponentHealth componentHealth = entity.FindComponent<ComponentHealth>(throwOnError: false);
		ComponentCreature componentCreature = entity.FindComponent<ComponentCreature>(throwOnError: false);
		byte b = 0;
		if (componentBody != null)
		{
			b |= 1;
		}
		if (componentHealth != null)
		{
			b |= 2;
		}
		if (componentCreature != null && componentCreature.ComponentLocomotion != null)
		{
			b |= 4;
		}
		writer.Write(b);
		if (componentBody != null)
		{
			writer.Write(componentBody.Position);
			writer.WriteQuaternion(componentBody.Rotation);
			writer.Write(componentBody.Velocity);
			writer.Write(componentBody.TargetCrouchFactor);
		}
		if (componentHealth != null)
		{
			writer.Write(componentHealth.Health);
			writer.Write(componentHealth.Air);
		}
		if (componentCreature != null && componentCreature.ComponentLocomotion != null)
		{
			writer.Write(componentCreature.ComponentLocomotion.LookAngles);
		}
	}

	private Vector3 GetClientCenter(ClientSlot client)
	{
		ComponentPlayer componentPlayer = client.PlayerData?.ComponentPlayer;
		if (componentPlayer != null)
		{
			return componentPlayer.ComponentBody.Position;
		}
		PlayerData playerData = m_subsystemPlayers.PlayersData.FirstOrDefault((PlayerData p) => !p.IsRemote);
		return playerData?.ComponentPlayer?.ComponentBody.Position ?? m_subsystemPlayers.GlobalSpawnPosition;
	}

	private void SendEntityAdd(ClientSlot client, Entity entity)
	{
		string text;
		try
		{
			text = MultiplayerEntities.SaveEntityXml(m_project, entity).ToString();
		}
		catch (Exception ex)
		{
			// Entities that own other entities cannot be serialized on their
			// own. They are recreated together with their owner, so this is
			// expected and only worth reporting once per entity.
			if (m_unserializableEntities.Add(EntityNetRegistry.GetOrCreateId(entity)))
			{
				Log.Warning("[Multiplayer] Could not serialize entity \"{0}\": {1}", entity.ValuesDictionary.DatabaseObject.Name, ex.Message);
			}
			return;
		}
		client.Channel.Send(MultiplayerMessage.EntityAdd, delegate(EngineBinaryWriter writer)
		{
			writer.Write(text);
		});
	}

	private void SyncPlayerStates()
	{
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			client.Channel.Send(MultiplayerMessage.PlayerState, delegate(EngineBinaryWriter writer)
			{
				writer.Write(m_subsystemPlayers.PlayersData.Count);
				foreach (PlayerData playersDatum in m_subsystemPlayers.PlayersData)
				{
					ComponentPlayer componentPlayer = playersDatum.ComponentPlayer;
					writer.Write(playersDatum.PlayerIndex);
					writer.Write(playersDatum.Name);
					writer.Write(playersDatum.Level);
					writer.Write(playersDatum.IsRemote ? 1 : 0);
					if (componentPlayer == null)
					{
						writer.Write(0);
						continue;
					}
					writer.Write(1);
					ComponentHealth componentHealth = componentPlayer.ComponentHealth;
					ComponentVitalStats componentVitalStats = componentPlayer.ComponentVitalStats;
					ComponentInventoryBase componentInventoryBase = componentPlayer.ComponentMiner.Inventory as ComponentInventoryBase;
					writer.Write(componentHealth.Health);
					writer.Write(componentHealth.Air);
					writer.Write(componentVitalStats.Food);
					writer.Write(componentVitalStats.Temperature);
					writer.Write(componentVitalStats.Wetness);
					if (componentInventoryBase == null)
					{
						writer.Write(0);
						continue;
					}
					writer.Write(componentInventoryBase.SlotsCount);
					writer.Write(componentInventoryBase.ActiveSlotIndex);
					for (int i = 0; i < componentInventoryBase.SlotsCount; i++)
					{
						writer.Write(componentInventoryBase.GetSlotValue(i));
						writer.Write(componentInventoryBase.GetSlotCount(i));
					}
				}
			});
		}
	}

	private void SyncWorldState()
	{
		double totalElapsedGameTime = m_subsystemGameInfo.TotalElapsedGameTime;
		double timeOfDayOffset = m_subsystemTimeOfDay.TimeOfDayOffset;
		float timeOfYear = m_subsystemGameInfo.WorldSettings.TimeOfYear;
		bool precipitation = m_subsystemWeather.IsPrecipitationStarted;
		bool fog = m_subsystemWeather.IsFogStarted;
		foreach (ClientSlot client in SnapshotClients())
		{
			if (client.Disconnected || !client.Ready)
			{
				continue;
			}
			client.Channel.Send(MultiplayerMessage.TimeSync, delegate(EngineBinaryWriter writer)
			{
				writer.Write(totalElapsedGameTime);
				writer.Write(timeOfDayOffset);
				writer.Write(timeOfYear);
				writer.Write(precipitation);
				writer.Write(fog);
			});
		}
	}

	private void BroadcastPlayerList()
	{
		List<Tuple<int, string, bool>> list = new List<Tuple<int, string, bool>>();
		foreach (PlayerData playersDatum in m_subsystemPlayers.PlayersData)
		{
			list.Add(new Tuple<int, string, bool>(playersDatum.PlayerIndex, playersDatum.Name, playersDatum.IsRemote));
		}
		foreach (ClientSlot client in SnapshotClients())
		{
			if (!client.Disconnected && client.Ready)
			{
				client.Channel.Send(MultiplayerMessage.PlayerJoined, delegate(EngineBinaryWriter writer)
				{
					writer.Write(list.Count);
					foreach (Tuple<int, string, bool> item in list)
					{
						writer.Write(item.Item1);
						writer.Write(item.Item2);
						writer.Write(item.Item3);
					}
				});
			}
		}
	}

	public void BroadcastChat(string sender, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		foreach (ClientSlot client in SnapshotClients())
		{
			if (!client.Disconnected && client.Ready)
			{
				client.Channel.Send(MultiplayerMessage.ChatMessage, delegate(EngineBinaryWriter writer)
				{
					writer.Write(sender ?? string.Empty);
					writer.Write(text);
				});
			}
		}
	}

	private void DisconnectClient(ClientSlot slot, string reason)
	{
		if (slot.Disconnected)
		{
			return;
		}
		slot.Disconnected = true;
		try
		{
			// Dispose (not just Close) also releases any frames that are still
			// queued for the writer thread.
			slot.Channel.Dispose();
		}
		catch (Exception)
		{
		}
		slot.PendingCells.Clear();
		slot.KnownEntities.Clear();
		slot.SnapshotEntityIds = null;
		slot.HasPendingInput = false;
		slot.PendingInput = default(PlayerInput);
		PlayerData playerData = slot.PlayerData;
		slot.PlayerData = null;
		if (playerData != null)
		{
			string name = playerData.Name;
			bool flag = false;
			try
			{
				flag = m_subsystemPlayers.PlayersData.Contains(playerData);
			}
			catch (Exception)
			{
			}
			try
			{
				m_subsystemTerrain.TerrainUpdater.RemoveUpdateLocation(playerData.PlayerIndex);
			}
			catch (Exception)
			{
			}
			try
			{
				if (flag)
				{
					m_subsystemPlayers.RemovePlayerData(playerData);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("[Multiplayer] Could not remove player: " + ex);
			}
			// The removal above is defensive: if the player record survived for
			// any reason it must not be left in the world forever.
			try
			{
				if (m_subsystemPlayers.PlayersData.Contains(playerData))
				{
					Log.Error("[Multiplayer] Player \"" + name + "\" is still in the world after disconnecting.");
				}
			}
			catch (Exception)
			{
			}
			BroadcastChat(null, name + " left the game");
			BroadcastPlayerList();
		}
		lock (m_clients)
		{
			m_clients.Remove(slot);
		}
		Log.Information("[Multiplayer] Client disconnected: {0}", reason);
	}

	public void Stop()
	{
		bool wasRunning = m_running;
		m_running = false;
		m_unserializableEntities.Clear();
		m_pendingCells.Clear();
		m_inventoryRecords.Clear();
		KeyValuePair<ClientSlot, NetChannel.Frame> queued;
		while (m_inbox.TryDequeue(out queued))
		{
		}
		try
		{
			m_listener?.Stop();
		}
		catch (Exception)
		{
		}
		// Wait for the accept loop so it cannot add a client after the list
		// below has been cleared.
		Thread acceptThread = m_acceptThread;
		m_acceptThread = null;
		if (acceptThread != null && acceptThread != Thread.CurrentThread)
		{
			try
			{
				acceptThread.Join(500);
			}
			catch (Exception)
			{
			}
		}
		if (!wasRunning)
		{
			// Nothing was started, but make sure a half-open listener or an
			// empty client list cannot keep the server alive.
			lock (m_clients)
			{
				m_clients.Clear();
			}
			return;
		}
		foreach (ClientSlot client in SnapshotClients())
		{
			client.Channel.Send(MultiplayerMessage.Disconnect, delegate(EngineBinaryWriter writer)
			{
				writer.Write("The host closed the game.");
			});
			client.Channel.Dispose();
		}
		List<ClientSlot> list = SnapshotClients();
		foreach (ClientSlot item in list)
		{
			if (item.PlayerData != null)
			{
				try
				{
					m_subsystemPlayers.RemovePlayerData(item.PlayerData);
				}
				catch (Exception)
				{
				}
				item.PlayerData = null;
			}
			item.Disconnected = true;
			item.PendingCells.Clear();
			item.KnownEntities.Clear();
			item.SnapshotEntityIds = null;
		}
		lock (m_clients)
		{
			m_clients.Clear();
		}
	}

	public void Dispose()
	{
		Stop();
	}
}
