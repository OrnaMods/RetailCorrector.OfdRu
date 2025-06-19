using RetailCorrector.Plugins;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

namespace OfdRu;

public class Plugin : SourcePlugin
{
    public override string Name { get; } = "OFD.ru";

    [DisplayName("Логин")]
    public string Login { get; set; } = null!;
    [DisplayName("Пароль")]
    public string Password { get; set; } = null!;
    private string PAC { get; set; } = null!;
    private string AccountId { get; set; } = null!;
    private string AgreementId { get; set; } = null!;

    private HttpClient http = null!;

    public override async Task OnLoad(AssemblyLoadContext ctx)
    {
        http = new HttpClient
    {
            BaseAddress = new Uri("https://lk.ofd.ru/")
        };
        await Auth();
        throw new NotImplementedException();
    }

    public override Task OnUnload()
    {
        throw new NotImplementedException();
    }

    public override Task<IEnumerable<Receipt>> Parse(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    private async Task Auth()
    {
        using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/Authorization/Authorize");
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
}