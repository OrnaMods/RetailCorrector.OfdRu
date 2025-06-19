using OfdRu.Validators;
using RetailCorrector.Plugins;
using System.Buffers.Text;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;

namespace OfdRu;

public class Plugin : SourcePlugin
{
    public override string Name { get; } = "OFD.ru";

    [DisplayName("Логин")]
    public string Login { get; set; } = null!;
    [DisplayName("Пароль")]
    public string Password { get; set; } = null!; 
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
    [DisplayName("Заводской номер ФН")]
    public string StorageId { get; set; } = null!;
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

    private string PAC { get; set; } = null!;
    private string AccountId { get; set; } = null!;
    private string AgreementId { get; set; } = null!;
    private string DeviceAgreementId { get; set; } = null!;

    private HttpClient http = null!;
    private DateOnly _dateFrom = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _dateTo = DateOnly.FromDateTime(DateTime.Today);
    private string? _deviceId = "";

    public override async Task OnLoad(AssemblyLoadContext ctx)
    {
        http = new HttpClient
        {
            BaseAddress = new Uri("https://lk.ofd.ru/")
        };
        throw new NotImplementedException();
    }

    public override Task OnUnload()
    {
        
        throw new NotImplementedException();
    }

    public override async Task<IEnumerable<Receipt>> Parse(CancellationToken token)
    {
        if (!await Auth()) return [];
        throw new NotImplementedException();
    }

