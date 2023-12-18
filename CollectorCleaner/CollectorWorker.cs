using Newtonsoft.Json;
using System;
using System.IO;
using System.ServiceProcess;
using System.Linq;
using File = System.IO.File;
using Timer = System.Timers.Timer;
using System.Timers;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Exceptions;
using System.Collections.Generic;
using System.Threading.Tasks;
using JsonConfig;

namespace CollectorCleaner
{

    public partial class CleanerWorker : ServiceBase
    {
        /// <summary>
        /// Таймер, отсчитывает время между циклами выполнением программы
        /// </summary>
        private Timer _timer;
        /// <summary>
        /// Конфиг джейсон с полями настройки
        /// </summary>
        private Config _config;
        /// <summary>
        /// путь до логов с именем файла
        /// </summary>
        private string _logFilePath;
        /// <summary>
        /// Флаг, будет ли отправлено сообщение или нет
        /// </summary>
        private bool _sendMsg = false;
        /// <summary>
        /// Логгер серилога
        /// </summary>
        private ILogger _logger;
        /// <summary>
        /// клиент, для отправки сообщений в корп мессенджер
        /// </summary>
        private MetterMostClient _mmClient;

        public CleanerWorker()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {

            try
            {
                //привязываемся к пути, где находится экзещник, чтоб тянуть логи и конфиг
                var currProc = System.Diagnostics.Process.GetCurrentProcess();
                FileInfo fileInfo = new FileInfo(currProc.MainModule.FileName);

                //чтение конфига из файла
                var configJson = File.ReadAllText(fileInfo.DirectoryName + "/config.json");
                //путь к логам
                _logFilePath = fileInfo.DirectoryName + "/logs/CClogs.txt";

                //создаём логгер
                var logger = new LoggerConfiguration()
                        .WriteTo.File(_logFilePath, shared:true, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 1)
                        .MinimumLevel.Verbose()
                        .Enrich.WithExceptionDetails();
                Log.Logger = logger.CreateLogger();

                _logger = Log.Logger.ForContext("ClassType", GetType());

                //читаем, десириализуем и проверяем конфиг джейсона
                try
                {
                    _config = JsonConvert.DeserializeObject<Config>(configJson);
                    JObject jsonObject = JObject.Parse(configJson);
                    CheckConfig(jsonObject);
                }
                catch (Exception ex)
                {
                    _logger.Fatal($"Проблемы с форматом Json, проверьте правиильность написания полей и/или прочтите документацию \n{ex}");
                }

                //создаём объект для отправки сообщения в ММ
                _mmClient = new MetterMostClient(_config.TokenWebHook);

                //подключаем таймер для соблюдение интервала чистки
                _timer = new Timer(_config.IntervalCycleInMinutes * 1000 * 60);
                _timer.Elapsed += OnTimerElapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;
                _timer.Start();

                _logger.Information("Сервис запущен");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка старта сервиса: \n{ex.Message}");
            }

        }

        protected override void OnStop()
        {
            _timer.Enabled = false;
            _timer.Stop();
            _logger.Information("Сервис остановлен");
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            CleanFolders(_config);
            CheckDiskSpace(_config);
        }

        /// <summary>
        /// Метод чистки папок и файлов старше n дней
        /// </summary>
        /// <param name="config">конфиг джейсон</param>
        private void CleanFolders(Config config)

        {
            foreach (var folder in config.Folders)
            {
                //Проверим корректность папки
                if (!Directory.Exists(folder.Path))
                {
                    _logger.Information($"Некорректная папка: {folder.Path}");
                    continue;
                }

                //получаем папки для удаления           
                var directoriesToDelete = Directory.GetDirectories(folder.Path, "*", SearchOption.TopDirectoryOnly)
                    .Where(d => (DateTime.Now - Directory.GetCreationTime(d)).TotalDays > config.DaysToKeep);

                //удаляем папки
                DeleteFolders(directoriesToDelete);

                //получаем файлы для удаления
                var filesToDelete = Directory.GetFiles(folder.Path, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => (DateTime.Now - File.GetCreationTime(f)).TotalDays > config.DaysToKeep);

                //удаляем файлы
                DeleteFiles(filesToDelete);

            }
        }

