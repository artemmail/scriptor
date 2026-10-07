package com.youscriptor.mobile.data.recording

import android.content.Context
import android.media.MediaPlayer
import android.media.MediaRecorder
import android.media.audiofx.Visualizer
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import com.youscriptor.mobile.data.model.RecordingDraft
import java.io.File
import java.util.UUID
import kotlin.math.sqrt

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
    private var visualizer: Visualizer? = null
    private val mainHandler = Handler(Looper.getMainLooper())
    private var playbackGeneration = 0

    fun play(path: String, onLevel: (Float) -> Unit, onComplete: () -> Unit) {
        stop()
        val player = MediaPlayer()
        try {
            player.apply {
                setDataSource(path)
                setOnCompletionListener {
                    stop()
                    onComplete()
                }
                prepare()
            }
            mediaPlayer = player
            attachWaveform(player, onLevel)
            player.start()
        } catch (error: Exception) {
            val wasRegistered = mediaPlayer === player
            stop()
            if (!wasRegistered) player.release()
            throw error
        }
    }

    private fun attachWaveform(player: MediaPlayer, onLevel: (Float) -> Unit) {
        val generation = playbackGeneration
        var capture: Visualizer? = null
        try {
            capture = Visualizer(player.audioSessionId)
            capture.captureSize = Visualizer.getCaptureSizeRange()[0]
            val rate = minOf(20_000, Visualizer.getMaxCaptureRate())
            if (rate <= 0) {
                capture.release()
                return
            }
            capture.setDataCaptureListener(object : Visualizer.OnDataCaptureListener {
                override fun onWaveFormDataCapture(
                    visualizer: Visualizer?, waveform: ByteArray, samplingRate: Int
                ) {
                    if (waveform.isEmpty()) return
                    val energy = waveform.sumOf { sample ->
                        val signed = (sample.toInt() and 0xff) - 128
                        signed * signed.toDouble()
                    } / waveform.size
                    val level = (sqrt(energy).toFloat() / 64f).coerceIn(0f, 1f)
                    mainHandler.post {
                        if (playbackGeneration == generation && mediaPlayer === player) onLevel(level)
                    }
                }

                override fun onFftDataCapture(
                    visualizer: Visualizer?, fft: ByteArray, samplingRate: Int
                ) = Unit
            }, rate, true, false)
            visualizer = capture
            capture.enabled = true
        } catch (_: Exception) {
            // Playback remains usable if the device cannot capture this audio session.
            runCatching { capture?.release() }
            visualizer = null
        }
    }

    fun stop() {
        playbackGeneration++
        visualizer?.let { capture ->
            runCatching { capture.enabled = false }
            capture.release()
        }
        visualizer = null
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
