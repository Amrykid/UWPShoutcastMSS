using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    internal class ChunkEncodedSocketWrapper : SocketWrapper
    {
        public ChunkEncodedSocketWrapper(StreamSocket baseSocket, DataReader dataReader, DataWriter dataWriter) : base(baseSocket, dataReader, dataWriter)
        {
        }
    }
}