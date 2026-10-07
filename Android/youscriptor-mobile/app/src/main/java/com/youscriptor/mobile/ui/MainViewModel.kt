package com.youscriptor.mobile.ui

import android.app.Application
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.youscriptor.mobile.ScriptorApplication
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.model.AuthProvider
import com.youscriptor.mobile.data.model.AuthSession
import com.youscriptor.mobile.data.model.NoteSyncState
import com.youscriptor.mobile.data.network.SubscriptionSummaryDto
import com.youscriptor.mobile.data.network.toUserFacingMessage
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val container = (application as ScriptorApplication).container

    val notes = container.notesRepository.observeNotes().stateIn(
        viewModelScope,
        SharingStarted.WhileSubscribed(5_000),
        emptyList()
    )

    val session: StateFlow<AuthSession?> = container.authRepository.session

    private val _selectedNoteIds = MutableStateFlow<Set<String>>(emptySet())
    val selectedNoteIds: StateFlow<Set<String>> = _selectedNoteIds.asStateFlow()

    private val _baseUrl = MutableStateFlow(container.authRepository.currentBaseUrl())
    val baseUrl: StateFlow<String> = _baseUrl.asStateFlow()

    private val _subscriptionSummary = MutableStateFlow<SubscriptionSummaryDto?>(null)
    val subscriptionSummary: StateFlow<SubscriptionSummaryDto?> = _subscriptionSummary.asStateFlow()

    private val _messages = MutableSharedFlow<String>()
    val messages = _messages.asSharedFlow()
    private val _loginRequests = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val loginRequests = _loginRequests.asSharedFlow()
    private var pendingSyncIds: List<String> = emptyList()

    init {
        viewModelScope.launch {
            if (session.value == null) {
                container.authRepository.refreshSession()
            }
            loadSubscriptionSummary()
        }
    }

    fun handleAuthCallback(uri: Uri) {
        val error = container.authRepository.handleAuthCallback(uri)
        viewModelScope.launch {
            if (error.isNullOrBlank()) {
                emitMessage("Вы вошли в аккаунт.")
                loadSubscriptionSummary()
                val pending = pendingSyncIds
                pendingSyncIds = emptyList()
                pending.forEach { enqueueSyncNote(it) }
            } else {
                emitMessage(error)
            }
        }
    }

    fun updateBaseUrl(url: String) {
        _baseUrl.value = url
    }

    fun saveBaseUrl() {
        container.authRepository.updateBaseUrl(_baseUrl.value)
        _baseUrl.value = container.authRepository.currentBaseUrl()
        viewModelScope.launch {
            emitMessage("Адрес сервера сохранён: ${_baseUrl.value}")
        }
    }

    fun startLogin(activity: android.app.Activity, provider: AuthProvider) {
        container.authRepository.startLogin(activity, provider)
    }

    fun refreshSession() {
        viewModelScope.launch {
            val refreshed = container.authRepository.refreshSession()
            if (refreshed) {
                emitMessage("Подключение обновлено.")
                loadSubscriptionSummary()
            } else {
                emitMessage("Не удалось обновить подключение. Попробуйте войти снова.")
            }
        }
    }

    fun logout() {
        viewModelScope.launch {
            container.authRepository.logout()
            _subscriptionSummary.value = null
            emitMessage("Вы вышли из аккаунта.")
        }
    }

    fun createRecordedNote(title: String, audioPath: String, durationSec: Long) {
        viewModelScope.launch {
            val now = System.currentTimeMillis()
            val normalizedTitle = title.trim()
            val isTitleCustomized = normalizedTitle.isNotBlank()
            val note = NoteEntity(
                id = UUID.randomUUID().toString(),
                title = if (isTitleCustomized) normalizedTitle else defaultTitle(),
                createdAt = now,
                updatedAt = now,
                audioLocalPath = audioPath,
                durationSec = durationSec,
                rawTranscript = "",
                formattedTranscript = "",
                userEditedText = "",
                syncState = NoteSyncState.LOCAL_ONLY,
                remoteTaskId = null,
                remoteStatus = null,
                lastSyncAt = null,
                lastError = null,
                isTitleCustomized = isTitleCustomized
            )
            container.notesRepository.saveNote(note)
            emitMessage("Запись сохранена на устройстве.")
        }
    }

    fun saveTitle(noteId: String, title: String) {
        viewModelScope.launch {
            val normalizedTitle = title.trim()
            if (normalizedTitle.isBlank()) {
                emitMessage("Введите название заметки.")
                return@launch
            }

            val note = container.notesRepository.getNote(noteId) ?: return@launch
            container.notesRepository.saveNote(
                note.copy(
                    title = normalizedTitle,
                    isTitleCustomized = true,
                    updatedAt = System.currentTimeMillis()
                )
            )

            runCatching {
                container.syncRepository.pushTitle(noteId, normalizedTitle)
            }.onSuccess {
                emitMessage(if (note.remoteTaskId == null) "Название сохранено на устройстве." else "Название сохранено.")
            }.onFailure {
                emitMessage(it.toUserFacingMessage())
            }
        }
    }

    fun saveEditedText(noteId: String, text: String) {
        viewModelScope.launch {
            val note = container.notesRepository.getNote(noteId) ?: return@launch
            container.notesRepository.saveNote(
                note.copy(
                    formattedTranscript = text,
                    userEditedText = text,
                    updatedAt = System.currentTimeMillis()
                )
            )
            runCatching {
                container.syncRepository.pushEditedText(noteId, text)
            }.onSuccess {
                emitMessage("Текст сохранён.")
            }.onFailure {
                emitMessage(it.toUserFacingMessage())
            }
        }
    }

    fun syncNote(noteId: String) {
        viewModelScope.launch {
            if (ensureSessionForSync(listOf(noteId))) enqueueSyncNote(noteId)
        }
    }

    fun syncSelectedNotes() {
        val noteIds = selectedNoteIds.value.toList()
        if (noteIds.isEmpty()) return
        viewModelScope.launch {
            if (ensureSessionForSync(noteIds)) noteIds.forEach { enqueueSyncNote(it) }
        }
    }

    fun cancelPendingSync() { pendingSyncIds = emptyList() }

    private suspend fun ensureSessionForSync(noteIds: List<String>): Boolean {
        if (session.value != null || container.authRepository.refreshSession()) return true
        pendingSyncIds = noteIds
        _loginRequests.emit(Unit)
        return false
    }

    private suspend fun enqueueSyncNote(noteId: String) {
        val note = container.notesRepository.getNote(noteId) ?: return
        if (note.syncState == NoteSyncState.SYNC_PENDING || note.syncState == NoteSyncState.SYNCING) return
        container.notesRepository.saveNote(
            note.copy(
                syncState = NoteSyncState.SYNC_PENDING,
                lastError = null,
                updatedAt = System.currentTimeMillis()
            )
        )
        container.syncRepository.enqueueSync(noteId)
        emitMessage("Запись отправлена на расшифровку.")
    }

    fun deleteNote(noteId: String) {
        viewModelScope.launch {
            val note = container.notesRepository.getNote(noteId) ?: return@launch
            File(note.audioLocalPath).delete()
            container.notesRepository.deleteNote(noteId)
            _selectedNoteIds.value = _selectedNoteIds.value - noteId
            emitMessage("Заметка удалена.")
        }
    }

    fun toggleSelection(noteId: String) {
        val current = _selectedNoteIds.value
        _selectedNoteIds.value = if (noteId in current) current - noteId else current + noteId
    }

    fun clearSelection() {
        _selectedNoteIds.value = emptySet()
    }

    fun shareSelectedTextIntent(): Intent? {
        val selected = notes.value.filter { it.id in _selectedNoteIds.value }
        if (selected.isEmpty()) {
            return null
        }

        val payload = selected.joinToString("\n\n---\n\n") { note ->
            buildString {
                append(note.title)
                append("\n\n")
                append(note.userEditedText.ifBlank {
                    note.formattedTranscript.ifBlank { note.rawTranscript }
                })
            }
        }

        return Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_TEXT, payload)
        }
    }

    fun shareTextIntent(noteId: String): Intent? {
        val note = notes.value.firstOrNull { it.id == noteId } ?: return null
        return Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_TEXT, "${note.title}\n\n${note.userEditedText.ifBlank { note.formattedTranscript.ifBlank { note.rawTranscript } }}")
        }
    }

    fun shareSelectedAudioIntent(): Intent? {
        val selected = notes.value.filter { it.id in _selectedNoteIds.value }
        if (selected.isEmpty()) {
            return null
        }

        return if (selected.size == 1) {
            shareFileIntent(File(selected.first().audioLocalPath), "audio/*")
        } else {
            val zip = createZipArchive(selected)
            shareFileIntent(zip, "application/zip")
        }
    }

    fun shareAudioIntent(noteId: String): Intent? {
        val note = notes.value.firstOrNull { it.id == noteId } ?: return null
        return shareFileIntent(File(note.audioLocalPath), "audio/*")
    }

    suspend fun shareSrtIntent(noteId: String): Intent? {
        val note = container.notesRepository.getNote(noteId)
        if (note == null) {
            emitMessage("Заметка не найдена.")
            return null
        }

        val remoteTaskId = note.remoteTaskId
        if (remoteTaskId.isNullOrBlank()) {
            emitMessage("Сначала расшифруйте запись, чтобы экспортировать субтитры.")
            return null
        }

        return runCatching {
            val response = container.authRepository.withAuthorizedApi { api ->
                api.exportSrt(remoteTaskId)
            }

            val body = response.body()
            if (!response.isSuccessful || body == null) {
                throw IllegalStateException("Субтитры пока недоступны. Дождитесь завершения расшифровки.")
            }

            val targetFile = File(
                getApplication<Application>().cacheDir,
                "${safeFileBaseName(note.title, note.id)}.srt"
            )

            body.byteStream().use { input ->
                FileOutputStream(targetFile).use { output ->
                    input.copyTo(output)
                }
            }

            shareFileIntent(targetFile, "application/x-subrip")
        }.onFailure {
            emitMessage(it.toUserFacingMessage())
        }.getOrNull()
    }

    fun loadSubscriptionSummary() {
        viewModelScope.launch {
            if (session.value == null) {
                _subscriptionSummary.value = null
                return@launch
            }

            runCatching {
                container.authRepository.withAuthorizedApi { api ->
                    api.getSubscriptionSummary()
                }
            }.onSuccess {
                _subscriptionSummary.value = it
            }.onFailure {
                emitMessage(it.toUserFacingMessage())
            }
        }
    }

    fun findNote(noteId: String): NoteEntity? = notes.value.firstOrNull { it.id == noteId }

    private suspend fun emitMessage(text: String) {
        _messages.emit(text)
    }

    private fun defaultTitle(): String = "Новая запись"

    private fun safeFileBaseName(title: String, fallback: String): String {
        val normalized = title
            .trim()
            .ifBlank { fallback }
            .map { char ->
                when {
                    char.isLetterOrDigit() || char == '-' || char == '_' -> char
                    else -> '_'
                }
            }
            .joinToString("")
            .trim('_')

        return normalized.ifBlank { fallback }
    }

    private fun shareFileIntent(file: File, mimeType: String): Intent? {
        if (!file.exists()) {
            return null
        }

        val uri = FileProvider.getUriForFile(
            getApplication(),
            "${getApplication<Application>().packageName}.fileprovider",
            file
        )

        return Intent(Intent.ACTION_SEND).apply {
            type = mimeType
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }

    private fun createZipArchive(notes: List<NoteEntity>): File {
        val zipFile = File(getApplication<Application>().cacheDir, "notes-share.zip")
        ZipOutputStream(FileOutputStream(zipFile)).use { output ->
            notes.forEach { note ->
                val file = File(note.audioLocalPath)
                if (!file.exists()) {
                    return@forEach
                }

                output.putNextEntry(ZipEntry(file.name))
                FileInputStream(file).use { input ->
                    input.copyTo(output)
                }
                output.closeEntry()
            }
        }
        return zipFile
    }
}
