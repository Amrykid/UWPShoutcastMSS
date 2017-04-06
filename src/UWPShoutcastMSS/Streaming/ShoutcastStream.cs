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
    public class ShoutcastStream
    {
        public ServerAudioInfo AudioInfo { get; internal set; }
        public ServerStationInfo StationInfo { get; internal set; }
        public MediaStreamSource MediaStreamSource { get; private set; }

        public event EventHandler<ShoutcastMediaSourceStreamMetadataChangedEventArgs> MetadataChanged;

        private TimeSpan timeOffSet = new TimeSpan();
        internal uint metadataInt = 100;
        private StreamSocket socket;
        private DataReader socketReader;
        private DataWriter socketWriter;
        private ShoutcastServerType serverType;
        private uint metadataPos;
        private uint byteOffset;
        private AudioEncodingProperties audioProperties = null;

        internal ShoutcastStream(StreamSocket socket, DataReader socketReader, DataWriter socketWriter)
        {
            this.socket = socket;
            this.socketReader = socketReader;
            this.socketWriter = socketWriter;

            StationInfo = new ServerStationInfo();
            AudioInfo = new ServerAudioInfo();
            //MediaStreamSource = new MediaStreamSource(null);
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

        private async void MediaStreamSource_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;

            var deferral = request.GetDeferral();

            MediaStreamSample sample = null;
            uint sampleLength = 0;

            //if metadataPos is less than mp3_sampleSize away from metadataInt
            if (metadataInt - metadataPos <= (AudioInfo.AudioFormat == StreamAudioFormat.MP3 ? MP3Parser.mp3_sampleSize : AAC_ADTSParser.aac_adts_sampleSize) 
                && metadataInt - metadataPos > 0)
            {
                //parse part of the frame.

                byte[] partialFrame = new byte[metadataInt - metadataPos];

                var read = await socketReader.LoadAsync(metadataInt - metadataPos);

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

                 if (read < MP3Parser.mp3_sampleSize)
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
    }
}