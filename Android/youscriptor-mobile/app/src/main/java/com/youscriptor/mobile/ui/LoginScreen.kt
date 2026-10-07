package com.youscriptor.mobile.ui

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.youscriptor.mobile.data.model.AuthProvider
import com.youscriptor.mobile.ui.theme.ScriptorColors

@Composable
internal fun LoginScreen(viewModel: MainViewModel, onBack: () -> Unit) {
    val context = LocalContext.current
    BackHandler { onBack() }

    Scaffold(topBar = { ScreenHeader("Войти в аккаунт", onBack) },
        contentWindowInsets = WindowInsets(0, 0, 0, 0)) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Eyebrow("YouScriptor рядом")
                Text("Ваши мысли —\nв тексте.", style = MaterialTheme.typography.displaySmall,
                    color = ScriptorColors.Olive)
                Text("Войдите, чтобы отправить запись на расшифровку.",
                    style = MaterialTheme.typography.bodyMedium, color = ScriptorColors.Muted)
            }
            Surface(color = ScriptorColors.Forest, contentColor = ScriptorColors.Cream,
                shape = RoundedCornerShape(28.dp)) {
                Column(Modifier.fillMaxWidth().padding(24.dp),
                    verticalArrangement = Arrangement.spacedBy(17.dp)) {
                    Eyebrow("Один аккаунт для приложения и сайта", ScriptorColors.Lime)
                    Text("Продолжите с тем же аккаунтом, которым пользуетесь на сайте.",
                        style = MaterialTheme.typography.titleMedium)
                    PrimaryAction("Войти через Google", NoteIcons.Arrow, {
                        context.loginActivity()?.let { viewModel.startLogin(it, AuthProvider.Google) }
                    }, Modifier.fillMaxWidth())
                    OutlineAction("Войти через Яндекс", NoteIcons.Arrow, {
                        context.loginActivity()?.let { viewModel.startLogin(it, AuthProvider.Yandex) }
                    }, Modifier.fillMaxWidth())
                }
            }
            Text("После входа запись автоматически отправится на расшифровку.",
                style = MaterialTheme.typography.bodySmall, color = ScriptorColors.Muted)
        }
    }
}

private tailrec fun Context.loginActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.loginActivity()
    else -> null
}
