using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
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
        private TimeSpan timeOffSet = new TimeSpan();
        internal uint metadataPos;
        internal uint byteOffset;
        private ConcurrentQueue<MediaStreamSample> sampleQueue = null;

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
            processingTaskCancel.Dispose();

            processingTask.Wait();

            isRunning = false;
        }

        private async void BufferAndSampleStream()
        {
            isRunning = true;

            //todo check for internet connection and socket connection as well
            while (!processingTaskCancel.IsCancellationRequested)
            {
                MediaStreamSample sample = null;
                uint sampleLength = 0;

                if (processingTaskCancel.IsCancellationRequested) return;

                //if metadataPos is less than mp3_sampleSize away from metadataInt
                if (shoutcastStream.metadataInt - metadataPos <=
                    (shoutcastStream.AudioInfo.AudioFormat == StreamAudioFormat.MP3 ? MP3Parser.mp3_sampleSize : AAC_ADTSParser.aac_adts_sampleSize)
                    && shoutcastStream.metadataInt - metadataPos > 0)
                {
                    //parse part of the frame.

                    byte[] partialFrame = new byte[shoutcastStream.metadataInt - metadataPos];

                    var read = await socketReader.LoadAsync(shoutcastStream.metadataInt - metadataPos);

                    socketReader.ReadBytes(partialFrame);

                    metadataPos += shoutcastStream.metadataInt - metadataPos;

                    switch (shoutcastStream.AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseMP3SampleAsync(partial: true, partialBytes: partialFrame);

                                if (processingTaskCancel.IsCancellationRequested) return;

                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                        case StreamAudioFormat.AAC_ADTS:
                        case StreamAudioFormat.AAC:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseAACSampleAsync(partial: true, partialBytes: partialFrame);

                                if (processingTaskCancel.IsCancellationRequested) return;

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

                    switch (shoutcastStream.AudioInfo.AudioFormat)
                    {
                        case StreamAudioFormat.MP3:
                            {
                                //mp3
                                Tuple<MediaStreamSample, uint> result = await ParseMP3SampleAsync();

                                if (processingTaskCancel.IsCancellationRequested) return;

                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                        case StreamAudioFormat.AAC_ADTS:
                        case StreamAudioFormat.AAC:
                            {
                                Tuple<MediaStreamSample, uint> result = await ParseAACSampleAsync();

                                if (processingTaskCancel.IsCancellationRequested) return;

                                sample = result.Item1;
                                sampleLength = result.Item2;
                            }
                            break;
                    }


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

        private async Task<Tuple<MediaStreamSample, uint>> ParseMP3SampleAsync(bool partial = false, byte[] partialBytes = null)
        {
            if (processingTaskCancel.IsCancellationRequested) return null;

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
                if (processingTaskCancel.IsCancellationRequested) return null;

                var read = await socketReader.LoadAsync(MP3Parser.mp3_sampleSize);

                if (processingTaskCancel.IsCancellationRequested) return null;

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
            if (processingTaskCancel.IsCancellationRequested) return null;

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
                if (processingTaskCancel.IsCancellationRequested) return null;
                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize);

                if (processingTaskCancel.IsCancellationRequested) return null;

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

        internal MediaStreamSample GetNextSample()
        {
            MediaStreamSample sample = null;
            bool result = sampleQueue.TryDequeue(out sample);

            return sample;
        }
    }
}