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

namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastMediaSourceManager
    {
        public enum StreamAudioFormat
        {
            ///"audio/mpeg";
            MP3,
            AAC
        }

        public Windows.Media.Core.MediaStreamSource MediaStreamSource { get; private set; }
        public ShoutcastStationInfo StationInfo { get; private set; }

        StreamSocket socket = null;
        DataWriter socketWriter = null;
        DataReader socketReader = null;

        Uri streamUrl = null;

        uint bitRate = 0;
        uint metadataInt = 0;
        uint metadataPos = 0;

        StreamAudioFormat contentType = StreamAudioFormat.MP3;

        public void Disconnect()
        {
            try
            {
                MediaStreamSource.SampleRequested -= MediaStreamSource_SampleRequested;
                MediaStreamSource.Starting -= MediaStreamSource_Starting;
                MediaStreamSource.Closed -= MediaStreamSource_Closed;
            }
            catch (Exception) { }

            socketWriter.Dispose();
            socketReader.Dispose();
            socket.Dispose();
        }

        //http://stackoverflow.com/questions/6294807/calculate-mpeg-frame-length-ms

        TimeSpan timeOffSet = new TimeSpan();
        private UInt64 byteOffset;

        #region MP3 Framesize and length for Layer II and Layer III - https://code.msdn.microsoft.com/windowsapps/MediaStreamSource-media-dfd55dff/sourcecode?fileId=111712&pathId=208523738

        UInt32 mp3_sampleSize = 1152;
        TimeSpan mp3_sampleDuration = new TimeSpan(0, 0, 0, 0, 70);
        #endregion

        //TODO
        UInt32 aac_sampleSize = 16;
        TimeSpan aac_sampleDuration = new TimeSpan(0, 0, 0, 0, 70);

        public ShoutcastMediaSourceManager(Uri url)
        {
            StationInfo = new ShoutcastStationInfo();

            streamUrl = url;

            socket = new StreamSocket();
        }

        public async Task ConnectAsync(uint sampleRate = 44100, string relativePath = ";")
        {
            await HandleConnection(relativePath);
            //AudioEncodingProperties obtainedProperties = await GetEncodingPropertiesAsync();

            switch (contentType)
            {
                case StreamAudioFormat.MP3:
                    {
                        MediaStreamSource = new Windows.Media.Core.MediaStreamSource(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(sampleRate, 2, (uint)bitRate)));
                        //MediaStreamSource.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(48000, 2, (uint)bitRate)));
                        //MediaStreamSource.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(32000, 2, (uint)bitRate)));
                        //MediaStreamSource.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(24000, 2, (uint)bitRate)));
                        //MediaStreamSource.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(22050, 2, (uint)bitRate)));
                    }
                    break;
                case StreamAudioFormat.AAC:
                    {
                        MediaStreamSource = new MediaStreamSource(new AudioStreamDescriptor(AudioEncodingProperties.CreateAac(sampleRate, 2, (uint)bitRate)));
                    }
                    break;
            }

            MediaStreamSource.SampleRequested += MediaStreamSource_SampleRequested;
            MediaStreamSource.CanSeek = false;
            MediaStreamSource.Starting += MediaStreamSource_Starting;
            MediaStreamSource.Closed += MediaStreamSource_Closed;
        }

        private async Task<AudioEncodingProperties> GetEncodingPropertiesAsync()
        {
            //grab the first frame and strip it for information

            AudioEncodingProperties obtainedProperties = null;
            IBuffer buffer = null;
            int sampleRate = 0;

            switch (contentType)
            {
                case StreamAudioFormat.MP3:
                    {
                        await socketReader.LoadAsync(mp3_sampleSize);
                        buffer = socketReader.ReadBuffer(mp3_sampleSize);
                        byteOffset += mp3_sampleSize;

                        byte[] bytesHeader = buffer.ToArray(0, 5); //first four bytes

                        #region Modified version of http://sahanganepola.blogspot.com/2010/07/c-class-to-get-mp3-header-details.html
                        //I don't like copying code without understanding it but this is a case where i dont fully understand everything going on.
                        //I need to read up on bitmasking and such.

                        var bithdr = (ulong)(((bytesHeader[0] & 255) << 24) | ((bytesHeader[1] & 255) << 16) | ((bytesHeader[2] & 255) << 8) | ((bytesHeader[3] & 255)));

                        var bitrateIndex = (int)((bithdr >> 12) & 15);
                        var versionIndex = (int)((bithdr >> 19) & 3);

                        var frequencyIndex = (int)((bithdr >> 10) & 3); //sampleRate

                        int[,] frequencyTable =    {
                             {32000, 16000,  8000}, // MPEG 2.5
                             {    0,     0,     0}, // reserved
                             {22050, 24000, 16000}, // MPEG 2
                             {44100, 48000, 32000}  // MPEG 1
                         };
                        #endregion

                        sampleRate = frequencyTable[versionIndex, frequencyIndex];

                        obtainedProperties = AudioEncodingProperties.CreateMp3((uint)sampleRate, 2, bitRate);
                        break;
                    }
                case StreamAudioFormat.AAC:
                    {
                        obtainedProperties = AudioEncodingProperties.CreateAac(0, 2, 0);
                        throw new Exception();
                    }
                default:
                    break;
            }

            metadataPos += buffer.Length; //very important or it will throw everything off!

            return obtainedProperties;
        }

        private void MediaStreamSource_Closed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
        {
            MediaStreamSource.Starting -= MediaStreamSource_Starting;
            MediaStreamSource.Closed -= MediaStreamSource_Closed;
            MediaStreamSource.SampleRequested -= MediaStreamSource_SampleRequested;
        }

        private void MediaStreamSource_Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            //args.Request.SetActualStartPosition(timeOffSet);
            //args.Request.
        }

        private async Task HandleConnection(string relativePath)
        {
            //http://www.smackfu.com/stuff/programming/shoutcast.html
            try
            {
                await socket.ConnectAsync(new Windows.Networking.HostName(streamUrl.Host), streamUrl.Port.ToString());

                socketWriter = new DataWriter(socket.OutputStream);
                socketReader = new DataReader(socket.InputStream);

                socketWriter.WriteString("GET /" + relativePath + " HTTP/1.1" + Environment.NewLine);
                socketWriter.WriteString("Icy-MetaData: 1" + Environment.NewLine);
                socketWriter.WriteString("User-Agent: Test Audio Player" + Environment.NewLine);
                socketWriter.WriteString(Environment.NewLine);
                await socketWriter.StoreAsync();
                await socketWriter.FlushAsync();

                string response = string.Empty;
                while (!response.EndsWith(Environment.NewLine + Environment.NewLine))
                {
                    await socketReader.LoadAsync(1);
                    response += socketReader.ReadString(1);
                }

                ParseResponse(response);
            }
            catch (Exception ex)
            {
                MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.FailedToConnectToServer);
            }
        }

        private void ParseResponse(string response)
        {
            string[] responseSplitByLine = response.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            var headers = responseSplitByLine.Where(line => line.Contains(":")).Select(line =>
            {
                string header = line.Substring(0, line.IndexOf(":"));
                string value = line.Substring(line.IndexOf(":") + 1);

                var pair = new KeyValuePair<string, string>(header.ToUpper(), value);

                return pair;
            }).ToArray();

            StationInfo.StationName = headers.First(x => x.Key == "ICY-NAME").Value;
            StationInfo.StationGenre = headers.First(x => x.Key == "ICY-GENRE").Value;

            bitRate = uint.Parse(headers.FirstOrDefault(x => x.Key == "ICY-BR").Value);
            metadataInt = uint.Parse(headers.First(x => x.Key == "ICY-METAINT").Value);
            contentType = headers.First(x => x.Key == "CONTENT-TYPE").Value.ToUpper().Trim() == "AUDIO/MPEG" ? StreamAudioFormat.MP3 : StreamAudioFormat.AAC;
        }

        private async void MediaStreamSource_SampleRequested(Windows.Media.Core.MediaStreamSource sender, Windows.Media.Core.MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;
            var deferral = request.GetDeferral();

            try
            {
                MediaStreamSample sample = null;

                request.ReportSampleProgress(25);

                //if metadataPos is less than mp3_sampleSize away from metadataInt
                if (metadataInt - metadataPos <= (contentType == StreamAudioFormat.MP3 ? mp3_sampleSize : aac_sampleSize) && metadataInt - metadataPos > 0)
                {
                    //parse part of the frame.

                    byte[] partialFrame = new byte[metadataInt - metadataPos];

                    await socketReader.LoadAsync(metadataInt - metadataPos);
                    socketReader.ReadBytes(partialFrame);

                    metadataPos += metadataInt - metadataPos;

                    switch (contentType)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                sample = await ParseMP3SampleAsync(partial: true, partialBytes: partialFrame);
                            }
                            break;
                        case StreamAudioFormat.AAC:
                            {
                                sample = await ParseAACSampleAsync(partial: true, partialBytes: partialFrame);
                            }
                            break;
                    }
                }
                else
                {
                    await HandleMetadata();

                    request.ReportSampleProgress(50);

                    switch (contentType)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                //mp3
                                sample = await ParseMP3SampleAsync();
                                //await MediaStreamSample.CreateFromStreamAsync(socket.InputStream, bitRate, new TimeSpan(0, 0, 1));
                            }
                            break;
                        case StreamAudioFormat.AAC:
                            {
                                sample = await ParseAACSampleAsync();
                            }
                            break;
                    }

                    metadataPos += sample.Buffer.Length;
                }

                request.Sample = sample;

                request.ReportSampleProgress(100);
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

            if (songInfo.Split('-').Count() >= 2)
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
                MetadataChanged(this, new ShoutcastMediaSourceManagerMetadataChangedEventArgs()
                {
                    Title = track,
                    Artist = artist
                });
            }
        }

        private async Task<MediaStreamSample> ParseMP3SampleAsync(bool partial = false, byte[] partialBytes = null)
        {
            //http://www.mpgedit.org/mpgedit/mpeg_format/MP3Format.html


            //uint frameHeaderCount = 32;
            //await socketReader.LoadAsync(frameHeaderCount);

            //byte[] frameHeader = new byte[4];
            //socketReader.ReadBytes(frameHeader);
            //BitArray frameHeaderArray = new BitArray(frameHeader);

            //string audioVersionID = frameHeader[1].GetBit( <<  + char.ConvertFromUtf32(frameHeaderArray.Get(19));

            //var db = audioVersionID;

            IBuffer buffer = null;
            MediaStreamSample sample = null;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                byteOffset += mp3_sampleSize - (ulong)partialBytes.Length;
            }
            else
            {
                await socketReader.LoadAsync(mp3_sampleSize);
                buffer = socketReader.ReadBuffer(mp3_sampleSize);

                byteOffset += mp3_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, timeOffSet);
            sample.Duration = mp3_sampleDuration;
            sample.KeyFrame = true;

            timeOffSet = timeOffSet.Add(mp3_sampleDuration);


            return sample;

            //return null;
        }

        private async Task<MediaStreamSample> ParseAACSampleAsync(bool partial = false, byte[] partialBytes = null)
        {
            
            IBuffer buffer = null;
            MediaStreamSample sample = null;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                byteOffset += aac_sampleSize - (ulong)partialBytes.Length;
            }
            else
            {
                await socketReader.LoadAsync(aac_sampleSize);
                buffer = socketReader.ReadBuffer(aac_sampleSize);

                byteOffset += aac_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, timeOffSet);
            sample.Duration = aac_sampleDuration;
            sample.KeyFrame = true;

            timeOffSet = timeOffSet.Add(aac_sampleDuration);


            return sample;
        }

        public event EventHandler<ShoutcastMediaSourceManagerMetadataChangedEventArgs> MetadataChanged;
    }
}
