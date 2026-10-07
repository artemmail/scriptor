package com.youscriptor.mobile.ui

import android.Manifest
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.youscriptor.mobile.data.recording.RecorderManager
import com.youscriptor.mobile.ui.theme.ScriptorColors
import kotlinx.coroutines.delay
import kotlin.math.sqrt

@Composable
internal fun RecordScreen(viewModel: MainViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    val recorder = remember { RecorderManager(context) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var title by rememberSaveable { mutableStateOf("") }
    var recording by remember { mutableStateOf(false) }
    var paused by remember { mutableStateOf(false) }
    var elapsed by remember { mutableLongStateOf(0L) }
    var error by remember { mutableStateOf<String?>(null) }
    var confirmDiscard by remember { mutableStateOf(false) }
    val audioLevels = remember { mutableStateListOf<Float>().apply { repeat(44) { add(0f) } } }
    val leave = { if (recording) confirmDiscard = true else onBack() }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) runCatching { recorder.start() }.onSuccess {
            recording = true; paused = false; elapsed = 0; error = null
        }.onFailure { error = "Не удалось включить микрофон. Проверьте, не использует ли его другое приложение." }
        else error = "Для записи разрешите доступ к микрофону в настройках Android."
    }
    LaunchedEffect(recording, paused) {
        while (recording && !paused) { delay(1_000); elapsed++ }
    }
    LaunchedEffect(recording, paused) {
        if (!recording) {
            audioLevels.indices.forEach { audioLevels[it] = 0f }
        }
        var smoothed = audioLevels.lastOrNull() ?: 0f
        while (recording && !paused) {
            val peak = recorder.amplitude().coerceIn(0, 32_767)
            val measured = sqrt(((peak - 180).coerceAtLeast(0) / 32_767f))
            smoothed = smoothed * .28f + measured * .72f
            audioLevels.removeAt(0)
            audioLevels.add(smoothed)
            delay(70)
        }
    }
    // No background recorder service: visibly pause when the app leaves the foreground.
    DisposableEffect(lifecycle, recorder) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP && recording && !paused) {
                runCatching { recorder.pause() }.onSuccess { paused = true }
            }
        }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer); recorder.cancel() }
    }
    BackHandler(recording) { confirmDiscard = true }
    if (confirmDiscard) AlertDialog(onDismissRequest = { confirmDiscard = false },
        icon = { Icon(NoteIcons.Mic, null) }, title = { Text("Удалить эту запись?") },
        text = { Text("Запись ещё не сохранена. Можно вернуться и сохранить её.") },
        confirmButton = { TextButton(onClick = { recorder.cancel(); recording = false; onBack() }) { Text("Удалить запись") } },
        dismissButton = { TextButton(onClick = { confirmDiscard = false }) { Text("Продолжить") } })

    Scaffold(topBar = { ScreenHeader("Новая запись", leave) }, contentWindowInsets = WindowInsets(0, 0, 0, 0)) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).imePadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Eyebrow("Сохраните важное")
                Text("Просто говорите.", style = MaterialTheme.typography.displaySmall, color = ScriptorColors.Olive)
                Text("Идеям не нужна клавиатура.", color = ScriptorColors.Muted)
            }
            Surface(color = ScriptorColors.Forest, contentColor = ScriptorColors.Cream, shape = RoundedCornerShape(28.dp)) {
                Column(Modifier.fillMaxWidth().padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(24.dp)) {
                    Surface(shape = CircleShape, color = ScriptorColors.Lime.copy(alpha = .12f)) {
                        Icon(NoteIcons.Mic, null, Modifier.padding(18.dp).size(30.dp), tint = ScriptorColors.Lime)
                    }
                    Text(formatDuration(elapsed), style = MaterialTheme.typography.headlineLarge.copy(fontSize = 48.sp, lineHeight = 56.sp))
                    LiveRecordingWaveform(audioLevels, recording, paused)
                    Text(if (!recording) "Готовы, когда будете готовы" else if (paused) "Запись на паузе" else "Идёт запись • микрофон включён",
                        color = ScriptorColors.Lime, style = MaterialTheme.typography.labelLarge)
                }
            }
            OutlinedTextField(title, { title = it }, Modifier.fillMaxWidth(),
                label = { Text("Название · необязательно") }, placeholder = { Text("О чём эта мысль?") },
                supportingText = { Text("Без названия — подберём его после расшифровки.") },
                shape = RoundedCornerShape(18.dp), maxLines = 3)
            error?.let { ErrorNotice(it) }
            if (!recording) PrimaryAction("Начать запись", NoteIcons.Mic,
                { permission.launch(Manifest.permission.RECORD_AUDIO) }, Modifier.fillMaxWidth())
            else {
                OutlineAction(if (paused) "Продолжить запись" else "Пауза", if (paused) NoteIcons.Play else NoteIcons.Pause, {
                    runCatching { if (paused) recorder.resume() else recorder.pause() }
                        .onSuccess { paused = !paused }.onFailure { error = "Не удалось изменить состояние записи." }
                }, Modifier.fillMaxWidth())
                PrimaryAction("Завершить и сохранить", NoteIcons.Check, {
                    runCatching { recorder.stop() }.onSuccess { draft ->
                        recording = false; paused = false
                        if (draft != null) { viewModel.createRecordedNote(title, draft.filePath, draft.durationSeconds); onBack() }
                    }.onFailure { recording = false; paused = false; error = "Запись слишком короткая или микрофон недоступен. Попробуйте ещё раз." }
                }, Modifier.fillMaxWidth())
                TextButton(onClick = { confirmDiscard = true }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    Text("Отменить запись")
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Icon(NoteIcons.Notes, null, Modifier.size(18.dp), tint = ScriptorColors.Muted)
                Text("Сначала сохраним аудио на устройстве. Отправить его на расшифровку можно из заметки.",
                    style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
            }
        }
    }
}

@Composable
private fun LiveRecordingWaveform(levels: List<Float>, recording: Boolean, paused: Boolean) {
    val description = when {
        paused -> "Запись на паузе, показан последний уровень звука"
        recording -> "Уровень звука в реальном времени"
        else -> "Микрофон выключен"
    }
    Canvas(Modifier.fillMaxWidth().height(64.dp).semantics { contentDescription = description }) {
        val middle = size.height / 2f
        drawLine(ScriptorColors.Lime.copy(alpha = .22f), Offset(0f, middle),
            Offset(size.width, middle), strokeWidth = 1.dp.toPx())
        val step = size.width / levels.size
        levels.forEachIndexed { index, level ->
            val halfHeight = (size.height * .46f * level).coerceAtLeast(1.5.dp.toPx())
            val x = step * (index + .5f)
            drawLine(
                color = ScriptorColors.Lime.copy(alpha = if (paused || !recording) .42f else 1f),
                start = Offset(x, middle - halfHeight),
                end = Offset(x, middle + halfHeight),
                strokeWidth = (step * .5f).coerceAtMost(4.dp.toPx()),
                cap = StrokeCap.Round
            )
        }
    }
}
