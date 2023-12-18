using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectorCleaner
{
    class Config
    {
        /// <summary>
        /// Массив с папками
        /// </summary>
        public List<Folder> Folders { get; set; }
        /// <summary>
        /// Срок годности файлов\папок
        /// </summary>
        public int DaysToKeep { get; set; } = 7;
        /// <summary>
        /// Время между чисткой в минутах
        /// </summary>
        public int IntervalCycleInMinutes { get; set; } = 12*60;
        /// <summary>
        ///Токен, для отправки сообщений с ММ
        /// </summary>
        public string TokenWebHook { get; set; }
        /// <summary>
        /// Имя диска, где будет производится проверка на свободное пространство
        /// </summary>
        public string DiscName { get; set; } = "C:";        
        /// <summary>
        /// Сообщение, которое отправляем в мессенджер
        /// </summary>
        public string Messgage { get; set; }
        /// <summary>
        /// Количество свободного пространства, после которого будут отправляться сообщения
        /// </summary>
        public int DiscSpaceInPercent { get; set; } = 20;
    }
    class Folder
    {
        /// <summary>
        /// Папки
        /// </summary>
        public string Path { get; set; }
    }
}
