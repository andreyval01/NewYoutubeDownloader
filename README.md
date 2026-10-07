# NewYoutubeDownloader

Программа для Windows. Скачивает видео, плейлисты и каналы с YouTube, Rutube и VK Video.

## Возможности

- YouTube, Rutube и VK Video
- Плейлисты в отдельных папках, нумерованные файлы и список воспроизведения `.m3u`
- Очередь, ограничение одновременных загрузок и продолжение после перезапуска
- Вход в аккаунт для закрытых роликов YouTube и Rutube
- Конвертация в обычные аудио- и видеоформаты
- Перекодирование уже скачанных файлов с оценкой размера
- Темы оформления и несколько языков

Открытые ролики скачиваются без аккаунта. Если источник требует сессию, войдите в настройках.

Версия 0.1. Готовая сборка: [релиз 0.1](https://github.com/andreyval01/NewYoutubeDownloader/releases/tag/v0.1).

Положите `ffmpeg.exe` рядом с `NewYoutubeDownloader.exe`. При сборке проект копирует `tools\yt-dlp.exe` рядом с программой.

```
dotnet build YoutubePlaylistDownloader\YoutubePlaylistDownloader.csproj -c Release
```
