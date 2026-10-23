#region // =====================================================================
// Multiplayer networking transport (UDP + TCP)
// Survivalcraft 2 Linux port - multiplayer layer
//
// Transport split (Vintage Story inspired):
//   * TCP control channel  - reliable, ordered: handshake, world snapshot, chat,
//     membership (join/leave), block changes, disconnect.
//   * UDP game channel     - unreliable, latest-wins: player input (client->host),
//     player state broadcast (host->clients), ping/pong.
//
// All socket work runs on dedicated background threads; game-thread delivery uses
// ConcurrentQueue + Dispatcher.Dispatch so Engine/Game code stays single-threaded.
// =====================================================================
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Multiplayer
{
	public enum NetChannel : byte
	{
		TcpControl = 0,   // reliable (TCP)
		UdpGame = 1,      // unreliable (UDP)
	}

	public enum NetMessageType : byte
	{
		// Control (TCP)
		Hello = 1,
		HelloAck = 2,
		Disconnect = 3,
		JoinRejected = 4,
		WorldSnapshotStart = 5,
		WorldSnapshotChunk = 6,
		WorldSnapshotEnd = 7,
		PlayerJoined = 8,
		PlayerLeft = 9,
		BlockChanged = 10,
		Chat = 11,
		// Game (UDP)
		Ping = 20,
		Pong = 21,
		PlayerState = 22,
	}

	public struct NetPacket
	{
		public NetChannel Channel;
		public NetMessageType Type;
		public byte[] Payload;   // payload without the leading type byte
		public IPEndPoint RemoteEndPoint;
	}

	/// <summary>Thin wrapper around a bound UDP socket.</summary>
	public sealed class UdpTransport : IDisposable
	{
		private readonly Socket m_socket;
		private readonly byte[] m_receiveBuffer = new byte[65507];
		private bool m_disposed;

		public int LocalPort => ((IPEndPoint)m_socket.LocalEndPoint).Port;

		public UdpTransport(int localPort)
		{
			m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
			{
				ReceiveBufferSize = 1 << 20,
				SendBufferSize = 1 << 20,
			};
			m_socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			m_socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
			m_socket.Blocking = false;
		}

		public void Send(EndPoint endPoint, byte[] data)
		{
			if (m_disposed || data == null) return;
			try { m_socket.SendTo(data, 0, data.Length, SocketFlags.None, endPoint); }
			catch (ObjectDisposedException) { }
			catch (SocketException) { }
		}

		public void StartReceive(Action<NetPacket> onPacket)
		{
			_ = Task.Run(() =>
			{
				EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
				while (!m_disposed)
				{
					int len;
					try { len = m_socket.ReceiveFrom(m_receiveBuffer, 0, m_receiveBuffer.Length, SocketFlags.None, ref remote); }
					catch (ObjectDisposedException) { break; }
					catch (SocketException) { continue; }
					catch (Exception) { continue; }
					if (len <= 1) continue;
					var pkt = new NetPacket
					{
						Channel = NetChannel.UdpGame,
						Type = (NetMessageType)m_receiveBuffer[0],
						RemoteEndPoint = (IPEndPoint)remote,
					};
					pkt.Payload = new byte[len - 1];
					Buffer.BlockCopy(m_receiveBuffer, 1, pkt.Payload, 0, len - 1);
					try { onPacket(pkt); } catch (Exception) { }
				}
			});
		}

		public void Close()
		{
			if (m_disposed) return;
			m_disposed = true;
			try { m_socket.Close(); } catch (Exception) { }
			m_socket.Dispose();
		}

		public void Dispose() => Close();
	}

	/// <summary>Reliable TCP control connection.</summary>
	public sealed class TcpControl : IDisposable
	{
		private readonly TcpClient m_client;
		private readonly NetworkStream m_stream;
		private readonly byte[] m_lengthBuffer = new byte[4];
		private bool m_disposed;

		public bool Connected => m_client.Connected;
		public IPEndPoint RemoteEndPoint => (IPEndPoint)m_client.Client.RemoteEndPoint;

		public TcpControl(TcpClient client)
		{
			m_client = client;
			m_client.NoDelay = true;
			m_client.ReceiveBufferSize = 1 << 20;
			m_client.SendBufferSize = 1 << 20;
			m_stream = client.GetStream();
		}

		/// <summary>Send one length-prefixed message: [4 bytes length][type byte][payload].</summary>
		public void Send(NetMessageType type, byte[] payload)
		{
			if (m_disposed || !Connected) return;
			payload ??= Array.Empty<byte>();
			int total = 1 + payload.Length;
			byte[] lengthBytes = BitConverter.GetBytes(total);
			if (BitConverter.IsLittleEndian) { } // network is little-endian on x86; both ends are same machine arch here
			List<byte> buffer = new List<byte>(4 + total);
			buffer.AddRange(lengthBytes);
			buffer.Add((byte)type);
			buffer.AddRange(payload);
			try { m_stream.Write(buffer.ToArray(), 0, buffer.Count); m_stream.Flush(); }
			catch (Exception) { }
		}

		public void StartReceive(Action<NetPacket> onMessage, Action onDisconnected)
		{
			_ = Task.Run(() =>
			{
				while (!m_disposed && Connected)
				{
					int read;
					try { read = ReadExactly(m_lengthBuffer, 0, 4); }
					catch (IOException) { break; }
					catch (ObjectDisposedException) { break; }
					catch (Exception) { break; }
					if (read == 0) break;
					int total = BitConverter.ToInt32(m_lengthBuffer, 0);
					if (total <= 0 || total > 1 << 20) break;
					byte[] frame = new byte[total];
					try { read = ReadExactly(frame, 0, total); }
					catch (IOException) { break; }
					catch (ObjectDisposedException) { break; }
					catch (Exception) { break; }
					if (read != total) break;
					var pkt = new NetPacket
					{
						Channel = NetChannel.TcpControl,
						Type = (NetMessageType)frame[0],
					};
					pkt.Payload = new byte[total - 1];
					Buffer.BlockCopy(frame, 1, pkt.Payload, 0, total - 1);
					try { onMessage(pkt); } catch (Exception) { }
				}
				try { onDisconnected(); } catch (Exception) { }
			});
		}

		private int ReadExactly(byte[] buf, int offset, int count)
		{
			int read = 0;
			while (read < count)
			{
				int n = m_stream.Read(buf, offset + read, count - read);
				if (n == 0) return read;
				read += n;
			}
			return read;
		}

		public void Close()
		{
			if (m_disposed) return;
			m_disposed = true;
			try { m_stream.Close(); } catch (Exception) { }
			try { m_client.Close(); } catch (Exception) { }
			m_client.Dispose();
		}

		public void Dispose() => Close();
	}

	/// <summary>TCP listener that accepts control connections.</summary>
	public sealed class TcpListenerTransport : IDisposable
	{
		private readonly TcpListener m_listener;
		private bool m_disposed;
		public int LocalPort => ((IPEndPoint)m_listener.LocalEndpoint).Port;

		public TcpListenerTransport(int port)
		{
			m_listener = new TcpListener(IPAddress.Any, port);
			m_listener.Start();
		}

		public void StartAccept(Func<TcpClient, bool> onAccept)
		{
			_ = Task.Run(async () =>
			{
				while (!m_disposed)
				{
					TcpClient client;
					try { client = await m_listener.AcceptTcpClientAsync(); }
					catch (ObjectDisposedException) { break; }
					catch (SocketException) { break; }
					catch (Exception) { break; }
					try { if (!onAccept(client)) client.Close(); }
					catch (Exception) { try { client.Close(); } catch { } }
				}
			});
		}

		public void Close()
		{
			if (m_disposed) return;
			m_disposed = true;
			try { m_listener.Stop(); } catch (Exception) { }
			m_listener.Dispose();
		}

		public void Dispose() => Close();
	}
}
