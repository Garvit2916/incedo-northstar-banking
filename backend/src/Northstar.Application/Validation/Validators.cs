using FluentValidation;
using Northstar.Application.Contracts;

namespace Northstar.Application.Validation;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .Custom((password, context) =>
            {
                foreach (var message in PasswordPolicy.Evaluate(password))
                {
                    context.AddFailure(nameof(RegisterRequest.Password), message);
                }
            });

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage(PasswordPolicy.MismatchMessage)
            .When(x => x.ConfirmPassword is not null);

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100);

        RuleFor(x => x.PhoneNumber).MaximumLength(30);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.AddressLine1).MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.Occupation).MaximumLength(100);
        RuleFor(x => x.AnnualIncomeBand).MaximumLength(50);
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.");
    }
}

public sealed class CreateApplicationRequestValidator : AbstractValidator<CreateApplicationRequest>
{
    public CreateApplicationRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Answers!)
            .Must(a => a is null || a.Count <= 50)
            .WithMessage("At most 50 application answers are permitted.")
            .When(x => x.Answers is not null);

        RuleFor(x => x.Answers!)
            .Must(a => a is null || a.All(kv => kv.Key.Length <= 100 && (kv.Value?.Length ?? 0) <= 2000))
            .WithMessage("Application answers exceed the permitted length.")
            .When(x => x.Answers is not null);
    }
}

public sealed class UpdateApplicationDraftRequestValidator : AbstractValidator<UpdateApplicationDraftRequest>
{
    public UpdateApplicationDraftRequestValidator()
    {
        RuleFor(x => x.Answers).NotNull();
        RuleFor(x => x.Answers)
            .Must(a => a.Count <= 50)
            .WithMessage("At most 50 application answers are permitted.");
        RuleFor(x => x.Answers)
            .Must(a => a.All(kv => kv.Key.Length <= 100 && (kv.Value?.Length ?? 0) <= 2000))
            .WithMessage("Application answers exceed the permitted length.");
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MinimumLength(5).WithMessage("Please provide a meaningful reason (at least 5 characters).")
            .MaximumLength(1000);
    }
}

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public sealed class AddTicketMessageRequestValidator : AbstractValidator<AddTicketMessageRequest>
{
    public AddTicketMessageRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class AssignTicketRequestValidator : AbstractValidator<AssignTicketRequest>
{
    public AssignTicketRequestValidator()
    {
        RuleFor(x => x.AgentUserId).NotEmpty();
    }
}

public sealed class ChangeTicketStatusRequestValidator : AbstractValidator<ChangeTicketStatusRequest>
{
    public ChangeTicketStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Eligibility).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Terms).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.MinimumAge).InclusiveBetween(0, 100);
    }
}

public sealed class DocumentReviewRequestValidator : AbstractValidator<DocumentReviewRequest>
{
    public DocumentReviewRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum()
            .Must(d => d != Northstar.Domain.DocumentReviewStatus.Pending)
            .WithMessage("A review decision of Pending is not a valid outcome.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
