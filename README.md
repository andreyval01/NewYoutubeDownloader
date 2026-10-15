# NewYoutubeDownloader

Программа для Windows. Скачивает видео, плейлисты и каналы с YouTube, Rutube и VK Video.

## Возможности

- YouTube, Rutube и VK Video
- Плейлисты в отдельных папках, нумерованные файлы и список воспроизведения `.m3u`
- Очередь, ограничение одновременных загрузок и продолжение после перезапуска
- Вход в аккаунт для закрытых роликов YouTube и Rutube
- Конвертация в обычные аудио- и видеоформаты
- Перекодирование уже скачанных файлов: кодек, разрешение, качество, папка сохранения, оценка размера, прогресс и время кодирования
- Отдельное число одновременных кодирований
- Пауза перекодирования, свои пресеты и продолжение после закрытия программы
- Темы оформления и несколько языков

Открытые ролики скачиваются без аккаунта. Если источник требует сессию, войдите в настройках.

Версия 0.5.

Сборка для Windows: [NewYoutubeDownloader-0.5-win-x64.zip](https://github.com/andreyval01/NewYoutubeDownloader/releases/download/v0.5/NewYoutubeDownloader-0.5-win-x64.zip)

Положите `ffmpeg.exe` рядом с `NewYoutubeDownloader.exe`. При сборке проект копирует `tools\yt-dlp.exe` рядом с программой.

```
dotnet build YoutubePlaylistDownloader\YoutubePlaylistDownloader.csproj -c Release
```
