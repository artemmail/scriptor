using System;
using System.Diagnostics;
using System.IO;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;

namespace YandexSpeech.Services
{
    public class OpusConversionService
    {
        /// <summary>
        /// Конвертирует любой поддерживаемый FFmpeg‑ом файл (mp3, webm, wav…)
        /// в .opus (Ogg/Opus). Требует установленный ffmpeg в PATH
        /// либо задайте полный путь в ffmpegPath.
        /// </summary>
        public void ConvertToOpus(string inputFile, string outputFile)
        {
            // Временный WAV (PCM 48 kHz 16‑bit mono)
            string tempWav = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".wav");

            const string ffmpegPath = "ffmpeg";   // укажи полный путь, если не в PATH
            Process? ffmpeg = null;

            try
            {
                #region 1. Декодируем входной файл в WAV через FFmpeg
                string ffArgs =
                    $"-y -i \"{inputFile}\" -vn -ac 1 -ar 48000 -acodec pcm_s16le \"{tempWav}\"";

                ffmpeg = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = ffArgs,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                ffmpeg.Start();
                ffmpeg.WaitForExit();

                if (ffmpeg.ExitCode != 0)
                {
                    string err = ffmpeg.StandardError.ReadToEnd();
                    throw new Exception($"FFmpeg error ({ffmpeg.ExitCode}): {err}");
                }
                #endregion

                #region 2. Считываем WAV → PCM и (опц) ресэмплируем
                using var waveReader = new WaveFileReader(tempWav);
                WaveStream pcmStream = waveReader;

                // Если формат не 48 kHz/16‑bit/mono — приводим к нужному
                if (waveReader.WaveFormat.SampleRate != 48000
                    || waveReader.WaveFormat.BitsPerSample != 16
                    || waveReader.WaveFormat.Channels != 1)
                {
                    var targetFormat = new WaveFormat(48000, 16, 1);
                    pcmStream = new WaveFormatConversionStream(targetFormat, waveReader);
                }

                // Читаем сразу целиком в память (можно потоково — зависит от задачи)
                using var mem = new MemoryStream();
                pcmStream.CopyTo(mem);
                byte[] rawPcmData = mem.ToArray();
                int channels = 1;           // мы задали -ac 1
                int sampleRate = 48000;       // мы задали -ar 48000
                #endregion

                #region 3. Кодируем Opus и пишем в Ogg
                //var opusEncoder = OpusEncoder.Create(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
                var opusEncoder = new OpusEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
                opusEncoder.Bitrate = 64000;     // ≈64 kbps, при необходимости поменяй

                using var outFile = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
                var oggStream = new OpusOggWriteStream(opusEncoder, outFile);

                int bytesPerSample = 2;                          // 16‑bit
                int frameSize = (sampleRate / 50) * channels; // 20 ms → 960 сэмплов при 48 kHz
                var pcmBuffer = new short[frameSize];

                int offset = 0;
                while (offset < rawPcmData.Length)
                {
                    int bytesLeft = rawPcmData.Length - offset;
                    int bytesToCopy = Math.Min(frameSize * bytesPerSample, bytesLeft);

                    Buffer.BlockCopy(rawPcmData, offset, pcmBuffer, 0, bytesToCopy);
                    offset += bytesToCopy;

                    oggStream.WriteSamples(pcmBuffer, 0, frameSize);
                }
                #endregion
            }
            finally
            {
                // Чистим ресурсы
                if (ffmpeg is { HasExited: false }) ffmpeg.Kill(true);
                if (File.Exists(tempWav))
                    File.Delete(tempWav);
            }
        }
    }
}
