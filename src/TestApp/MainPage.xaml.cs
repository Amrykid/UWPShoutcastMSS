using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using UWPShoutcastMSS.Streaming;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace TestApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            stationComboBox.ItemsSource = new StationItem[]
            {
                new StationItem
                {
                    Name = "Hitradio OE3 (MP3, 128kb)",
                    Url = new Uri("http://194.232.200.156:8000/")
                },
                new StationItem
                {
                    Name = "yggdrasilradio.net (AAC, 56kb)",
                    Url = new Uri("http://95.211.241.92:9100/")
                },
            };

            stationComboBox.SelectedIndex = 0;
        }

        UWPShoutcastMSS.Streaming.ShoutcastMediaSourceStream shoutcastStream = null;

        private async void StreamManager_MetadataChanged(object sender, ShoutcastMediaSourceStreamMetadataChangedEventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
            {
                SongBox.Text = "Song: " + e.Title;
                ArtistBox.Text = "Artist: " + e.Artist;
            }));
        }

        private async void playButton_Click(object sender, RoutedEventArgs e)
        {
            playButton.IsEnabled = false;
            stationComboBox.IsEnabled = false;
            stopButton.IsEnabled = true;

            var selectedStation = stationComboBox.SelectedItem as StationItem;

            if (selectedStation != null)
            {
                //Went to the shoutcast website and grabbed the highest ranked POP stream: Hitradio OE3
                shoutcastStream = new UWPShoutcastMSS.Streaming.ShoutcastMediaSourceStream(selectedStation.Url);
                shoutcastStream.MetadataChanged += StreamManager_MetadataChanged;
                await shoutcastStream.ConnectAsync();

                MediaPlayer.SetMediaStreamSource(shoutcastStream.MediaStreamSource);
                MediaPlayer.Play();
            }
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            playButton.IsEnabled = true;
            stationComboBox.IsEnabled = true;
            stopButton.IsEnabled = false;

            if (shoutcastStream != null)
            {
                shoutcastStream.MetadataChanged -= StreamManager_MetadataChanged;
                MediaPlayer.Stop();
                MediaPlayer.Source = null;

                shoutcastStream.Disconnect();
                shoutcastStream = null;
            }
        }
    }
}
