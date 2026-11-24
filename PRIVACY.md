# Atoll privacy policy

Atoll collects no personal data and has no telemetry or ads. Everything it keeps stays on your computer:
settings and notes in `%APPDATA%\DynamicIsland`, chat logins and the error log in `%LOCALAPPDATA%\DynamicIsland`.
The Gemini key for the voice assistant is stored encrypted for your Windows user (DPAPI) and never written to the log.

Atoll connects to the internet only for features you can see, and sends only what that feature needs:

| Feature | Service | What is sent |
|---|---|---|
| Weather | open-meteo.com (forecast, air quality, place search) | the city name or coordinates you set |
| Weather, finding a small town Open-Meteo doesn't know | nominatim.openstreetmap.org | the city name you typed, and the interface language |
| Weather, when Open-Meteo doesn't answer | api.met.no | the coordinates of your city (rounded to 4 digits) |
| Song lyrics | lrclib.net | track title and artist |
| "Translate" in the screenshot editor and in the text tools | translate.googleapis.com | the recognised or typed text, only when you click |
| Voice assistant (Gemini Live), only while it is switched on and listening | generativelanguage.googleapis.com | sound from your microphone and your own Gemini key, under Google's privacy policy |
| AI chat | the chat site you pick (Gemini, Claude, DeepSeek…) | what you type there, under that site's own privacy policy |
| Music played in Atoll, only the service you open in the Music tab: YouTube Music, YouTube, Spotify, SoundCloud, Deezer, Apple Music | the service's own site inside the island (music.youtube.com, youtube.com, open.spotify.com, soundcloud.com, deezer.com, music.apple.com), its sign-in pages and whatever hosts that site uses | your search, and what you do on the site, under that site's own privacy policy; Atoll doesn't read your sign-in, its cookies stay in `%LOCALAPPDATA%\DynamicIsland\WebView2Music` |
| Internet radio, only when you open it | radio-browser.info (station directory) and the station you pick | your search and which station you started; the station sees your IP address |
| Calendar | the ICS link you add | nothing |
| Theme catalogue | raw.githubusercontent.com | nothing personal |
| Update check (GitHub version only) | api.github.com | nothing personal |

Screenshots, text recognition (OCR), QR codes, screen recordings and the clipboard are processed locally and are never uploaded.
Uninstalling Atoll removes the app; you can delete the folders above to remove your settings too.

Questions: open an issue at https://github.com/Fuwni/dynamic-island-windows/issues

## Политика конфиденциальности (по-русски)

Atoll не собирает личные данные, в нём нет телеметрии и рекламы. Всё, что он хранит, остаётся на вашем компьютере:
настройки и заметки — в `%APPDATA%\DynamicIsland`, входы в чаты и журнал ошибок — в `%LOCALAPPDATA%\DynamicIsland`.
Ключ Gemini для голосового ассистента хранится зашифрованным для вашего пользователя Windows (DPAPI) и никогда не пишется в журнал.

Atoll выходит в интернет только для функций, которые вы видите, и отправляет только то, что нужно этой функции:

| Функция | Сервис | Что отправляется |
|---|---|---|
| Погода | open-meteo.com (прогноз, качество воздуха, поиск места) | название города или координаты, которые вы указали |
| Погода: поиск небольшого города, которого не знает Open-Meteo | nominatim.openstreetmap.org | введённое название города и язык интерфейса |
| Погода, когда Open-Meteo не отвечает | api.met.no | координаты вашего города (округлённые до 4 знаков) |
| Тексты песен | lrclib.net | название трека и исполнитель |
| «Перевести» в редакторе скриншотов и в инструментах текста | translate.googleapis.com | распознанный или набранный текст, только по нажатию |
| Голосовой ассистент (Gemini Live), только пока он включён и слушает | generativelanguage.googleapis.com | звук с микрофона и ваш собственный ключ Gemini, по политике конфиденциальности Google |
| ИИ-чат | выбранный вами сайт чата (Gemini, Claude, DeepSeek…) | то, что вы там пишете, по политике этого сайта |
| Музыка в самом Atoll, только сервис, который вы открыли во вкладке «Музыка»: YouTube Music, YouTube, Spotify, SoundCloud, Deezer, Apple Music | собственный сайт сервиса внутри островка (music.youtube.com, youtube.com, open.spotify.com, soundcloud.com, deezer.com, music.apple.com), его страницы входа и адреса, которыми пользуется сам сайт | ваш поиск и то, что вы делаете на сайте, по политике этого сайта; Atoll не читает ваш вход, куки лежат в `%LOCALAPPDATA%\DynamicIsland\WebView2Music` |
| Интернет-радио, только когда вы его открыли | radio-browser.info (каталог станций) и выбранная станция | ваш поиск и какую станцию вы включили; станция видит ваш IP-адрес |
| Календарь | добавленная вами ссылка ICS | ничего |
| Каталог тем | raw.githubusercontent.com | ничего личного |
| Проверка обновлений (только версия с GitHub) | api.github.com | ничего личного |

Скриншоты, распознавание текста (OCR), QR-коды, запись экрана и буфер обмена обрабатываются на компьютере и никуда не отправляются.
Удаление Atoll удаляет программу; чтобы удалить и настройки, удалите папки выше.

Вопросы: создайте issue на https://github.com/Fuwni/dynamic-island-windows/issues
