using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    public class SocketWrapper: IDisposable
    {
        protected StreamSocket BaseSocket { get; set; }
        protected DataReader SocketDataReader { get; set; }
        protected DataWriter SocketDataWriter { get; set; }
        public SocketWrapper(StreamSocket baseSocket, DataReader dataReader, DataWriter dataWriter)
        {
            if (baseSocket == null)
                throw new ArgumentNullException(nameof(baseSocket));

            if (dataReader == null)
                SocketDataReader = new DataReader(BaseSocket.InputStream);

            if (dataWriter == null)
                SocketDataWriter = new DataWriter(BaseSocket.OutputStream);

            BaseSocket = baseSocket;
            SocketDataReader = dataReader;
            SocketDataWriter = dataWriter;

            InitializeDataStream();
        }

        protected SocketWrapper()
        {

        }

        protected virtual void InitializeDataStream()
        {
           
        }

        public virtual uint UnconsumedBufferLength { get { return SocketDataReader != null ? SocketDataReader.UnconsumedBufferLength : uint.MaxValue; } }

        public virtual DataReaderLoadOperation LoadAsync(uint amount)
        {
            return SocketDataReader.LoadAsync(amount);
        }

        public virtual Task<string> ReadStringAsync(uint codeUnitCount)
        {
            return Task.FromResult<string>(SocketDataReader.ReadString(codeUnitCount));
        }

        public virtual Task<IBuffer> ReadBufferAsync(uint length)
        {
            return Task.FromResult<IBuffer>(SocketDataReader.ReadBuffer(length));
        }

        public virtual Task<byte> ReadByteAsync()
        {
            return Task.FromResult<byte>(SocketDataReader.ReadByte());
        }

        public virtual Task ReadBytesAsync(byte[] buffer)
        {
            SocketDataReader.ReadBytes(buffer);
            return Task.CompletedTask;
        }


        protected virtual void SubclassDispose() { }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).

                    SubclassDispose();

                    SocketDataReader.Dispose();
                    SocketDataWriter.Dispose();
                    BaseSocket.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SocketWrapper() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
