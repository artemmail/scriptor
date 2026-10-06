package com.youscriptor.mobile.data.sync

import android.app.Application
import androidx.work.CoroutineWorker
import androidx.work.Constraints
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import com.youscriptor.mobile.ScriptorApplication
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.model.NoteSyncState
import com.youscriptor.mobile.data.network.UpdateMarkdownRequest
import com.youscriptor.mobile.data.network.UpdateTitleRequest
import com.youscriptor.mobile.data.network.toUserFacingMessage
import com.youscriptor.mobile.data.repository.AuthRepository
import com.youscriptor.mobile.data.repository.NotesRepository
import kotlinx.coroutines.delay
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import java.io.File
import java.util.UUID

class SyncRepository(
    private val application: Application,
    private val notesRepository: NotesRepository,
    private val authRepository: AuthRepository
) {
    fun enqueueSync(noteId: String) {
        val constraints = Constraints.Builder()
            .setRequiredNetworkType(NetworkType.CONNECTED)
            .build()

        val request = OneTimeWorkRequestBuilder<NoteSyncWorker>()
            .setConstraints(constraints)
            .setInputData(workDataOf(KEY_NOTE_ID to noteId))
            .build()

        WorkManager.getInstance(application)
            .enqueueUniqueWork("note-sync-$noteId", ExistingWorkPolicy.REPLACE, request)
    }

    suspend fun pushEditedText(noteId: String, text: String) {
        val note = notesRepository.getNote(noteId) ?: return
        notesRepository.saveNote(
            note.copy(
                userEditedText = text,
                formattedTranscript = text,
                updatedAt = System.currentTimeMillis()
            )
        )

        val remoteTaskId = note.remoteTaskId ?: return
        authRepository.withAuthorizedApi { api ->
            api.updateMarkdown(remoteTaskId, UpdateMarkdownRequest(markdown = text))
        }
    }

    suspend fun pushTitle(noteId: String, title: String) {
        val remoteTaskId = notesRepository.getNote(noteId)?.remoteTaskId ?: return
        authRepository.withAuthorizedApi { api ->
            api.updateTitle(remoteTaskId, UpdateTitleRequest(title = title))
        }
    }

    suspend fun syncNoteNow(noteId: String): SyncOutcome {
        val original = notesRepository.getNote(noteId) ?: return SyncOutcome.Success
        val audioFile = File(original.audioLocalPath)
        if (!audioFile.exists()) {
            notesRepository.saveNote(
                original.copy(
                    syncState = NoteSyncState.FAILED,
                    lastError = "Audio file not found on device.",
                    updatedAt = System.currentTimeMillis()
                )
            )
            return SyncOutcome.Failure
        }

        var note = original.copy(
            syncState = NoteSyncState.SYNCING,
            lastError = null,
            updatedAt = System.currentTimeMillis()
        )
        notesRepository.saveNote(note)

        try {
            var remoteTaskId = note.remoteTaskId
            if (remoteTaskId.isNullOrBlank()) {
                val task = authRepository.withAuthorizedApi { api ->
                    api.uploadAudio(
                        MultipartBody.Part.createFormData(
                            "file",
                            audioFile.name,
                            audioFile.asRequestBody("audio/mp4".toMediaType())
                        )
                    )
                }
                remoteTaskId = task.id
                note = (notesRepository.getNote(noteId) ?: note).copy(
                    remoteTaskId = remoteTaskId,
                    remoteStatus = task.status,
                    updatedAt = System.currentTimeMillis()
                )
                notesRepository.saveNote(note)

                val latestTitle = (notesRepository.getNote(noteId) ?: note)
                    .takeIf { it.isTitleCustomized }
                    ?.title
                    ?.trim()

                if (!latestTitle.isNullOrBlank()) {
                    authRepository.withAuthorizedApi { api ->
                        api.updateTitle(remoteTaskId, UpdateTitleRequest(title = latestTitle))
                    }
                }
            }

            repeat(8) {
                val details = authRepository.withAuthorizedApi { api ->
                    api.getTask(remoteTaskId!!)
                }

                if (details.done) {
                    val latestNote = notesRepository.getNote(noteId) ?: note
                    val finalText = details.markdownText
                        ?.takeIf { it.isNotBlank() }
                        ?: details.processedText
                        ?.takeIf { it.isNotBlank() }
                        ?: details.recognizedText.orEmpty()
                    val resolvedTitle = if (latestNote.isTitleCustomized) {
                        latestNote.title
                    } else {
                        details.title?.takeIf { it.isNotBlank() } ?: latestNote.title
                    }

                    notesRepository.saveNote(
                        latestNote.copy(
                            title = resolvedTitle,
                            rawTranscript = details.recognizedText.orEmpty(),
                            formattedTranscript = finalText,
                            userEditedText = if (latestNote.userEditedText.isBlank()) finalText else latestNote.userEditedText,
                            syncState = if (details.error.isNullOrBlank()) NoteSyncState.SYNCED else NoteSyncState.FAILED,
                            remoteStatus = details.status,
                            lastSyncAt = System.currentTimeMillis(),
                            lastError = details.error,
                            updatedAt = System.currentTimeMillis()
                        )
                    )
                    return if (details.error.isNullOrBlank()) SyncOutcome.Success else SyncOutcome.Failure
                }

                val latestNote = notesRepository.getNote(noteId) ?: note
                notesRepository.saveNote(
                    latestNote.copy(
                        remoteStatus = details.status,
                        syncState = NoteSyncState.SYNCING,
                        updatedAt = System.currentTimeMillis()
                    )
                )
                delay(POLL_DELAY_MS)
            }

            return SyncOutcome.Retry
        } catch (ex: Exception) {
            val latestNote = notesRepository.getNote(noteId) ?: note
            notesRepository.saveNote(
                latestNote.copy(
                    syncState = NoteSyncState.FAILED,
                    lastError = ex.toUserFacingMessage(),
                    updatedAt = System.currentTimeMillis()
                )
            )
            return SyncOutcome.Failure
        }
    }

    companion object {
        const val KEY_NOTE_ID = "note_id"
        private const val POLL_DELAY_MS = 4_000L
    }
}

enum class SyncOutcome {
    Success,
    Retry,
    Failure
}

class NoteSyncWorker(
    appContext: android.content.Context,
    params: WorkerParameters
) : CoroutineWorker(appContext, params) {
    override suspend fun doWork(): Result {
        val noteId = inputData.getString(SyncRepository.KEY_NOTE_ID) ?: return Result.failure()
        val app = applicationContext as ScriptorApplication
        return when (app.container.syncRepository.syncNoteNow(noteId)) {
            SyncOutcome.Success -> Result.success()
            SyncOutcome.Retry -> Result.retry()
            SyncOutcome.Failure -> Result.failure()
        }
    }
}
