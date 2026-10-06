package com.youscriptor.mobile.ui

import android.content.Intent
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.model.NoteSyncState
import com.youscriptor.mobile.ui.theme.ScriptorColors
import com.youscriptor.mobile.ui.theme.YouScriptorTheme

@Composable
internal fun NotesScreen(viewModel: MainViewModel, onRecord: () -> Unit, onSettings: () -> Unit,
    onOpen: (String) -> Unit) {
    val notes by viewModel.notes.collectAsStateWithLifecycle()
    val selected by viewModel.selectedNoteIds.collectAsStateWithLifecycle()
    val context = LocalContext.current
    BackHandler(selected.isNotEmpty()) { viewModel.clearSelection() }
    NotesContent(notes, selected, onRecord, onSettings, onOpen, viewModel::toggleSelection,
        viewModel::clearSelection, viewModel::syncNote, viewModel::syncSelectedNotes,
        { viewModel.shareSelectedTextIntent()?.let { context.startActivity(Intent.createChooser(it, "Поделиться текстом")) } },
        { viewModel.shareSelectedAudioIntent()?.let { context.startActivity(Intent.createChooser(it, "Поделиться аудио")) } })
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun NotesContent(notes: List<NoteEntity>, selected: Set<String>, onRecord: () -> Unit,
    onSettings: () -> Unit, onOpen: (String) -> Unit, onSelect: (String) -> Unit,
    onClearSelection: () -> Unit, onSync: (String) -> Unit, onSyncSelected: () -> Unit,
    onShareText: () -> Unit, onShareAudio: () -> Unit) {
    var query by rememberSaveable { mutableStateOf("") }
    var filter by rememberSaveable { mutableIntStateOf(0) }
    val visible = notes.filter { note ->
        (query.isBlank() || note.title.contains(query.trim(), true) || note.transcript().contains(query.trim(), true)) &&
            when (filter) {
                1 -> note.syncState == NoteSyncState.LOCAL_ONLY
                2 -> note.syncState == NoteSyncState.SYNCED
                else -> true
            }
    }
    Scaffold(contentWindowInsets = WindowInsets(0, 0, 0, 0),
        floatingActionButton = {
            if (notes.isNotEmpty() && selected.isEmpty()) ExtendedFloatingActionButton(
                onClick = onRecord, icon = { Icon(NoteIcons.Mic, null) }, text = { Text("Записать мысль") },
                containerColor = ScriptorColors.Lime, contentColor = ScriptorColors.Forest,
                shape = RoundedCornerShape(20.dp))
        }) { padding ->
        LazyColumn(Modifier.fillMaxSize().padding(padding).imePadding(),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 104.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)) {
            item {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    IconTile(NoteIcons.Audio)
                    Text("YouScriptor", Modifier.weight(1f).padding(start = 10.dp), style = MaterialTheme.typography.titleMedium)
                    IconButton(onClick = onSettings) { Icon(NoteIcons.Settings, "Аккаунт и настройки") }
                }
            }
            item {
                Column(Modifier.padding(top = 12.dp, bottom = 4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Eyebrow("Личное пространство")
                    Text("Ваши мысли,", style = MaterialTheme.typography.headlineLarge)
                    Text("в одном месте.", style = MaterialTheme.typography.displaySmall, color = ScriptorColors.Olive)
                    Text("Записывайте голосом. Возвращайтесь к важному.",
                        Modifier.padding(top = 6.dp), color = ScriptorColors.Muted, style = MaterialTheme.typography.bodyMedium)
                }
            }
            if (notes.isEmpty()) {
                item {
                    Surface(color = ScriptorColors.Forest, contentColor = ScriptorColors.Cream,
                        shape = RoundedCornerShape(28.dp)) {
                        Column(Modifier.fillMaxWidth().padding(24.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
                            Eyebrow("Из голоса — в текст", ScriptorColors.Lime)
                            VoiceMotif(Modifier.fillMaxWidth().height(84.dp))
                            Text("Начните с одной мысли", style = MaterialTheme.typography.headlineMedium)
                            Text("Идея, встреча или план на день. Сохраните запись, а когда понадобится — получите текст.",
                                color = ScriptorColors.Cream.copy(alpha = .8f))
                            PrimaryAction("Создать первую запись", NoteIcons.Mic, onRecord, Modifier.fillMaxWidth())
                        }
                    }
                }
                item { Text("Аудио хранится на устройстве. Для расшифровки войдите в аккаунт YouScriptor.",
                    color = ScriptorColors.Muted, style = MaterialTheme.typography.bodySmall) }
            } else {
                item {
                    OutlinedTextField(query, { query = it }, Modifier.fillMaxWidth(), singleLine = true,
                        placeholder = { Text("Найти мысль или запись") },
                        leadingIcon = { Icon(NoteIcons.Search, null) },
                        trailingIcon = { if (query.isNotEmpty()) IconButton(onClick = { query = "" }) { Icon(NoteIcons.Close, "Очистить поиск") } },
                        shape = RoundedCornerShape(18.dp), colors = OutlinedTextFieldDefaults.colors(
                            unfocusedBorderColor = ScriptorColors.Border, unfocusedContainerColor = Color.White))
                }
                item {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf("Все · ${notes.size}", "На устройстве", "Готово").forEachIndexed { index, label ->
                            FilterChip(selected = filter == index, onClick = { filter = index }, label = { Text(label) },
                                shape = RoundedCornerShape(14.dp), colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = ScriptorColors.Forest, selectedLabelColor = ScriptorColors.Cream))
                        }
                    }
                }
                if (selected.isNotEmpty()) item {
                    PaperCard {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("Выбрано: ${selected.size}", Modifier.weight(1f), style = MaterialTheme.typography.titleMedium)
                            IconButton(onClick = onClearSelection) { Icon(NoteIcons.Close, "Снять выделение") }
                        }
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlineAction("Текст", NoteIcons.Text, onShareText)
                            OutlineAction("Аудио", NoteIcons.Audio, onShareAudio)
                        }
                        PrimaryAction("Расшифровать выбранные", NoteIcons.Cloud, onSyncSelected, Modifier.fillMaxWidth())
                    }
                }
                item {
                    Text(if (query.isNotBlank() || filter != 0) "Найдено: ${visible.size}" else "ПОСЛЕДНИЕ ЗАПИСИ",
                        style = MaterialTheme.typography.labelMedium, color = ScriptorColors.Muted)
                }
                if (visible.isEmpty()) item {
                    PaperCard {
                        IconTile(NoteIcons.Search)
                        Text("Пока ничего не найдено", style = MaterialTheme.typography.titleMedium)
                        Text("Попробуйте другую фразу или выберите все записи.", color = ScriptorColors.Muted)
                        TextButton(onClick = { query = ""; filter = 0 }) { Text("Сбросить поиск и фильтры") }
                    }
                }
                items(visible, key = { it.id }) { note ->
                    NoteCard(note, note.id in selected, selected.isNotEmpty(),
                        { if (selected.isEmpty()) onOpen(note.id) else onSelect(note.id) },
                        { onSelect(note.id) }, { onSync(note.id) })
                }
                if (selected.isEmpty() && visible.isNotEmpty()) item {
                    Text("Удерживайте карточку, чтобы выбрать несколько заметок.",
                        style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun NoteCard(note: NoteEntity, selected: Boolean, selecting: Boolean,
    onOpen: () -> Unit, onSelect: () -> Unit, onSync: () -> Unit) {
    Surface(Modifier.fillMaxWidth().semantics { this.selected = selected }
        .combinedClickable(onClick = onOpen, onLongClick = onSelect,
            onLongClickLabel = "Выбрать заметку", role = Role.Button),
        shape = RoundedCornerShape(24.dp), color = if (selected) ScriptorColors.Mist else Color.White,
        border = BorderStroke(1.dp, if (selected) ScriptorColors.Olive else ScriptorColors.Border)) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    SyncStateChip(note.syncState)
                    Text(formatDuration(note.durationSec), color = ScriptorColors.Muted, style = MaterialTheme.typography.labelMedium)
                }
                if (selecting) Checkbox(selected, { onSelect() })
            }
            Text(note.title, style = MaterialTheme.typography.titleLarge, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (note.transcript().isNotBlank()) Text(note.transcript(), maxLines = 3, overflow = TextOverflow.Ellipsis,
                color = ScriptorColors.Muted, style = MaterialTheme.typography.bodyMedium)
            else Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                VoiceMotif(Modifier.width(70.dp).height(26.dp), ScriptorColors.Olive)
                Text("Голосовая заметка", color = ScriptorColors.Muted, style = MaterialTheme.typography.bodySmall)
            }
            note.lastError?.takeIf { it.isNotBlank() }?.let { ErrorNotice(it) }
            Text(formatRecordedAt(note.createdAt), style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
            if (!selecting) {
                HorizontalDivider(color = ScriptorColors.Border)
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    TextButton(onClick = onSync, enabled = note.canTranscribe(), modifier = Modifier.weight(1f)) {
                        Icon(NoteIcons.Cloud, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(note.transcriptionActionLabel())
                    }
                    IconButton(onClick = onOpen) { Icon(NoteIcons.Arrow, "Открыть заметку") }
                }
            }
        }
    }
}

@Preview(name = "First note", widthDp = 360, heightDp = 800, showBackground = true)
@Composable
private fun EmptyNotesPreview() {
    YouScriptorTheme { NotesContent(emptyList(), emptySet(), {}, {}, {}, {}, {}, {}, {}, {}, {}) }
}
