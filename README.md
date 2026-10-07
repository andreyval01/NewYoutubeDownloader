# NewYoutubeDownloader

A Windows app for downloading videos, playlists and channels from YouTube, Rutube and VK Video.

## Features

- YouTube, Rutube and VK Video
- Playlists in their own folders, numbered files and an `.m3u` playback list
- Queue, limited simultaneous downloads, resume after restart
- Optional sign-in for restricted YouTube and Rutube content
- Convert to common audio and video formats
- Themes and multiple languages

Public videos download without an account. Sign in under Settings when a source requires a session.

Version 0.1.

Place `ffmpeg.exe` next to `NewYoutubeDownloader.exe`. The project copies `tools\yt-dlp.exe` beside the program when it builds.

```
dotnet build YoutubePlaylistDownloader\YoutubePlaylistDownloader.csproj -c Release
```
