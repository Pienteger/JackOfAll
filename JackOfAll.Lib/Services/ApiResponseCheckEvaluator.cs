using System.Globalization;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public enum ApiCheckSource { Status, ResponseTime, Header, JsonPath, Body }
public enum ApiCheckOperator { Exists, Equals, NotEquals, Contains, NotContains, LessThan, InRange }

public record ApiResponseCheck
{
    public bool Enabled { get; set; } = true;
    public ApiCheckSource Source { get; set; }
    public string Selector { get; set; } = "";
    public ApiCheckOperator Operator { get; set; } = ApiCheckOperator.Equals;
    public string Expected { get; set; } = "";
}

public sealed record ApiCheckResult(
    ApiResponseCheck Check, bool Passed, bool Skipped, string Actual, string Message);

public static class ApiResponseCheckEvaluator
{
    public static List<ApiCheckResult> Evaluate(
        ApiResponse response, IEnumerable<ApiResponseCheck> checks, JsonElement? jsonRoot = null)
    {
        var results = new List<ApiCheckResult>();
        foreach (var check in checks.Where(check => check.Enabled))
            results.Add(EvaluateOne(response, check, jsonRoot));
        return results;
    }

    private static ApiCheckResult EvaluateOne(
        ApiResponse response, ApiResponseCheck check, JsonElement? jsonRoot) =>
        check.Source switch
        {
            ApiCheckSource.Status => EvaluateStatus(response, check),
            ApiCheckSource.ResponseTime => EvaluateResponseTime(response, check),
            ApiCheckSource.Header => EvaluateHeader(response, check),
            ApiCheckSource.JsonPath => EvaluateJsonPath(response, check, jsonRoot),
            ApiCheckSource.Body => EvaluateBody(response, check),
            _ => Skip(check, "Unsupported check source.")
        };

    private static ApiCheckResult EvaluateStatus(ApiResponse response, ApiResponseCheck check)
    {
        var actual = response.StatusCode.ToString(CultureInfo.InvariantCulture);
        if (check.Operator == ApiCheckOperator.InRange)
        {
            if (!TryParseRange(check.Expected, out var minimum, out var maximum))
                return Fail(check, actual, "Expected a range such as 200-299.");
            return Result(check, response.StatusCode >= minimum && response.StatusCode <= maximum, actual);
        }
        if (!int.TryParse(check.Expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
            return Fail(check, actual, "Expected a numeric status code.");
        return check.Operator switch
        {
            ApiCheckOperator.Equals => Result(check, response.StatusCode == expected, actual),
            ApiCheckOperator.NotEquals => Result(check, response.StatusCode != expected, actual),
            _ => Skip(check, "Unsupported status operator.")
        };
    }

    private static ApiCheckResult EvaluateResponseTime(ApiResponse response, ApiResponseCheck check)
    {
        var actual = response.ElapsedMs.ToString(CultureInfo.InvariantCulture);
        if (check.Operator != ApiCheckOperator.LessThan)
            return Skip(check, "Unsupported response-time operator.");
        if (!long.TryParse(check.Expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
            return Fail(check, actual, "Expected milliseconds as a number.");
        return Result(check, response.ElapsedMs < expected, actual);
    }

    private static ApiCheckResult EvaluateHeader(ApiResponse response, ApiResponseCheck check)
    {
        var header = response.Headers.FirstOrDefault(pair =>
            pair.Key.Equals(check.Selector, StringComparison.OrdinalIgnoreCase));
        var exists = header.Key is not null;
        var actual = exists ? header.Value : "<missing>";
        return check.Operator switch
        {
            ApiCheckOperator.Exists => Result(check, exists, actual),
            ApiCheckOperator.Equals => Result(check, exists && actual == check.Expected, actual),
            ApiCheckOperator.Contains => Result(check,
                exists && actual.Contains(check.Expected, StringComparison.Ordinal), actual),
            _ => Skip(check, "Unsupported header operator.")
        };
    }

    private static ApiCheckResult EvaluateJsonPath(
        ApiResponse response, ApiResponseCheck check, JsonElement? jsonRoot)
    {
        if (response.IsBodyTruncated)
            return Skip(check, "JSONPath checks are skipped for truncated responses.");
        if (jsonRoot is null)
            return Fail(check, "<invalid JSON>", "The response is not valid JSON.");

        var value = ApiEnvironmentService.ExtractJsonPath(jsonRoot.Value, check.Selector);
        var actual = value ?? "<missing>";
        return check.Operator switch
        {
            ApiCheckOperator.Exists => Result(check, value is not null, actual),
            ApiCheckOperator.Equals => Result(check, value == check.Expected, actual),
            ApiCheckOperator.Contains => Result(check,
                value?.Contains(check.Expected, StringComparison.Ordinal) == true, actual),
            _ => Skip(check, "Unsupported JSONPath operator.")
        };
    }

    private static ApiCheckResult EvaluateBody(ApiResponse response, ApiResponseCheck check)
    {
        if (response.IsBodyTruncated && check.Operator == ApiCheckOperator.NotContains)
            return Skip(check, "Negative body checks are skipped for truncated responses.");

        var contains = response.Body.Contains(check.Expected, StringComparison.Ordinal);
        if (response.IsBodyTruncated && check.Operator == ApiCheckOperator.Contains && !contains)
            return Skip(check, "Text was not found in the available response preview.");

        return check.Operator switch
        {
            ApiCheckOperator.Contains => Result(check, contains, contains ? check.Expected : "<not found>"),
            ApiCheckOperator.NotContains => Result(check, !contains, contains ? check.Expected : "<not found>"),
            _ => Skip(check, "Unsupported body operator.")
        };
    }

    private static ApiCheckResult Result(ApiResponseCheck check, bool passed, string actual) =>
        new(check, passed, false, actual,
            passed ? "Passed" : $"Expected {check.Expected}; actual {actual}.");

    private static ApiCheckResult Fail(ApiResponseCheck check, string actual, string message) =>
        new(check, false, false, actual, message);

    private static ApiCheckResult Skip(ApiResponseCheck check, string message) =>
        new(check, false, true, "", message);

    private static bool TryParseRange(string value, out int minimum, out int maximum)
    {
        minimum = 0;
        maximum = 0;
        var parts = value.Replace("..", "-", StringComparison.Ordinal).Split('-', 2);
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), out minimum)
            && int.TryParse(parts[1].Trim(), out maximum)
            && minimum <= maximum;
    }
}
