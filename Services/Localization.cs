using System.Windows;

namespace SoundCheck.Services;

/// <summary>
/// Lightweight i18n. Strings are pushed into Application.Resources under
/// "L_&lt;key&gt;" — XAML can bind via <c>{DynamicResource L_Foo}</c> and code-behind
/// can read via <see cref="T"/>. Switching language rewrites all entries, so
/// every DynamicResource binding refreshes automatically.
///
/// Languages supported: <c>ru</c>, <c>en</c>. Default <c>ru</c> (the player's
/// original language) so a brand-new user sees no change.
///
/// Note: a thin space (U+2009) is used as letter-spacer to match the player's
/// existing typographic style for ALL-CAPS section headers.
/// </summary>
public static class Localization
{
    public const string Ru = "ru";
    public const string En = "en";

    private static string _current = En;
    public static string Current => _current;

    public static event Action? Changed;

    /// <summary>key → (ru, en).</summary>
    private static readonly Dictionary<string, (string ru, string en)> _strings = new()
    {
        // ── Sidebar ─────────────────────────────────────────────────────────
        ["Actions"]            = ("Д Е Й С Т В И Я",          "A C T I O N S"),
        ["Add"]                = ("Добавить",                                                     "Add"),
        ["AddFiles"]           = ("Добавить файлы",                                              "Add files"),
        ["AddFolder"]          = ("Добавить папку",                                              "Add folder"),
        ["ClearAll"]           = ("Очистить всё",                                                "Clear all"),
        ["Recent"]             = ("Н Е Д А В Н О",                 "R E C E N T"),
        ["RecentEmpty"]        = ("История пуста",                                               "No history yet"),
        ["StatsTracks"]        = ("Т Р Е К О В",                        "T R A C K S"),
        ["StatsListened"]      = ("П Р О С Л У Ш А Н О", "L I S T E N E D"),
        ["StatsSession"]       = ("С Е С С И Я",                        "S E S S I O N"),

        // ── Header ──────────────────────────────────────────────────────────
        ["LibraryHeader"]      = ("Б И Б Л И О Т Е К А", "L I B R A R Y"),
        ["MyCollection"]       = ("Моя коллекция",                                               "My collection"),
        ["TracksWord"]         = (" треков",                                                     " tracks"),
        ["TotalDuration"]      = (" общая длительность",                                         " total duration"),
        ["FoundLabel"]         = ("найдено: ",                                                   "found: "),
        ["SearchPlaceholder"]  = ("Поиск...",                                                    "Search..."),
        ["SortBy"]             = ("Сортировка",          "Sort by"),
        ["SortAdded"]          = ("По добавлению",       "Date added"),
        ["SortTitle"]          = ("По названию",         "Title"),
        ["SortArtist"]         = ("По исполнителю",      "Artist"),
        ["SortDuration"]       = ("По длительности",     "Duration"),
        ["SearchClear"]        = ("Очистить поиск",                                              "Clear search"),
        ["TabAll"]             = ("В С Е   Т Р Е К И",   "A L L   T R A C K S"),
        ["TabRecent"]          = ("Н Е Д А В Н Е Е",          "R E C E N T"),
        ["PillStats"]          = ("С Т А Т И С Т И К А", "S T A T S"),
        ["TooltipSettings"]    = ("Настройки",                                                   "Settings"),
        ["TooltipHelp"]        = ("Горячие клавиши",                                             "Hotkeys"),
        ["TooltipMin"]         = ("Свернуть",                                                    "Minimize"),
        ["TooltipMax"]         = ("Развернуть",                                                  "Maximize"),
        ["TooltipClose"]       = ("Закрыть",                                                     "Close"),
        ["LibEmpty"]           = ("Перетащите аудиофайлы сюда или нажмите «Добавить»",            "Drop audio files here or click \"Add\""),

        // ── Bottom player bar ───────────────────────────────────────────────
        ["NoTrack"]            = ("Нет трека",                                                   "No track"),
        ["PickFromPlaylist"]   = ("Выберите песню из плейлиста",                                 "Pick a song from the playlist"),
        ["TooltipShuffle"]     = ("Случайный порядок (S)",                                       "Shuffle (S)"),
        ["TooltipRepeat"]      = ("Повтор (R)",                                                  "Repeat (R)"),
        ["TooltipQueue"]       = ("Очередь воспроизведения",                                     "Playback queue"),
        ["TooltipSleep"]       = ("Таймер сна",                                                  "Sleep timer"),
        ["TooltipMute"]        = ("Без звука (M)",                                               "Mute (M)"),

        // ── Sleep menu ──────────────────────────────────────────────────────
        ["Sleep15"]            = ("15 минут",                                                    "15 minutes"),
        ["Sleep30"]            = ("30 минут",                                                    "30 minutes"),
        ["Sleep45"]            = ("45 минут",                                                    "45 minutes"),
        ["Sleep1h"]            = ("1 час",                                                       "1 hour"),
        ["Sleep2h"]            = ("2 часа",                                                      "2 hours"),
        ["SleepCustom"]        = ("Своё время",                                                   "Custom"),
        ["SleepCustomHint"]    = ("1—180 минут · отпусти — запустить",                            "1–180 minutes · release to start"),
        ["SleepCancel"]        = ("Отменить таймер",                                              "Cancel timer"),

        // ── Tray menu ───────────────────────────────────────────────────────
        ["TrayNothing"]        = ("ничего не играет",                                            "nothing playing"),
        ["TrayShow"]           = ("Показать плеер",                                              "Show player"),
        ["TrayQuit"]           = ("Выйти",                                                       "Quit"),

        // ── Settings overlay ────────────────────────────────────────────────
        ["SettingsHeader"]     = ("Н А С Т Р О Й К И",   "S E T T I N G S"),
        ["SettingsSubtitle"]   = ("Параметры плеера",                                            "Player settings"),
        ["CatData"]            = ("Д А Н Н Ы Е",                        "D A T A"),
        ["CatPlayback"]        = ("В О С П Р О И З В Е Д Е Н И Е", "P L A Y B A C K"),
        ["CatInterface"]       = ("И Н Т Е Р Ф Е Й С",   "I N T E R F A C E"),
        ["CatSystem"]          = ("С И С Т Е М А",                 "S Y S T E M"),
        ["CatLanguage"]        = ("Я З Ы К",                                      "L A N G U A G E"),
        ["ResetLink"]          = ("↺ сбросить",                                                  "↺ reset"),
        ["LibraryFolder"]      = ("Папка библиотеки",                                            "Library folder"),
        ["BtnOpen"]            = ("📂  Открыть",                                                  "📂  Open"),
        ["BtnChange"]          = ("✎  Изменить",                                                  "✎  Change"),
        ["LibStatsNoDb"]       = ("library.db ещё не создан",                                    "library.db not created yet"),
        ["DeleteDb"]           = ("Удалить базу данных",                                          "Delete database"),
        ["DeleteDbDesc"]       = ("Очищает библиотеку, плейлисты, историю и статистику. Настройки и аудиофайлы на диске не тронуты.", "Clears the library, playlists, history and stats. Your settings and the audio files on disk are kept."),
        ["DeleteDbBtn"]        = ("🗑  Удалить базу",                                              "🗑  Delete DB"),
        ["TracksManage"]       = ("Треки в библиотеке",                                           "Tracks in library"),
        ["TracksManageDesc"]   = ("Переименование (Enter) и удаление прямо здесь",               "Rename (Enter) and delete right here"),
        ["TracksEmpty"]        = ("Библиотека пуста",                                             "Library is empty"),
        ["CatTracks"]          = ("Т Р Е К И",                          "T R A C K S"),
        ["TrackSearchPh"]      = ("Поиск трека или исполнителя...",                               "Search track or artist..."),
        ["TracksNotFound"]     = ("Ничего не найдено",                                            "Nothing found"),
        ["BackupData"]         = ("Резервная копия",                                              "Backup"),
        ["BackupDataDesc"]     = ("Сохранить или восстановить всю библиотеку, историю, статистику и настройки (library.db).", "Save or restore the whole library, history, stats and settings (library.db)."),
        ["BackupExportBtn"]    = ("💾  Сохранить копию",                                           "💾  Export"),
        ["BackupImportBtn"]    = ("⤵  Восстановить",                                              "⤵  Import"),
        ["BackupSavedFmt"]     = ("Копия сохранена:\n{0}",                                        "Backup saved:\n{0}"),
        ["BackupSaveFailFmt"]  = ("Не удалось сохранить копию: {0}",                              "Backup failed: {0}"),
        ["ConfirmImportTitle"] = ("ВОССТАНОВИТЬ ИЗ КОПИИ",                                        "RESTORE FROM BACKUP"),
        ["ConfirmImportMsg"]   = ("Текущая библиотека и настройки будут заменены содержимым выбранного файла. Плеер перезапустится.", "Your current library and settings will be replaced with the selected file. The player will restart."),
        ["ConfirmImportBtn"]   = ("ВОССТАНОВИТЬ",                                                 "RESTORE"),
        ["LogsTitle"]          = ("Журнал",                                                       "Logs"),
        ["LogsSubtitle"]       = ("События и ошибки приложения",                                  "App events and errors"),
        ["LogsBtn"]            = ("📋  Журнал",                                                    "📋  Logs"),
        ["LogsRefresh"]        = ("Обновить",                                                     "Refresh"),
        ["LogsCopy"]           = ("Копировать",                                                   "Copy"),
        ["LogsClear"]          = ("Очистить",                                                     "Clear"),
        ["LogsOpenFile"]       = ("Открыть файл",                                                 "Open file"),
        ["LogsEmpty"]          = ("Журнал пуст",                                                  "Log is empty"),
        ["ConfirmDeleteDbTitle"] = ("УДАЛИТЬ БАЗУ ДАННЫХ",                                        "DELETE DATABASE"),
        ["ConfirmDeleteDbMsg"] = ("Будут удалены все треки, плейлисты, история и статистика. Плеер станет пустым. Настройки и аудиофайлы на диске не пострадают.", "All tracks, playlists, history and stats will be removed and the player will become empty. Your settings and the audio files on disk are not affected."),
        ["ConfirmDeleteDbBtn"] = ("УДАЛИТЬ",                                                      "DELETE"),
        ["RememberTrack"]      = ("Запоминать последний трек",                                   "Remember last track"),
        ["RememberTrackDesc"]  = ("При запуске продолжит с того места где остановился",          "Resume from where you left off on next launch"),
        ["SeekStep"]           = ("Шаг перемотки ← →",                                            "Seek step ← →"),
        ["SeekStepDesc"]       = ("Секунд при нажатии стрелок",                                  "Seconds per arrow key press"),
        ["VolumeStep"]         = ("Шаг громкости [ ]",                                            "Volume step [ ]"),
        ["VolumeStepDesc"]     = ("Процентов при нажатии скобок",                                "Percent per bracket key press"),
        ["Crossfade"]          = ("Кроссфейд",                                                    "Crossfade"),
        ["CrossfadeDesc"]      = ("Плавный переход между треками",                                "Smooth fade between tracks"),
        ["Equalizer"]          = ("Эквалайзер",            "Equalizer"),
        ["EqualizerDesc"]      = ("10-полосный эквалайзер звука", "10-band audio equalizer"),
        ["EqFlat"]             = ("Сброс",                 "Flat"),
        ["EqBass"]             = ("Бас",                   "Bass"),
        ["EqVocal"]            = ("Вокал",                 "Vocal"),
        ["EqTreble"]           = ("Высокие",              "Treble"),
        ["EqCustom"]           = ("Свой",                 "Custom"),
        ["EqSaveCustom"]       = ("★ Сохранить",          "★ Save"),
        ["EqResetHint"]        = ("Двойной клик — сброс полосы", "Double-click to reset band"),
        ["Off"]                = ("Выкл",                                                         "Off"),
        ["Particles"]          = ("Частицы фона",                                                "Background particles"),
        ["ParticlesDesc"]      = ("Плавающие точки между фоном и контентом",                     "Floating dots between background and content"),
        ["FloatingBg"]         = ("Плавающая обложка",                                           "Floating cover art"),
        ["FloatingBgDesc"]     = ("Большая полупрозрачная обложка плывёт за фоном",              "Large translucent cover drifts behind the UI"),
        ["LogoEq"]             = ("Анимация логотипа",                                           "Logo animation"),
        ["LogoEqDesc"]         = ("Полосы эквалайзера и волна по буквам soundcheck",             "Equalizer bars + shimmer wave across letters"),
        ["AccentFromCover"]    = ("Цвет из обложки",                                             "Accent from cover"),
        ["AccentFromCoverDesc"] = ("Акцентный цвет плеера подстраивается под текущий трек",      "The accent color adapts to the current track"),
        ["AccentColorLabel"]   = ("Свой акцентный цвет",                                         "Custom accent color"),
        ["AccentColorDesc"]    = ("Используется, когда «цвет из обложки» выключен",              "Used when \"accent from cover\" is off"),
        ["BlurBg"]             = ("Размытый фон",                                                "Blurred background"),
        ["BlurBgDesc"]         = ("Большая размытая обложка за интерфейсом",                     "Large blurred cover art behind the UI"),
        ["ReduceMotion"]       = ("Меньше движения",                                             "Reduce motion"),
        ["ReduceMotionDesc"]   = ("Отключает все декоративные анимации сразу",                   "Turns off all decorative animations at once"),
        ["AutoStart"]          = ("Запуск с Windows",                                            "Start with Windows"),
        ["AutoStartDesc"]      = ("Открывать плеер автоматически после входа",                   "Open the player automatically after sign-in"),
        ["CloseToTray"]        = ("✕ закрывает в трей",                                          "✕ closes to tray"),
        ["CloseToTrayDesc"]    = ("Иначе — полный выход из приложения",                          "Otherwise the app quits fully"),
        ["MinToTray"]          = ("─ сворачивает в трей",                                        "─ minimizes to tray"),
        ["MinToTrayDesc"]      = ("Иначе — в обычную панель задач",                              "Otherwise to the regular taskbar"),
        ["LanguageDesc"]       = ("Часть экранов (профиль, помощь) пока только на русском",      "Some screens (profile, help) are Russian-only for now"),
        ["LanguagePickLabel"]  = ("Выберите язык интерфейса",                                     "Choose interface language"),
        ["LanguagePickDesc"]   = ("Применяется мгновенно ко всему плееру",                        "Applies instantly across the whole player"),
        ["ResetAllBig"]        = ("↺  СБРОСИТЬ ВСЁ",                                              "↺  RESET ALL"),
        ["ResetAllHint"]       = ("Сброс восстановит значения «как было сразу после установки»",  "Reset restores defaults as if freshly installed"),
        ["BtnCloseShort"]      = ("✕ З А К Р Ы Т Ь",          "✕ C L O S E"),

        // ── Common toasts / runtime messages ────────────────────────────────
        ["ToastLoadedFmt"]     = ("Загружено из библиотеки: {0} треков",                          "Loaded from library: {0} tracks"),
        ["ToastSleepCancelled"] = ("Таймер сна отменён",                                          "Sleep timer cancelled"),
        ["ToastSleepFired"]    = ("⏰ Таймер сна сработал — пауза",                                "⏰ Sleep timer fired — paused"),
        ["ToastSleepSetFmt"]   = ("⏰ Таймер сна: пауза через {0}",                                "⏰ Sleep timer: pause in {0}"),
        ["ToastEnterMins"]     = ("Введите число минут (например 25)",                            "Enter a number of minutes (e.g. 25)"),
        ["ToastLibraryCleared"] = ("Библиотека очищена",                                          "Library cleared"),
        ["ToastDbDeleted"]     = ("База данных очищена", "Database cleared"),
        ["ToastSortReorder"]   = ("Чтобы менять порядок вручную, включите сортировку «По добавлению»", "Switch sorting to \"Date added\" to reorder manually"),
        ["ToastFileMissing"]   = ("Файл не найден на диске", "File not found on disk"),
        ["ToastPlayFailed"]    = ("Не удалось воспроизвести трек", "Couldn't play the track"),
        ["MissingFile"]        = ("Файл не найден — возможно, перемещён или удалён", "File not found — moved or deleted?"),
        ["ToastStatsReset"]    = ("Статистика и история сброшены",                                "Stats and history reset"),
        ["ToastAddedFmt"]      = ("Добавлено: {0}",                                               "Added: {0}"),
        ["ToastSkippedFmt"]    = ("Пропущено: {0}",                                               "Skipped: {0}"),
        ["ToastAddedSkippedFmt"] = ("Добавлено: {0} · пропущено: {1}",                            "Added: {0} · skipped: {1}"),
        ["ToastDropTitle"]     = ("Перетащите аудиофайлы",                                        "Drop audio files"),

        // ── Context menu (right-click on a track row) ───────────────────────
        ["CtxPlay"]            = ("Воспроизвести",                                                "Play"),
        ["CtxPlayNext"]        = ("Играть следующим",                                             "Play next"),
        ["CtxAddQueue"]        = ("В очередь",                                                    "Add to queue"),
        ["CtxDelete"]          = ("Удалить",                                                      "Delete"),
        ["RowQueueAdd"]        = ("В очередь",                                                    "Add to queue"),
        ["RowDelete"]          = ("Удалить",                                                      "Delete"),
        ["CoverFullscreen"]    = ("Открыть в полном экране",                                      "Open fullscreen"),
        ["TooltipPrev"]        = ("Предыдущий (Shift+←)",                                         "Previous (Shift+←)"),
        ["TooltipNext"]        = ("Следующий (Shift+→)",                                          "Next (Shift+→)"),
        ["TooltipPlayPause"]   = ("Играть/Пауза (Space)",                                         "Play/Pause (Space)"),

        // ── Confirm dialog ──────────────────────────────────────────────────
        ["ConfirmTitle"]       = ("ПОДТВЕРЖДЕНИЕ",                                                "CONFIRMATION"),
        ["ConfirmCancel"]      = ("ОТМЕНА",                                                       "CANCEL"),
        ["ConfirmOk"]          = ("ПОДТВЕРДИТЬ",                                                  "CONFIRM"),
        ["ConfirmClearLib"]    = ("Очистить всю библиотеку?",                                     "Clear the entire library?"),
        ["ConfirmDeleteTrack"] = ("Удалить трек из библиотеки?",                                  "Remove track from library?"),
        ["ConfirmResetStats"]  = ("Сбросить статистику и историю?",                               "Reset all stats and history?"),
        ["ConfirmResetTitle"]  = ("СБРОСИТЬ СТАТИСТИКУ",                                          "RESET STATS"),
        ["ConfirmResetMsg"]    = ("Вся статистика прослушиваний и история будут безвозвратно удалены.", "All play stats and history will be permanently deleted."),
        ["ConfirmResetBtn"]    = ("СБРОСИТЬ",                                                     "RESET"),
        ["ConfirmClearTitle"]  = ("ОЧИСТИТЬ БИБЛИОТЕКУ",                                          "CLEAR LIBRARY"),
        ["ConfirmClearMsg"]    = ("Все треки будут удалены из плейлиста. Файлы на диске останутся нетронутыми.", "All tracks will be removed from the playlist. Files on disk are left untouched."),
        ["ConfirmClearBtn"]    = ("ОЧИСТИТЬ",                                                     "CLEAR"),

        // ── Queue panel ─────────────────────────────────────────────────────
        ["QueueHeader"]        = ("О Ч Е Р Е Д Ь",          "Q U E U E"),
        ["QueueEmpty"]         = ("Очередь пуста",                                                "Queue is empty"),
        ["QueueShuffle"]       = ("⇄ П Е Р Е М Е Ш А Т Ь", "⇄ S H U F F L E"),
        ["QueueClear"]         = ("✕ О Ч И С Т И Т Ь",                "✕ C L E A R"),
        ["QueuePlayAll"]       = ("▶ И Г Р А Т Ь   В С Ё", "▶ P L A Y   A L L"),
        ["QueueEmptyShort"]    = ("пусто",                                                        "empty"),

        // ── Now Playing fullscreen ──────────────────────────────────────────
        ["NowPlayingLabel"]    = ("С Е Й Ч А С  И Г Р А Е Т", "N O W   P L A Y I N G"),
        ["ResetStatsBtn"]      = ("↺  Сбросить статистику",                                       "↺  Reset stats"),

        // ── Help overlay ────────────────────────────────────────────────────
        ["HelpHeader"]         = ("П О М О Щ Ь",                 "H E L P"),
        ["HelpSubtitle"]       = ("Горячие клавиши и действия",                                   "Hotkeys and actions"),
        ["HelpKeyboard"]       = ("К Л А В И А Т У Р А", "K E Y B O A R D"),
        ["HelpMouse"]          = ("М Ы Ш Ь   И   Ф А Й Л Ы", "M O U S E   A N D   F I L E S"),
        ["HelpMediaKeys"]      = ("М Е Д И А - К Л А В И Ш И", "M E D I A   K E Y S"),
        ["HelpPlayPause"]      = ("Воспроизвести / Пауза",                                        "Play / Pause"),
        ["HelpPrevTrack"]      = ("Предыдущий трек",                                              "Previous track"),
        ["HelpNextTrack"]      = ("Следующий трек",                                               "Next track"),
        ["HelpShuffle"]        = ("Перемешать (Shuffle)",                                         "Shuffle"),
        ["HelpRepeatOne"]      = ("Повтор одного трека",                                          "Repeat one track"),
        ["HelpMute"]           = ("Без звука (mute)",                                             "Mute"),
        ["HelpSeek10"]         = ("Перемотка ±10 секунд",                                         "Seek ±10 seconds"),
        ["HelpVolume10"]       = ("Громкость ±10%",                                               "Volume ±10%"),
        ["HelpDeleteTrack"]    = ("Удалить текущий трек",                                         "Delete current track"),
        ["HelpEscClose"]       = ("Закрыть модалку / очередь",                                    "Close modal / queue"),
        ["HelpPlayTrack"]      = ("Воспроизвести трек",                                           "Play track"),
        ["HelpClickRow"]       = ("Клик на треке",                                                "Click a track"),
        ["HelpContext"]        = ("Контекстное меню",                                             "Context menu"),
        ["HelpRightClick"]     = ("Правая кнопка на треке",                                       "Right-click a track"),
        ["HelpSortTracks"]     = ("Сортировка треков",                                            "Reorder tracks"),
        ["HelpDragTrack"]      = ("Drag-drop трека",                                              "Drag a track"),
        ["HelpOpenNp"]         = ("Открыть Now Playing",                                          "Open Now Playing"),
        ["HelpClickCover"]     = ("Клик на обложку снизу",                                        "Click the cover thumbnail"),
        ["HelpAddFilesAction"] = ("Добавить файлы / папку",                                        "Add files / folder"),
        ["HelpDragIntoWindow"] = ("Перетащи в окно",                                              "Drag into the window"),

        // ── Track list column headers ───────────────────────────────────────
        ["ColTitle"]           = ("Н А З В А Н И Е",      "T I T L E"),
        ["ColArtist"]          = ("И С П О Л Н И Т Е Л Ь", "A R T I S T"),
        ["ColDuration"]        = ("Д Л И Н А",                          "D U R A T I O N"),
        ["CtxRemoveQueue"]     = ("Убрать из очереди",                                             "Remove from queue"),
        ["HelpMediaIntro"]     = ("Поддерживаются медиа-кнопки клавиатуры, наушников и Bluetooth-гарнитур:", "Keyboard, headphone and Bluetooth-headset media keys are supported:"),

        // ── Profile overlay (stats) ─────────────────────────────────────────
        ["ProfileHeader"]      = ("П Р О Ф И Л Ь",          "P R O F I L E"),
        ["ProfileSubtitle"]    = ("Статистика и история",                                         "Stats and history"),
        ["ProfileTracksLabel"] = ("Т Р Е К О В",                        "T R A C K S"),
        ["ProfileListenedLabel"] = ("П Р О С Л У Ш А Н О", "L I S T E N E D"),
        ["ProfilePlaysLabel"]  = ("В С Е Г О   В К Л Ю Ч Е Н И Й", "T O T A L   P L A Y S"),
        ["ProfileActivity"]    = ("А К Т И В Н О С Т Ь",   "A C T I V I T Y"),
        ["ProfileDay30"]       = ("За 30 дней (слева — старое, справа — сегодня)",                "Last 30 days (old → today, left → right)"),
        ["ProfileHours"]       = ("По часам суток",                                               "By hour of day"),
        ["ProfileTop"]         = ("Т О П   Т Р Е К О В", "T O P   T R A C K S"),
        ["ProfileHistory"]     = ("И С Т О Р И Я",                 "H I S T O R Y"),
        ["ProfileEmptyTop"]    = ("Топ пуст. Послушай несколько треков чтобы статистика накопилась.", "Top is empty. Listen to a few tracks for stats to fill in."),
        ["ProfileEmptyHist"]   = ("История пуста",                                                "No history yet"),
        ["ProfileReset"]       = ("✕  С Б Р О С И Т Ь",   "✕  R E S E T"),
        ["ProfilePlayCount"]   = ("прослушиваний",                                                "plays"),

        // ── Drag drop overlay ───────────────────────────────────────────────
        ["DropTitle"]          = ("Отпустите чтобы добавить",                                     "Release to add"),
        ["DropHint"]           = ("Поддерживаются .mp3, .wav, .flac, .ogg, .m4a, .aac",            "Supports .mp3, .wav, .flac, .ogg, .m4a, .aac"),

        // ── File Manager ────────────────────────────────────────────────────
        ["FmTitle"]            = ("Файловый менеджер",                                            "File Manager"),
        ["FmSubtitle"]         = ("Пакетное переименование по тегам",                             "Batch rename by tags"),
        ["FmBrowse"]           = ("Обзор",                                                        "Browse"),
        ["FmRefresh"]          = ("Обновить",                                                     "Refresh"),
        ["FmPathPh"]           = ("Путь к папке с музыкой...",                                     "Path to your music folder..."),
        ["FmTabTrack"]         = ("Треки",                                                        "Track"),
        ["FmTabLyric"]         = ("Тексты",                                                       "Lyric"),
        ["FmTabCover"]         = ("Обложки",                                                      "Cover"),
        ["FmRenameFormat"]     = ("Формат имени",                                                 "Rename Format"),
        ["FmPreviewLabel"]     = ("Превью: ",                                                     "Preview: "),
        ["FmSelectAll"]        = ("Выбрать всё",                                                  "Select All"),
        ["FmSelectedFmt"]      = ("выбрано {0} из {1}",                                           "{0} of {1} file(s) selected"),
        ["FmPreviewBtn"]       = ("👁  Превью",                                                    "👁  Preview"),
        ["FmRenameBtn"]        = ("✎  Переименовать",                                             "✎  Rename"),
        ["FmEmpty"]            = ("Нет аудиофайлов. Выбери папку через «Обзор».",                 "No audio files. Pick a folder with Browse."),
        ["FmScanning"]         = ("Сканирование...",                                              "Scanning..."),
        ["FmRenamedFmt"]       = ("Переименовано: {0}",                                           "Renamed: {0}"),
        ["FmRenameFailFmt"]    = ("Не удалось: {0}",                                              "Failed: {0}"),
        ["FmNothingSelected"]  = ("Ничего не выбрано",                                            "Nothing selected"),
        ["FmRenameFile"]       = ("Переименовать файл",                                          "Rename file"),

        // ── Tag editor ──────────────────────────────────────────────────────
        ["CtxEditTags"]        = ("Изменить теги",        "Edit tags"),
        ["TagEditTitle"]       = ("Р Е Д А К Т О Р   Т Е Г О В", "E D I T   T A G S"),
        ["TagFieldTitle"]      = ("Название",             "Title"),
        ["TagFieldArtist"]     = ("Исполнитель",          "Artist"),
        ["TagFieldAlbum"]      = ("Альбом",               "Album"),
        ["TagSave"]            = ("Сохранить",            "Save"),
        ["TagCoverChange"]     = ("Изменить обложку",     "Change cover"),
        ["TagCoverRemove"]     = ("Удалить обложку",      "Remove cover"),
        ["TagExplicit"]        = ("Ненормативный контент (E)", "Explicit content (E)"),
        ["ToastTagsSaved"]     = ("Теги сохранены",       "Tags saved"),
        ["ToastTagsFailed"]    = ("Не удалось сохранить теги (файл занят?)", "Couldn't save tags (file in use?)"),

        // ── Playlists ───────────────────────────────────────────────────────
        ["Playlists"]          = ("П Л Е Й Л И С Т Ы", "P L A Y L I S T S"),
        ["NewPlaylist"]        = ("Новый плейлист", "New playlist"),
        ["NewPlaylistDots"]    = ("＋ Новый плейлист…", "＋ New playlist…"),
        ["NoPlaylists"]        = ("Плейлистов пока нет", "No playlists yet"),
        ["PlaylistRename"]     = ("Переименовать", "Rename"),
        ["PlaylistDelete"]     = ("Удалить плейлист", "Delete playlist"),
        ["CtxAddPlaylist"]     = ("В плейлист", "Add to playlist"),
        ["NewPlaylistTitle"]   = ("НОВЫЙ ПЛЕЙЛИСТ", "NEW PLAYLIST"),
        ["RenamePlaylistTitle"] = ("ПЕРЕИМЕНОВАТЬ ПЛЕЙЛИСТ", "RENAME PLAYLIST"),
        ["PlaylistNamePh"]     = ("Название плейлиста", "Playlist name"),
        ["ConfirmDeletePlaylistTitle"] = ("УДАЛИТЬ ПЛЕЙЛИСТ", "DELETE PLAYLIST"),
        ["ConfirmDeletePlaylistMsg"]   = ("Плейлист «{0}» будет удалён. Сами треки останутся в библиотеке.", "Playlist \"{0}\" will be deleted. The tracks stay in your library."),
        ["ToastPlaylistCreated"] = ("Плейлист создан: {0}", "Playlist created: {0}"),
        ["ToastAddedToPlaylist"] = ("Добавлено в «{0}»", "Added to \"{0}\""),
        ["ToastAlreadyInPlaylist"] = ("Трек уже в плейлисте", "Already in the playlist"),
        ["PlaylistsPageTitle"] = ("Плейлисты", "Playlists"),
        ["PlaylistsPageCount"] = ("Всего плейлистов: {0}", "{0} playlist(s) total"),
        ["VisualPreset"]       = ("Профиль производительности",                                  "Performance profile"),
        ["VisualPresetDesc"]   = ("Один клик — несколько настроек ниже сразу.",                  "One click sets several toggles below."),
        ["PresetBeauty"]       = ("Красота",                                                      "Beauty"),
        ["PresetBalanced"]     = ("Баланс",                                                       "Balanced"),
        ["PresetPerformance"]  = ("Производительность",                                           "Performance"),
        ["TabPlaylists"]       = ("Плейлисты",                                                    "Playlists"),
        ["AddTracks"]          = ("Добавить треки", "Add tracks"),
        ["AddTracksTitle"]     = ("ДОБАВИТЬ В ПЛЕЙЛИСТ", "ADD TO PLAYLIST"),
        ["PlaylistCover"]      = ("Изменить фото", "Change photo"),
        ["NewPlaylistShort"]   = ("Создать", "New"),
        ["CreatePlaylist"]     = ("Создать плейлист", "Create playlist"),
        ["PlayWord"]           = ("Играть", "Play"),
        ["ShuffleWord"]        = ("Перемешать", "Shuffle"),
        ["PlaylistShuffle"]    = ("Перемешать", "Shuffle"),
        ["PlaylistPlay"]       = ("▶ Воспроизвести", "▶ Play"),
        ["PlaylistOpen"]       = ("Открыть", "Open"),
        ["PlaylistAddTracks"]  = ("Добавить треки…", "Add tracks…"),
        ["PlaylistMerge"]      = ("Объединить с…", "Merge with…"),
        ["CtxRemovePlaylist"]  = ("Убрать из плейлиста", "Remove from playlist"),
        ["ToastPlaylistEmpty"] = ("Плейлист пуст", "Playlist is empty"),
        ["ToastPlaylistsMerged"] = ("Объединено: +{0} в «{1}»", "Merged: +{0} into \"{1}\""),
        ["PlaylistExport"]     = ("Экспорт .m3u", "Export .m3u"),
        ["PlaylistImport"]     = ("Импорт плейлиста (.m3u)", "Import playlist (.m3u)"),
        ["PlaylistEmptyHint"]  = ("Плейлист пуст — добавьте треки", "Playlist is empty — add some tracks"),
        ["ToastPlaylistExported"] = ("Плейлист экспортирован: «{0}»", "Playlist exported: \"{0}\""),
        ["ToastPlaylistImported"] = ("Импортировано {0} треков → «{1}»", "Imported {0} tracks → \"{1}\""),
        ["ToastImportEmpty"]   = ("В файле нет доступных треков", "No usable tracks in the file"),
        ["ToastImportFailed"]  = ("Не удалось импортировать плейлист", "Couldn't import the playlist"),
        ["ToastExportFailed"]  = ("Не удалось экспортировать плейлист", "Couldn't export the playlist"),
        // ── Clean missing files (Settings → Data) ───────────────────────────
        ["CleanMissing"]       = ("Пропавшие файлы", "Missing files"),
        ["CleanMissingBtn"]    = ("Очистить пропавшие", "Remove missing"),
        ["CleanMissingDesc"]   = ("Убрать из библиотеки треки, файлы которых больше не найдены на диске. Сами файлы не трогаются.", "Remove from the library tracks whose files no longer exist on disk. The files themselves are not touched."),
        ["ConfirmCleanMissingTitle"] = ("ОЧИСТИТЬ ПРОПАВШИЕ", "REMOVE MISSING"),
        ["ConfirmCleanMissingMsg"] = ("Будет удалено из библиотеки треков с отсутствующими файлами: {0}. Записи в плейлистах тоже очистятся.", "{0} track(s) with missing files will be removed from the library. Their playlist entries are cleared too."),
        ["ToastNoMissing"]     = ("Пропавших файлов нет", "No missing files"),
        ["ToastMissingRemoved"] = ("Удалено пропавших: {0}", "Removed missing: {0}"),
    };

    /// <summary>Returns the localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    public static string T(string key)
    {
        if (!_strings.TryGetValue(key, out var pair)) return key;
        return _current == En ? pair.en : pair.ru;
    }

    /// <summary>One-shot initial push; call once at startup before any UI is shown.</summary>
    public static void Init(string lang)
    {
        _current = (lang == Ru) ? Ru : En;
        PushAll();
    }

    /// <summary>Change the active language and update every DynamicResource binding.</summary>
    public static void SetLanguage(string lang)
    {
        lang = (lang == Ru) ? Ru : En;
        if (lang == _current) return;
        _current = lang;
        PushAll();
        Changed?.Invoke();
    }

    private static void PushAll()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        foreach (var (key, pair) in _strings)
            res["L_" + key] = (_current == En) ? pair.en : pair.ru;
    }
}
