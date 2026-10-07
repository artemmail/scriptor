package com.youscriptor.mobile.data.recording

import android.content.Context
import android.media.MediaPlayer
import android.media.MediaRecorder
import android.os.SystemClock
import com.youscriptor.mobile.data.model.RecordingDraft
import java.io.File
import java.util.UUID

class RecorderManager(
    private val context: Context
) {
    private var recorder: MediaRecorder? = null
    private var outputFile: File? = null
    private var startedAtMs: Long = 0L
    private var pauseStartedAtMs: Long = 0L
    private var pausedAccumulatedMs: Long = 0L

    /** Peak of samples recorded since the previous read; zero when the recorder is idle. */
    fun amplitude(): Int = runCatching { recorder?.maxAmplitude ?: 0 }.getOrDefault(0)

    fun start(): String {
        val dir = File(context.filesDir, "notes").apply { mkdirs() }
        val file = File(dir, "note-${UUID.randomUUID()}.m4a")

        recorder = MediaRecorder().apply {
            setAudioSource(MediaRecorder.AudioSource.MIC)
            setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            setAudioEncodingBitRate(128_000)
            setAudioSamplingRate(44_100)
            setOutputFile(file.absolutePath)
            prepare()
            start()
        }

        outputFile = file
        startedAtMs = SystemClock.elapsedRealtime()
        pauseStartedAtMs = 0L
        pausedAccumulatedMs = 0L
        return file.absolutePath
    }

    fun pause() {
        recorder?.pause()
        pauseStartedAtMs = SystemClock.elapsedRealtime()
    }

    fun resume() {
        recorder?.resume()
        if (pauseStartedAtMs != 0L) {
            pausedAccumulatedMs += SystemClock.elapsedRealtime() - pauseStartedAtMs
            pauseStartedAtMs = 0L
        }
    }

    fun stop(): RecordingDraft? {
        val currentRecorder = recorder ?: return null
        return try {
            currentRecorder.stop()
            val draft = outputFile?.let {
                RecordingDraft(
                    filePath = it.absolutePath,
                    durationSeconds = (((if (pauseStartedAtMs != 0L) pauseStartedAtMs else SystemClock.elapsedRealtime()) - startedAtMs - pausedAccumulatedMs) / 1000L)
                        .coerceAtLeast(1L)
                )
            }
            draft
        } finally {
            currentRecorder.reset()
            currentRecorder.release()
            recorder = null
            outputFile = null
            startedAtMs = 0L
            pauseStartedAtMs = 0L
            pausedAccumulatedMs = 0L
        }
    }

    fun cancel() {
        val currentRecorder = recorder ?: return
        try {
            currentRecorder.stop()
        } catch (_: Exception) {
            // Ignore partially recorded state.
        } finally {
            currentRecorder.reset()
            currentRecorder.release()
            recorder = null
            outputFile?.delete()
            outputFile = null
        }
    }
}

class AudioPlayerController {
    private var mediaPlayer: MediaPlayer? = null

    fun play(path: String, onComplete: () -> Unit) {
        stop()
        mediaPlayer = MediaPlayer().apply {
            setDataSource(path)
            setOnCompletionListener {
                stop()
                onComplete()
            }
            prepare()
            start()
        }
    }

    fun stop() {
        mediaPlayer?.run {
            try {
                stop()
            } catch (_: Exception) {
                // Ignore transitional player states.
            }
            release()
        }
        mediaPlayer = null
    }
}
