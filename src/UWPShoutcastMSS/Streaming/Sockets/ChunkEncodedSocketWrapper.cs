using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    internal class ChunkEncodedSocketWrapper : SocketWrapper
    {
        private InMemoryRandomAccessStream bufferStream = null;
        private CancellationTokenSource bufferTaskCancelTokenSource = null;
        private Task bufferTask = null;
        private DataReader rawDataReader = null;

        public ChunkEncodedSocketWrapper(StreamSocket baseSocket, DataReader dataReader, DataWriter dataWriter)
        {
            BaseSocket = baseSocket;
            SocketDataWriter = dataWriter;

            bufferStream = new InMemoryRandomAccessStream();
            bufferTaskCancelTokenSource = new CancellationTokenSource();
            bufferTask = Task.Run(function: ProcessStreamChunksAsync, cancellationToken: bufferTaskCancelTokenSource.Token);

            rawDataReader = dataReader;
            SocketDataReader = new DataReader(bufferStream.CloneStream().GetInputStreamAt(0));

            InitializeDataStream();
        }

        protected override void SubclassDispose()
        {
            bufferTaskCancelTokenSource.Cancel();
            bufferTaskCancelTokenSource.Dispose();
        }

        private async Task<int> ParseChunkLengthAsync()
        {
            string response = string.Empty;
            while (!response.EndsWith(Environment.NewLine))
            //loop until we get the line-ending signifying the end
            {
                await rawDataReader.LoadAsync(1);
                response += rawDataReader.ReadString(1);
            }

            return int.Parse(response.Trim(), System.Globalization.NumberStyles.HexNumber);
        }

        private async Task ProcessStreamChunksAsync()
        {
            while (!bufferTaskCancelTokenSource.IsCancellationRequested)
            {
                int chunkLength = await ParseChunkLengthAsync();

                await rawDataReader.LoadAsync((uint)(chunkLength + 2)); //extra 2 for the excluded line ending.

                IBuffer data = rawDataReader.ReadBuffer((uint)chunkLength);
                var lineEnding = rawDataReader.ReadString(2);

                await bufferStream.WriteAsync(data);
            }
        }
    }
}