using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using UWPShoutcastMSS.Parsers.Audio;
using Windows.Media.Core;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming.Providers
{
    internal class AACADTSAudioProvider : IAudioProvider
    {
        public uint GetSampleSize()
        {
            return AAC_ADTSParser.aac_adts_sampleSize;
        }

        public async Task<ServerAudioInfo> GrabFrameInfoAsync(ShoutcastStreamProcessor processor, ServerAudioInfo serverSentInfo)
        {
            ServerAudioInfo audioInfo = new ServerAudioInfo();
            audioInfo.AudioFormat = StreamAudioFormat.AAC_ADTS;

            //load the first byte
            byte lastByte = await processor.ReadByteFromSocketAsync();

            while (true) //wait for frame sync
            {
                var curByte = await processor.ReadByteFromSocketAsync();

                if (AAC_ADTSParser.IsFrameSync(lastByte, curByte)) //check if we're at the frame sync. if we are, parse some of the audio data
                {
                    byte[] header = new byte[AAC_ADTSParser.HeaderLength];
                    header[0] = lastByte;
                    header[1] = curByte;


                    Array.Copy(await processor.ReadBytesFromSocketAsync(5), 0, header, 2, 5);

                    //todo deal with CRC

                    try
                    {
                        audioInfo.SampleRate = (uint)AAC_ADTSParser.GetSampleRate(header);

                        audioInfo.ChannelCount = (uint)AAC_ADTSParser.GetChannelCount(header);

                        //bitrate gets sent by the server.
                        audioInfo.BitRate = serverSentInfo.BitRate;
                        //audioInfo.BitRate = (uint)AAC_ADTSParser.GetBitRate(header);

                        //if (audioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");
                    }
                    catch (IndexOutOfRangeException)
                    {
                        //probably not the header. continue.
                        lastByte = curByte;
                        continue;
                    }
                    break;
                }
                else
                {
                    lastByte = curByte;
                }
            }

            return audioInfo;
        }

        public StreamAudioFormat AudioFormat => StreamAudioFormat.AAC_ADTS;
        public uint HeaderLength => AAC_ADTSParser.HeaderLength;

        public async Task<Tuple<MediaStreamSample, uint>> ParseSampleAsync(ShoutcastStreamProcessor processor,
            DataReader socketReader, bool partial = false, byte[] partialBytes = null)
        {
            IBuffer buffer = null;
            MediaStreamSample sample = null;
            uint sampleLength = 0;

            if (partial)
            {
                buffer = partialBytes.AsBuffer();
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize - (uint)partialBytes.Length;
                //processor.byteOffset += sampleLength;
            }
            else
            {
                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize);

                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize);

                //processor.byteOffset += AAC_ADTSParser.aac_adts_sampleSize;
                sampleLength = AAC_ADTSParser.aac_adts_sampleSize;
            }

            sample = MediaStreamSample.CreateFromBuffer(buffer, processor.timeOffSet);
            sample.Duration = AAC_ADTSParser.aac_adts_sampleDuration;
            sample.KeyFrame = true;

            processor.timeOffSet = processor.timeOffSet.Add(AAC_ADTSParser.aac_adts_sampleDuration);


            return new Tuple<MediaStreamSample, uint>(sample, sampleLength);
        }


    }
}
