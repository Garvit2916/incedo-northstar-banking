using System.Text.RegularExpressions;

namespace Northstar.Application.Validation;

/// <summary>
/// The single authoritative registration password policy. The same rule set is
/// mirrored by the ASP.NET Core Identity password options (which act as
/// defence-in-depth) and by the frontend validator in
/// <c>frontend/src/lib/passwordPolicy.ts</c>. Keep the three in sync.
///
/// Sign-in is deliberately NOT subject to this policy: passwords that predate a
/// policy change must still be accepted by <c>login</c>.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    public const string UppercasePattern = "[A-Z]";
    public const string LowercasePattern = "[a-z]";
    public const string DigitPattern = "[0-9]";
    public const string SpecialPattern = "[^A-Za-z0-9]";

    public const string RequiredMessage = "Password is required.";
    public const string MinimumLengthMessage = "Password must be at least 10 characters.";
    public const string UppercaseMessage = "Password must contain at least one uppercase letter.";
    public const string LowercaseMessage = "Password must contain at least one lowercase letter.";
    public const string DigitMessage = "Password must contain at least one number.";
    public const string SpecialMessage = "Password must contain at least one special character.";
    public const string NoSurroundingWhitespaceMessage = "Password must not start or end with a space.";
    public const string MismatchMessage = "Passwords do not match.";

    /// <summary>
    /// Evaluates a password against every rule. Returns the messages for each
    /// unmet requirement; an empty result means the password satisfies the policy.
    /// An empty or missing password yields exactly the "required" message.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new[] { RequiredMessage };
        }

        var failures = new List<string>();
        if (password.Length < MinimumLength) failures.Add(MinimumLengthMessage);
        if (!Regex.IsMatch(password, UppercasePattern)) failures.Add(UppercaseMessage);
        if (!Regex.IsMatch(password, LowercasePattern)) failures.Add(LowercaseMessage);
        if (!Regex.IsMatch(password, DigitPattern)) failures.Add(DigitMessage);
        if (!Regex.IsMatch(password, SpecialPattern)) failures.Add(SpecialMessage);
        if (password != password.Trim()) failures.Add(NoSurroundingWhitespaceMessage);
        return failures;
    }

    public static bool IsValid(string? password) => Evaluate(password).Count == 0;
}
