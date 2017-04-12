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

        public async Task<ServerAudioInfo> GrabFrameInfoAsync(ShoutcastStreamProcessor streamProcessor, DataReader socketReader, ServerAudioInfo serverSentInfo)
        {
            ServerAudioInfo audioInfo = new ServerAudioInfo();
            audioInfo.AudioFormat = StreamAudioFormat.AAC_ADTS;

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

                    audioInfo.SampleRate = (uint)AAC_ADTSParser.GetSampleRate(header);

                    audioInfo.ChannelCount = (uint)AAC_ADTSParser.GetChannelCount(header);

                    //bitrate gets sent by the server.
                    audioInfo.BitRate = serverSentInfo.BitRate;
                    //audioInfo.BitRate = (uint)AAC_ADTSParser.GetBitRate(header);

                    //if (audioInfo.BitRate == 0) throw new Exception("Unknown bitrate.");
                    break;
                }
                else
                {
                    streamProcessor.byteOffset += 1;
                    streamProcessor.metadataPos += 1;
                    lastByte = curByte;
                }
            }

            return audioInfo;
        }

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
                processor.byteOffset += sampleLength;
            }
            else
            {
                await socketReader.LoadAsync(AAC_ADTSParser.aac_adts_sampleSize);

                buffer = socketReader.ReadBuffer(AAC_ADTSParser.aac_adts_sampleSize);

                processor.byteOffset += AAC_ADTSParser.aac_adts_sampleSize;
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
