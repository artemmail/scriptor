package com.youscriptor.mobile.ui

import android.content.Intent
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.youscriptor.mobile.data.local.NoteEntity
import com.youscriptor.mobile.data.recording.AudioPlayerController
import com.youscriptor.mobile.ui.theme.ScriptorColors
import kotlinx.coroutines.launch

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun NoteDetailScreen(note: NoteEntity?, viewModel: MainViewModel, onBack: () -> Unit) {
    if (note == null) {
        Scaffold(topBar = { ScreenHeader("Заметка", onBack) }, contentWindowInsets = WindowInsets(0, 0, 0, 0)) { padding ->
            Column(Modifier.padding(padding).padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Text("Заметка недоступна", style = MaterialTheme.typography.headlineMedium)
                Text("Она могла быть удалена. Вернитесь к списку записей.")
                OutlineAction("К заметкам", NoteIcons.Back, onBack)
            }
        }
        return
    }
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val player = remember(note.id) { AudioPlayerController() }
    var playing by remember(note.id) { mutableStateOf(false) }
    val playbackLevels = remember(note.id) { mutableStateListOf<Float>().apply { repeat(44) { add(0f) } } }
    val clearPlaybackWave = { playbackLevels.indices.forEach { playbackLevels[it] = 0f } }
    var draftTitle by rememberSaveable(note.id) { mutableStateOf(note.title) }
    var draftText by rememberSaveable(note.id) { mutableStateOf(note.transcript()) }
    var titleBaseline by rememberSaveable(note.id) { mutableStateOf(note.title) }
    var textBaseline by rememberSaveable(note.id) { mutableStateOf(note.transcript()) }
    var confirmDelete by remember { mutableStateOf(false) }
    var confirmLeave by remember { mutableStateOf(false) }
    var showRaw by rememberSaveable { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val dirty = draftTitle != titleBaseline || draftText != textBaseline
    // Incoming sync results update clean fields, without resetting a draft being edited.
    LaunchedEffect(note.title, note.transcript()) {
        if (draftTitle == titleBaseline) draftTitle = note.title
        if (draftText == textBaseline) draftText = note.transcript()
        titleBaseline = note.title
        textBaseline = note.transcript()
    }
    DisposableEffect(player) { onDispose { player.stop() } }
    val leave = { if (dirty) confirmLeave = true else onBack() }
    BackHandler(dirty) { confirmLeave = true }
    if (confirmLeave) AlertDialog(onDismissRequest = { confirmLeave = false },
        title = { Text("Есть несохранённые правки") }, text = { Text("Вернитесь в редактор, чтобы сохранить изменения.") },
        confirmButton = { TextButton(onClick = onBack) { Text("Выйти без сохранения") } },
        dismissButton = { TextButton(onClick = { confirmLeave = false }) { Text("К редактору") } })
    if (confirmDelete) AlertDialog(onDismissRequest = { confirmDelete = false },
        icon = { Icon(NoteIcons.Delete, null) }, title = { Text("Удалить заметку?") },
        text = { Text("Заметка и её аудиозапись будут удалены с этого устройства.") },
        confirmButton = { TextButton(onClick = { player.stop(); viewModel.deleteNote(note.id); onBack() }) { Text("Удалить") } },
        dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text("Оставить") } })
    Scaffold(topBar = { ScreenHeader("Ваша заметка", leave) {
        IconButton(onClick = { confirmDelete = true }) { Icon(NoteIcons.Delete, "Удалить заметку") }
    } }, contentWindowInsets = WindowInsets(0, 0, 0, 0)) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).imePadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)) {
            SyncStateChip(note.syncState)
            Text(note.title, style = MaterialTheme.typography.headlineLarge)
            Text(formatRecordedAt(note.createdAt), color = ScriptorColors.Muted, style = MaterialTheme.typography.bodySmall)
            Surface(color = ScriptorColors.Forest, contentColor = ScriptorColors.Cream, shape = RoundedCornerShape(24.dp)) {
                Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        FilledIconButton(onClick = {
                            runCatching {
                                if (playing) { player.stop(); playing = false; clearPlaybackWave() }
                                else {
                                    clearPlaybackWave()
                                    player.play(note.audioLocalPath,
                                        onLevel = { level ->
                                            val smoothed = playbackLevels.last() * .28f + level * .72f
                                            playbackLevels.removeAt(0)
                                            playbackLevels.add(smoothed)
                                        },
                                        onComplete = { playing = false; clearPlaybackWave() })
                                    playing = true
                                }
                            }.onFailure {
                                playing = false; clearPlaybackWave()
                                error = "Не удалось открыть аудио. Возможно, файл был удалён."
                            }
                        }, modifier = Modifier.size(56.dp), colors = IconButtonDefaults.filledIconButtonColors(
                            containerColor = ScriptorColors.Lime, contentColor = ScriptorColors.Forest)) {
                            Icon(if (playing) NoteIcons.Stop else NoteIcons.Play,
                                if (playing) "Остановить аудио" else "Слушать запись")
                        }
                        PlaybackWaveform(playbackLevels, playing, Modifier.weight(1f).height(44.dp))
                        Text(formatDuration(note.durationSec), style = MaterialTheme.typography.labelLarge)
                    }
                    Text(if (playing) "Воспроизводится запись" else "Оригинальная аудиозапись", style = MaterialTheme.typography.bodySmall)
                }
            }
            note.lastError?.takeIf { it.isNotBlank() }?.let { ErrorNotice(it) }
            error?.let { ErrorNotice(it) }
            PrimaryAction(note.transcriptionActionLabel(), NoteIcons.Cloud, { viewModel.syncNote(note.id) },
                Modifier.fillMaxWidth(), enabled = note.canTranscribe())
            PaperCard {
                Eyebrow("Редактор заметки")
                OutlinedTextField(draftTitle, { draftTitle = it }, Modifier.fillMaxWidth(),
                    label = { Text("Название") }, shape = RoundedCornerShape(16.dp), maxLines = 3)
                TextButton(onClick = { viewModel.saveTitle(note.id, draftTitle) },
                    enabled = draftTitle.isNotBlank() && draftTitle != titleBaseline) {
                    Icon(NoteIcons.Check, null, Modifier.size(18.dp)); Spacer(Modifier.width(8.dp)); Text("Сохранить название")
                }
                HorizontalDivider(color = ScriptorColors.Border)
                Text("Текст заметки", style = MaterialTheme.typography.titleMedium)
                if (note.transcript().isBlank()) Text("После расшифровки текст появится здесь. Вы также можете записать мысль вручную.",
                    color = ScriptorColors.Muted, style = MaterialTheme.typography.bodySmall)
                OutlinedTextField(draftText, { draftText = it }, Modifier.fillMaxWidth().heightIn(min = 260.dp, max = 480.dp),
                    placeholder = { Text("Здесь начинается ваша мысль…") },
                    label = { Text("Расшифровка") }, shape = RoundedCornerShape(16.dp), minLines = 8)
                Text("${draftText.length} символов", style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
                PrimaryAction("Сохранить текст", NoteIcons.Save, { viewModel.saveEditedText(note.id, draftText) },
                    Modifier.fillMaxWidth(), enabled = draftText != textBaseline)
            }
            PaperCard {
                Text("Поделиться", style = MaterialTheme.typography.titleMedium)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlineAction("Текст", NoteIcons.Text, {
                        context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                            type = "text/plain"; putExtra(Intent.EXTRA_TEXT, "$draftTitle\n\n$draftText")
                        }, "Поделиться текстом"))
                    }, enabled = draftText.isNotBlank())
                    OutlineAction("Аудио", NoteIcons.Audio, {
                        viewModel.shareAudioIntent(note.id)?.let { context.startActivity(Intent.createChooser(it, "Поделиться аудио")) }
                    })
                    OutlineAction("Субтитры SRT", NoteIcons.Captions, {
                        scope.launch { viewModel.shareSrtIntent(note.id)?.let { context.startActivity(Intent.createChooser(it, "Поделиться субтитрами")) } }
                    }, enabled = note.remoteTaskId != null)
                }
            }
            if (note.rawTranscript.isNotBlank()) {
                TextButton(onClick = { showRaw = !showRaw }) { Text(if (showRaw) "Скрыть исходную расшифровку" else "Исходная расшифровка") }
                if (showRaw) PaperCard { SelectionContainer { Text(note.rawTranscript) } }
            }
        }
    }
}

@Composable
private fun PlaybackWaveform(levels: List<Float>, playing: Boolean, modifier: Modifier = Modifier) {
    val description = if (playing) "Уровень звука воспроизводимой записи" else "Воспроизведение остановлено"
    Canvas(modifier.semantics { contentDescription = description }) {
        val middle = size.height / 2f
        drawLine(ScriptorColors.Lime.copy(alpha = .22f), Offset(0f, middle),
            Offset(size.width, middle), strokeWidth = 1.dp.toPx())
        val step = size.width / levels.size
        levels.forEachIndexed { index, level ->
            val halfHeight = (size.height * .46f * level).coerceAtLeast(1.5.dp.toPx())
            val x = step * (index + .5f)
            drawLine(
                color = ScriptorColors.Lime.copy(alpha = if (playing) 1f else .42f),
                start = Offset(x, middle - halfHeight),
                end = Offset(x, middle + halfHeight),
                strokeWidth = (step * .5f).coerceAtMost(4.dp.toPx()),
                cap = StrokeCap.Round
            )
        }
    }
}
