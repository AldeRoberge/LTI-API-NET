using FluentValidation;

namespace ADG.LTI.Options;

public class LtiOptionsValidator : AbstractValidator<LtiOptions>
{
    public LtiOptionsValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("ClientId is required.");
        RuleFor(x => x.DeploymentId).NotEmpty().WithMessage("DeploymentId is required.");
        RuleFor(x => x.MoodleAuthorizationEndpoint).NotEmpty().WithMessage("MoodleAuthorizationEndpoint is required.");
        RuleFor(x => x.RedirectionUrl).NotEmpty().WithMessage("RedirectionUrl is required.");
        RuleFor(x => x.Issuer).NotEmpty().WithMessage("Issuer is required.");
        RuleFor(x => x.JwksUrl).NotEmpty().WithMessage("JwksUrl is required.");
    }
}