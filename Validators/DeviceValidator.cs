namespace OfdRu.Validators
{
    internal class DeviceValidator
    {
        public static bool Valid(string value)
        {
            foreach(var @char in value)
                if (@char > '9' || @char < '0')
                    return false;
            return true;
        }
    }
}
