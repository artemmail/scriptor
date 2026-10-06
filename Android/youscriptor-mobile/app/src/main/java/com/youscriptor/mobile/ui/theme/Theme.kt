package com.youscriptor.mobile.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

object ScriptorColors {
    val Cream = Color(0xFFF7F8F2)
    val Ink = Color(0xFF22241F)
    val Forest = Color(0xFF293323)
    val Olive = Color(0xFF526B2C)
    val Lime = Color(0xFFD4F568)
    val Mist = Color(0xFFEDF3E2)
    val Border = Color(0xFFDFE2D7)
    val Muted = Color(0xFF666C5E)
}

private val Colors = lightColorScheme(
    primary = ScriptorColors.Olive, onPrimary = Color.White,
    primaryContainer = ScriptorColors.Lime, onPrimaryContainer = ScriptorColors.Forest,
    secondary = ScriptorColors.Olive, onSecondary = Color.White,
    secondaryContainer = ScriptorColors.Mist, onSecondaryContainer = ScriptorColors.Forest,
    tertiary = ScriptorColors.Forest, onTertiary = ScriptorColors.Lime,
    background = ScriptorColors.Cream, onBackground = ScriptorColors.Ink,
    surface = ScriptorColors.Cream, onSurface = ScriptorColors.Ink,
    surfaceVariant = ScriptorColors.Mist, onSurfaceVariant = ScriptorColors.Muted,
    surfaceContainer = ScriptorColors.Mist,
    surfaceContainerLow = Color.White, surfaceContainerHigh = ScriptorColors.Mist,
    outline = Color(0xFF89917D), outlineVariant = ScriptorColors.Border,
    inverseSurface = ScriptorColors.Forest, inverseOnSurface = ScriptorColors.Cream,
    inversePrimary = ScriptorColors.Lime,
    error = Color(0xFFAB3F32), onError = Color.White,
    errorContainer = Color(0xFFFFEDE8), onErrorContainer = Color(0xFF7B2920)
)

private val EditorialTypography = Typography(
    displaySmall = TextStyle(fontFamily = FontFamily.Serif, fontSize = 38.sp,
        lineHeight = 42.sp, fontStyle = FontStyle.Italic, letterSpacing = (-1).sp),
    headlineLarge = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.SemiBold,
        fontSize = 32.sp, lineHeight = 38.sp, letterSpacing = (-0.8).sp),
    headlineMedium = TextStyle(fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.SemiBold,
        fontSize = 26.sp, lineHeight = 32.sp, letterSpacing = (-0.5).sp),
    titleLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 22.sp, lineHeight = 28.sp),
    titleMedium = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 17.sp, lineHeight = 23.sp),
    bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 25.sp),
    bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 21.sp),
    bodySmall = TextStyle(fontSize = 12.sp, lineHeight = 18.sp),
    labelLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 14.sp, lineHeight = 20.sp),
    labelMedium = TextStyle(fontWeight = FontWeight.Medium, fontSize = 12.sp, lineHeight = 16.sp)
)

@Composable
fun YouScriptorTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = Colors,
        typography = EditorialTypography,
        shapes = Shapes(
            extraSmall = RoundedCornerShape(8.dp), small = RoundedCornerShape(12.dp),
            medium = RoundedCornerShape(20.dp), large = RoundedCornerShape(28.dp),
            extraLarge = RoundedCornerShape(32.dp)
        ),
        content = content
    )
}
