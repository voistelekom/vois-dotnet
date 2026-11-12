# VOIS .NET istemcisi

VOIS **SMS** ve **Ses** API'leri için resmi .NET paketi. .NET 6/7/8+ ve **.NET Framework 4.6.1+** (netstandard2.0) ile çalışır.

- Doküman: https://docs.vois.com.tr/
- Destek: destek@vois.com.tr · 0850 340 6 340

## Kurulum

```bash
dotnet add package Vois
```

## SMS

API kullanıcı adı ve şifresi SMS hesabınız açılırken VOIS tarafından verilir (panel girişinden farklıdır).

```csharp
using Vois;

using var sms = new VoisSmsClient("KULLANICI", "SIFRE");

decimal kredi = await sms.CreditAsync();                 // bağlantı testi, SMS göndermez

long pkgId = await sms.SendSingleAsync("905321234567", "Randevunuz yarın 10:00", "BASLIK", "Randevu hatirlatma",
    new SmsOptions { Encoding = 1, CustomId = "randevu-42" });

await sms.SendBulkAsync(new[] { "905321234567", "905441234567" }, "Kampanya metni", "BASLIK", "Kasim kampanyasi",
    new SmsOptions { Commercial = true });
await sms.SendPersonalizedAsync(new[] { new PersonalizedMessage("905321234567", "Sayın Ayşe Hanım…", "F-1001") }, "BASLIK", "Fatura bildirimi");
await sms.SendOtpAsync("905321234567", "Doğrulama kodunuz: 482915", "BASLIK", new SmsOptions { Validity = 3 });

try
{
    await sms.SendSingleAsync("905321234567", "Merhaba", "BASLIK", "Deneme gonderim");
}
catch (VoisSmsException e)
{
    // e.ErrorCode → ör. ERR_USER_CREDIT_REQUIRED; e.Message → Türkçe açıklama
}
```

Rapor yöntemleri (`PackagesAsync`, `PackageItemsAsync`, `SummaryAsync`, `InquiryAsync`, `SendersAsync`, `GatewaysAsync`, `InboundAsync`) yanıtın `data` alanını `JsonElement` olarak döndürür. Ham gövde için (ör. MMS): `SendAsync(dictionary)`.

### VOIS API anahtarıyla SMS

SMS kullanıcı adı yerine panelde oluşturduğunuz VOIS API anahtarıyla da gönderebilirsiniz (anahtarda `sms:send` / `sms:read` yetkisi olmalı). Yöntemler aynıdır.

```csharp
using var sms = VoisSmsClient.FromApiKey("vois_1234_…");
decimal kredi = await sms.CreditAsync();
try
{
    await sms.SendSingleAsync("905321234567", "Merhaba", "BASLIK", "Deneme gonderim");
}
catch (VoisSmsException e)
{
    // e.ErrorCode — VOIS tarafı hatalarda: insufficient_scope, sms_not_configured, sms_blocked, rate_limited …
}
```

## Ses (tıkla-ara, çağrı geçmişi, kayıtlar)

Anahtarı VOIS panelinizde oluşturun: **Ayarlar › Açık REST API & Webhook › API & Webhook Yönetimi**.

```csharp
using var voice = new VoisVoiceClient("vois_1234_…");

var dahililer = await voice.ExtensionsAsync();               // arama yapmaz; Registered = telefon bağlı mı

try
{
    var cagri = await voice.CallAsync("101", "05321234567", new CallOptions { Reference = "musteri-4521" });
    Console.WriteLine(cagri.CallId);
}
catch (VoisVoiceException e)
{
    // e.ErrorCode → ör. extension_unavailable (telefon bağlı değil); e.Message → kullanıcıya gösterebilirsiniz
}

await voice.HangupAsync(callId);                                // false: çağrı zaten bitmiş
var sayfa = await voice.CdrAsync(new DateTime(2026, 11, 1), DateTime.Today, new CdrFilter { Direction = "inbound" });
await foreach (var c in voice.CdrAllAsync(new DateTime(2026, 11, 1), DateTime.Today)) { /* bütün sayfalar */ }
var kayit = await voice.DownloadRecordingAsync(callId, @"C:\kayitlar");   // klasör: cagri-<id>.wav ya da .mp3
```

ASP.NET Core'da tek bir `HttpClient` paylaşmak için `IHttpClientFactory` kullanın; paket verdiğiniz `HttpClient`'ı değiştirmez ve kapatmaz:

```csharp
builder.Services.AddHttpClient("vois");
builder.Services.AddSingleton(sp => new VoisVoiceClient(builder.Configuration["Vois:ApiKey"]!,
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("vois")));
```

## Webhook yönetimi

Anahtarınızla (`webhooks:manage` yetkisi) kendi webhook'larınızı kodla yönetin. Bir anahtar yalnızca kendi oluşturduklarını görür ve siler.

```csharp
using var wh = new VoisWebhookClient("vois_1234_…");
try
{
    var kayit = await wh.CreateAsync("call.ended", "https://siteniz.com/vois/webhook");
    var gizli = kayit.Secret;                 // yalnızca burada görünür; saklayın
}
catch (VoisWebhookException e)
{
    // e.ErrorCode: invalid_event, url_not_allowed, https_required, limit_reached …
}
foreach (var h in await wh.ListAsync()) Console.WriteLine($"{h.Id} {h.Event} {h.Url}");
bool silindi = await wh.DeleteAsync(3);       // false: bulunamadı
```

## Webhook doğrulama (ASP.NET Core)

```csharp
app.MapPost("/vois/webhook", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();                   // ham gövde — model binding'den önce
    try
    {
        var ev = VoisWebhook.Verify(body, req.Headers["X-VOIS-Signature"], builder.Configuration["Vois:WebhookSecret"]!);
        // ev.Event → call.ringing / call.answered / call.ended; ev.Id → tekrarları ayıklamak için saklayın
        return Results.NoContent();
    }
    catch (VoisSignatureException)
    {
        return Results.Unauthorized();
    }
});
```

İmza sabit zamanlı karşılaştırılır; `fired_at` 5 dakikadan eskiyse (tekrar gönderim) reddedilir.

## Hatalar

| Sınıf | Ne zaman |
|---|---|
| `VoisSmsException` | SMS API hata döndürdü (`ErrorCode`, `Status`, `HttpStatus`) |
| `VoisVoiceException` | Ses API hata döndürdü (`ErrorCode`, `HttpStatus`) |
| `VoisWebhookException` | Webhook yönetimi hata döndürdü (`ErrorCode`, `HttpStatus`) |
| `VoisSignatureException` | Webhook imzası geçersiz ya da olay eski |
| `VoisException` | Ağ hatası ya da beklenmeyen yanıt (diğerlerinin üst sınıfı) |

Hata kodlarının tam listesi: [SMS](https://docs.vois.com.tr/sms/#hatalar) · [Ses](https://docs.vois.com.tr/voice/#hatalar)

## Lisans

MIT
