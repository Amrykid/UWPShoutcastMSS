# UWPShoutcastMSS

A library for connecting to Shoutcast in Windows 10 UWP applications. It's more of a hack than a proper library, but it gets the job done.

## How to use it

Easy, peasy. 

### Foreground Audio
Assuming you set up an invisible MediaPlayer named 'MediaPlayer', the following should work:
```c#
ShoutcastStream shoutcastStream = await ShoutcastStreamFactory.ConnectAsync( new Uri("http://194.232.200.156:8000/"));
MediaPlayer.SetMediaStreamSource(shoutcastStream.MediaStreamSource);
MediaPlayer.Play();
```

### Background Audio
Use the following in your audio background task.

```c#
//Initialize the stream and connect
ShoutcastStream shoutcastStream = await ShoutcastStreamFactory.ConnectAsync( new Uri("http://194.232.200.156:8000/"));
//Hook up an event handler for grabbing metadata when it changes. This means you can update your "Now Playing" display.
streamManager.MetadataChanged += Metadata_EventHandler;
```

```c#
//Old-style background audio works like this
//Set the manager's underlying stream as the BackgroundMediaPlayer's MediaSource.
BackgroundMediaPlayer.Current.SetMediaSource(streamManager.MediaStreamSource);
//Play!
BackgroundMediaPlayer.Current.Play();
```

#### Single-process Background Audio
```c#
//New-style background audio works like this
//Make sure to have the background audio permission set in your application's manifest.
MediaPlayer.SetMediaStreamSource(shoutcastStream.MediaStreamSource);
MediaPlayer.Play();
```

## Want to contribute?
I'm pretty new to the open source scene so if you have any contributions, feel free to send a pull request. I will be re-evaluating the license of the code in the future if it is a problem. I will also be looking into setting up a Nuget package if this library takes off.

## Known Issues