    private async Task<bool> Auth()
    {
        using (var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/Authorization/Authorize"))
        {
            var content = $"{{\"Login\":\"{Login}\",\"Password\":\"{Password}\",\"V2Auth\":true,\"version\":1}}";
            req.Content = new StringContent(content, MediaTypeHeaderValue.Parse("application/json"));
            using var resp = await http!.SendAsync(req);
            content = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception(content);
            var cookies = resp.Headers.GetValues("Set-Cookie")
                .Select(c => c.Split(";")[0].Split("="))
                .ToDictionary(i => i[0], i => i[1]);
            PAC = cookies["PAC"];
            AccountId = cookies["AccountId"];
            AgreementId = JsonNode.Parse(content)!["Data"]!["AvailableAccounts"]!.AsArray()
                    .Where(a => a!["Type"]!.GetValue<string>() == "Client").ToArray()[0]!["ObjectId"]!
                    .GetValue<string>();
        }
        using (var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/rpc/v1/GroupFilter"))
        {
            var content = $"{{\"version\":2,\"Search\":\"{DeviceId}\",\"Page\":0,\"PageSize\":10,\"OfdAgreementId\":\"{AgreementId}\"}}";
            req.Content = new StringContent(content, MediaTypeHeaderValue.Parse("application/json"));
            req.Headers.Add("Cookie", $"PAC={PAC}; AccountId={AccountId}");
            using var resp = await http!.SendAsync(req);
            content = await resp.Content.ReadAsStringAsync();
            var list = JsonNode.Parse(content)!["Data"]!["List"]!.AsArray().Where(d => d!["RegNumber"]!.GetValue<string>() == DeviceId).ToArray();
            if (list.Length == 0)
            {
                Notify("ККТ не найдена");
                return false;
            }
            DeviceAgreementId = list[0]!["Id"]!.GetValue<string>();
        }
        return true;
    }

    private async Task<List<Receipt>> GetReceiptsByDay(DateOnly day)
    {
        // GET https://lk.ofd.ru/api/document/v2/GetKktsDocuments?AccountId=dbf1031f-445d-430b-a5fa-92816d3941bf&OfdAgreementId=dc5cf05e-261b-41ed-a35d-5daa8279b2d6&KKTRegNumber=0007782174014830&fNNumber=7284440700356301&docType%5B%5D=cash_receipt&dateFrom=2025-06-18T00:00:00.000&dateTo=2025-06-19T23:59:59.000&Page=0&PageCount=50
        var list = new List<Receipt>();
        var page = 0;
        var maxPage = 0;
        var _day = day.ToString("yyyy'-'MM'-'dd");
        do
        {
            var uri = $"/api/document/v2/GetKktsDocuments?AccountId={AccountId}&OfdAgreementId={AgreementId}&KKTRegNumber={DeviceId}&fNNumber={StorageId}&docType%5B%5D=cash_receipt&docType%5B%5D=cash_receipt_correction&dateFrom={_day}T00:00:00.000&dateTo={_day}T23:59:59.000&Page={page}&PageCount=50";
            using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
            req.Headers.Add("Cookie", $"PAC={PAC}; AccountId={AccountId}");
            using var resp = await http!.SendAsync(req);
            var content = await resp.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(content)!["Data"]!;
            foreach (var i in json["List"]!.AsArray())
                list.Add(await GetDetailReceipt(i!["DocNumber"]!.GetValue<int>()));
            if (page == 0) maxPage = json["TotalCount"]!.GetValue<int>();
            page++;

        } while (page < maxPage);
        return list;
    }

    private async Task<Receipt> GetDetailReceipt(int docId)
    {
        var uri = $"api/Customer/GetJsonDoc?KktAgreementId={DeviceAgreementId}&DocNumber={docId}&CustomFnNumber={StorageId}";
        using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri);
        req.Headers.Add("Cookie", $"PAC={PAC}; AccountId={AccountId}");
        using var resp = await http!.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(content)!["Data"]!["TlvDictionary"]!;
        return ToReceipt(json);
    }

    private static Receipt ToReceipt(JsonNode node) => new()
    {
        Items = [.. node["1059"]!.AsArray().Select(p => ToPosition(p!))],
        IndustryData = [ToIndustryData(node["1261"]!)],
        Payment = new()
        {
            Cash = node["1031"]!.GetValue<uint>(),
            ECash = node["1081"]!.GetValue<uint>(),
            Post = node["1216"]!.GetValue<uint>(),
            Pre = node["1215"]!.GetValue<uint>(),
            Provision = node["1217"]!.GetValue<uint>(),
        },
        CorrectionType = CorrType.ByYourself,
        ActNumber = null,
        Created = DateTime.ParseExact(node["1012"]!.GetValue<string>(), 
            "yyyy'-'MM'-'dd'T'HH':'mm':'ss", null),
        FiscalSign = DecodeSign(node["1077"]!.GetValue<string>()),
        Operation = (Operation)node["1054"]!.GetValue<byte>(),
        TotalSum = node["1020"]!.GetValue<uint>()
    };

    private static string DecodeSign(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[6];
        Base64.DecodeFromUtf8(utf8, bytes, out _, out _);
        return $"{BitConverter.ToUInt32([.. bytes[2..].Reverse()])}";
    }

    private static IndustryData ToIndustryData(JsonNode node) => new()
    {
        Date = DateOnly.Parse(node["1263"]!.GetValue<string>()),
        GosId = byte.Parse(node["1262"]!.GetValue<string>()),
        Number = int.Parse(node["1264"]!.GetValue<string>()),
        Value = node["1265"]!.GetValue<string>()
    };

    private static Position ToPosition(JsonNode node) => new()
    {
        Codes = ToPositionCode(node["1163"]!),
        IndustryData = [ToIndustryData(node["1260"]!)],
        Name = node["1030"]?.GetValue<string>() ?? "",
        Quantity = (uint)(node["1023"]!.GetValue<double>() * 1000),
        Price = node["1079"]!.GetValue<uint>(),
        TotalSum = node["1043"]!.GetValue<uint>(),
        MeasureUnit = (MeasureUnit?)node["2108"]?.GetValue<byte>() ?? MeasureUnit.None,
        PayType = (PaymentType)node["1214"]!.GetValue<byte>(),
        PosType = (PositionType)node["1212"]!.GetValue<byte>(),
        TaxRate = (TaxRate)node["1199"]!.GetValue<byte>(),
    };

    private static PositionCode ToPositionCode(JsonNode node) => new()
    {
        Unknown = node["1300"]!.GetValue<string>(),
        EAN8 = node["1301"]!.GetValue<string>(),
        EAN13 = node["1302"]!.GetValue<string>(),
        ITF14 = node["1303"]!.GetValue<string>(),
        GS1_0 = node["1304"]!.GetValue<string>(),
        GS1_M = node["1305"]!.GetValue<string>(),
        KMK = node["1306"]!.GetValue<string>(),
        MI = node["1307"]!.GetValue<string>(),
        EGAIS2 = node["1308"]!.GetValue<string>(),
        EGAIS3 = node["1309"]!.GetValue<string>(),
        F1 = node["1320"]!.GetValue<string>(),
        F2 = node["1321"]!.GetValue<string>(),
        F3 = node["1322"]!.GetValue<string>(),
        F4 = node["1323"]!.GetValue<string>(),
        F5 = node["1324"]!.GetValue<string>(),
        F6 = node["1325"]!.GetValue<string>(),
    };
}