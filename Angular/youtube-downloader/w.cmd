# 0) Убедись, что у тебя именно то локальное состояние, которое хочешь сделать main
git status
# если есть незафиксированные правки — зафиксируй (или stasheй)
git add -A
git commit -m "Make current local state the new main"  # при необходимости

# 1) Подтянуть ссылки с сервера
git fetch origin

# 2) Сохранить текущий origin/main в отдельную ветку на сервере (backup)
git push origin origin/main:refs/heads/old-main
# При желании используй другое имя, например: main-backup-2025-10-24

# 3) Убедиться, что локально ты на нужной ветке.
# Если ты уже на main и это нужное состояние — ок.
# Если ты на другой ветке с нужным состоянием — переименуй её в main:
# git branch -M main

# 4) Перезаписать серверный main твоим локальным состоянием (с защитой от лишних сюрпризов)
git push --force-with-lease origin main:main
