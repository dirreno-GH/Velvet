# Microsoft / Minecraft лицензия в Velvet

Velvet поддерживает официальный вход Microsoft для Minecraft Java без обхода лицензии.

## Что уже делает Velvet

После того как у Velvet есть ваш собственный Microsoft Application (client) ID, кнопка **«Войти через Microsoft»** выполняет официальный пользовательский поток:

1. Microsoft OAuth Device Code.
2. Xbox Live User Token.
3. Xbox Secure Token Service (XSTS).
4. Minecraft Services Launcher Login.
5. Проверку Minecraft entitlements.
6. Получение официального Minecraft Java профиля.
7. Запуск Minecraft с лицензионными `name`, `UUID` и Minecraft access token.

Velvet не просит пароль Microsoft и не хранит его.

В текущей реализации временные токены находятся только в памяти процесса Velvet и удаляются при закрытии программы. На диск сохраняется только Application (client) ID, который является публичным идентификатором приложения, а не паролем или client secret.

## Шаг 1. Создать Microsoft App Registration

Откройте Microsoft Entra admin center:

https://entra.microsoft.com/

Создайте новый **App registration**.

Рекомендуемые параметры:

- Name: `Velvet Launcher`
- Supported account types: **Personal Microsoft accounts only** или вариант, включающий personal Microsoft accounts.
- Для desktop/public-client приложения не добавляйте client secret в Velvet.
- В разделе **Authentication** включите **Allow public client flows = Yes**.

После создания скопируйте:

**Application (client) ID**

Он выглядит примерно как GUID:

`00000000-0000-0000-0000-000000000000`

Именно этот Client ID нужно вставить в поле **MICROSOFT ЛИЦЕНЗИЯ** в Velvet.

## Шаг 2. Проверить OAuth

Нажмите в Velvet:

**Войти через Microsoft**

Velvet запросит device code, скопирует его в буфер обмена и откроет официальную страницу Microsoft.

Вход и пароль вводятся только на странице Microsoft.

Используемые scope:

- `XboxLive.SignIn`
- `XboxLive.offline_access`

## Шаг 3. Minecraft AppID approval

Новый Microsoft Client ID может пройти Microsoft OAuth, Xbox Live и XSTS, но Minecraft Services может вернуть:

`403 Invalid app registration`

Это означает, что Client ID ещё не разрешён Minecraft Services.

Текущий публичный путь, на который указывают свежие случаи Microsoft Q&A, — Minecraft AppID registration/approval:

https://help.minecraft.net/hc/en-us/articles/16254801392141

В свежем подтверждённом случае разработчик стороннего launcher также использовал форму:

https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=v4j5cvGGr0GRqy180BHbR-ajEQ1td1ROpz00KtS8Gd5UNVpPTkVLNFVROVQxNkdRMEtXVjNQQjdXVC4u

Процесс и требования могут меняться, поэтому перед отправкой заявки проверяйте актуальную страницу Minecraft Help.

## Что указать в заявке

Подготовьте:

- название приложения: Velvet Launcher / Velvet Vault;
- ссылку на публичный репозиторий:
  https://github.com/dirreno-GH/Velvet
- Application (client) ID;
- описание: open-source Windows desktop Minecraft Java launcher;
- способ входа: Microsoft OAuth Device Code → Xbox Live → XSTS → Minecraft Services;
- подтверждение, что Velvet не использует Client ID другого launcher;
- подтверждение, что Velvet не обходит Microsoft/Minecraft entitlement;
- ссылку на открытый исходный код и BUILD_FROM_SOURCE.md.

## После одобрения

После добавления Client ID в allowlist Minecraft Services:

1. вставьте Client ID в Velvet;
2. нажмите **Войти через Microsoft**;
3. завершите вход в браузере;
4. Velvet проверит `game_minecraft` entitlement;
5. Velvet получит Minecraft Java профиль;
6. статус покажет **«Microsoft лицензия подтверждена»**;
7. при запуске 1.12.2 или 1.21.4 Velvet будет использовать официальный Minecraft access token вместо offline token.

## Безопасность

Не публикуйте:

- пароль Microsoft;
- access token;
- refresh token;
- XSTS token.

Application (client) ID сам по себе не является секретом и используется публичными desktop-клиентами.

Не используйте Client ID официального Minecraft Launcher, Prism Launcher, Modrinth или другого стороннего приложения без разрешения.
