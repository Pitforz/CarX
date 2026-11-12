# CarX

Хоррор от первого лица в ливневых тоннелях Лас-Вегаса (хакатон, тема «Братья Карамазовы»).

## Движок
- Unity **6000.6.9f1** (у всей команды строго эта версия), шаблон **Universal 3D (URP)**
- Git + Git LFS (обязательно `git lfs install` перед клонированием)

## Как открыть проект
1. Установить Git и Git LFS, выполнить `git lfs install`.
2. `git clone https://github.com/Pitforz/CarX.git`
3. Unity Hub → Add → Add project from disk → папка `CarX`.
4. Открыть той же версией Unity, что указана в `ProjectSettings/ProjectVersion.txt`.

## Правила
- Не коммитить `Library/`, `Temp/`, `Logs/`, `UserSettings/` (уже в .gitignore).
- Всегда коммитить `.meta` вместе с файлом.
- Одну сцену одновременно редактирует один человек; остальное — через префабы.
- Сторонние ассеты кладём в `Assets/ThirdParty/`, свои — в `Assets/_Project/`.

## AI-контент
Всё, что сгенерировано ИИ (арт, текст, музыка, голос), записывается в `AI_DISCLOSURE.md`.
