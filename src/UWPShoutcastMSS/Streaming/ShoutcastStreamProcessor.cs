using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using UWPShoutcastMSS.Streaming.Providers;
using Windows.Media.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming
{
    internal class ShoutcastStreamProcessor
    {
        private ShoutcastStream shoutcastStream;
        private StreamSocket socket;
        private DataReader socketReader;
        private DataWriter socketWriter;
        private Task processingTask = null;
        private CancellationTokenSource processingTaskCancel = null;
        private volatile bool isRunning = false;
        internal TimeSpan timeOffSet = new TimeSpan();
        internal uint metadataPos;
        internal uint byteOffset;
        private ConcurrentQueue<MediaStreamSample> sampleQueue = null;
        private IAudioProvider sampleProvider = null;

        public ShoutcastStreamProcessor(ShoutcastStream shoutcastStream, StreamSocket socket, DataReader socketReader, DataWriter socketWriter)
        {
            this.shoutcastStream = shoutcastStream;
            this.socket = socket;
            this.socketReader = socketReader;
            this.socketWriter = socketWriter;

            sampleQueue = new ConcurrentQueue<MediaStreamSample>();
        }

        internal void StartProcessing()
        {
            if (isRunning) throw new InvalidOperationException();

            processingTaskCancel = new CancellationTokenSource();
            processingTask = new Task(BufferAndSampleStream, processingTaskCancel.Token);

            processingTask.Start();
        }

        internal void StopProcessing()
        {
            if (!isRunning) return;

            processingTaskCancel.Cancel();

            processingTask.Wait();

            processingTaskCancel.Dispose();

            isRunning = false;
        }

        private async void BufferAndSampleStream()
        {
            isRunning = true;

            sampleProvider = AudioProviderFactory.GetAudioProvider(shoutcastStream.AudioInfo.AudioFormat);

            //todo check for internet connection and socket connection as well
            while (!processingTaskCancel.IsCancellationRequested)
            {
                MediaStreamSample sample = null;
                uint sampleLength = 0;

                if (processingTaskCancel.IsCancellationRequested) return;

                //if metadataPos is less than mp3_sampleSize away from metadataInt
                if (shoutcastStream.metadataInt - metadataPos <= sampleProvider.GetSampleSize()
                    && shoutcastStream.metadataInt - metadataPos > 0)
                {
                    //parse part of the frame.

                    byte[] partialFrame = new byte[shoutcastStream.metadataInt - metadataPos];

                    var read = await socketReader.LoadAsync(shoutcastStream.metadataInt - metadataPos);

                    socketReader.ReadBytes(partialFrame);

                    metadataPos += shoutcastStream.metadataInt - metadataPos;


                    Tuple<MediaStreamSample, uint> result = await sampleProvider.ParseSampleAsync(this, socketReader, partial: true, partialBytes: partialFrame);

                    if (processingTaskCancel.IsCancellationRequested) return;

                    sample = result.Item1;
                    sampleLength = result.Item2;
                }
                else
                {
                    await HandleMetadata();

                    Tuple<MediaStreamSample, uint> result = await sampleProvider.ParseSampleAsync(this, socketReader);

                    if (processingTaskCancel.IsCancellationRequested) return;

                    sample = result.Item1;
                    sampleLength = result.Item2;


                    if (sample == null || sampleLength == 0) //OLD bug: on RELEASE builds, sample.Buffer causes the app to die due to a possible .NET Native bug
                    {
                        //MediaStreamSource.NotifyError(MediaStreamSourceErrorStatus.DecodeError);
                        //deferral.Complete();
                        //return;
                        continue;
                    }
                    else
                    {
                        metadataPos += sampleLength;
                    }
                }

                sampleQueue.Enqueue(sample);
            }

            isRunning = false;
        }

        private async Task HandleMetadata()
        {
            if (metadataPos == shoutcastStream.metadataInt)
            {
                metadataPos = 0;

                if (processingTaskCancel.IsCancellationRequested) return;

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

        internal MediaStreamSample GetNextSample()
        {
            MediaStreamSample sample = null;
            bool result = sampleQueue.TryDequeue(out sample);

            return sample;
        }
    }
}