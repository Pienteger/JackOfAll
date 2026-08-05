using System.Net.Http;
using System.Net.Sockets;

namespace JackOfAll.Lib.Services;

public enum ApiRequestErrorKind
{
    Timeout,
    Dns,
    Tls,
    ConnectionRefused,
    Connection,
    Cancelled,
    InvalidUrl,
    InvalidRequest,
    Unknown
}

public sealed record ApiRequestError(ApiRequestErrorKind Kind, string Message);

public sealed class ApiRequestValidationException(string message) : Exception(message);

public static class ApiRequestErrorClassifier
{
    public static ApiRequestError Classify(Exception exception)
    {
        if (exception is UriFormatException or InvalidOperationException or ArgumentException)
            return new(ApiRequestErrorKind.InvalidUrl, "The request URL is invalid.");
        if (exception is ApiRequestValidationException validation)
            return new(ApiRequestErrorKind.InvalidRequest, validation.Message);
        if (exception is TaskCanceledException)
            return new(ApiRequestErrorKind.Timeout, "The request timed out.");

        if (exception is HttpRequestException http)
        {
            if (http.HttpRequestError == HttpRequestError.NameResolutionError)
                return new(ApiRequestErrorKind.Dns, "The host name could not be resolved.");
            if (http.HttpRequestError == HttpRequestError.SecureConnectionError)
                return new(ApiRequestErrorKind.Tls, "A secure TLS connection could not be established.");
            if (FindSocketException(http) is { SocketErrorCode: SocketError.ConnectionRefused })
                return new(ApiRequestErrorKind.ConnectionRefused, "The server refused the connection.");
            if (http.HttpRequestError == HttpRequestError.ConnectionError)
                return new(ApiRequestErrorKind.Connection, "The server could not be reached.");
        }

        return new(ApiRequestErrorKind.Unknown, exception.Message);
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SocketException socket) return socket;
        return null;
    }
}
