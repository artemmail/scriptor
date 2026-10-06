package com.youscriptor.mobile.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.model.NoteSyncState
import com.youscriptor.mobile.ui.theme.ScriptorColors
import java.text.DateFormat
import java.util.Date

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun ScreenHeader(title: String, onBack: () -> Unit, actions: @Composable RowScope.() -> Unit = {}) {
    TopAppBar(
        title = { Text(title, style = MaterialTheme.typography.titleMedium) },
        navigationIcon = { IconButton(onClick = onBack) { Icon(NoteIcons.Back, "Назад") } },
        actions = actions,
        colors = TopAppBarDefaults.topAppBarColors(containerColor = ScriptorColors.Cream),
        windowInsets = WindowInsets(0, 0, 0, 0)
    )
}

@Composable
internal fun Eyebrow(text: String, color: Color = ScriptorColors.Olive) {
    Text(text.uppercase(), color = color, style = MaterialTheme.typography.labelMedium)
}

@Composable
internal fun PrimaryAction(text: String, icon: ImageVector, onClick: () -> Unit,
    modifier: Modifier = Modifier, enabled: Boolean = true) {
    Button(onClick = onClick, modifier = modifier.heightIn(min = 54.dp), enabled = enabled,
        shape = RoundedCornerShape(18.dp),
        colors = ButtonDefaults.buttonColors(containerColor = ScriptorColors.Lime,
            contentColor = ScriptorColors.Forest), contentPadding = PaddingValues(18.dp, 14.dp)) {
        Icon(icon, null, Modifier.size(20.dp))
        Spacer(Modifier.width(10.dp))
        Text(text)
    }
}

@Composable
internal fun OutlineAction(text: String, icon: ImageVector, onClick: () -> Unit,
    modifier: Modifier = Modifier, enabled: Boolean = true) {
    OutlinedButton(onClick = onClick, modifier = modifier.heightIn(min = 48.dp), enabled = enabled,
        shape = RoundedCornerShape(16.dp), border = BorderStroke(1.dp, ScriptorColors.Border)) {
        Icon(icon, null, Modifier.size(19.dp))
        Spacer(Modifier.width(8.dp))
        Text(text)
    }
}

@Composable
internal fun PaperCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Surface(modifier, shape = RoundedCornerShape(24.dp), color = Color.White,
        border = BorderStroke(1.dp, ScriptorColors.Border)) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp), content = content)
    }
}

@Composable
internal fun IconTile(icon: ImageVector, modifier: Modifier = Modifier) {
    Surface(modifier.size(46.dp), shape = RoundedCornerShape(15.dp),
        color = ScriptorColors.Mist, contentColor = ScriptorColors.Olive) {
        Box(contentAlignment = Alignment.Center) { Icon(icon, null, Modifier.size(23.dp)) }
    }
}

/** Decorative voice motif, deliberately not an audio amplitude or playback progress meter. */
@Composable
internal fun VoiceMotif(modifier: Modifier = Modifier, color: Color = ScriptorColors.Lime) {
    Canvas(modifier.clearAndSetSemantics { }) {
        val bars = listOf(.12f, .22f, .43f, .28f, .62f, .88f, .55f, .36f, .72f, 1f,
            .68f, .42f, .82f, .56f, .32f, .65f, .44f, .25f, .36f, .18f, .12f)
        val step = size.width / bars.size
        bars.forEachIndexed { index, fraction ->
            val x = step * (index + .5f)
            val half = (size.height * fraction / 2).coerceAtLeast(2.dp.toPx())
            drawLine(color, Offset(x, size.height / 2 - half), Offset(x, size.height / 2 + half),
                strokeWidth = (step * .35f).coerceAtMost(5.dp.toPx()), cap = StrokeCap.Round)
        }
    }
}

@Composable
internal fun SyncStateChip(state: NoteSyncState) {
    val busy = state == NoteSyncState.SYNC_PENDING || state == NoteSyncState.SYNCING
    val failed = state == NoteSyncState.FAILED
    val label = when (state) {
        NoteSyncState.LOCAL_ONLY -> "На устройстве"
        NoteSyncState.SYNC_PENDING -> "В очереди"
        NoteSyncState.SYNCING -> "Расшифровывается"
        NoteSyncState.SYNCED -> "Готово"
        NoteSyncState.FAILED -> "Нужен повтор"
    }
    Surface(shape = CircleShape,
        color = if (failed) MaterialTheme.colorScheme.errorContainer else ScriptorColors.Mist,
        contentColor = if (failed) MaterialTheme.colorScheme.error else ScriptorColors.Olive) {
        Row(Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            if (busy) CircularProgressIndicator(Modifier.size(12.dp), strokeWidth = 1.5.dp)
            else Icon(if (failed) NoteIcons.Warning else if (state == NoteSyncState.SYNCED) NoteIcons.Check else NoteIcons.Audio,
                null, Modifier.size(14.dp))
            Text(label, style = MaterialTheme.typography.labelMedium)
        }
    }
}

@Composable
internal fun ErrorNotice(message: String) {
    Surface(color = MaterialTheme.colorScheme.errorContainer, shape = RoundedCornerShape(16.dp)) {
        Row(Modifier.padding(14.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(NoteIcons.Warning, null, Modifier.size(20.dp), tint = MaterialTheme.colorScheme.error)
            Text(message, color = MaterialTheme.colorScheme.onErrorContainer,
                style = MaterialTheme.typography.bodySmall)
        }
    }
}

internal fun NoteEntity.transcript() = userEditedText.ifBlank { formattedTranscript.ifBlank { rawTranscript } }
internal fun NoteEntity.canTranscribe() = syncState != NoteSyncState.SYNC_PENDING && syncState != NoteSyncState.SYNCING
internal fun NoteEntity.transcriptionActionLabel() = when (syncState) {
    NoteSyncState.LOCAL_ONLY -> "Расшифровать"
    NoteSyncState.SYNC_PENDING -> "В очереди"
    NoteSyncState.SYNCING -> "Обработка…"
    NoteSyncState.SYNCED -> "Расшифровать снова"
    NoteSyncState.FAILED -> "Повторить"
}
internal fun formatRecordedAt(timestamp: Long): String =
    DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT).format(Date(timestamp))
internal fun formatDuration(totalSeconds: Long): String {
    val seconds = totalSeconds.coerceAtLeast(0)
    return if (seconds >= 3600) "%d:%02d:%02d".format(seconds / 3600, seconds % 3600 / 60, seconds % 60)
    else "%02d:%02d".format(seconds / 60, seconds % 60)
}
