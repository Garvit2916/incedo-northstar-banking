using FluentValidation;
using Northstar.Application.Contracts;
using Northstar.Application.Validation;
using Xunit;

namespace Northstar.Application.Tests;

/// <summary>
/// Verifies the authoritative registration password policy. These rules must
/// stay in agreement with the frontend validator
/// (frontend/src/lib/passwordPolicy.ts) and the Identity password options.
/// </summary>
public sealed class PasswordPolicyTests
{
    private static readonly RegisterRequestValidator Validator = new();

    // A password that satisfies every rule: 10+ chars, upper, lower, digit, special.
    private const string ValidPassword = "Northstar#1";

    [Fact]
    public void Valid_password_satisfies_policy()
    {
        Assert.True(PasswordPolicy.IsValid(ValidPassword));
        Assert.Empty(PasswordPolicy.Evaluate(ValidPassword));
    }

    [Theory]
    [InlineData("Short#1")]                 // fewer than 10 characters
    [InlineData("northstar#1")]             // missing uppercase
    [InlineData("NORTHSTAR#1")]             // missing lowercase
    [InlineData("Northstar#")]              // missing number
    [InlineData("Northstar1")]              // missing special character
    [InlineData(" Northstar#1")]            // leading whitespace
    [InlineData("Northstar#1 ")]            // trailing whitespace
    public void Invalid_passwords_are_rejected_with_a_specific_message(string password)
    {
        var failures = PasswordPolicy.Evaluate(password);

        Assert.NotEmpty(failures);
        Assert.False(PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void Exactly_ten_characters_with_all_classes_is_valid()
    {
        // 10 characters exactly, containing upper, lower, digit and special.
        Assert.True(PasswordPolicy.IsValid("North#123a"));
    }

    [Fact]
    public void Minimum_length_message_is_reported_for_short_password()
    {
        Assert.Contains(PasswordPolicy.MinimumLengthMessage, PasswordPolicy.Evaluate("Short#1"));
    }

    [Fact]
    public void Missing_uppercase_message_is_reported()
    {
        Assert.Contains(PasswordPolicy.UppercaseMessage, PasswordPolicy.Evaluate("northstar#1"));
    }

    [Fact]
    public void Missing_lowercase_message_is_reported()
    {
        Assert.Contains(PasswordPolicy.LowercaseMessage, PasswordPolicy.Evaluate("NORTHSTAR#1"));
    }

    [Fact]
    public void Missing_number_message_is_reported()
    {
        Assert.Contains(PasswordPolicy.DigitMessage, PasswordPolicy.Evaluate("Northstar#"));
    }

    [Fact]
    public void Missing_special_message_is_reported()
    {
        Assert.Contains(PasswordPolicy.SpecialMessage, PasswordPolicy.Evaluate("Northstar1"));
    }

    [Fact]
    public void Whitespace_message_is_reported_and_password_is_not_trimmed()
    {
        Assert.Contains(PasswordPolicy.NoSurroundingWhitespaceMessage, PasswordPolicy.Evaluate(" Northstar#1"));
        Assert.Contains(PasswordPolicy.NoSurroundingWhitespaceMessage, PasswordPolicy.Evaluate("Northstar#1 "));
        // A leading space is part of the value; the rest still validates independently.
        Assert.True(PasswordPolicy.IsValid("Northstar#1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_or_missing_password_reports_only_the_required_message(string? password)
    {
        var failures = PasswordPolicy.Evaluate(password);
        Assert.Equal(new[] { PasswordPolicy.RequiredMessage }, failures);
    }

    [Fact]
    public void Validator_accepts_a_valid_registration()
    {
        var request = new RegisterRequest("new@northstar.example", ValidPassword, "Ada", "Lovelace", null, ValidPassword);
        var result = Validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_rejects_weak_password()
    {
        var request = new RegisterRequest("new@northstar.example", "password", "Ada", "Lovelace", null, "password");
        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Validator_rejects_mismatched_confirmation()
    {
        var request = new RegisterRequest("new@northstar.example", ValidPassword, "Ada", "Lovelace", null, "Different#1");
        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(RegisterRequest.ConfirmPassword) &&
            e.ErrorMessage == PasswordPolicy.MismatchMessage);
    }

    [Fact]
    public void Validator_ignores_confirmation_when_not_supplied()
    {
        // Backward compatible: callers that do not send a confirmation are not rejected.
        var request = new RegisterRequest("new@northstar.example", ValidPassword, "Ada", "Lovelace", null);
        var result = Validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_requires_first_and_last_name()
    {
        var request = new RegisterRequest("new@northstar.example", ValidPassword, "", "", null, ValidPassword);
        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.FirstName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.LastName));
    }
}
