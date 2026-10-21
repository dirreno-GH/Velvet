# Velvet Vault 2.1

![Velvet Vault](VelvetVault-logo.svg)

**Velvet Vault** — локальное Windows-приложение в стиле Velvet с набором встроенных инструментов и зашифрованным хранилищем паролей.

## 🔎 Полный исходный код открыт

Velvet можно полностью проверить и собрать самостоятельно. Готовый EXE не является единственным способом получить приложение.

**Основной исходный код:** [VelvetVault_2.1.cs](VelvetVault_2.1.cs)

**[Скачать весь исходный код ZIP](https://github.com/dirreno-GH/Velvet/archive/refs/heads/main.zip)**

Файлы, необходимые для проверки и сборки:

- [VelvetVault_2.1.cs](VelvetVault_2.1.cs) — полный C# / WPF код приложения
- [VelvetVault.csproj](VelvetVault.csproj) — проект .NET Framework 4.8
- [VelvetVault-logo.svg](VelvetVault-logo.svg) — исходник логотипа
- [.github/workflows/build.yml](.github/workflows/build.yml) — открытый GitHub Actions workflow, который собирает EXE
- [SHA256SUMS.txt](SHA256SUMS.txt) — SHA-256 текущей автоматической сборки
- [BUILD_FROM_SOURCE.md](BUILD_FROM_SOURCE.md) — пошаговая самостоятельная сборка
- [MICROSOFT_AUTH_SETUP.md](MICROSOFT_AUTH_SETUP.md) — официальный Microsoft/Xbox/Minecraft вход для лицензии

Если вы не хотите запускать готовый неподписанный EXE, можно клонировать репозиторий, проверить исходники и собрать Velvet самостоятельно. Не требуется отключать Microsoft Defender или добавлять Velvet в исключения.

## Что есть в версии 2.1

- 🔐 Хранилище паролей
- 🏫 Расписание уроков — отдельные списки Пн–Вс, предмет, начало, конец и класс
- 🎓 Расписание пар — отдельные списки Пн–Вс, предмет, начало, конец и группа
- ⏱ Таймер — старт, пауза, сброс и голосовое сообщение по окончании
- ✅ Задачи на сегодня — выполнение и удаление
- 🧮 Калькулятор
- 📝 Заметки
- ₴ Финансы
- ⇄ Конвертер файлов
- 📄 Документы
- ◇ Velvet launcher — Minecraft Java 1.8.9 Vanilla, 1.12.2 Legacy Fabric и 1.21.4 Vanilla; сам скачивает официальные файлы Mojang. Без подтверждённой лицензии запускает только встроенный Demo Mode, с Microsoft-лицензией — полную игру
- 🎨 Светлая, тёмная, серая и тёмно-зелёная темы
- 🔒 Автоблокировка хранилища

## Скачать или собрать самостоятельно

### Вариант 1 — проверить исходники и собрать самому

См. **[BUILD_FROM_SOURCE.md](BUILD_FROM_SOURCE.md)**.

### Вариант 2 — готовая автоматическая сборка

Готовая Windows-версия находится прямо в репозитории:

**[VelvetVault-2.1.exe](VelvetVault-2.1.exe)**

Текущий EXE не имеет доверенной Authenticode-подписи. Антивирусные продукты могут относиться к неподписанным программам осторожно. Любое срабатывание следует проверять отдельно; не отключайте защиту ради запуска Velvet.

## Проверка скачанного файла

Для каждой автоматической сборки GitHub создаёт `SHA256SUMS.txt` и build-provenance attestation. Это позволяет проверить, что EXE действительно получен из исходников этого репозитория.

Проверка SHA-256 в PowerShell:

```powershell
Get-FileHash .\VelvetVault-2.1.exe -Algorithm SHA256
```

Сравните результат со значением в `SHA256SUMS.txt`.

## Code signing policy

**Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/)** — для подписанных релизов после одобрения проекта SignPath Foundation.

См. [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

> Текущий EXE ещё не подписан SignPath. Репозиторий подготовлен для подачи заявки; доверенная подпись будет включена только после официального одобрения проекта.

## Microsoft лицензия

Velvet содержит официальный путь авторизации Microsoft для лицензированного Minecraft Java: Microsoft OAuth Device Code → Xbox Live → XSTS → Minecraft Services → проверка entitlement → Minecraft профиль.

Для работы нужен собственный Microsoft Application (client) ID. Новому Client ID также может потребоваться Minecraft AppID approval/allowlist. Подробная инструкция: **[MICROSOFT_AUTH_SETUP.md](MICROSOFT_AUTH_SETUP.md)**.

Velvet не просит и не хранит пароль Microsoft. Временные токены текущей реализации находятся только в памяти приложения.

## Системные требования

- Windows 10 или Windows 11
- .NET Framework 4.8

## Сборка из исходника

Проект написан на C# / WPF и собирается через MSBuild. Полная инструкция находится в **[BUILD_FROM_SOURCE.md](BUILD_FROM_SOURCE.md)**.

В проекте нет закрытого исходного модуля, который нужен для сборки основного EXE: основной код находится в `VelvetVault_2.1.cs`, параметры проекта — в `VelvetVault.csproj`, а автоматическая сборка описана в открытом workflow GitHub Actions.

## Безопасность

Хранилище работает локально на компьютере. Пароли шифруются перед сохранением, а мастер-пароль используется для получения ключа шифрования.

## Версия

**2.1.0.0**


## Издатель

**Velvet Corp.**

Текущая EXE-сборка содержит метаданные Velvet Corp. Для отображения проверенного издателя в предупреждениях Windows и снижения количества предупреждений SmartScreen требуется Authenticode-подпись действительным сертификатом подписи кода.


## Лицензия

Исходный код распространяется по [MIT License](LICENSE).


## Удаление

Velvet Vault распространяется как portable EXE и не устанавливает системную службу.

1. Закройте Velvet Vault.
2. Удалите файл `VelvetVault-2.1.exe`.
3. Если нужно удалить также локальные данные приложения, удалите папку:
   `%LOCALAPPDATA%\VelvetVault`

Удаление папки данных удалит локальные настройки и сохранённые данные Velvet, поэтому предварительно сделайте резервную копию нужной информации.
