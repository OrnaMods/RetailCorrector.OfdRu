namespace OfdRu.Validators
{
    internal class VatinValidator
    {
        public static bool Valid(string value)
        {
            if (value.Length != 10 && value.Length != 12) return false;
            if (!DeviceValidator.Valid(value)) return false;
            return IsCompanyVatin(value) || IsPersonVatin(value);
        }

        private static bool IsCompanyVatin(string vatin)
        {
            if (vatin.Length != 10) return false;
            var ns10 = new[] { 2, 4, 10, 3, 5, 9, 4, 6, 8 };
            for (var i = 0; i < ns10.Length; i++)
                ns10[i] *= vatin[i] - 48;
            var n10 = ns10.Sum() % 11 % 10;
            return n10 == vatin[9] - 48;
        }

        private static bool IsPersonVatin(string vatin)
        {
            if (vatin.Length != 12) return false;
            var ns12 = new[] { 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 };
            for (var i = 0; i < ns12.Length; i++)
                ns12[i] *= vatin[i] - 48;
            var na12 = new[] { 3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 };
            na12[10] *= ns12.Sum() % 11 % 10;
            for (var i = 0; i < ns12.Length; i++)
                na12[i] *= vatin[i] - 48;
            var n12 = na12.Sum() % 11 % 10;
            return na12[10] == vatin[10] - 48 && n12 == vatin[11] - 48;
        }


    }
}
