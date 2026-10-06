package com.youscriptor.mobile.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.unit.dp

/** A single, rounded outline family. Decorative icons have no spoken label at call sites. */
internal object NoteIcons {
    private fun icon(name: String, data: String) = ImageVector.Builder(
        name, 24.dp, 24.dp, 24f, 24f
    ).addPath(
        pathData = PathParser().parsePathString(data).toNodes(),
        fill = null, stroke = SolidColor(Color.Black), strokeLineWidth = 1.7f,
        strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round
    ).build()

    val Notes = icon("Notes", "M7 3H17Q20 3 20 6V18Q20 21 17 21H7Q4 21 4 18V6Q4 3 7 3M8 8H16M8 12H16M8 16H12")
    val Mic = icon("Microphone", "M9 5A3 3 0 0 1 15 5V11A3 3 0 0 1 9 11ZM6 10V11A6 6 0 0 0 18 11V10M12 17V21M9 21H15")
    val Search = icon("Search", "M16.5 10A6.5 6.5 0 1 1 3.5 10A6.5 6.5 0 1 1 16.5 10M15 15L21 21")
    val Settings = icon("Settings", "M4 6H8M12 6H20M4 12H14M18 12H20M4 18H6M10 18H20M10 4V8M16 10V14M8 16V20")
    val Back = icon("Back", "M20 12H4M10 6L4 12L10 18")
    val Close = icon("Close", "M6 6L18 18M18 6L6 18")
    val Text = icon("Text", "M4 5H20M12 5V20M8 20H16")
    val Audio = icon("Audio", "M4 10V14M8 6V18M12 3V21M16 7V17M20 10V14")
    val Cloud = icon("CloudUpload", "M6 17H5A4 4 0 0 1 5 9A7 7 0 0 1 18.5 8A4.5 4.5 0 0 1 19 17H18M12 21V11M8 15L12 11L16 15")
    val Check = icon("Check", "M5 12L10 17L20 6")
    val Refresh = icon("Refresh", "M20 9A8 8 0 0 0 6 6L3 9M3 4V9H8M4 15A8 8 0 0 0 18 18L21 15M16 15H21V20")
    val Delete = icon("Delete", "M3 6H21M9 6V3H15V6M6 6L7 21H17L18 6M10 10V17M14 10V17")
    val Play = icon("Play", "M8 4L20 12L8 20Z")
    val Pause = icon("Pause", "M8 5V19M16 5V19")
    val Stop = icon("Stop", "M6 6H18V18H6Z")
    val Save = icon("Save", "M5 3H16L21 8V21H3V3ZM8 3V9H16V3M7 21V14H17V21")
    val Share = icon("Share", "M8 11L16 6M8 13L16 18M8 12A3 3 0 1 1 2 12A3 3 0 1 1 8 12M22 5A3 3 0 1 1 16 5A3 3 0 1 1 22 5M22 19A3 3 0 1 1 16 19A3 3 0 1 1 22 19")
    val Arrow = icon("Arrow", "M4 12H20M14 6L20 12L14 18")
    val Chevron = icon("Chevron", "M9 5L16 12L9 19")
    val Captions = icon("Captions", "M5 4H19Q21 4 21 6V18Q21 20 19 20H5Q3 20 3 18V6Q3 4 5 4M6 9H10M14 9H18M6 14H18M6 17H13")
    val Link = icon("Link", "M10 14L14 10M8 16L6 18A4 4 0 0 1 0 12L5 7A4 4 0 0 1 11 7M13 17A4 4 0 0 0 19 17L23 13A4 4 0 0 0 17 7L15 9")
    val Logout = icon("Logout", "M9 3H4V21H9M9 12H21M16 7L21 12L16 17")
    val User = icon("User", "M16 7A4 4 0 1 1 8 7A4 4 0 1 1 16 7M4 21V19A8 6 0 0 1 20 19V21")
    val Warning = icon("Warning", "M12 3L22 21H2ZM12 9V14M12 17V17.1")
}
