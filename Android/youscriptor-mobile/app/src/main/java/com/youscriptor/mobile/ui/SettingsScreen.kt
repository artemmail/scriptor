package com.youscriptor.mobile.ui

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.youscriptor.mobile.data.model.AuthProvider
import com.youscriptor.mobile.data.network.DEFAULT_SERVER_BASE_URL
import com.youscriptor.mobile.ui.theme.ScriptorColors
import java.net.URI

@Composable
internal fun SettingsScreen(viewModel: MainViewModel, onBack: () -> Unit) {
    val baseUrl by viewModel.baseUrl.collectAsStateWithLifecycle()
    val session by viewModel.session.collectAsStateWithLifecycle()
    val summary by viewModel.subscriptionSummary.collectAsStateWithLifecycle()
    val context = LocalContext.current
    var draftUrl by rememberSaveable(baseUrl) { mutableStateOf(baseUrl) }
    var advanced by rememberSaveable { mutableStateOf(false) }
    var confirmLogout by remember { mutableStateOf(false) }
    var browserError by remember { mutableStateOf<String?>(null) }
    if (confirmLogout) AlertDialog(onDismissRequest = { confirmLogout = false },
        title = { Text("Выйти из аккаунта?") }, text = { Text("Ваши записи останутся на устройстве.") },
        confirmButton = { TextButton(onClick = { viewModel.logout(); confirmLogout = false }) { Text("Выйти") } },
        dismissButton = { TextButton(onClick = { confirmLogout = false }) { Text("Остаться") } })
    Scaffold(topBar = { ScreenHeader("Аккаунт и настройки", onBack) },
        contentWindowInsets = WindowInsets(0, 0, 0, 0)) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).imePadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Eyebrow("YouScriptor рядом")
                Text("Ваше пространство.", style = MaterialTheme.typography.displaySmall, color = ScriptorColors.Olive)
            }
            PaperCard {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    IconTile(NoteIcons.User)
                    Column(Modifier.weight(1f)) {
                        Text(session?.displayName?.ifBlank { session?.email.orEmpty() } ?: "Добро пожаловать",
                            style = MaterialTheme.typography.titleMedium)
                        Text(if (session != null) "Аккаунт подключён" else "Войдите для расшифровки", color = ScriptorColors.Muted,
                            style = MaterialTheme.typography.bodySmall)
                    }
                }
                val current = session
                if (current == null) {
                    Text("Используйте тот же аккаунт, что и на сайте, чтобы отправлять записи на расшифровку.", color = ScriptorColors.Muted)
                    PrimaryAction("Войти через Google", NoteIcons.Arrow, {
                        context.findActivity()?.let { viewModel.startLogin(it, AuthProvider.Google) }
                    }, Modifier.fillMaxWidth())
                    OutlineAction("Войти через Яндекс", NoteIcons.Arrow, {
                        context.findActivity()?.let { viewModel.startLogin(it, AuthProvider.Yandex) }
                    }, Modifier.fillMaxWidth())
                } else {
                    Text(current.email, color = ScriptorColors.Muted)
                    OutlineAction("Обновить подключение", NoteIcons.Refresh, { viewModel.refreshSession() }, Modifier.fillMaxWidth())
                    TextButton(onClick = { confirmLogout = true }) {
                        Icon(NoteIcons.Logout, null, Modifier.size(18.dp)); Spacer(Modifier.width(8.dp)); Text("Выйти из аккаунта")
                    }
                }
            }
            if (session != null) {
                Surface(color = ScriptorColors.Forest, contentColor = ScriptorColors.Cream, shape = RoundedCornerShape(28.dp)) {
                    Column(Modifier.fillMaxWidth().padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        Eyebrow("Ваш тариф", ScriptorColors.Lime)
                        val currentSummary = summary
                        if (currentSummary != null) {
                            Text(currentSummary.planName ?: "Бесплатный", style = MaterialTheme.typography.headlineMedium)
                            Text("${currentSummary.remainingTranscriptionMinutes}", style = MaterialTheme.typography.displaySmall,
                                color = ScriptorColors.Lime)
                            Text("минут расшифровки осталось")
                            HorizontalDivider(color = ScriptorColors.Cream.copy(alpha = .2f))
                            Text("Видео доступно: ${currentSummary.remainingVideos}", style = MaterialTheme.typography.bodyMedium)
                            TextButton(onClick = {
                                runCatching {
                                    val target = URI(baseUrl).resolve(currentSummary.billingUrl)
                                    require(target.scheme == "https" || target.scheme == "http")
                                    context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(target.toString())))
                                }.onFailure { browserError = "Не удалось открыть страницу тарифа в браузере." }
                            }, colors = ButtonDefaults.textButtonColors(contentColor = ScriptorColors.Lime)) {
                                Text("Управлять тарифом на сайте")
                            }
                        } else Text("Загрузите актуальный остаток минут и информацию о тарифе.")
                        PrimaryAction("Обновить баланс", NoteIcons.Refresh, { viewModel.loadSubscriptionSummary() }, Modifier.fillMaxWidth())
                    }
                }
            }
            browserError?.let { ErrorNotice(it) }
            PaperCard {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    IconTile(NoteIcons.Cloud)
                    Text("Подключение к сайту", Modifier.weight(1f), style = MaterialTheme.typography.titleMedium)
                }
                Text("Запись работает без интернета. Вход в аккаунт и расшифровка требуют подключения к серверу.",
                    color = ScriptorColors.Muted, style = MaterialTheme.typography.bodyMedium)
                TextButton(onClick = { advanced = !advanced }) { Text(if (advanced) "Скрыть адрес сервера" else "Изменить адрес сервера") }
                if (advanced) {
                    OutlinedTextField(draftUrl, { draftUrl = it }, Modifier.fillMaxWidth(),
                        label = { Text("Адрес сервера") }, singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri), shape = RoundedCornerShape(16.dp))
                    PrimaryAction("Сохранить адрес", NoteIcons.Check, {
                        viewModel.updateBaseUrl(draftUrl); viewModel.saveBaseUrl()
                    }, Modifier.fillMaxWidth(), enabled = draftUrl.isNotBlank())
                    TextButton(onClick = { draftUrl = DEFAULT_SERVER_BASE_URL }) { Text("Восстановить стандартный адрес") }
                }
            }
            Text("YouScriptor · Голос становится текстом", Modifier.align(Alignment.CenterHorizontally),
                style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
        }
    }
}

private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
