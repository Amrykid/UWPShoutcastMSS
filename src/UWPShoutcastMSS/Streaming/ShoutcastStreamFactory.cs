using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming
{
    public static class ShoutcastStreamFactory
    {
        public const string DefaultUserAgent = "Shoutcast Player (http://github.com/Amrykid/UWPShoutcastMSS)";

        public static Task<ShoutcastStream> ConnectAsync(Uri serverUrl)
        {
            return ConnectAsync(serverUrl, new ShoutcastStreamFactoryConnectionSettings()
            {
                UserAgent = DefaultUserAgent
            });
        }
        public static async Task<ShoutcastStream> ConnectAsync(Uri serverUrl,
            ShoutcastStreamFactoryConnectionSettings settings)
        {
            //http://www.smackfu.com/stuff/programming/shoutcast.html

            StreamSocket socket = new StreamSocket();
            DataWriter socketWriter = null;
            DataReader socketReader = null;

            ShoutcastStream shoutStream = null;

            await socket.ConnectAsync(new Windows.Networking.HostName(serverUrl.Host), serverUrl.Port.ToString());
            
            socketWriter = new DataWriter(socket.OutputStream);
            socketReader = new DataReader(socket.InputStream);

            //build a http request
            StringBuilder requestBuilder = new StringBuilder();
            requestBuilder.AppendLine("GET " + serverUrl.LocalPath + " HTTP/1.1");
            requestBuilder.AppendLine("Icy-MetaData: 1");
            requestBuilder.AppendLine("Host: " + serverUrl.Host + (serverUrl.Port != 80 ? ":" + serverUrl.Port : ""));
            requestBuilder.AppendLine("Connection: Keep-Alive");
            requestBuilder.AppendLine("User-Agent: " + settings.UserAgent);
            requestBuilder.AppendLine();

            //send the http request
            socketWriter.WriteString(requestBuilder.ToString());
            await socketWriter.StoreAsync();
            await socketWriter.FlushAsync();

            //start reading the headers from the response
            string response = string.Empty;
            while (!response.EndsWith(Environment.NewLine + Environment.NewLine)) 
                //loop until we get the double line-ending signifying the end of the headers
            {
                await socketReader.LoadAsync(1);
                response += socketReader.ReadString(1);
            }

            shoutStream = new ShoutcastStream(socket, socketReader, socketWriter);

            string httpLine = response.Substring(0, response.IndexOf('\n')).Trim();

            if (string.IsNullOrWhiteSpace(httpLine)) throw new InvalidOperationException("httpLine is null or whitespace");

            var action = ParseHttpCode(httpLine, response, shoutStream);

            switch(action.ActionType)
            {
                case ConnectionActionType.Success:
                    var headers = ParseResponse(response, shoutStream);
                    await shoutStream.HandleHeadersAsync(headers);
                    return shoutStream;
                case ConnectionActionType.Fail:
                    throw action.ActionException;
                default:
                    throw new Exception("We weren't able to connect for some reason.");
            }
        }

        private static ConnectionAction ParseHttpCode(string httpLine, string response, ShoutcastStream shoutStream)
        {
            var bits = httpLine.Split(new char[] { ' ' }, 3);

            var protocolBit = bits[0].ToUpper(); //always 'HTTP' or 'ICY
            int statusCode = int.Parse(bits[1]);

            switch(protocolBit)
            {
                case "HTTP":
                    {
                        switch(statusCode)
                        {
                            case 200: return ConnectionAction.FromSuccess();
                        }
                    }
                    break;
                case "ICY":
                    {
                        switch(statusCode)
                        {
                            case 200: return ConnectionAction.FromSuccess();
                        }
                    }
                    break;
            }

            return null;
        }

        private static KeyValuePair<string, string>[] ParseResponse(string response, ShoutcastStream shoutStream)
        {
            string[] responseSplitByLine = response.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            KeyValuePair<string, string>[] headers = ParseHttpResponseToKeyPairArray(responseSplitByLine);         

            shoutStream.metadataInt = uint.Parse(headers.First(x => x.Key == "ICY-METAINT").Value);

            shoutStream.StationInfo.StationName = headers.First(x => x.Key == "ICY-NAME").Value;
            shoutStream.StationInfo.StationGenre = headers.First(x => x.Key == "ICY-GENRE").Value;

            if (headers.Any(x => x.Key.ToUpper() == "ICY-DESCRIPTION"))
                shoutStream.StationInfo.StationDescription = headers.First(x => x.Key.ToUpper() == "ICY-DESCRIPTION").Value;

            shoutStream.AudioInfo.BitRate = uint.Parse(headers.FirstOrDefault(x => x.Key == "ICY-BR").Value);

            switch (headers.First(x => x.Key == "CONTENT-TYPE").Value.ToLower().Trim())
            {
                case "audio/mpeg":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.MP3;
                    break;
                case "audio/aac":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.AAC;
                    break;
                case "audio/aacp":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.AAC_ADTS;
                    break;
            }

            return headers;
        }

        private static KeyValuePair<string, string>[] ParseHttpResponseToKeyPairArray(string[] responseSplitByLine)
        {
            return responseSplitByLine.Where(line => line.Contains(":")).Select(line =>
            {
                string header = line.Substring(0, line.IndexOf(":"));
                string value = line.Substring(line.IndexOf(":") + 1);

                var pair = new KeyValuePair<string, string>(header.ToUpper(), value);

                return pair;
            }).ToArray();
        }

        internal class ConnectionAction
        {
            public ConnectionActionType ActionType { get; set; }
            public Uri ActionUrl { get; set; }
            public Exception ActionException { get; set; }

            public static ConnectionAction FromSuccess() { return new ConnectionAction() { ActionType = ConnectionActionType.Success }; }
        }
        internal enum ConnectionActionType
        {
            Fail = 0,
            Success = 1,
            Redirect = 2
        }
    }
}

