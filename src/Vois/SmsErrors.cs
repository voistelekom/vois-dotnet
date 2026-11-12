using System.Collections.Generic;

namespace Vois
{
    /// <summary>Turkish descriptions of SMS API error codes (same as https://docs.vois.com.tr/sms/#hatalar).</summary>
    public static class SmsErrors
    {
        public static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["ERR_BAD_REQUEST"] = "İstek hatalı ya da eksik. Gövdeyi ve zorunlu alanları kontrol edin.",
            ["ERR_INVALID_PARAM"] = "Bir parametre eksik ya da geçersiz. Zorunlu alanları, tarih biçimini (yyyy-MM-dd HH:mm) ve numara biçimini kontrol edin.",
            ["ERR_UNAUTHORIZED_REQUEST"] = "Kullanıcı adı veya şifre hatalı, hesapta IP kısıtı var ya da API yetkisi açık değil.",
            ["ERR_USER_NOT_AUTHORIZED"] = "Hesabınızın bu işlem için yetkisi yok (ör. OTP yetkisi kapalı).",
            ["ERR_INVALID_SMS_SENDER"] = "Gönderen başlığı hatalı ya da onaylı değil.",
            ["ERR_EMPTY_SMS_PACKAGE"] = "Pakette gönderilebilecek geçerli numara kalmadı: numaralar hatalı biçimde, kara listede ya da ticari iletide İYS onayı yok.",
            ["ERR_INVALID_SMS_SENDING_DATE"] = "Gönderim tarihi geçersiz ya da geçmişte.",
            ["ERR_USER_CREDIT_REQUIRED"] = "Yeterli krediniz yok.",
            ["ERR_PARENT_USER_CREDIT_REQUIRED"] = "Bağlı olduğunuz ana hesapta yeterli kredi yok. Bizimle iletişime geçin.",
            ["ERR_USER_BRAND_CODE_NOT_FOUND"] = "Ticari gönderim için İYS marka kodunuz tanımlı değil.",
            ["ERR_USER_IYS_SETTINGS_NOT_FOUND"] = "Hesabınızda İYS tanımları yok.",
            ["ERR_SMS_PKG_CUSTOM_ID_DUPLICATION"] = "Bu customID ile daha önce paket oluşturulmuş.",
            ["ERR_SMS_PKG_DUPLICATION"] = "Aynı mesaj bu numaralara son 30 dakika içinde zaten gönderildi.",
            ["ERR_INVALID_DATE_RANGE"] = "Tarih aralığı geçersiz.",
            ["ERR_SMS_PKG_NOT_FOUND"] = "Paket bulunamadı.",
            ["ERR_SMS_PKG_NOT_CHANGEABLE"] = "Paket bu aşamada değiştirilemez/iptal edilemez.",
            ["ERR_SMS_PKG_CREATING_FAILED"] = "Paket oluşturulamadı. Tekrar deneyin; sürerse bize bildirin.",
            ["ERR_INVALID_PRICING_PLAN"] = "Hesabınızın fiyat planı geçersiz. Bizimle iletişime geçin.",
            ["ERR_CREDIT_PLAN_NOT_FOUND"] = "Kredi planı bulunamadı. Bizimle iletişime geçin.",
            ["ERR_USER_NOT_FOUND"] = "Kullanıcı bulunamadı.",
            ["ERR_MO_NOT_FOUND"] = "Hesabınızda MO kısa numarası tanımlı değil.",
            ["ERR_DAILY_REQUEST_LIMIT"] = "Günlük rapor sorgulama sınırı aşıldı.",
            ["ERR_CONCURRENT_REQUEST_LIMIT"] = "Aynı anda çok fazla istek gönderildi; istekleri sıraya koyun.",
            ["ERR_NOT_FOUND"] = "İstek yapılan adres hatalı.",
            ["ERR_SYSTEM_MAINTENANCE"] = "Sistem bakımda; bir süre sonra tekrar deneyin.",
            ["ERR_INTERNAL_SERVER"] = "Sunucu hatası; tekrar deneyin, sürerse bize bildirin.",
            ["ERR_RATE_LIMIT"] = "Saniyedeki istek sınırı aşıldı; kısa bir süre bekleyip tekrar deneyin.",
            ["ERR_SERVICE_UNAVAILABLE"] = "SMS servisine geçici olarak ulaşılamıyor; birkaç dakika sonra tekrar deneyin.",
        };
    }
}
