using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Networking.Connectivity;
using UWPShoutcastMSS.Parsers.Audio;

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastMediaSourceStream
    {
        //http://stackoverflow.com/questions/6294807/calculate-mpeg-frame-length-ms

        TimeSpan timeOffSet = new TimeSpan();
        private UInt64 byteOffset;

        public Windows.Media.Core.MediaStreamSource MediaStreamSource { get; private set; }
        public ServerStationInfo StationInfo { get; private set; }
        public ServerAudioInfo AudioInfo { get; private set; }

        public bool ShouldGetMetadata { get; private set; }
        public static string UserAgent { get; set; }

        StreamSocket socket = null;
        DataWriter socketWriter = null;
        DataReader socketReader = null;
        private volatile bool connected = false;
        private string relativePath = ";";
        private ShoutcastServerType serverType = ShoutcastServerType.Shoutcast;

        Uri streamUrl = null;

        uint metadataInt = 0;
        uint metadataPos = 0;

        public void Disconnect()
        {
            try
            {
                MediaStreamSource.SampleRequested -= MediaStreamSource_SampleRequested;
                MediaStreamSource.Starting -= MediaStreamSource_Starting;
                MediaStreamSource.Closed -= MediaStreamSource_Closed;
            }
            catch (Exception) { }

            try
            {
                socketWriter.Dispose();
                socketReader.Dispose();
                socket.Dispose();
            }
            catch (Exception) { }

            connected = false;
        }

        public ShoutcastMediaSourceStream(Uri url, ShoutcastServerType stationServerType = ShoutcastServerType.Shoutcast, string relativePath = ";", bool getMetadata = true)
        {
            StationInfo = new ServerStationInfo();

            streamUrl = url;

            serverType = stationServerType;

            socket = new StreamSocket();

            AudioInfo = new ServerAudioInfo();

            ShouldGetMetadata = getMetadata;

            this.relativePath = relativePath;
        }


        public async Task ReconnectAsync()
        {
            if (MediaStreamSource == null) throw new InvalidOperationException();

            metadataPos = 0;

            try
            {
                socketWriter.Dispose();
                socketReader.Dispose();
                socket.Dispose();
            }
            catch (Exception) { }

            connected = false;

            socket = new StreamSocket();

            await ConnectAsync();
        }
        public async Task<bool> ConnectAsync()
        {
            var response = await EstablishConnectionAsync();

            connected = response.Item1;
            if (connected == false) return false;

            AudioEncodingProperties obtainedProperties = await GetEncodingPropertiesAsync(response.Item2);

            MediaStreamSource = new Windows.Media.Core.MediaStreamSource(new AudioStreamDescriptor(obtainedProperties));

            MediaStreamSource.SampleRequested += MediaStreamSource_SampleRequested;
            MediaStreamSource.CanSeek = false;
            MediaStreamSource.Starting += MediaStreamSource_Starting;
            MediaStreamSource.Closed += MediaStreamSource_Closed;

            connected = true;

            return connected;

        }

        private async Task<AudioEncodingProperties> GetEncodingPropertiesAsync(KeyValuePair<string, string>[] headers)
        {
            //if this happens to be an Icecast 2 server, it'll send the audio information for us.
            if (headers.Any(x => x.Key.ToLower() == "ice-audio-info"))
            {
                //looks like it is indeed an Icecast 2 server. lets strip out the data and be on our way.

                serverType = ShoutcastServerType.Icecast;

                /* example: ice-audio-info: ice-bitrate=32;ice-samplerate=32000;ice-channels=2
                 * "Note that unlike SHOUTcast, it is not necessary to parse ADTS audio frames to obtain the Audio Sample Rate."
                 * from: http://www.indexcom.com/streaming/player/Icecast2.html
                 */

                string headerValue = headers.First(x => x.Key.ToLower() == "ice-audio-info").Value;

                //split the properties and values and parsed them into a usable object.
                KeyValuePair<string, string>[] propertiesAndValues = headerValue.Split(';')
                    .Select(x => new KeyValuePair<string, string>(
                        x.Substring(0, x.IndexOf("=")).ToLower().Trim(),
                        x.Substring(x.IndexOf("=") + 1))).ToArray();

                //grab each value that we need.

                if (AudioInfo.BitRate == 0) //usually this is sent in the regular headers. grab it if it isn't.
                    AudioInfo.BitRate = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-bitrate" || x.Key == "bitrate").Value);


                if (propertiesAndValues.Any(x => x.Key == "ice-channels" || x.Key == "channels") && propertiesAndValues.Any(x => x.Key == "ice-samplerate" || x.Key == "samplerate"))
                {
                    AudioInfo.ChannelCount = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-channels" || x.Key == "channels").Value);
                    AudioInfo.SampleRate = uint.Parse(propertiesAndValues.First(x => x.Key == "ice-samplerate" || x.Key == "samplerate").Value);

                    //now just create the appropriate AudioEncodingProperties object.
                    switch (AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            return AudioEncodingProperties.CreateMp3(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                        case StreamAudioFormat.AAC:
                        case StreamAudioFormat.AAC_ADTS:
                            return AudioEncodingProperties.CreateAacAdts(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                    }
                }
                else
                {
                    //something is missing from audio-info so we need to fallback.

                    return await ParseEncodingFromMediaAsync();
                }
            }
            else
            {
                return await ParseEncodingFromMediaAsync();
            }

            return null;
        }

        private async Task<AudioEncodingProperties> ParseEncodingFromMediaAsync()
        {
            //grab the first frame and strip it for information

            AudioEncodingProperties obtainedProperties = null;
            IBuffer buffer = null;

            switch (AudioInfo.AudioFormat)
            {
                case StreamAudioFormat.MP3:
                    {
                        //load the first byte
                        await socketReader.LoadAsync(1);
                        byte lastByte = socketReader.ReadByte();
                        byteOffset += 1;
                        metadataPos += 1;

                        while (true) //wait for frame sync
                        {
                            await socketReader.LoadAsync(1);
                            var curByte = socketReader.ReadByte();

                            if (MP3Parser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                            {
                                byteOffset += 1;
                                metadataPos += 1;

                                byte[] header = new byte[MP3Parser.HeaderLength];
                                header[0] = lastByte;
                                header[1] = curByte;

                                await socketReader.LoadAsync(2);
                                header[2] = socketReader.ReadByte();
                                header[3] = socketReader.ReadByte();
                                byteOffset += 2;
                                metadataPos += 2;

                                AudioInfo.SampleRate = (uint)MP3Parser.GetSampleRate(header);

                                AudioInfo.ChannelCount = (uint)MP3Parser.GetChannelCount(header);

                                AudioInfo.BitRate = (uint)MP3Parser.GetBitRate(header);

                                if (AudioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");
                                break;
                            }
                            else
                            {
                                byteOffset += 1;
                                metadataPos += 1;
                                lastByte = curByte;
                            }
                        }

                        //skip the entire first frame/sample to get back on track
                        await socketReader.LoadAsync(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                        buffer = socketReader.ReadBuffer(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                        byteOffset += MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength;


                        obtainedProperties = AudioEncodingProperties.CreateMp3((uint)AudioInfo.SampleRate, (uint)AudioInfo.ChannelCount, AudioInfo.BitRate);

                        break;
                    }
                case StreamAudioFormat.AAC:
                    {
                        //obtainedProperties = AudioEncodingProperties.CreateAac(0, 2, 0);
                        throw new Exception("Not supported.");
                    }
                case StreamAudioFormat.AAC_ADTS:
                    {
                        //load the first byte
                        await socketReader.LoadAsync(1);
                        byte lastByte = socketReader.ReadByte();
                        byteOffset += 1;
                        metadataPos += 1;

                        while (true) //wait for frame sync
                        {
                            await socketReader.LoadAsync(1);
                            var curByte = socketReader.ReadByte();

                            if (AAC_ADTSParser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                            {
                                byteOffset += 1;
                                metadataPos += 1;

                                byte[] header = new byte[AAC_ADTSParser.HeaderLength];
                                header[0] = lastByte;
                                header[1] = curByte;

                                await socketReader.LoadAsync(5);
                                header[2] = socketReader.ReadByte();
                                header[3] = socketReader.ReadByte();
                                header[4] = socketReader.ReadByte();
                                header[5] = socketReader.ReadByte();
                                header[6] = socketReader.ReadByte();
                                byteOffset += 5;
                                metadataPos += 5;

                                //todo deal with CRC

                                AudioInfo.SampleRate = (uint)AAC_ADTSParser.GetSampleRate(header);

                                AudioInfo.ChannelCount = (uint)AAC_ADTSParser.GetChannelCount(header);

                                //bitrate gets sent by the server.
                                //bitRate = (uint)AAC_ADTSParser.GetBitRate(header);

                                if (AudioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");

                                //skip the entire first frame/sample to get back on track
                                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                                byteOffset += AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength;

                                obtainedProperties = AudioEncodingProperties.CreateAacAdts((uint)AudioInfo.SampleRate, (uint)AudioInfo.ChannelCount, AudioInfo.BitRate);

                                break;
                            }
                            else
                            {
                                byteOffset += 1;
                                metadataPos += 1;
                                lastByte = curByte;
                            }
                        }
                    }
                    break;
                default:
                    break;
            }

            metadataPos += buffer.Length; //very important or it will throw everything off!

            return obtainedProperties;
        }

        private void MediaStreamSource_Closed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
        {
            //todo fire an event?
            Disconnect();
        }

        private void MediaStreamSource_Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            //todo fire an event?
        }

        private async Task<Tuple<bool, KeyValuePair<string, string>[]>> EstablishConnectionAsync()
        {
            //http://www.smackfu.com/stuff/programming/shoutcast.html
            try
            {
                await socket.ConnectAsync(new Windows.Networking.HostName(streamUrl.Host), streamUrl.Port.ToString());

                socketWriter = new DataWriter(socket.OutputStream);
                socketReader = new DataReader(socket.InputStream);
            }
            catch (Exception ex)
            {
                if (MediaStreamSource != null)
                    MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.FailedToConnectToServer);
                else
                    throw new Exception("Connection Error", ex);

                return new Tuple<bool, KeyValuePair<string, string>[]>(false, null);
            }

            //todo figure out how to resolve http requests better to get rid of this hack.
            String httpPath = "";

            if (streamUrl.Host.Contains("radionomy.com") || serverType == ShoutcastServerType.Radionomy)
            {
                httpPath = streamUrl.LocalPath;
                serverType = ShoutcastServerType.Radionomy;
            }
            else
            {
                httpPath = "/" + relativePath;
            }

            socketWriter.WriteString("GET " + httpPath + " HTTP/1.1" + Environment.NewLine);

            if (ShouldGetMetadata)
                socketWriter.WriteString("Icy-MetaData: 1" + Environment.NewLine);

            socketWriter.WriteString("Host: " + streamUrl.Host + (streamUrl.Port != 80 ? ":" + streamUrl.Port : "") + Environment.NewLine);
            socketWriter.WriteString("Connection: Keep-Alive" + Environment.NewLine);
            socketWriter.WriteString("User-Agent: " + (UserAgent ?? "Shoutcast Player (http://github.com/Amrykid/UWPShoutcastMSS)") + Environment.NewLine);
            socketWriter.WriteString(Environment.NewLine);
            await socketWriter.StoreAsync();
            await socketWriter.FlushAsync();

            string response = string.Empty;
            while (!response.EndsWith(Environment.NewLine + Environment.NewLine))
            {
                await socketReader.LoadAsync(1);
                response += socketReader.ReadString(1);
            }

            //todo support http 2.0. maybe usage of the http client would solve this.
            if (response.StartsWith("HTTP/1.0 200 OK") || response.StartsWith("HTTP/1.1 200 OK") || response.StartsWith("ICY 200"))
            {
                var headers = ParseResponse(response);

                return new Tuple<bool, KeyValuePair<string, string>[]>(true, headers);
            }
            else
            {
                //wasn't successful. handle each case accordingly.

                if (response.StartsWith("HTTP /1.0 302") || response.StartsWith("HTTP/1.1 302"))
                {
                    socketReader.Dispose();
                    socketWriter.Dispose();
                    socket.Dispose();

                    var parsedResponse = ParseHttpResponseToKeyPairArray(response.Split(new string[] { "\r\n" }, StringSplitOptions.None).Skip(1).ToArray());

                    socket = new StreamSocket();
                    streamUrl = new Uri(parsedResponse.First(x => x.Key.ToLower() == "location").Value);

                    return await EstablishConnectionAsync();
                }
                else if (response.StartsWith("HTTP/1.0 404"))
                {
                    throw new Exception("Station is unavailable.");
                }
                else if (response.StartsWith("ICY 401")) //ICY 401 Service Unavailable
                {
                    if (MediaStreamSource != null)
                        MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.FailedToConnectToServer);
                    else
                        throw new Exception("Station is unavailable at this time. Maybe they're down for maintainence?");

                    return new Tuple<bool, KeyValuePair<string, string>[]>(false, null);
                }
                else if (response.StartsWith("HTTP/1.1 503")) //HTTP/1.1 503 Server limit reached
                {
                    throw new Exception("Station is unavailable at this time. The maximum amount of listeners has been reached.");
                }
            }

            return new Tuple<bool, KeyValuePair<string, string>[]>(false, null); //not connected and no headers.
        }

        private KeyValuePair<string, string>[] ParseResponse(string response)
        {
            string[] responseSplitByLine = response.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            KeyValuePair<string, string>[] headers = ParseHttpResponseToKeyPairArray(responseSplitByLine);

            StationInfo.StationName = headers.First(x => x.Key == "ICY-NAME").Value;
            StationInfo.StationGenre = headers.First(x => x.Key == "ICY-GENRE").Value;

            if (headers.Any(x => x.Key.ToUpper() == "ICY-DESCRIPTION"))
                StationInfo.StationDescription = headers.First(x => x.Key.ToUpper() == "ICY-DESCRIPTION").Value;

            if (StationInfoChanged != null)
                StationInfoChanged(this, EventArgs.Empty);

            AudioInfo.BitRate = uint.Parse(headers.FirstOrDefault(x => x.Key == "ICY-BR").Value);
            metadataInt = uint.Parse(headers.First(x => x.Key == "ICY-METAINT").Value);

            switch (headers.First(x => x.Key == "CONTENT-TYPE").Value.ToLower().Trim())
            {
                case "audio/mpeg":
                    AudioInfo.AudioFormat = StreamAudioFormat.MP3;
                    break;
                case "audio/aac":
                    AudioInfo.AudioFormat = StreamAudioFormat.AAC;
                    break;
                case "audio/aacp":
                    AudioInfo.AudioFormat = StreamAudioFormat.AAC_ADTS;
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

        private static bool IsInternetConnected()
        {
            ConnectionProfile connections = NetworkInformation.GetInternetConnectionProfile();
            bool internet = (connections != null) &&
                (connections.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
            return internet;
        }

        private async void MediaStreamSource_SampleRequested(Windows.Media.Core.MediaStreamSource sender, Windows.Media.Core.MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;

            if (!IsInternetConnected() || !connected)
            {
                connected = false;
                Disconnect();
                sender.NotifyError(MediaStreamSourceErrorStatus.ConnectionToServerLost);
                return;
            }


            var deferral = request.GetDeferral();

            try
            {
                MediaStreamSample sample = null;
                uint sampleLength = 0;

                //request.ReportSampleProgress(25);

                //if metadataPos is less than mp3_sampleSize away from metadataInt
                if (metadataInt - metadataPos <= (AudioInfo.AudioFormat == StreamAudioFormat.MP3 ? MP3Parser.mp3_sampleSize : AAC_ADTSParser.aac_adts_sampleSize) && metadataInt - metadataPos > 0)
                {
                    //parse part of the frame.

                    byte[] partialFrame = new byte[metadataInt - metadataPos];

                    var read = await socketReader.LoadAsync(metadataInt - metadataPos);

                    if (read == 0)
                    {
                        Disconnect();
                        MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.ConnectionToServerLost);
                        return;
                    }

                    socketReader.ReadBytes(partialFrame);

                    metadataPos += metadataInt - metadataPos;

                    switch (AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseMP3SampleAsync(partial: true, partialBytes: partialFrame);
                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                        case StreamAudioFormat.AAC_ADTS:
                        case StreamAudioFormat.AAC:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseAACSampleAsync(partial: true, partialBytes: partialFrame);
                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                    }
                }
                else
                {
                    await HandleMetadata();

                    //request.ReportSampleProgress(50);

                    switch (AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                //mp3
                                Tuple<MediaStreamSample, uint> result = await ParseMP3SampleAsync();
                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                        case StreamAudioFormat.AAC_ADTS:
                        case StreamAudioFormat.AAC:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseAACSampleAsync();
                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                    }

                    try
                    {
                        if (sample == null || sampleLength == 0) //OLD bug: on RELEASE builds, sample.Buffer causes the app to die due to a possible .NET Native bug
                        {
                            MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.DecodeError);
                            deferral.Complete();
                            return;
                        }
                        else
                            metadataPos += sampleLength;
                    }
                    catch (Exception) { }
                }

                if (sample != null)
                    request.Sample = sample;

                //request.ReportSampleProgress(100);
            }
            catch (Exception)
            {
                MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.Other);
            }

            deferral.Complete();
        }

        private async Task HandleMetadata()
        {
            if (metadataPos == metadataInt)
            {
                metadataPos = 0;

                await socketReader.LoadAsync(1);
                uint metaInt = socketReader.ReadByte();

                if (metaInt > 0)
                {
                    uint metaDataInfo = metaInt * 16;

                    await socketReader.LoadAsync((uint)metaDataInfo);

                    var metadata = socketReader.ReadString((uint)metaDataInfo);

                    if (ShouldGetMetadata)
                        ParseSongMetadata(metadata);
                }

                byteOffset = 0;
            }
        }

        private void ParseSongMetadata(string metadata)
        {
            string[] semiColonSplit = metadata.Split(';');
            var headers = semiColonSplit.Where(line => line.Contains("=")).Select(line =>
            {
                string header = line.Substring(0, line.IndexOf("="));
                string value = line.Substring(line.IndexOf("=") + 1);

                var pair = new KeyValuePair<string, string>(header.ToUpper(), value.Trim('\'').Trim());

                return pair;
            }).ToArray();

            string track = "", artist = "";
            string songInfo = headers.First(x => x.Key == "STREAMTITLE").Value;

            if (songInfo.Split(new string[] { " - " }, StringSplitOptions.None).Count() >= 2)
            {
                artist = songInfo.Split(new string[] { " - " }, StringSplitOptions.None)[0].Trim();
                track = songInfo.Split(new string[] { " - " }, StringSplitOptions.None)[1].Trim();

                MediaStreamSource.MusicProperties.Title = track;
                MediaStreamSource.MusicProperties.Artist = artist;
            }
            else
            {
                track = songInfo.Trim();
                artist = "Unknown";
            }

            if (MetadataChanged != null)
            {
                MetadataChanged(this, new ShoutcastMediaSourceStreamMetadataChangedEventArgs()
                {
                    Title = track,
                    Artist = artist
                });
            }
        }

        private async Task<Tuple<MediaStreamSample, uint>> ParseMP3SampleAsync(bool partial = false, byte[] partialBytes = null)
        {
            //http://www.mpgedit.org/mpgedit/mpeg_format/MP3Format.html

            IBuffer buffer = null;
            MediaStreamSample sample = null;
            uint sampleLength = 0;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                sampleLength = MP3Parser.mp3_sampleSize - (uint)partialBytes.Length;
                byteOffset += sampleLength;
            }
            else
            {
                var read = await socketReader.LoadAsync(MP3Parser.mp3_sampleSize);

                if (read == 0)
                {
                    Disconnect();
                    MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.ConnectionToServerLost);
                    return new Tuple<MediaStreamSample, uint>(null, 0);
                }
                else if (read < MP3Parser.mp3_sampleSize)
                {
                    buffer = socketReader.ReadBuffer(read);

                    byteOffset += MP3Parser.mp3_sampleSize;
                }
                else
                {
                    buffer = socketReader.ReadBuffer(MP3Parser.mp3_sampleSize);

                    byteOffset += MP3Parser.mp3_sampleSize;
                }

                sampleLength = MP3Parser.mp3_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, timeOffSet);
            sample.Duration = MP3Parser.mp3_sampleDuration;
            sample.KeyFrame = true;

            timeOffSet = timeOffSet.Add(MP3Parser.mp3_sampleDuration);


            return new Tuple<MediaStreamSample, uint>(sample, sampleLength);
        }

        private async Task<Tuple<MediaStreamSample, uint>> ParseAACSampleAsync(bool partial = false, byte[] partialBytes = null)
        {
            IBuffer buffer = null;
            MediaStreamSample sample = null;
            uint sampleLength = 0;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize - (uint)partialBytes.Length;
                byteOffset += sampleLength;
            }
            else
            {
                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize);
                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize);

                byteOffset += AAC_ADTSParser.aac_adts_sampleSize;
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, timeOffSet);
            sample.Duration = AAC_ADTSParser.aac_adts_sampleDuration;
            sample.KeyFrame = true;

            timeOffSet = timeOffSet.Add(AAC_ADTSParser.aac_adts_sampleDuration);


            return new Tuple<MediaStreamSample, uint>(sample, sampleLength);
        }

        public event EventHandler StationInfoChanged;
        public event EventHandler<ShoutcastMediaSourceStreamMetadataChangedEventArgs> MetadataChanged;
    }
}
