using System;
using System.IO;
using MultiplayerMod.Platform.Steam.Network.Messaging;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// Length-prefixed message framing over a TCP stream. TCP is already a reliable, ordered byte stream, so unlike
/// the Steam transport there is no need for datagram fragmentation/reassembly — each logical message is a single
/// [4-byte big-endian length][payload] frame. Payloads are serialized with the shared surrogate-configured
/// <see cref="NetworkSerializer"/> formatter, so the same command graphs (and large payloads like LoadWorld) that
/// travel over Steam travel here unchanged.
/// </summary>
public static class DirectFraming {

    // Guards against a corrupt/oversized length header allocating unbounded memory. Generous enough for a full
    // world save (the Steam send buffer is provisioned to 128 MiB for the same reason).
    private const int MaxFrameSize = 256 * 1024 * 1024;

    /// <summary>Serializes a payload into a complete frame (header + body) once, so it can be written to many
    /// connections without re-serializing — important for large broadcasts such as the world save.</summary>
    public static byte[] Serialize(object payload) {
        using var buffer = new MemoryStream();
        NetworkSerializer.CreateFormatter().Serialize(buffer, payload);
        var length = (int) buffer.Length;
        var framed = new byte[4 + length];
        framed[0] = (byte) (length >> 24);
        framed[1] = (byte) (length >> 16);
        framed[2] = (byte) (length >> 8);
        framed[3] = (byte) length;
        Buffer.BlockCopy(buffer.GetBuffer(), 0, framed, 4, length);
        return framed;
    }

    public static void Write(Stream stream, byte[] framed) {
        // Single writer per connection (the game thread), but lock the stream so a frame is never interleaved
        // with another if that ever changes.
        lock (stream) {
            stream.Write(framed, 0, framed.Length);
            stream.Flush();
        }
    }

    public static void Write(Stream stream, object payload) => Write(stream, Serialize(payload));

    /// <summary>Reads one frame, blocking until it arrives. Returns null on a clean connection close (EOF).</summary>
    public static object? Read(Stream stream) {
        var header = ReadExactly(stream, 4);
        if (header == null)
            return null;

        var length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (length <= 0 || length > MaxFrameSize)
            throw new IOException($"Invalid frame length {length}");

        var payload = ReadExactly(stream, length);
        if (payload == null)
            return null;

        using var streamPayload = new MemoryStream(payload);
        return NetworkSerializer.CreateFormatter().Deserialize(streamPayload);
    }

    private static byte[]? ReadExactly(Stream stream, int count) {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count) {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                return null; // connection closed
            offset += read;
        }
        return buffer;
    }

}
