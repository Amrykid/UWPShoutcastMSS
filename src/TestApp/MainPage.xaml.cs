using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using UWPShoutcastMSS.Streaming;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
        }
        ShoutcastMediaSourceManager streamManager = null;
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            ((Button)sender).IsEnabled = false;

            //Went to the shoutcast website and grabbed the highest ranked POP stream: Hitradio OE3
            streamManager = new ShoutcastMediaSourceManager(new Uri("http://194.232.200.156:8000"));
            streamManager.MetadataChanged += StreamManager_MetadataChanged;
            await streamManager.ConnectAsync();

            MediaPlayer.SetMediaStreamSource(streamManager.MediaStreamSource);
            MediaPlayer.Play();
        }

        private async void StreamManager_MetadataChanged(object sender, ShoutcastMediaSourceManagerMetadataChangedEventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
            {
                SongBox.Text = "Song: " + e.Title;
                ArtistBox.Text = "Artist: " + e.Artist;
            }));
        }
    }
}
