using OfdRu.Validators;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using RetailCorrector.Plugins;
using Payment = RetailCorrector.Payment;

[assembly: Guid("5f45a5b3-b129-4346-bb55-219fbd2b4156")]

namespace OfdRu
{
    public class Plugin : SourcePlugin
    {
        public override string Name => "OFD.ru";
        [DisplayName(Constants.DateFromName)]
        public DateOnly DateFrom
        {
            get => _dateFrom;
            set
            {
                if (value <= _dateTo)
                    _dateFrom = value;
                else Notify(Constants.DateFromError);
            }
        }
        [DisplayName(Constants.DateToName)]
        public DateOnly DateTo
        {
            get => _dateTo;
            set
            {
                if (value >= _dateFrom) 
                    _dateTo = value;
                else Notify(Constants.DateToError);
            }
        }
        [DisplayName(Constants.VatinName)]
        public string Vatin
        {
            get => _vatin ?? "";
            set
            {
                if(VatinValidator.Valid(value)) _vatin = value;
                else Notify(Constants.VatinError);
            }
        }
        [DisplayName(Constants.DeviceName)]
        public string DeviceId
        {
            get => _deviceId ?? "";
            set
            {
                if (DeviceValidator.Valid(value)) _deviceId = value;
                else Notify(Constants.DeviceError);
            }
        }

        [DisplayName(Constants.TokenName)] public string Token { get; set; } = "";

        private DateOnly _dateFrom = DateOnly.FromDateTime(DateTime.Today);
        private DateOnly _dateTo = DateOnly.FromDateTime(DateTime.Today);
        private string? _vatin = "";
        private string? _deviceId = "";

        private HttpClient? http = null;
        private string _pathUri = null!;

        public override Task OnLoad(AssemblyLoadContext ctx)
        {
            http = new HttpClient
            {
                BaseAddress= new Uri(Constants.BaseUri),
                Timeout = TimeSpan.FromSeconds(15),
            };
            return Task.FromResult(true);
        }

        public override async Task Parse(CancellationToken token)
        {
            InvokeParseStarted((int)(_dateTo.ToDateTime(TimeOnly.MinValue) - _dateFrom.ToDateTime(TimeOnly.MinValue)).TotalDays + 1);
            _pathUri = $"/api/integration/v2/inn/{_vatin}/kkt/{_deviceId}/receipts-info?AuthToken={Token}";
            for (var day = _dateFrom; day <= _dateTo; day = day.AddDays(1))
            {
                token.ThrowIfCancellationRequested();
                await ParseByDay(day);
                InvokeProgressUpdated((int)(day.ToDateTime(TimeOnly.MinValue) - _dateFrom.ToDateTime(TimeOnly.MinValue)).TotalDays + 1);
            }
        }

        private static Payment ParsePayment(JsonNode node) => new()
        {
            Cash = node["CashSumm"]!.GetValue<uint>(),
            ECash = node["ECashSumm"]!.GetValue<uint>(),
            Pre = node["PrepaidSumm"]!.GetValue<uint>(),
            Post = node["CreditSumm"]!.GetValue<uint>(),
            Provision = node["ProvisionSumm"]!.GetValue<uint>(),
        };

        private static Receipt ParseReceipt(JsonNode node)
        {
            var items = node["Items"]!.AsArray();
            var receipt = new Receipt();
            receipt.ActNumber = " ";
            receipt.CorrectionType = CorrType.ByYourself;
            receipt.Created = ToDateTime(node["DocDateTime"]!.GetValue<string>());
            receipt.FiscalSign = node["DecimalFiscalSign"]!.GetValue<string>();
            receipt.TotalSum = node["TotalSumm"]!.GetValue<uint>();
            receipt.Payment = ParsePayment(node);
            receipt.Items = new Position[items.Count];
            receipt.Operation = ToOperation(node["OperationType"]!.GetValue<string>());
            for (var i = 0; i < items.Count; i++)
                receipt.Items[i] = ParsePosition(items[i]!);
            return receipt;
        }

        private static Position ParsePosition(JsonNode node)
        {
            var position = new Position();
            position.Name = node["Name"]!.GetValue<string>();
            position.Price = node["Price"]!.GetValue<uint>();
            position.Quantity = ToQuantity(node["Quantity"]!.GetValue<double>());
            position.TotalSum = node["Total"]!.GetValue<uint>();
            position.PayType = (PaymentType)node["CalculationMethod"]!.GetValue<int>();
            position.MeasureUnit = (MeasureUnit)int.Parse(node["ProductUnitOfMeasure"]?.GetValue<string>() ?? "-1");
            position.TaxRate = (TaxRate)node["NDS_Rate"]!.GetValue<int>();
            position.Codes = ToPositionCode(node["ProductCode"]!);
            position.PosType = (PositionType)node["SubjectType"]!.GetValue<int>();
            return position;
        }

        private static PositionCode ToPositionCode(JsonNode node) => new()
        {
            Unknown = node["Code_Undefined"]?.GetValue<string>() ?? "",
            EAN8 = node["Code_EAN_8"]?.GetValue<string>() ?? "",
            EAN13 = node["Code_EAN_13"]?.GetValue<string>() ?? "",
            ITF14 = node["Code_ITF_14"]?.GetValue<string>() ?? "",
            GS1_0 = node["Code_GS_1"]?.GetValue<string>() ?? "",
            GS1_M = node["Code_GS_1M"]?.GetValue<string>() ?? "",
            KMK = node["Code_KMK"]?.GetValue<string>() ?? "",
            MI = node["Code_MI"]?.GetValue<string>() ?? "",
            EGAIS2 = node["Code_EGAIS_2"]?.GetValue<string>() ?? "",
            EGAIS3 = node["Code_EGAIS_3"]?.GetValue<string>() ?? "",
            F1 = node["Code_F_1"]?.GetValue<string>() ?? "",
            F2 = node["Code_F_2"]?.GetValue<string>() ?? "",
            F3 = node["Code_F_3"]?.GetValue<string>() ?? "",
            F4 = node["Code_F_4"]?.GetValue<string>() ?? "",
            F5 = node["Code_F_5"]?.GetValue<string>() ?? "",
            F6 = node["Code_F_6"]?.GetValue<string>() ?? "",
        };

        private static uint ToQuantity(double raw) => (uint)Math.Round(raw * 1000);

        private static Operation ToOperation(string text) => text.ToLower() switch
        {
            "income" => Operation.Income,
            "outcome" => Operation.Outcome,
            "refund income" => Operation.RefundIncome,
            "refund outcome" => Operation.RefundOutcome
        };

        private static DateTime ToDateTime(string text) =>
            DateTime.ParseExact(text, "yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);

        private async Task ParseByDay(DateOnly day, int numberTry = 1)
        {
            await Task.Delay(numberTry * 1100);
            var dayText = day.ToString("yyyy'-'MM'-'dd");
            var uri = $"{_pathUri}&dateFrom={dayText}T00:00:00&dateTo={dayText}T23:59:59";
            var response = await http!.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                if (numberTry == Constants.CountTries)
                    return;
                await ParseByDay(day, numberTry + 1);
            }
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(content)!;
            var arr = json!["Data"]!.AsArray();
                for (var i = 0; i < arr.Count; i++)
                    InvokeAddedReceipt(ParseReceipt(arr[i]!));
        }

        public override Task OnUnload()
        {
            http!.Dispose();
            http = null;
            Token = null;
            _deviceId = null;
            _vatin = null;
            return Task.CompletedTask;
        }
    }
}
