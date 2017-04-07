using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using Windows.Media.MediaProperties;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Media.Core;
using System.Runtime.InteropServices.WindowsRuntime;

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastStream : IDisposable
    {
        public ServerAudioInfo AudioInfo { get; internal set; }
        public ServerStationInfo StationInfo { get; internal set; }
        public MediaStreamSource MediaStreamSource { get; private set; }

        public event EventHandler<ShoutcastMediaSourceStreamMetadataChangedEventArgs> MetadataChanged;

        private ShoutcastStreamProcessor streamProcessor = null;
        internal uint metadataInt = 100;
        private StreamSocket socket;
        private DataReader socketReader;
        private DataWriter socketWriter;
        private ShoutcastServerType serverType;
        private AudioEncodingProperties audioProperties = null;

        internal ShoutcastStream(StreamSocket socket, DataReader socketReader, DataWriter socketWriter)
        {
            this.socket = socket;
            this.socketReader = socketReader;
            this.socketWriter = socketWriter;

            StationInfo = new ServerStationInfo();
            AudioInfo = new ServerAudioInfo();
            //MediaStreamSource = new MediaStreamSource(null);
            streamProcessor = new ShoutcastStreamProcessor(this, socket, socketReader, socketWriter);
        }

        private async Task<AudioEncodingProperties> DetectAudioEncodingPropertiesAsync(KeyValuePair<string, string>[] headers)
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
                            audioProperties = AudioEncodingProperties.CreateMp3(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                            break;
                        case StreamAudioFormat.AAC:
                        case StreamAudioFormat.AAC_ADTS:
                            audioProperties = AudioEncodingProperties.CreateAacAdts(AudioInfo.SampleRate, AudioInfo.ChannelCount, AudioInfo.BitRate);
                            break;
                    }
                }
                else
                {
                    //something is missing from audio-info so we need to fallback.

                    audioProperties = await ParseEncodingFromMediaAsync();
                }
            }
            else
            {
                audioProperties = await ParseEncodingFromMediaAsync();
            }

            return audioProperties;
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
                        streamProcessor.byteOffset += 1;
                        streamProcessor.metadataPos += 1;

                        while (true) //wait for frame sync
                        {
                            await socketReader.LoadAsync(1);
                            var curByte = socketReader.ReadByte();

                            if (MP3Parser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                            {
                                streamProcessor.byteOffset += 1;
                                streamProcessor.metadataPos += 1;

                                byte[] header = new byte[MP3Parser.HeaderLength];
                                header[0] = lastByte;
                                header[1] = curByte;

                                await socketReader.LoadAsync(2);
                                header[2] = socketReader.ReadByte();
                                header[3] = socketReader.ReadByte();
                                streamProcessor.byteOffset += 2;
                                streamProcessor.metadataPos += 2;

                                AudioInfo.SampleRate = (uint)MP3Parser.GetSampleRate(header);

                                AudioInfo.ChannelCount = (uint)MP3Parser.GetChannelCount(header);

                                AudioInfo.BitRate = (uint)MP3Parser.GetBitRate(header);

                                if (AudioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");
                                break;
                            }
                            else
                            {
                                streamProcessor.byteOffset += 1;
                                streamProcessor.metadataPos += 1;
                                lastByte = curByte;
                            }
                        }

                        //skip the entire first frame/sample to get back on track
                        await socketReader.LoadAsync(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                        buffer = socketReader.ReadBuffer(MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength);
                        streamProcessor.byteOffset += MP3Parser.mp3_sampleSize - MP3Parser.HeaderLength;


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
                        streamProcessor.byteOffset += 1;
                        streamProcessor.metadataPos += 1;

                        while (true) //wait for frame sync
                        {
                            await socketReader.LoadAsync(1);
                            var curByte = socketReader.ReadByte();

                            if (AAC_ADTSParser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                            {
                                streamProcessor.byteOffset += 1;
                                streamProcessor.metadataPos += 1;

                                byte[] header = new byte[AAC_ADTSParser.HeaderLength];
                                header[0] = lastByte;
                                header[1] = curByte;

                                await socketReader.LoadAsync(5);
                                header[2] = socketReader.ReadByte();
                                header[3] = socketReader.ReadByte();
                                header[4] = socketReader.ReadByte();
                                header[5] = socketReader.ReadByte();
                                header[6] = socketReader.ReadByte();
                                streamProcessor.byteOffset += 5;
                                streamProcessor.metadataPos += 5;

                                //todo deal with CRC

                                AudioInfo.SampleRate = (uint)AAC_ADTSParser.GetSampleRate(header);

                                AudioInfo.ChannelCount = (uint)AAC_ADTSParser.GetChannelCount(header);

                                //bitrate gets sent by the server.
                                //bitRate = (uint)AAC_ADTSParser.GetBitRate(header);

                                if (AudioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");

                                //skip the entire first frame/sample to get back on track
                                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength);
                                streamProcessor.byteOffset += AAC_ADTSParser.aac_adts_sampleSize - AAC_ADTSParser.HeaderLength;

                                obtainedProperties = AudioEncodingProperties.CreateAacAdts((uint)AudioInfo.SampleRate, (uint)AudioInfo.ChannelCount, AudioInfo.BitRate);

                                break;
                            }
                            else
                            {
                                streamProcessor.byteOffset += 1;
                                streamProcessor.metadataPos += 1;
                                lastByte = curByte;
                            }
                        }
                    }
                    break;
                default:
                    break;
            }

            streamProcessor.metadataPos += buffer.Length; //very important or it will throw everything off!

            return obtainedProperties;
        }

        internal void RaiseMetadataChangedEvent(ShoutcastMediaSourceStreamMetadataChangedEventArgs shoutcastMediaSourceStreamMetadataChangedEventArgs)
        {
            if (MetadataChanged != null)
            {
                MetadataChanged(this, shoutcastMediaSourceStreamMetadataChangedEventArgs);
            }
        }

        internal async Task HandleHeadersAsync(KeyValuePair<string, string>[] headers)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers), "Headers are null");
            if (headers.Length == 0) throw new ArgumentException(paramName: "headers", message: "Header count is 0");

            await DetectAudioEncodingPropertiesAsync(headers);

            if (audioProperties == null) throw new InvalidOperationException("Unable to detect audio encoding properties.");

            var audioStreamDescriptor = new AudioStreamDescriptor(audioProperties);
            MediaStreamSource = new MediaStreamSource(audioStreamDescriptor);
            MediaStreamSource.SampleRequested += MediaStreamSource_SampleRequested;
        }

        public void Disconnect()
        {
            MediaStreamSource.SampleRequested -= MediaStreamSource_SampleRequested;

            socketWriter.Dispose();
            socketReader.Dispose();

            socket.Dispose();
        }

        private async void MediaStreamSource_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;

            var deferral = request.GetDeferral();

            request.Sample = await streamProcessor.GetNextSampleAsync();

            deferral.Complete();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}