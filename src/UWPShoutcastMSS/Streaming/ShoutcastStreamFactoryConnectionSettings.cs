﻿namespace UWPShoutcastMSS.Streaming
{
    public class ShoutcastStreamFactoryConnectionSettings
    {
        public string UserAgent { get; set; }
        public string RelativePath { get; set; } = ";"; //sometimes ";" is needed.
        public bool RequestSongMetdata { get; set; } = true;
    }
}