using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Streaming.Sockets
{
    internal static class SocketWrapperFactory
    {
        internal static SocketWrapper CreateSocketWrapper(ShoutcastStreamFactory.ShoutcastStreamFactoryInternalConnectResult result)
        {
            var transferEncoding = result.httpResponseHeaders.FirstOrDefault(x => x.Key.ToLower() == "transfer-encoding").Value ?? "identity";

            switch(transferEncoding)
            {
                case "chunked":
                    return new ChunkEncodedSocketWrapper(result.socket, result.socketReader, result.socketWriter);
                case "identity":
                default:
                    return new SocketWrapper(result.socket, result.socketReader, result.socketWriter);
            }
        }
    }
}
