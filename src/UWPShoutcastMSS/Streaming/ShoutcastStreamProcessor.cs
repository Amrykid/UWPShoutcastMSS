using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using UWPShoutcastMSS.Streaming.Providers;
using UWPShoutcastMSS.Streaming.Sockets;
using Windows.Media.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming
{
    internal class ShoutcastStreamProcessor
    {
        private ShoutcastStream shoutcastStream;
        private SocketWrapper socket;
        private Task processingTask = null;
        private CancellationTokenSource processingTaskCancel = null;
        private volatile bool isRunning = false;
        internal TimeSpan timeOffSet = new TimeSpan();
        internal uint metadataPos;
        internal uint byteOffset;
        private ConcurrentQueue<MediaStreamSample> sampleQueue = null;
        private IAudioProvider sampleProvider = null;

        public ShoutcastStreamProcessor(ShoutcastStream shoutcastStream, SocketWrapper socket)
        {
            this.shoutcastStream = shoutcastStream;
            this.socket = socket;
        }

        private async Task HandleMetadataAsync(CancellationToken cancelToken)
        {
            if (!shoutcastStream.serverSettings.RequestSongMetdata) return;

            cancelToken.ThrowIfCancellationRequested();

            if (metadataPos == shoutcastStream.metadataInt)
            {
                metadataPos = 0;

                await socket.LoadAsync(1);

                if (socket.UnconsumedBufferLength > 0)
                {
                    uint metaInt = await socket.ReadByteAsync();

                    if (metaInt > 0)
                    {
                        try
                        {
                            uint metaDataInfo = metaInt * 16;

                            cancelToken.ThrowIfCancellationRequested();

                            await socket.LoadAsync((uint)metaDataInfo);

                            var metadata = await socket.ReadStringAsync((uint)metaDataInfo);

                            ParseSongMetadata(metadata);
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Error occurred while parsing metadata.", e);
                            //if (e is System.ArgumentOutOfRangeException || e is NullReferenceException)
                            //{
                            //    //No mapping for the Unicode character exists in the target multi-byte code page.

                            //    shoutcastStream.MediaStreamSource.MusicProperties.Title = "Unknown Song";
                            //    shoutcastStream.MediaStreamSource.MusicProperties.Artist = "Unknown Artist";

                            //    shoutcastStream.RaiseMetadataChangedEvent(new ShoutcastMediaSourceStreamMetadataChangedEventArgs()
                            //    {
                            //        Title = "Unknown Song",
                            //        Artist = "Unknown Artist"
                            //    });
                            //}
                        }
                    }

                    cancelToken.ThrowIfCancellationRequested();
                    
                    //byteOffset = 0;
                }
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

                shoutcastStream.MediaStreamSource.MusicProperties.Title = track;
                shoutcastStream.MediaStreamSource.MusicProperties.Artist = artist;
            }
            else
            {
                track = songInfo.Trim();
                artist = "Unknown";
            }

            shoutcastStream.RaiseMetadataChangedEvent(new ShoutcastMediaSourceStreamMetadataChangedEventArgs()
            {
                Title = track,
                Artist = artist
            });
        }

        internal async Task<MediaStreamSample> GetNextSampleAsync(CancellationToken cancelToken)
        {
            if (sampleProvider == null)
                sampleProvider = AudioProviderFactory.GetAudioProvider(shoutcastStream.AudioInfo.AudioFormat);

            //todo check for internet connection and socket connection as well

            cancelToken.ThrowIfCancellationRequested();

            MediaStreamSample sample = null;
            uint sampleLength = 0;


            //if metadataPos is less than sampleSize away from metadataInt
            if (shoutcastStream.serverSettings.RequestSongMetdata && (shoutcastStream.metadataInt - metadataPos <= sampleProvider.GetSampleSize()
                && shoutcastStream.metadataInt - metadataPos > 0))
            {
                //parse part of the frame.
                byte[] partialFrame = new byte[shoutcastStream.metadataInt - metadataPos];
                var read = await socket.LoadAsync(shoutcastStream.metadataInt - metadataPos);

                if (read == 0 || read < partialFrame.Length)
                {
                    //disconnected.
                    throw new ShoutcastDisconnectionException();
                }

                await socket.ReadBytesAsync(partialFrame);
                metadataPos += shoutcastStream.metadataInt - metadataPos;
                Tuple<MediaStreamSample, uint> result = await sampleProvider.ParseSampleAsync(this, socket, partial: true, partialBytes: partialFrame).ConfigureAwait(false);
                sample = result.Item1;
                sampleLength = result.Item2;

                cancelToken.ThrowIfCancellationRequested();
            }
            else
            {
                await HandleMetadataAsync(cancelToken);
                Tuple<MediaStreamSample, uint> result = await sampleProvider.ParseSampleAsync(this, socket).ConfigureAwait(false);
                sample = result.Item1;
                sampleLength = result.Item2;

                cancelToken.ThrowIfCancellationRequested();

                if (sample == null || sampleLength == 0) //OLD bug: on RELEASE builds, sample.Buffer causes the app to die due to a possible .NET Native bug
                {
                    //MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.DecodeError);
                    //deferral.Complete();
                    //return;
                    return null;
                }
                else
                {
                    if (shoutcastStream.serverSettings.RequestSongMetdata)
                        metadataPos += sampleLength;
                }
            }

            return sample;
        }

        internal async Task<byte> ReadByteFromSocketAsync()
        {
            return (await ReadBytesFromSocketAsync(1))[0];
        }

        internal async Task<byte[]> ReadBytesFromSocketAsync(uint count)
        {
            await socket.LoadAsync(count);

            byte[] result = new byte[count];

            for (int i = 0; i < count; i++)
            {
                result[i] = await socket.ReadByteAsync();
                //byteOffset += 1;
                if (shoutcastStream.serverSettings.RequestSongMetdata) metadataPos += 1;
            }

            return result;
        }
    }
}