using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Engine.Serialization;

namespace Game;

/// <summary>
/// A reliable, length-prefixed message channel on top of a connected TCP socket.
///
/// Reading and writing happen on dedicated background threads so the game thread
/// never blocks on the network. Received messages are delivered through
/// <see cref="MessageReceived"/> on the reader thread; callers are expected to
/// enqueue them and process them from the game thread.
/// </summary>
public sealed class NetChannel : IDisposable
{
	public struct Frame
	{
		public MultiplayerMessage Type;

		public byte[] Body;
	}

	private readonly Socket m_socket;

	private readonly NetworkStream m_stream;

	private readonly BlockingCollection<byte[]> m_sendQueue = new BlockingCollection<byte[]>();

	private readonly Thread m_readerThread;

	private readonly Thread m_writerThread;

	private volatile bool m_closed;

	private string m_closeReason = string.Empty;

	public event Action<Frame> MessageReceived;

	public bool IsClosed => m_closed;

	public string CloseReason => m_closeReason;

	public Socket Socket => m_socket;

	public NetChannel(Socket socket, string threadName)
	{
		m_socket = socket;
		m_socket.NoDelay = true;
		try
		{
			// Detect clients that vanish without closing the connection (power
			// loss, killed process). The input watchdog in the server is the
			// primary mechanism; keepalive makes the socket fail as well.
			m_socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, optionValue: true);
			m_socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);
			m_socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
			m_socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
		}
		catch (Exception)
		{
		}
		m_stream = new NetworkStream(socket, ownsSocket: false);
		m_readerThread = new Thread(ReadLoop)
		{
			IsBackground = true,
			Name = threadName + " Reader"
		};
		m_writerThread = new Thread(WriteLoop)
		{
			IsBackground = true,
			Name = threadName + " Writer"
		};
	}

	public void Start()
	{
		m_readerThread.Start();
		m_writerThread.Start();
	}

	public void Send(MultiplayerMessage type, Action<EngineBinaryWriter> writePayload)
	{
		if (m_closed)
		{
			return;
		}
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(new byte[4], 0, 4);
		using (EngineBinaryWriter engineBinaryWriter = new EngineBinaryWriter(memoryStream, leaveOpen: true))
		{
			engineBinaryWriter.Write((byte)type);
			writePayload?.Invoke(engineBinaryWriter);
			engineBinaryWriter.Flush();
		}
		int num = (int)memoryStream.Length - 4;
		if (num > MultiplayerProtocol.MaxFrameSize)
		{
			throw new InvalidOperationException("Message is too large to send.");
		}
		byte[] array = memoryStream.ToArray();
		array[0] = (byte)num;
		array[1] = (byte)(num >> 8);
		array[2] = (byte)(num >> 16);
		array[3] = (byte)(num >> 24);
		try
		{
			m_sendQueue.Add(array);
		}
		catch (InvalidOperationException)
		{
		}
	}

	public void SendRaw(MultiplayerMessage type, byte[] payload)
	{
		Send(type, delegate(EngineBinaryWriter writer)
		{
			if (payload != null && payload.Length != 0)
			{
				writer.Write(payload);
			}
		});
	}

	/// <summary>Reads a single frame synchronously. Used during the join handshake.</summary>
	public static Frame ReadFrame(Stream stream)
	{
		byte[] array = new byte[4];
		if (!ReadExactly(stream, array, 4))
		{
			throw new EndOfStreamException("Connection closed while reading a frame header.");
		}
		int num = array[0] | (array[1] << 8) | (array[2] << 16) | (array[3] << 24);
		if (num <= 0 || num > MultiplayerProtocol.MaxFrameSize)
		{
			throw new InvalidOperationException("Invalid frame length " + num + ".");
		}
		byte[] array2 = new byte[num];
		if (!ReadExactly(stream, array2, num))
		{
			throw new EndOfStreamException("Connection closed while reading a frame body.");
		}
		Frame result = default(Frame);
		result.Type = (MultiplayerMessage)array2[0];
		result.Body = new byte[num - 1];
		Array.Copy(array2, 1, result.Body, 0, num - 1);
		return result;
	}

	/// <summary>Writes a single frame synchronously. Used during the join handshake.</summary>
	public static void WriteFrame(Stream stream, MultiplayerMessage type, Action<EngineBinaryWriter> writePayload)
	{
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(new byte[4], 0, 4);
		using (EngineBinaryWriter engineBinaryWriter = new EngineBinaryWriter(memoryStream, leaveOpen: true))
		{
			engineBinaryWriter.Write((byte)type);
			writePayload?.Invoke(engineBinaryWriter);
			engineBinaryWriter.Flush();
		}
		int num = (int)memoryStream.Length - 4;
		if (num > MultiplayerProtocol.MaxFrameSize)
		{
			throw new InvalidOperationException("Message is too large to send.");
		}
		byte[] array = memoryStream.ToArray();
		array[0] = (byte)num;
		array[1] = (byte)(num >> 8);
		array[2] = (byte)(num >> 16);
		array[3] = (byte)(num >> 24);
		stream.Write(array, 0, array.Length);
		stream.Flush();
	}

	private static bool ReadExactly(Stream stream, byte[] buffer, int count)
	{
		int num = 0;
		while (num < count)
		{
			int num2 = stream.Read(buffer, num, count - num);
			if (num2 <= 0)
			{
				return false;
			}
			num += num2;
		}
		return true;
	}

	private void ReadLoop()
	{
		try
		{
			while (!m_closed)
			{
				Frame frame = ReadFrame(m_stream);
				MessageReceived?.Invoke(frame);
			}
		}
		catch (Exception ex)
		{
			Close(ex is EndOfStreamException ? "Connection closed." : "Network error: " + ex.Message);
		}
	}

	private void WriteLoop()
	{
		try
		{
			foreach (byte[] item in m_sendQueue.GetConsumingEnumerable())
			{
				m_stream.Write(item, 0, item.Length);
				m_stream.Flush();
			}
		}
		catch (Exception ex)
		{
			Close("Network error: " + ex.Message);
		}
	}

	public void Close(string reason)
	{
		if (m_closed)
		{
			if (!string.IsNullOrEmpty(reason))
			{
				m_closeReason = reason;
			}
			return;
		}
		m_closed = true;
		m_closeReason = reason ?? string.Empty;
		try
		{
			m_sendQueue.CompleteAdding();
		}
		catch (Exception)
		{
		}
		try
		{
			m_socket.Shutdown(SocketShutdown.Both);
		}
		catch (Exception)
		{
		}
		try
		{
			m_socket.Close();
		}
		catch (Exception)
		{
		}
		try
		{
			m_stream.Dispose();
		}
		catch (Exception)
		{
		}
	}

	public void Dispose()
	{
		Close("Disposed.");
	}
}
