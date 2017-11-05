﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    internal class ChunkEncodedSocketWrapper : SocketWrapper
    {
        private InMemoryRandomAccessStream bufferStream = null;
        private IRandomAccessStream clonedBufferStream = null;
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

            clonedBufferStream = bufferStream.CloneStream();
            SocketDataReader = new DataReader(clonedBufferStream.GetInputStreamAt(0));

            InitializeDataStream();

            Task.Delay(3000).ContinueWith(x =>
            {
                //wait 3 seconds and then allow streaming to proceed.
                InitialBufferLock.Release(1);
            });
        }

        protected override void SubclassDispose()
        {
            bufferTaskCancelTokenSource.Cancel();
            bufferTaskCancelTokenSource.Dispose();

            clonedBufferStream.Dispose();
            bufferStream.Dispose();
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