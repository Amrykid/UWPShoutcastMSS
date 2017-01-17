using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UWPShoutcastMSS.Parsers.Audio
{
    public static class MP3Parser
    {
        //Reference: https://www.mp3-tech.org/programmer/frame_header.html

        public static readonly byte[] FrameSync = new byte[] { (byte)255, (byte)224 }; //12 bits - ADTS sync bits.
        public const int HeaderLength = 4;
        //public const int HeaderLengthWithCRC = 9;

        public static bool IsFrameSync(byte firstByte, byte secondByte)
        {
            return (firstByte == FrameSync[0] && (secondByte & FrameSync[1]) == (byte)224 && secondByte != (byte)255);
        }

        public static double GetMPEGAudioVersion(byte[] header)
        {
            /*
            00 - MPEG Version 2.5 (later extension of MPEG 2)
            01 - reserved
            10 - MPEG Version 2 (ISO/IEC 13818-3)
            11 - MPEG Version 1 (ISO/IEC 11172-3)
            */

            byte data = header[1];

            data = (byte)(data << 3);
            data = (byte)(data >> 6);

            int value = (int)data;

            switch (value)
            {
                case 3:
                    return 1.0; //MPEG v1
                case 2:
                    return 2.0; //MPEG v2
                case 0:
                    return 2.5; //MPEG v2.5
                default:
                    throw new Exception("Reserved or unknown version.");
            }
        }

        public static int GetMPEGAudioLayer(byte[] header)
        {
            /*
            00 - reserved
            01 - Layer III
            10 - Layer II
            11 - Layer I
            */

            byte data = header[1];
            data = (byte)(data << 5);
            data = (byte)(data >> 6);

            int value = (int)data;

            switch (value)
            {
                case 1:
                    return 3; //layer 3
                case 2:
                    return 2; //layer 2
                case 3:
                    return 1; //layer 1
                default:
                    throw new Exception("Reserved or unknown version.");
            }
        }

        public static int GetBitRate(byte[] header)
        {
            int[,] bitRateTable = new int[5, 16];

            bitRateTable[0, 0] = -1; //"free"
            bitRateTable[1, 0] = -1; //"free"
            bitRateTable[2, 0] = -1; //"free"
            bitRateTable[3, 0] = -1; //"free"
            bitRateTable[4, 0] = -1; //"free"



            //Version 1, Layer 1
            bitRateTable[0, 1] = 32;
            bitRateTable[0, 2] = 64;
            bitRateTable[0, 3] = 96;
            bitRateTable[0, 4] = 128;
            bitRateTable[0, 5] = 160;
            bitRateTable[0, 6] = 192;
            bitRateTable[0, 7] = 224;
            bitRateTable[0, 8] = 256;
            bitRateTable[0, 9] = 288;
            bitRateTable[0, 10] = 320;
            bitRateTable[0, 11] = 352;
            bitRateTable[0, 12] = 384;
            bitRateTable[0, 13] = 416;
            bitRateTable[0, 14] = 448;
            bitRateTable[0, 15] = -2; //bad

            //Version 1, Layer 2
            bitRateTable[1, 1] = 32;
            bitRateTable[1, 2] = 48;
            bitRateTable[1, 3] = 56;
            bitRateTable[1, 4] = 64;
            bitRateTable[1, 5] = 80;
            bitRateTable[1, 6] = 96;
            bitRateTable[1, 7] = 112;
            bitRateTable[1, 8] = 128;
            bitRateTable[1, 9] = 160;
            bitRateTable[1, 10] = 192;
            bitRateTable[1, 11] = 224;
            bitRateTable[1, 12] = 256;
            bitRateTable[1, 13] = 320;
            bitRateTable[1, 14] = 384;
            bitRateTable[1, 15] = -2; //bad


            //Version 1, Layer 3
            bitRateTable[2, 1] = 32;
            bitRateTable[2, 2] = 40;
            bitRateTable[2, 3] = 48;
            bitRateTable[2, 4] = 56;
            bitRateTable[2, 5] = 64;
            bitRateTable[2, 6] = 80;
            bitRateTable[2, 7] = 96;
            bitRateTable[2, 8] = 112;
            bitRateTable[2, 9] = 128;
            bitRateTable[2, 10] = 160;
            bitRateTable[2, 11] = 192;
            bitRateTable[2, 12] = 224;
            bitRateTable[2, 13] = 256;
            bitRateTable[2, 14] = 320;
            bitRateTable[2, 15] = -2; //bad


            //Version 2, Layer 1
            bitRateTable[3, 1] = 32;
            bitRateTable[3, 2] = 48;
            bitRateTable[3, 3] = 56;
            bitRateTable[3, 4] = 64;
            bitRateTable[3, 5] = 80;
            bitRateTable[3, 6] = 96;
            bitRateTable[3, 7] = 112;
            bitRateTable[3, 8] = 128;
            bitRateTable[3, 9] = 144;
            bitRateTable[3, 10] = 160;
            bitRateTable[3, 11] = 176;
            bitRateTable[3, 12] = 192;
            bitRateTable[3, 13] = 224;
            bitRateTable[3, 14] = 256;
            bitRateTable[3, 15] = -2; //bad


            //Version 2, Layer 2 or 3
            bitRateTable[4, 1] = 8;
            bitRateTable[4, 2] = 16;
            bitRateTable[4, 3] = 24;
            bitRateTable[4, 4] = 32;
            bitRateTable[4, 5] = 40;
            bitRateTable[4, 6] = 48;
            bitRateTable[4, 7] = 56;
            bitRateTable[4, 8] = 64;
            bitRateTable[4, 9] = 80;
            bitRateTable[4, 10] = 96;
            bitRateTable[4, 11] = 112;
            bitRateTable[4, 12] = 128;
            bitRateTable[4, 13] = 144;
            bitRateTable[4, 14] = 160;
            bitRateTable[4, 15] = -2; //bad

            double mpegVersion = GetMPEGAudioVersion(header);

            int mpegLayer = GetMPEGAudioLayer(header);

            int tableColumn = 0;

            if (mpegVersion == 1.0)
            {
                switch (mpegLayer)
                {
                    case 1:
                        tableColumn = 0;
                        break;
                    case 2:
                        tableColumn = 1;
                        break;
                    case 3:
                        tableColumn = 2;
                        break;
                }
            }
            else if (mpegVersion == 2.0)
            {
                if (mpegLayer == 1)
                {
                    tableColumn = 3;
                }
                else if (mpegLayer == 2 || mpegLayer == 3)
                {
                    tableColumn = 4;
                }
                else
                {
                    throw new Exception("Unknown MPEG layer.");
                }
            }


            byte data = header[2];

            data = (byte)(data >> 4);

            int value = (int)data;

            int bitRate = bitRateTable[tableColumn, value];

            return bitRate;
        }

        public static int GetSampleRate(byte[] header)
        {
            int[] sampleRateTable = null;

            double mpegVersion = GetMPEGAudioVersion(header);

            if (mpegVersion == 1.0)
            {
                sampleRateTable = new int[] { 44100, 48000, 32000 };
            }
            else if (mpegVersion == 2.0)
            {
                sampleRateTable = new int[] { 22050, 24000, 16000 };
            }
            else if (mpegVersion == 2.5)
            {
                sampleRateTable = new int[] { 11025, 12000, 8000 };
            }

            byte data = header[2];

            data = (byte)(data << 4);
            data = (byte)(data >> 6);

            int value = (int)data;

            int sampleRate = sampleRateTable[value];

            return sampleRate;
        }

        public static int GetChannelConfig(byte[] header)
        {
            /* 00 - Stereo
               01 - Joint stereo (Stereo)
               10 - Dual channel (2 mono channels)
               11 - Single channel (Mono)
            */

            byte data = header[3];

            data = (byte)(data >> 6);

            int value = (int)data;

            return value;
        }

        public static int GetChannelCount(byte[] header)
        {
            int channelMode = MP3Parser.GetChannelConfig(header);
            switch (channelMode)
            {
                case 0: //stereo
                case 1: // "joint" stereo
                    return 2;
                case 3: //mono
                    return 1;
                default:
                    throw new Exception("Unsupported audio channel mode.");
            }
        }

        private static string ByteToBinaryString(byte bte)
        {
            return Convert.ToString(bte, 2).PadLeft(8, '0');
        }
    }
}
