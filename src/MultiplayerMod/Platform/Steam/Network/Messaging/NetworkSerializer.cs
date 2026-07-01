using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using MultiplayerMod.Platform.Steam.Network.Messaging.Surrogates;

namespace MultiplayerMod.Platform.Steam.Network.Messaging;

public static class NetworkSerializer {

    /// <summary>
    /// Creates a BinaryFormatter configured with the surrogate selector. Every serialize/deserialize path
    /// (including fragmented-message reassembly) must use this — a formatter without the selector fails on
    /// surrogate-only game types (Tag, Vector3, ChoreType, ...) that appear in large payloads such as LoadWorld.
    /// </summary>
    public static BinaryFormatter CreateFormatter() =>
        new() { SurrogateSelector = SerializationSurrogates.Selector };

    public static SerializedNetworkMessage Serialize(INetworkMessage message) {
        return new SerializedNetworkMessage(message);
    }

    public static unsafe INetworkMessage Deserialize(INetworkMessageHandle message) =>
        (INetworkMessage) CreateFormatter()
            .Deserialize(
                new UnmanagedMemoryStream((byte*) message.Pointer.ToPointer(), message.Size)
            );

}
