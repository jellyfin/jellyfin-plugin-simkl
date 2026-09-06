using System;
using System.Net;

namespace Jellyfin.Plugin.Simkl.API.Exceptions
{
    /// <summary>
    /// Thrown when a call to the Simkl API returned a response that could not be used:
    /// a non-success HTTP status code, or a body that could not be deserialized.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Net.Http.HttpClient.SendAsync(System.Net.Http.HttpRequestMessage)"/> does
    /// not throw for 4xx/5xx responses, so these have to be detected and surfaced explicitly.
    /// </remarks>
    public class SimklApiException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApiException"/> class.
        /// </summary>
        public SimklApiException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApiException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public SimklApiException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApiException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public SimklApiException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApiException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="statusCode">The HTTP status code returned by Simkl.</param>
        /// <param name="responseBody">The (possibly truncated) response body.</param>
        /// <param name="retryAfter">The Retry-After the server asked for, if any.</param>
        public SimklApiException(string message, HttpStatusCode? statusCode, string? responseBody, TimeSpan? retryAfter)
            : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            RetryAfter = retryAfter;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApiException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="statusCode">The HTTP status code returned by Simkl.</param>
        /// <param name="responseBody">The (possibly truncated) response body.</param>
        /// <param name="innerException">The inner exception.</param>
        public SimklApiException(string message, HttpStatusCode? statusCode, string? responseBody, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        /// <summary>
        /// Gets the HTTP status code returned by Simkl, if the failure was an HTTP error.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Gets the response body returned by Simkl, truncated for logging.
        /// </summary>
        public string? ResponseBody { get; }

        /// <summary>
        /// Gets how long the server asked us to wait before retrying, from the Retry-After
        /// header. Null when the server did not say.
        /// </summary>
        public TimeSpan? RetryAfter { get; }

        /// <summary>
        /// Gets a value indicating whether Simkl rejected the call for exceeding a rate limit.
        /// </summary>
        public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

        /// <summary>
        /// Gets a value indicating whether retrying the same call could plausibly succeed.
        /// </summary>
        public bool IsTransient
        {
            get
            {
                if (StatusCode == null)
                {
                    // No status code means the body could not be parsed; the next response may be fine.
                    return true;
                }

                var code = (int)StatusCode.Value;
                return StatusCode == HttpStatusCode.RequestTimeout
                       || StatusCode == HttpStatusCode.TooManyRequests
                       || code >= 500;
            }
        }
    }
}
