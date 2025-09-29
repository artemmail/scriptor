# 0) корень репозитория
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

# 1) взять патч из буфера, отрезать всё до первого "diff --git", убрать \r
$raw = Get-Clipboard -Raw
$start = $raw.IndexOf("diff --git ")
if ($start -ge 0) { $raw = $raw.Substring($start) }
$raw = $raw -replace "`r",""

# 2) на всякий случай восстановить пропавшие "index " перед 40hex..40hex
$raw = [regex]::Replace($raw, '(^|\n)(?=[0-9a-f]{40}\.\.[0-9a-f]{40}\n)', '${1}index ', 'Multiline')

# 3) сохранить «чистый» файл без BOM и применить по частям
$raw | Set-Content clean.diff -Encoding utf8

# Сначала код (без картинок) — максимально надёжно
git -c core.autocrlf=false apply --check --stat --summary --include='Angular/youtube-downloader/src/app/**' clean.diff
git -c core.autocrlf=false apply --3way --index       --include='Angular/youtube-downloader/src/app/**' clean.diff

# Затем assets (бинарные патчи)
git -c core.autocrlf=false apply --check --summary --include='Angular/youtube-downloader/src/assets/**' clean.diff
git -c core.autocrlf=false apply --3way --index       --include='Angular/youtube-downloader/src/assets/**' clean.diff

git commit -m "Apply About3 patch from CodeX"
