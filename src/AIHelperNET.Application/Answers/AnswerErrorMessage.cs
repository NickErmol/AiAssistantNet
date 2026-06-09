using System.Net;
using System.Net.Http;

namespace AIHelperNET.Application.Answers;

/// <summary>
/// Maps provider exceptions to concise, user-facing error messages without leaking
/// raw HTTP bodies, status JSON, or internal details.
/// </summary>
public static class AnswerErrorMessage
{
    /// <summary>
    /// Returns a friendly, non-technical message suitable for display in the answer card.
    /// The raw exception (with full diagnostic detail) is preserved by the caller; this method
    /// only produces the string shown to the user.
    /// </summary>
    /// <param name="ex">The exception thrown by the answer provider.</param>
    /// <returns>A user-facing error string with no raw body, JSON, or API credentials.</returns>
    public static string ForUser(Exception ex)
    {
        if (ex is InvalidOperationException && ex.Message.Contains("API key"))
            return "No API key configured — add one in Settings.";

        if (ex is HttpRequestException http)
        {
            if (http.StatusCode is null)
                return "Couldn't reach the AI service — check your connection.";

            int code = (int)http.StatusCode;

            if (code is 401 or 403)
                return "Authentication failed — check your API key in Settings.";

            if (code == 429)
                return "Rate limit reached — please wait a moment and try again.";

            if (code is 503 or 529 || (code >= 500 && code < 600))
                return "The AI service is temporarily unavailable. Please try again.";

            // Other HTTP errors with no status match (4xx client, unknown, etc.)
            return "Couldn't reach the AI service — check your connection.";
        }

        return "Something went wrong generating the answer. Please try again.";
    }
}
