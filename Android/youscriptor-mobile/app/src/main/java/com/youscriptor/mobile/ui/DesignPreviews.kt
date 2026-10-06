package com.youscriptor.mobile.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.tooling.preview.Preview
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.model.NoteSyncState
import com.youscriptor.mobile.ui.theme.YouScriptorTheme

private val previewNotes = listOf(
    NoteEntity(
        id = "preview-ready", title = "Идеи для нового проекта", createdAt = 1780900800000,
        updatedAt = 1780900800000, audioLocalPath = "", durationSec = 142,
        rawTranscript = "", formattedTranscript = "Начать с главного: собрать мысли, обсудить детали и наметить первый шаг.",
        userEditedText = "", syncState = NoteSyncState.SYNCED, remoteTaskId = null,
        remoteStatus = null, lastSyncAt = null, lastError = null, isTitleCustomized = true
    ),
    NoteEntity(
        id = "preview-local", title = "Мысль по дороге домой", createdAt = 1780890800000,
        updatedAt = 1780890800000, audioLocalPath = "", durationSec = 58,
        rawTranscript = "", formattedTranscript = "", userEditedText = "",
        syncState = NoteSyncState.LOCAL_ONLY, remoteTaskId = null,
        remoteStatus = null, lastSyncAt = null, lastError = null, isTitleCustomized = true
    )
)

@Preview(name = "Library", widthDp = 390, heightDp = 900, showBackground = true)
@Preview(name = "Small phone / large text", widthDp = 320, heightDp = 900, fontScale = 1.3f, showBackground = true)
@Composable
private fun LibraryPreview() {
    YouScriptorTheme { NotesContent(previewNotes, emptySet(), {}, {}, {}, {}, {}, {}, {}, {}, {}) }
}

@Preview(name = "Selection", widthDp = 360, heightDp = 900, showBackground = true)
@Composable
private fun SelectionPreview() {
    YouScriptorTheme { NotesContent(previewNotes, setOf("preview-ready"), {}, {}, {}, {}, {}, {}, {}, {}, {}) }
}