        /// <summary>
        ///Чистка списка папок
        /// </summary>
        /// <param name="list">лист формата IEnumerable</param>
        private void DeleteFolders(IEnumerable<string> list)
        {
            foreach (var item in list)
            {
                try
                {
                    Directory.Delete(item, recursive: true);
                    _logger.Verbose($"Папка {item} удалена");
                }
                catch (Exception ex)
                {
                    _logger.Verbose($"Ошибка удаления папки: \n{ex.Message}");
                }
            }
        }

        /// <summary>
        ///Чистка списка файлов
        /// </summary>
        /// <param name="list">лист формата IEnumerable</param>
        private void DeleteFiles(IEnumerable<string> list)
        {
            foreach (var item in list)
            {
                try
                {
                    File.Delete(item);
                    _logger.Verbose($"Файл {item} удалён");
                }
                catch (Exception ex)
                {
                    _logger.Verbose($"Ошибка удаления файла: \n{ex.Message}");
                }
            }
        }

        /// <summary>
        /// Проверяем свободное пространство на диске.
        /// </summary>
        /// <param name="config">Конфиг, где будет лежать токен вебхука для бота в ММ и имя диска</param>
        private void CheckDiskSpace(Config config)
        {
            string drive = config.DiscName;
            DriveInfo driveInfo = new DriveInfo(drive);
            double freeSpaceInPercent = Math.Round(((double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize) * 100, 0);
            if (freeSpaceInPercent < _config.DiscSpaceInPercent)
            {
                _logger.Error($"Свободного места {freeSpaceInPercent}% меньше, чем {config.DiscSpaceInPercent}% на диске {config.DiscName}. Отправляю сообщение");
                try
                {
                    if (_sendMsg)
                    {
                        _mmClient.SendMessageToMM($"{config.Messgage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Ошибка при отправке сообщения");
                }
            }
            else
            {
                _logger.Debug($"Сейчас {freeSpaceInPercent}% свободного пространства. Больше, чем необходимо по конфигу: {config.DiscSpaceInPercent}%. Всё ок.");
            }
        }

        /// <summary>
        /// Проверка полей конфига
        /// </summary>
        /// <param name="jsonObject">объект джейсона</param>
        private void CheckConfig(JObject jsonObject)
        {
            JToken token;
            //проверка наличия всех папок в конфиге
            try
            {
                foreach (var folder in _config.Folders)
                {
                    if (!Directory.Exists(folder.Path))
                    {
                        throw new Exception($"Папка {folder.Path} не найдена");
                    }
                    _logger.Debug($"Папка {folder.Path} корректна");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Проблема с папками: \n {ex}");
            }
            //проверяем поле дней            
            try
            {
                int DaysToKeep = (int)jsonObject["DaysToKeep"];

                if (!jsonObject.TryGetValue("DaysToKeep", out token))
                {
                    throw new Exception("Значение поля 'DaysToKeep' нет в конфиге.");
                }
                if (token.Type != JTokenType.Integer)
                {
                    throw new Exception("Значение поля 'DaysToKeep' не числовое");
                }
                if (DaysToKeep <= 0)
                {
                    throw new ArgumentException("Значение поля 'DaysToKeep' должно быть больше нуля");
                }

                _logger.Debug($"Значение поля 'DaysToKeep': {_config.DaysToKeep}");
            }
            catch (Exception ex)
            {
                _logger.Error($"В поле 'DaysToKeep' ошибка: \n {ex}. \nПо умолчанию будет 7 дней ");
            }
            //проверяем поле времени цикла
            try
            {
                int IntervalCycle = (int)jsonObject["IntervalCycleInMinutes"];
                if (!jsonObject.TryGetValue("IntervalCycleInMinutes", out token))
                {
                    throw new Exception("Значение поля 'IntervalCycleInMinutes' нет в конфиге.");
                }
                if (token.Type != JTokenType.Integer)
                {
                    throw new Exception("Значение поля 'IntervalCycleInMinutes' не числовое");
                }
                if (IntervalCycle <= 0)
                {
                    throw new ArgumentException("Значение поля 'IntervalCycleInMinutes' должно быть больше нуля");
                }

                _logger.Debug($"Значение поля 'IntervalCycleInMinutes': {_config.IntervalCycleInMinutes}");

            }
            catch (Exception ex)
            {
                _logger.Error($"В поле 'IntervalCycleInMinutes' ошибка: \n{ex}. \nПо умолчанию будет 12 часов");
            }
            //поля для отправки сообщений
            try
            {
                string TokenWebHook = (string)jsonObject["TokenWebHook"];
                string Messgage = (string)jsonObject["Messgage"];
                if (!jsonObject.TryGetValue("TokenWebHook", out token))
                {
                    throw new Exception("Значение поля 'TokenWebHook' нет в конфиге.");
                }
                if (!jsonObject.TryGetValue("Messgage", out var tokenMessage))
                {
                    throw new Exception("Значение поля 'Messgage' нет в конфиге.");
                }
                if (string.IsNullOrWhiteSpace(TokenWebHook) || string.IsNullOrWhiteSpace(Messgage))
                {
                    throw new ArgumentException("Значение поля 'TokenWebHook' или 'Messgage' не может быть пустым или содержать только пробельные символы");
                }
                if (token.Type != JTokenType.String || tokenMessage.Type != JTokenType.String)
                {
                    throw new Exception("Значение поля 'TokenWebHook' или 'Messgage' некорректного типа");
                }

                _logger.Debug($"Значение поля 'TokenWebHook': {_config.TokenWebHook}");
                _logger.Debug($"Значение поля 'Messgage': {_config.Messgage}");
                _sendMsg = true;

            }
            catch (Exception ex)
            {
                _logger.Error($"В поле для сообщений ошибка\n{ex}: \n сообщение отправляться не будет");
            }
            //Проверка поля размера дискового пространства 
            try
            {
                int discSpace = (int)jsonObject["DiscSpaceInPercent"];
                if (!jsonObject.TryGetValue("DiscSpaceInPercent", out token))
                {
                    throw new Exception("Значение поля 'DiscSpaceInPercent' нет в конфиге.");
                }
                if (token.Type != JTokenType.Integer)
                {
                    throw new Exception("Значение поля 'DiscSpaceInPercent' не числовое ");
                }

                if (token.Value<int>() > 0 && token.Value<int>() < 100)
                {
                    _config.DiscSpaceInPercent = token.Value<int>();
                }
                else
                {
                    throw new Exception("Неверное значение DiscSpaceInPercent в конфиге, по умолчанию будет 20%");
                }

                _logger.Debug($"Значение поля DiscSpaceInPercent: {_config.DiscSpaceInPercent}");
            }
            catch (Exception ex)
            {
                _logger.Error($"В поле 'DiscSpaceInPercent' ошибка: {ex}. По умолчанию будет 20%");
            }
            //Проверка поля с именем диска            
            try
            {
                string discName = (string)jsonObject["DiscName"];
                if (string.IsNullOrWhiteSpace(discName))
                {
                    throw new ArgumentException("Значение поля 'DiscName' не может быть пустым или содержать только пробельные символы");
                }
                // Проверяем поле DiscName
                if (!jsonObject.TryGetValue("DiscName", out token))
                {
                    throw new Exception("Значение поля 'DiscName' нет в конфиге.");
                }
                // Проверяем, что оно является строкой и не пустое
                if (token.Type != JTokenType.String || token.ToString().Length != 2)
                {
                    throw new Exception("Значение поля 'DiscName' некорректного типа или записан неправильно.");
                }

                _logger.Debug($"Значение поля DiscName: {_config.DiscName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"В поле 'DiscName' ошибка: {ex}. По умолчанию будет диск С:");
            }

        }
    }
}
