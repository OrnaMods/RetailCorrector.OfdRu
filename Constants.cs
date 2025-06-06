namespace OfdRu
{
    internal static class Constants
    {
        public const string DateFromName = "Начало периода сканирования";
        public const string DateToName = "Конец периода сканирования";
        public const string VatinName = "ИНН организации";
        public const string DeviceName = "Регистрационный номер";
        public const string TokenName = "Аутентификационный токен";

        public const string DateFromError =
            "Начало периода сканирования не должно быть позднее конца периода сканирования";
        public const string DateToError =
            "Конец периода сканирования не должно быть ранее начала периода сканирования";
        public const string VatinError = 
            "ИНН организации некорректен";
        public const string DeviceError = 
            "Регистрационный номер некорректен";

        public const string DateOnlyFormat = "yyyy-MM-dd";
        public const string BaseUri = "https://ofd.ru";
        public const int CountTries = 3;
    }
}
