using System;

namespace Vois
{
    /// <summary>Base class for every error thrown by this package (network, unexpected response, API errors).</summary>
    public class VoisException : Exception
    {
        public VoisException(string message, int? httpStatus = null, Exception? inner = null) : base(message, inner)
        {
            HttpStatus = httpStatus;
        }

        /// <summary>HTTP status of the response, or null when no response was received.</summary>
        public int? HttpStatus { get; }
    }

    /// <summary>SMS API returned an error. Branch on <see cref="ErrorCode"/> (e.g. ERR_USER_CREDIT_REQUIRED); Message is Turkish.</summary>
    public sealed class VoisSmsException : VoisException
    {
        public VoisSmsException(string message, string errorCode, int? status, int? httpStatus) : base(message, httpStatus)
        {
            ErrorCode = errorCode;
            Status = status;
        }

        public string ErrorCode { get; }

        /// <summary>err.status from the body (not always equal to the HTTP status).</summary>
        public int? Status { get; }
    }

    /// <summary>Voice API returned an error. Branch on <see cref="ErrorCode"/> (e.g. extension_unavailable); Message is the API's Turkish text.</summary>
    public sealed class VoisVoiceException : VoisException
    {
        public VoisVoiceException(string message, string errorCode, int? httpStatus) : base(message, httpStatus)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    /// <summary>Webhook management API returned an error. Branch on <see cref="ErrorCode"/> (unauthorized, insufficient_scope, invalid_event, url_not_allowed, https_required, limit_reached …).</summary>
    public sealed class VoisWebhookException : VoisException
    {
        public VoisWebhookException(string message, string errorCode, int? httpStatus) : base(message, httpStatus)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    /// <summary>Webhook failed signature or freshness check: reject it with HTTP 401.</summary>
    public sealed class VoisSignatureException : VoisException
    {
        public VoisSignatureException(string message) : base(message, 401)
        {
        }
    }
}
