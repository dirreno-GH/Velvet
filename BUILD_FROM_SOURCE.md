# Сборка Velvet Vault 2.1 из исходного кода

Этот репозиторий содержит исходный код, необходимый для самостоятельной проверки и сборки основного Windows-приложения Velvet Vault 2.1.

## Что проверять

Основные файлы проекта:

- `VelvetVault_2.1.cs` — основной C# / WPF исходный код приложения.
- `VelvetVault.csproj` — конфигурация проекта .NET Framework 4.8.
- `VelvetVault-logo.svg` — исходник логотипа.
- `.github/workflows/build.yml` — открытый сценарий GitHub Actions, который собирает опубликованный EXE.
- `SHA256SUMS.txt` — SHA-256 текущего автоматически собранного EXE.

## Требования для локальной сборки

Нужен Windows 10 или Windows 11 и среда сборки с поддержкой .NET Framework 4.8 / WPF.

Подходящие варианты:

- Visual Studio 2022 с компонентом «Разработка классических приложений .NET».
- Visual Studio Build Tools 2022 с MSBuild и .NET Framework 4.8 Developer Pack.

## Получение исходников

Склонируйте репозиторий Git:

`git clone https://github.com/dirreno-GH/Velvet.git`

Перейдите в папку проекта:

`cd Velvet`

## Сборка через MSBuild

В Developer PowerShell for Visual Studio выполните:

`msbuild VelvetVault.csproj /t:Restore /p:Configuration=Release /p:Platform="Any CPU"`

Затем:

`msbuild VelvetVault.csproj /p:Configuration=Release /p:Platform="Any CPU" /p:RestorePackages=false`

После успешной сборки файл `VelvetVault-2.1.exe` будет находиться внутри каталога `bin` в папке Release.

## Как собирается версия из репозитория

GitHub Actions использует тот же `VelvetVault.csproj` и выполняет восстановление зависимостей и сборку через MSBuild. После сборки workflow:

1. находит `VelvetVault-2.1.exe`;
2. вычисляет SHA-256;
3. записывает его в `SHA256SUMS.txt`;
4. создаёт build-provenance attestation;
5. публикует EXE как artifact и копирует текущую сборку в корень репозитория.

Полный сценарий можно проверить в `.github/workflows/build.yml`.

## Проверка SHA-256

Для проверки уже скачанного EXE в PowerShell:

`Get-FileHash .\VelvetVault-2.1.exe -Algorithm SHA256`

Сравните полученное значение с `SHA256SUMS.txt`.

## Антивирус и неподписанный EXE

Текущий EXE может не иметь доверенной Authenticode-подписи. Сам по себе факт неподписанного EXE не доказывает безопасность или вредоносность программы.

Если Microsoft Defender или другой антивирус показывает срабатывание:

- не отключайте защиту;
- не добавляйте файл в исключения только ради запуска;
- проверьте исходный код;
- при необходимости соберите приложение самостоятельно;
- сравните поведение и хэш своей сборки с автоматической сборкой GitHub.

## Minecraft launcher

Velvet launcher не должен скачивать платный Minecraft client JAR для нелицензионного использования и не должен обходить Microsoft entitlement или Microsoft/Xbox authentication.

Для текущих offline-функций launcher использует уже существующие локальные файлы Minecraft на компьютере пользователя. Поддерживаются отдельные пути для Minecraft 1.12.2 Legacy Fabric и Minecraft 1.21.4 Vanilla.

Microsoft-авторизация для лицензированного аккаунта, если будет добавлена в будущем, должна использовать официальный OAuth/Xbox/Minecraft Services поток без обхода проверки лицензии.
